using System;
using UnityEngine;

/// <summary>
/// The Dead Hills - SCREAMER's third map, built on a real Unity Terrain.
/// Rolling Perlin hills surround a flattened plateau that covers the exact
/// HouseLayout play rectangle, so every task, spawn, closet, waypoint and
/// treeline barrier carries over untouched (the barriers, props, cabins,
/// anchors and lanterns are reused straight from ForestFactory). The terrain
/// starts with a dusty generated layer; Auto-Install repaints it with any
/// imported TerrainLayer assets (the terrain sample pack's layers drop right
/// in), and the wasteland ruins loom on the same RuinAnchors.
///
/// The wizard owns persistence: it saves the TerrainData this factory
/// creates as an asset, then calls BuildAll with it.
/// </summary>
public static class TerrainFactory
{
    const string RootName = "DeadHills";

    const int HeightmapResolution = 257;
    static readonly Vector3 TerrainSize = new Vector3(120f, 26f, 120f);
    static readonly Vector3 TerrainOrigin = new Vector3(-60f, 0f, -56f);

    // The flattened plateau: the HouseLayout play rect plus a margin.
    const float PlateauMinX = -23f, PlateauMaxX = 23f;
    const float PlateauMinZ = -23f, PlateauMaxZ = 31f;
    const float HillFalloff = 16f; // units from plateau edge to full hills

    /// <summary>Creates the heightmap in memory; the wizard saves it as an asset.</summary>
    public static TerrainData CreateData()
    {
        var data = new TerrainData();
        data.heightmapResolution = HeightmapResolution;
        data.size = TerrainSize;

        var heights = new float[HeightmapResolution, HeightmapResolution];
        for (int iz = 0; iz < HeightmapResolution; iz++)
        {
            for (int ix = 0; ix < HeightmapResolution; ix++)
            {
                float wx = TerrainOrigin.x + ix / (float)(HeightmapResolution - 1) * TerrainSize.x;
                float wz = TerrainOrigin.z + iz / (float)(HeightmapResolution - 1) * TerrainSize.z;

                float outside = DistanceOutsidePlateau(wx, wz);
                if (outside <= 0f) { heights[iz, ix] = 0f; continue; }

                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(outside / HillFalloff));
                float hills = Mathf.PerlinNoise(wx * 0.030f + 7.3f, wz * 0.030f + 2.1f) * 0.62f
                            + Mathf.PerlinNoise(wx * 0.090f + 3.7f, wz * 0.090f + 9.4f) * 0.28f
                            + Mathf.PerlinNoise(wx * 0.230f + 1.1f, wz * 0.230f + 5.8f) * 0.10f;
                heights[iz, ix] = Mathf.Clamp01(rise * hills);
            }
        }
        data.SetHeights(0, 0, heights);

        // A dusty base layer so the hills are never untextured; Auto-Install
        // swaps in real TerrainLayer assets once a pack provides them.
        var dust = new TerrainLayer();
        dust.diffuseTexture = SolidTexture(new Color(0.36f, 0.30f, 0.24f));
        dust.tileSize = new Vector2(12f, 12f);
        data.terrainLayers = new[] { dust };

        return data;
    }

    public static void BuildAll(Transform mapRoot, Func<string, Color, Material> mat, TerrainData data)
    {
        Func<string, Color, Material> material = mat ?? ScreamerPalette.MakeRuntimeMaterial;

        if (mapRoot != null)
        {
            Transform stale = mapRoot.Find(RootName);
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);
        }

        var root = new GameObject(RootName);
        if (mapRoot != null) root.transform.SetParent(mapRoot, false);

        UnityEngine.Random.InitState(20261004); // same deterministic dressing as the woods

        GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
        terrainObject.name = "Terrain";
        terrainObject.transform.SetParent(root.transform, false);
        terrainObject.transform.position = TerrainOrigin;

        // The whole gameplay shell comes straight from the woods: identical
        // barrier colliders, props, cabins, anchors, lanterns and directors.
        ForestFactory.BuildTreelines(root.transform, material);
        ForestFactory.BuildClearingDressing(root.transform, material);
        ForestFactory.BuildCampfireAndStumps(root.transform, material);
        ForestFactory.BuildBackdropCabins(root.transform, material);
        ForestFactory.BuildAnchors(root.transform);
        ApplyDustLighting();
        ForestFactory.BuildLightsAndDirectors(root.transform, material);
        HouseFactory.BuildScreenFxOverlay(root.transform);
    }

    static float DistanceOutsidePlateau(float x, float z)
    {
        float dx = Mathf.Max(PlateauMinX - x, 0f, x - PlateauMaxX);
        float dz = Mathf.Max(PlateauMinZ - z, 0f, z - PlateauMaxZ);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static Texture2D SolidTexture(Color color)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>Dusk over dead ground: warmer fog than the woods, longer shadows.</summary>
    static void ApplyDustLighting()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.12f, 0.10f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.016f;
        RenderSettings.fogColor = new Color(0.14f, 0.09f, 0.07f);

        QualitySettings.shadowDistance = 70f;
    }
}
