using System;
using UnityEngine;

/// <summary>
/// The whitelisted prop proxies the Mimic can disguise as. v1 ships four hand-built
/// primitive props that match the HouseFactory furniture family; real asset-pack
/// props get added here one by one after a collider/pivot hand-check. Never
/// whitelist "any mesh".
///
/// Proxies are plain local visuals (no NetworkObject): the disguise state and the
/// chosen prop index replicate through MimicDisguise, and every client builds the
/// same proxy locally. Proxy colliders are kept so the survivors' slap raycast can
/// land on the "furniture" -- they resolve to the monster through
/// GetComponentInParent while the proxy is parented under the monster root.
/// </summary>
public static class MimicPropCatalog
{
    /// <summary>Display names, index-aligned with BuildPropProxy.</summary>
    public static readonly string[] PropNames = { "Couch", "Armchair", "FloorLamp", "TVStand" };

    /// <summary>
    /// Builds the prop proxy for the given whitelist index as a fresh scene object.
    /// <paramref name="mat"/> maps (materialName, color) to a Material -- the wizard
    /// passes an asset-backed version, play mode uses ScreamerPalette.MakeRuntimeMaterial.
    /// </summary>
    public static GameObject BuildPropProxy(int index, Func<string, Color, Material> mat)
    {
        index = Mathf.Clamp(index, 0, PropNames.Length - 1);
        switch (index)
        {
            case 0: return BuildCouch(mat);
            case 1: return BuildArmchair(mat);
            case 2: return BuildFloorLamp(mat);
            default: return BuildTvStand(mat);
        }
    }

    static GameObject BuildCouch(Func<string, Color, Material> mat)
    {
        GameObject root = new GameObject(PropNames[0]);
        Material shell = mat("MimicProp_CouchShell", ScreamerPalette.ShagRust);
        Material cushion = mat("MimicProp_CouchCushion", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.NoodleCream, 0.35f));

        Prim(PrimitiveType.Cube, "Seat", root.transform, new Vector3(0f, 0.4f, 0f), new Vector3(4f, 0.8f, 1.4f), shell);
        Prim(PrimitiveType.Cube, "Backrest", root.transform, new Vector3(0f, 1.2f, -0.52f), new Vector3(4f, 0.9f, 0.35f), shell);
        Prim(PrimitiveType.Cube, "Arm_L", root.transform, new Vector3(-1.78f, 1.0f, 0f), new Vector3(0.45f, 0.5f, 1.4f), shell);
        Prim(PrimitiveType.Cube, "Arm_R", root.transform, new Vector3(1.78f, 1.0f, 0f), new Vector3(0.45f, 0.5f, 1.4f), shell);
        Prim(PrimitiveType.Cube, "Cushion_L", root.transform, new Vector3(-0.88f, 0.9f, 0.08f), new Vector3(1.6f, 0.22f, 1.15f), cushion);
        Prim(PrimitiveType.Cube, "Cushion_R", root.transform, new Vector3(0.88f, 0.9f, 0.08f), new Vector3(1.6f, 0.22f, 1.15f), cushion);
        return root;
    }

    static GameObject BuildArmchair(Func<string, Color, Material> mat)
    {
        GameObject root = new GameObject(PropNames[1]);
        Material shell = mat("MimicProp_ChairShell", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.MidnightPlum, 0.25f));
        Material cushion = mat("MimicProp_ChairCushion", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.NoodleCream, 0.3f));

        Prim(PrimitiveType.Cube, "Seat", root.transform, new Vector3(0f, 0.33f, 0f), new Vector3(1.4f, 0.65f, 1.3f), shell);
        Prim(PrimitiveType.Cube, "Backrest", root.transform, new Vector3(0f, 1.05f, -0.5f), new Vector3(1.4f, 0.9f, 0.3f), shell);
        Prim(PrimitiveType.Cube, "Arm_L", root.transform, new Vector3(-0.7f, 0.72f, 0f), new Vector3(0.3f, 0.45f, 1.3f), shell);
        Prim(PrimitiveType.Cube, "Arm_R", root.transform, new Vector3(0.7f, 0.72f, 0f), new Vector3(0.3f, 0.45f, 1.3f), shell);
        Prim(PrimitiveType.Cube, "Cushion", root.transform, new Vector3(0f, 0.72f, 0.06f), new Vector3(1.15f, 0.18f, 1.05f), cushion);
        return root;
    }

    static GameObject BuildFloorLamp(Func<string, Color, Material> mat)
    {
        GameObject root = new GameObject(PropNames[2]);
        Material metal = mat("MimicProp_LampMetal", ScreamerPalette.MidnightPlum);
        Material shade = mat("MimicProp_LampShade", ScreamerPalette.LamplightAmber);

        Prim(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.05f, 0.5f), metal);
        Prim(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.08f, 0.78f, 0.08f), metal);
        Prim(PrimitiveType.Cylinder, "Shade", root.transform, new Vector3(0f, 1.72f, 0f), new Vector3(0.55f, 0.22f, 0.55f), shade);
        return root;
    }

    static GameObject BuildTvStand(Func<string, Color, Material> mat)
    {
        GameObject root = new GameObject(PropNames[3]);
        Material wood = mat("MimicProp_StandWood", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.MidnightPlum, 0.4f));
        Material trim = mat("MimicProp_StandTrim", ScreamerPalette.ShagRust);

        Prim(PrimitiveType.Cube, "Slab", root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(2.4f, 0.5f, 0.55f), wood);
        Prim(PrimitiveType.Cube, "Plinth", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(2.2f, 0.3f, 0.5f), trim);
        Prim(PrimitiveType.Cube, "Drawer_L", root.transform, new Vector3(-0.6f, 0.55f, 0.29f), new Vector3(0.9f, 0.3f, 0.05f), trim);
        Prim(PrimitiveType.Cube, "Drawer_R", root.transform, new Vector3(0.6f, 0.55f, 0.29f), new Vector3(0.9f, 0.3f, 0.05f), trim);
        return root;
    }

    /// <summary>Creates a primitive child. Colliders are intentionally KEPT (hand-checked, slappable).</summary>
    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        Renderer r = go.GetComponent<Renderer>();
        if (r != null && material != null) r.sharedMaterial = material;
        return go;
    }
}
