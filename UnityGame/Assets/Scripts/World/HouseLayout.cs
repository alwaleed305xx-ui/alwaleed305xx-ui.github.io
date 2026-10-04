using UnityEngine;

/// <summary>
/// The single source of truth for the Henderson House geometry: every binding
/// wall, floor and fence segment, room bounds, station/spawn/prop positions
/// and the hand-authored bot waypoint graph (GDD sections 3 and 10).
///
/// HouseFactory builds cubes from these numbers; TaskFactory, RoundFlowFactory
/// and BotFactory read positions from here. Nothing in the project may inline
/// a map coordinate - if the house ever moves, it moves in this file only.
///
/// Conventions: floor top at y = 0; walls 4 units high and 1 unit thick
/// (cube scale); all doorway gaps 2.5 wide, full height.
/// </summary>
public static class HouseLayout
{
    /// <summary>One axis-aligned cube: a floor slab, wall segment or fence run.</summary>
    public struct Box
    {
        public string name;
        public Vector3 center;
        public Vector3 size;

        public Box(string name, Vector3 center, Vector3 size)
        {
            this.name = name;
            this.center = center;
            this.size = size;
        }
    }

    /// <summary>A named room: XZ bounds for queries, center for tells, lights and bot logic.</summary>
    public struct RoomDef
    {
        public string name;
        public Rect boundsXZ; // x = min X, y = min Z, width = size X, height = size Z
        public Vector3 center;

        public RoomDef(string name, Rect boundsXZ, Vector3 center)
        {
            this.name = name;
            this.boundsXZ = boundsXZ;
            this.center = center;
        }
    }

    // ------------------------- Floors (GDD 3.1) -------------------------

    public static readonly Box[] Floors =
    {
        new Box("Floor_House", new Vector3(0f, -0.25f, -4f), new Vector3(41f, 0.5f, 33f)),
        new Box("Floor_Yard", new Vector3(0f, -0.25f, 20f), new Vector3(29f, 0.5f, 17f))
    };

    // ------------------------- Walls (GDD 3.3, binding) -------------------------
    // Gap = no cube. Doorway gaps are 2.5 wide, full height. The three
    // Closet_* segments form the Screaming Closet, opening facing east.

    public static readonly Box[] Walls =
    {
        // Outer shell
        new Box("Wall_S", new Vector3(0f, 2f, -20.5f), new Vector3(41f, 4f, 1f)),
        new Box("Wall_W", new Vector3(-20.5f, 2f, -4f), new Vector3(1f, 4f, 34f)),
        new Box("Wall_E", new Vector3(20.5f, 2f, -4f), new Vector3(1f, 4f, 34f)),
        new Box("Wall_N_L", new Vector3(-10.875f, 2f, 12.5f), new Vector3(19.25f, 4f, 1f)),
        new Box("Wall_N_R", new Vector3(10.875f, 2f, 12.5f), new Vector3(19.25f, 4f, 1f)),

        // Kitchen | Den (gap at Z = 4)
        new Box("W_KD_S", new Vector3(-12f, 2f, -0.625f), new Vector3(1f, 4f, 6.75f)),
        new Box("W_KD_N", new Vector3(-12f, 2f, 8.625f), new Vector3(1f, 4f, 6.75f)),

        // Bathroom | Den (gap at Z = 7)
        new Box("W_BD_S", new Vector3(12f, 2f, 3.875f), new Vector3(1f, 4f, 3.75f)),
        new Box("W_BD_N", new Vector3(12f, 2f, 10.125f), new Vector3(1f, 4f, 3.75f)),

        // Utility | Den (gap at Z = -1)
        new Box("W_UD_S", new Vector3(12f, 2f, -3.125f), new Vector3(1f, 4f, 1.75f)),
        new Box("W_UD_N", new Vector3(12f, 2f, 1.125f), new Vector3(1f, 4f, 1.75f)),

        // Bathroom | Utility (solid - the bathroom is reachable only from the den)
        new Box("W_BU", new Vector3(16f, 2f, 2f), new Vector3(8f, 4f, 1f)),

        // Hallway | Den + Kitchen (gaps at X = 0 den door, X = -16 kitchen service door)
        new Box("W_H1", new Vector3(-18.625f, 2f, -4f), new Vector3(2.75f, 4f, 1f)),
        new Box("W_H2", new Vector3(-8f, 2f, -4f), new Vector3(13.5f, 4f, 1f)),
        new Box("W_H3", new Vector3(10.625f, 2f, -4f), new Vector3(18.75f, 4f, 1f)),

        // Hallway | Bedroom + Garage (gaps at X = -12 bedroom door, X = +8 garage door)
        new Box("W_H4", new Vector3(-16.625f, 2f, -8f), new Vector3(6.75f, 4f, 1f)),
        new Box("W_H5", new Vector3(-2f, 2f, -8f), new Vector3(17.5f, 4f, 1f)),
        new Box("W_H6", new Vector3(14.625f, 2f, -8f), new Vector3(10.75f, 4f, 1f)),

        // Bedroom | Garage (solid)
        new Box("W_BG", new Vector3(-4f, 2f, -14f), new Vector3(1f, 4f, 12f)),

        // The Screaming Closet (three-sided box inside the bedroom, opening faces east)
        new Box("Closet_N", new Vector3(-18f, 2f, -11.5f), new Vector3(4f, 4f, 0.5f)),
        new Box("Closet_S", new Vector3(-18f, 2f, -16.5f), new Vector3(4f, 4f, 0.5f)),
        new Box("Closet_W", new Vector3(-19.75f, 2f, -14f), new Vector3(0.5f, 4f, 5.5f))
    };

    // ------------------------- Yard fence (GDD 3.3) -------------------------
    // 2.2 high, 0.4 thick; the north run splits around the cellar-door gap
    // (3.5 wide at X = 0).

    public static readonly Box[] Fence =
    {
        new Box("Fence_W", new Vector3(-14.2f, 1.1f, 20f), new Vector3(0.4f, 2.2f, 17f)),
        new Box("Fence_E", new Vector3(14.2f, 1.1f, 20f), new Vector3(0.4f, 2.2f, 17f)),
        new Box("Fence_N_L", new Vector3(-7.875f, 1.1f, 28.2f), new Vector3(12.25f, 2.2f, 0.4f)),
        new Box("Fence_N_R", new Vector3(7.875f, 1.1f, 28.2f), new Vector3(12.25f, 2.2f, 0.4f))
    };

    // ------------------------- Spawns and stations (GDD 3.4) -------------------------

    /// <summary>Eight survivor spawns around the den couch - the lobby IS the den.</summary>
    public static readonly Vector3[] SurvivorSpawns =
    {
        new Vector3(-2f, 0.1f, 2f),
        new Vector3(2f, 0.1f, 2f),
        new Vector3(-4f, 0.1f, 5f),
        new Vector3(4f, 0.1f, 5f),
        new Vector3(0f, 0.1f, 7f),
        new Vector3(-2f, 0.1f, 9f),
        new Vector3(2f, 0.1f, 9f),
        new Vector3(0f, 0.1f, 4f)
    };

    /// <summary>Garage center - two doorways from anywhere the survivors spawn.</summary>
    public static readonly Vector3 MonsterSpawn = new Vector3(10f, 0.1f, -16f);

    public static readonly Vector3 ScreamStation = new Vector3(-18f, 0.5f, -14f);
    public static readonly Vector3 KaraokeStation = new Vector3(-9f, 0.5f, 9f);
    public static readonly Vector3 DancePad = new Vector3(7f, 0.05f, 7.5f);
    public static readonly Vector3 NoodleStove = new Vector3(-16f, 0.9f, 8f);
    public static readonly Vector3 ToiletStation = new Vector3(16.5f, 0.5f, 9.5f);
    public static readonly Vector3 ChickenStart = new Vector3(0f, 0.4f, 20f);
    public static readonly Vector3 CellarDoor = new Vector3(0f, 0.6f, 27.5f);

    /// <summary>Lobby seating and the Mimic's favorite lie. Faces +Z, toward the TV wall.</summary>
    public static readonly Vector3 Couch = new Vector3(0f, 0.4f, 2.5f);

    /// <summary>
    /// The den hearth. GDD 3.4 centers it at X = 0, but that slot is also the
    /// back-door gap in the north wall (GDD 3.3) - the only house-to-yard
    /// opening, which the chicken task and the finale sprint both need
    /// walkable. The hearth therefore sits east of the doorway, mirroring the
    /// TV across it; everything else about it (Z, height, light, flicker,
    /// house-tell dim) follows the spec.
    /// </summary>
    public static readonly Vector3 Fireplace = new Vector3(6f, 0.8f, 11.4f);

    public static readonly Vector3 Tv = new Vector3(-6f, 1f, 11.4f);

    // ------------------------- Rooms (GDD 3.2) -------------------------

    public static readonly RoomDef[] Rooms =
    {
        new RoomDef("Den", new Rect(-12f, -4f, 24f, 16f), new Vector3(0f, 0f, 4f)),
        new RoomDef("Kitchen", new Rect(-20f, -4f, 8f, 16f), new Vector3(-16f, 0f, 4f)),
        new RoomDef("Bathroom", new Rect(12f, 2f, 8f, 10f), new Vector3(16f, 0f, 7f)),
        new RoomDef("UtilityNook", new Rect(12f, -4f, 8f, 6f), new Vector3(16f, 0f, -1f)),
        new RoomDef("Hallway", new Rect(-20f, -8f, 40f, 4f), new Vector3(0f, 0f, -6f)),
        new RoomDef("Bedroom", new Rect(-20f, -20f, 16f, 12f), new Vector3(-12f, 0f, -14f)),
        new RoomDef("Garage", new Rect(-4f, -20f, 24f, 12f), new Vector3(8f, 0f, -14f)),
        new RoomDef("Backyard", new Rect(-14f, 12f, 28f, 16f), new Vector3(0f, 0f, 20f))
    };

    // ------------------------- Waypoint graph (GDD section 10) -------------------------
    // Hand-authored: one node per doorway plus 2-4 per room. Every segment
    // between connected nodes is walkable by a standing character - no edge
    // crosses a wall, the couch, or any furniture collider.

    /// <summary>Node positions at floor level. Indices are referenced by <see cref="WaypointEdges"/>.</summary>
    public static readonly Vector3[] WaypointNodes =
    {
        // Doorway nodes (0..7)
        new Vector3(-12f, 0f, 4f),    //  0 Kitchen | Den door
        new Vector3(12f, 0f, 7f),     //  1 Bathroom | Den door
        new Vector3(12f, 0f, -1f),    //  2 Utility | Den door
        new Vector3(0f, 0f, -4f),     //  3 Den | Hallway door
        new Vector3(-16f, 0f, -4f),   //  4 Kitchen | Hallway service door
        new Vector3(-12f, 0f, -8f),   //  5 Hallway | Bedroom door
        new Vector3(8f, 0f, -8f),     //  6 Hallway | Garage door
        new Vector3(0f, 0f, 12.5f),   //  7 Back door (Den | Yard)

        // Den (8..11)
        new Vector3(-8f, 0f, 8f),     //  8 karaoke corner
        new Vector3(7f, 0f, 7.5f),    //  9 dance pad
        new Vector3(0f, 0f, 9f),      // 10 north-center, in front of the TV wall
        new Vector3(4f, 0f, 0f),      // 11 south-center, east of the couch

        // Kitchen (12..13)
        new Vector3(-16f, 0f, 8f),    // 12 stove
        new Vector3(-16f, 0f, 0f),    // 13 south kitchen

        // Bathroom (14..15)
        new Vector3(16f, 0f, 9.5f),   // 14 toilet
        new Vector3(14.5f, 0f, 5f),   // 15 entry corner

        // Utility nook (16)
        new Vector3(16f, 0f, -1f),    // 16 nook center (Mimic ambush pocket)

        // Hallway spine (17..19)
        new Vector3(-14f, 0f, -6f),   // 17 west
        new Vector3(0f, 0f, -6f),     // 18 center
        new Vector3(10f, 0f, -6f),    // 19 east

        // Bedroom (20..21)
        new Vector3(-12f, 0f, -14f),  // 20 center
        new Vector3(-16.5f, 0f, -14f),// 21 Screaming Closet mouth

        // Garage (22..23)
        new Vector3(8f, 0f, -14f),    // 22 center
        new Vector3(13f, 0f, -16f),   // 23 lair corner (monster spawn side)

        // Backyard (24..27)
        new Vector3(0f, 0f, 15f),     // 24 porch, just outside the back door
        new Vector3(-8f, 0f, 20f),    // 25 west yard
        new Vector3(8f, 0f, 20f),     // 26 east yard
        new Vector3(0f, 0f, 25f)      // 27 cellar-door approach
    };

    /// <summary>
    /// Undirected edges: each entry is a two-element pair { a, b } connecting
    /// WaypointNodes[a] and WaypointNodes[b]. Consumers that want adjacency
    /// lists mirror each pair in both directions.
    /// </summary>
    public static readonly int[][] WaypointEdges =
    {
        // Kitchen door
        new[] { 0, 8 }, new[] { 0, 11 }, new[] { 0, 12 }, new[] { 0, 13 },
        // Bathroom door
        new[] { 1, 9 }, new[] { 1, 10 }, new[] { 1, 14 }, new[] { 1, 15 },
        // Utility door
        new[] { 2, 9 }, new[] { 2, 11 }, new[] { 2, 16 },
        // Den | hallway door
        new[] { 3, 11 }, new[] { 3, 18 },
        // Kitchen service door
        new[] { 4, 13 }, new[] { 4, 17 },
        // Bedroom door
        new[] { 5, 17 }, new[] { 5, 20 },
        // Garage door
        new[] { 6, 19 }, new[] { 6, 22 },
        // Back door
        new[] { 7, 10 }, new[] { 7, 24 },
        // Den interior (routes skirt the couch at (0, 2.5))
        new[] { 8, 9 }, new[] { 8, 10 },
        new[] { 9, 10 }, new[] { 9, 11 },
        new[] { 10, 11 },
        // Kitchen interior
        new[] { 12, 13 },
        // Bathroom interior
        new[] { 14, 15 },
        // Hallway spine
        new[] { 17, 18 }, new[] { 18, 19 },
        // Bedroom interior
        new[] { 20, 21 },
        // Garage interior
        new[] { 22, 23 },
        // Yard
        new[] { 24, 25 }, new[] { 24, 26 }, new[] { 24, 27 },
        new[] { 25, 26 }, new[] { 25, 27 }, new[] { 26, 27 }
    };
}
