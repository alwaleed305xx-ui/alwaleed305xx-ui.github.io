using System;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Builds the two hideable closets of the Hide &amp; Shriek update (GDD 14.2):
/// - The bedroom "screaming closet" alcove (its walls already exist in
///   HouseLayout as Closet_N/S/W) finally gets doors and a purpose.
/// - A freestanding garage wardrobe nobody remembers buying.
/// Art-directed primitives, same placeholder contract as the rest of the
/// house; real closet models later replace the panels under the same roots.
///
/// Called by ScreamerSetupWizard right after the task stations are built.
/// </summary>
public static class HideSpotFactory
{
    public static HideSpot[] BuildAll(Transform mapRoot, Func<string, Color, Material> mat)
    {
        GameObject root = new GameObject("HideSpots");
        root.transform.SetParent(mapRoot, false);

        var spots = new HideSpot[2];

        // 1) Bedroom screaming-closet alcove: interior x [-19.5,-16], z [-16.25,-11.75],
        //    opening on the east face (x = -16). Root forward (+Z local) points out.
        spots[0] = BuildAlcoveCloset(root.transform, mat,
            position: new Vector3(-16f, 0f, -14f),
            yaw: 90f,
            openingWidth: 4.5f,
            insideLocal: new Vector3(0f, 0.1f, -1.6f));

        // 2) Garage wardrobe, parked in the south-east corner, opening north.
        //    Nudge it freely if the garage junk dressing lands on the same spot.
        spots[1] = BuildWardrobe(root.transform, mat,
            position: new Vector3(19f, 0f, -19f),
            yaw: 0f);

        return spots;
    }

    // ------------------------- alcove closet (doors only) -------------------------

    static HideSpot BuildAlcoveCloset(Transform parent, Func<string, Color, Material> mat,
        Vector3 position, float yaw, float openingWidth, Vector3 insideLocal)
    {
        GameObject root = NewSpotRoot("HideSpot_BedroomCloset", parent, position, yaw);

        Material doorWood = mat("ClosetDoorWood", new Color32(0x6B, 0x45, 0x2B, 0xFF));
        float half = openingWidth * 0.5f;
        float doorWidth = half - 0.05f;

        Transform leftHinge = NewHinge(root.transform, "HingeL", new Vector3(-half, 0f, 0f));
        Transform rightHinge = NewHinge(root.transform, "HingeR", new Vector3(half, 0f, 0f));
        BuildDoorPanel(leftHinge, doorWidth, doorWood, hingeOnLeft: true);
        BuildDoorPanel(rightHinge, doorWidth, doorWood, hingeOnLeft: false);

        return WireSpot(root, leftHinge, rightHinge, insideLocal, new Vector3(0f, 0.1f, 1.5f));
    }

    // ------------------------- freestanding wardrobe -------------------------

    static HideSpot BuildWardrobe(Transform parent, Func<string, Color, Material> mat,
        Vector3 position, float yaw)
    {
        GameObject root = NewSpotRoot("HideSpot_GarageWardrobe", parent, position, yaw);

        Material shellWood = mat("WardrobeWood", new Color32(0x4F, 0x33, 0x22, 0xFF));
        Material doorWood = mat("ClosetDoorWood", new Color32(0x6B, 0x45, 0x2B, 0xFF));

        // Shell: back, two sides, top. Interior is 1.6 wide x 1.0 deep - a
        // CharacterController (radius 0.45) fits with its dignity mostly intact.
        Panel(root.transform, "Back", shellWood, new Vector3(0f, 1.2f, -0.55f), new Vector3(1.8f, 2.4f, 0.1f));
        Panel(root.transform, "SideL", shellWood, new Vector3(-0.85f, 1.2f, 0f), new Vector3(0.1f, 2.4f, 1.2f));
        Panel(root.transform, "SideR", shellWood, new Vector3(0.85f, 1.2f, 0f), new Vector3(0.1f, 2.4f, 1.2f));
        Panel(root.transform, "Top", shellWood, new Vector3(0f, 2.45f, 0f), new Vector3(1.8f, 0.1f, 1.2f));

        Transform leftHinge = NewHinge(root.transform, "HingeL", new Vector3(-0.85f, 0f, 0.55f));
        Transform rightHinge = NewHinge(root.transform, "HingeR", new Vector3(0.85f, 0f, 0.55f));
        BuildDoorPanel(leftHinge, 0.8f, doorWood, hingeOnLeft: true, height: 2.3f);
        BuildDoorPanel(rightHinge, 0.8f, doorWood, hingeOnLeft: false, height: 2.3f);

        return WireSpot(root, leftHinge, rightHinge,
            new Vector3(0f, 0.1f, -0.05f), new Vector3(0f, 0.1f, 1.4f));
    }

    // ------------------------- shared pieces -------------------------

    static GameObject NewSpotRoot(string name, Transform parent, Vector3 position, float yaw)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = position;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        root.AddComponent<NetworkObject>();
        return root;
    }

    static Transform NewHinge(Transform parent, string name, Vector3 localPos)
    {
        GameObject hinge = new GameObject(name);
        hinge.transform.SetParent(parent, false);
        hinge.transform.localPosition = localPos;
        return hinge.transform;
    }

    static void BuildDoorPanel(Transform hinge, float width, Material material,
        bool hingeOnLeft, float height = 3.6f)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Door";
        panel.transform.SetParent(hinge, false);
        // The panel extends from its hinge toward the closet's center line.
        float toCenter = hingeOnLeft ? width * 0.5f : -width * 0.5f;
        panel.transform.localPosition = new Vector3(toCenter, height * 0.5f, 0f);
        panel.transform.localScale = new Vector3(width, height, 0.12f);
        panel.GetComponent<Renderer>().sharedMaterial = material;

        // A little knob so the door reads as a door at a glance.
        GameObject knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        knob.name = "Knob";
        UnityEngine.Object.DestroyImmediate(knob.GetComponent<Collider>());
        knob.transform.SetParent(hinge, false);
        knob.transform.localPosition = new Vector3(toCenter * 1.75f, height * 0.45f, 0.1f);
        knob.transform.localScale = Vector3.one * 0.12f;
        knob.GetComponent<Renderer>().sharedMaterial = material;
    }

    static void Panel(Transform parent, string name, Material material, Vector3 localPos, Vector3 localScale)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = name;
        panel.transform.SetParent(parent, false);
        panel.transform.localPosition = localPos;
        panel.transform.localScale = localScale;
        panel.GetComponent<Renderer>().sharedMaterial = material;
    }

    static HideSpot WireSpot(GameObject root, Transform leftHinge, Transform rightHinge,
        Vector3 insideLocal, Vector3 exitLocal)
    {
        HideSpot spot = root.AddComponent<HideSpot>();
        spot.leftHinge = leftHinge;
        spot.rightHinge = rightHinge;
        spot.insideLocalPosition = insideLocal;
        spot.exitLocalPosition = exitLocal;

        TaskFactory.BuildWorldLabel(root.transform, GameCopy.ClosetLabel,
            new Vector3(0f, 2.7f, 0.4f), 2.2f, 24, billboard: true);
        return spot;
    }
}
