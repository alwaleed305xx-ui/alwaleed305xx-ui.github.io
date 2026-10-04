using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Runtime-compilable factory for the feel layer; the editor wizard calls
/// <see cref="Install"/> once while assembling the scene (after the house, so
/// room data and Camera.main may exist), and a play-mode call works for
/// isolated module tests. Never references UnityEditor.
///
/// Installs:
/// - <see cref="ScreamerCam"/> on the main camera (creating the camera first
///   if the scene has none yet - the wizard runs this factory before the UI);
/// - a "FeelDirector" object carrying <see cref="GagFeedback"/>,
///   <see cref="NoiseRingFx"/>, <see cref="MonsterMorphFx"/> and
///   <see cref="PhotoFinishDirector"/>;
/// - the killcam pair: <see cref="KillcamRecorder"/> on an in-scene
///   NetworkObject, and <see cref="KillcamPlayer"/> wired with four cinematic
///   wall anchors per room (GDD 12.2), placed high in each room's corners;
/// - the wizard's material delegate into <see cref="ParticleFactory"/>.
/// </summary>
public static class FeelFactory
{
    /// <summary>Cinematic anchors sit this far inside each room's corner...</summary>
    const float AnchorCornerInset = 1.3f;

    /// <summary>...at security-camera height, just under the 4-unit ceiling line.</summary>
    const float AnchorHeight = 3.4f;

    public static void Install(System.Func<string, Color, Material> mat)
    {
        ParticleFactory.Install(mat);

        InstallCameraFeel();
        InstallFeelDirector();
        InstallKillcam();
    }

    // ------------------------- Camera -------------------------

    static void InstallCameraFeel()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            // The wizard calls this factory before UiFactory; whoever runs
            // first provides the camera, the other finds it already there.
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            if (Object.FindObjectOfType<AudioListener>() == null)
                go.AddComponent<AudioListener>();

            // A sane pre-menu frame: standing in the den, facing the hearth.
            go.transform.position = new Vector3(0f, 1.7f, -1f);
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        }

        if (cam.GetComponent<ScreamerCam>() == null)
            cam.gameObject.AddComponent<ScreamerCam>();
    }

    // ------------------------- Feel director -------------------------

    static void InstallFeelDirector()
    {
        if (Object.FindObjectOfType<GagFeedback>() != null)
            return; // already installed in this scene

        var go = new GameObject("FeelDirector");
        go.AddComponent<GagFeedback>();
        go.AddComponent<NoiseRingFx>();
        go.AddComponent<MonsterMorphFx>();
        go.AddComponent<PhotoFinishDirector>();
    }

    // ------------------------- Killcam -------------------------

    static void InstallKillcam()
    {
        if (Object.FindObjectOfType<KillcamRecorder>() == null)
        {
            // In-scene placed NetworkObject: the same replication mechanism the
            // GameManager and task stations use. Saving the wizard's scene
            // stamps its identity.
            var recorderGo = new GameObject("KillcamRecorder");
            recorderGo.AddComponent<NetworkObject>();
            recorderGo.AddComponent<KillcamRecorder>();
        }

        if (Object.FindObjectOfType<KillcamPlayer>() == null)
        {
            var playerGo = new GameObject("KillcamPlayer");
            KillcamPlayer player = playerGo.AddComponent<KillcamPlayer>();
            player.anchors = BuildCinematicAnchors(playerGo.transform);
        }
    }

    /// <summary>
    /// Four wall-mount anchors per room, placed in the inset corners near the
    /// ceiling line - the replay camera picks the nearest one with line of
    /// sight to the kill (GDD 12.2). Plain empties, serialized with the scene.
    /// </summary>
    static Transform[] BuildCinematicAnchors(Transform parent)
    {
        var root = new GameObject("CinematicAnchors").transform;
        root.SetParent(parent, false);

        HouseLayout.RoomDef[] rooms = HouseLayout.Rooms;
        var anchors = new Transform[rooms.Length * 4];

        for (int r = 0; r < rooms.Length; r++)
        {
            Rect bounds = rooms[r].boundsXZ;

            // Keep the inset sane in narrow rooms (the hallway is 4 deep).
            float insetX = Mathf.Min(AnchorCornerInset, bounds.width * 0.25f);
            float insetZ = Mathf.Min(AnchorCornerInset, bounds.height * 0.25f);

            var corners = new[]
            {
                new Vector3(bounds.xMin + insetX, AnchorHeight, bounds.yMin + insetZ),
                new Vector3(bounds.xMax - insetX, AnchorHeight, bounds.yMin + insetZ),
                new Vector3(bounds.xMin + insetX, AnchorHeight, bounds.yMax - insetZ),
                new Vector3(bounds.xMax - insetX, AnchorHeight, bounds.yMax - insetZ)
            };

            for (int c = 0; c < 4; c++)
            {
                var anchor = new GameObject("KillcamAnchor_" + rooms[r].name + "_" + (c + 1)).transform;
                anchor.SetParent(root, false);
                anchor.position = corners[c];
                anchors[r * 4 + c] = anchor;
            }
        }
        return anchors;
    }
}
