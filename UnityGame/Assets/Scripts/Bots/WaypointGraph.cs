using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The hand-authored navigation graph the bots walk on: ~25 nodes (one per
/// doorway plus a few per room, data from HouseLayout) with A* over them.
/// Deliberately not a NavMesh - future furniture clutter can never break a
/// graph that only knows doorways and room centers (GDD 10).
///
/// BotFactory feeds the graph via <see cref="SetGraph"/> while the wizard
/// builds the scene; the data is kept in flat serialized arrays (jagged
/// arrays do not survive Unity serialization) so the saved scene ships it.
/// If the scene somehow loads without data, the graph lazily re-reads
/// HouseLayout at runtime.
/// </summary>
public class WaypointGraph : MonoBehaviour
{
    public static WaypointGraph Instance { get; private set; }

    // Serialized flat: node positions plus one (from, to) pair per edge.
    [SerializeField] Vector3[] nodePositions = new Vector3[0];
    [SerializeField] int[] edgeFrom = new int[0];
    [SerializeField] int[] edgeTo = new int[0];

    // Runtime adjacency, rebuilt from the flat arrays on first use.
    List<int>[] adjacency;

    public int NodeCount => nodePositions != null ? nodePositions.Length : 0;

    void Awake()
    {
        Instance = this;
        EnsureGraph();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>World position of a graph node (bounds-checked).</summary>
    public Vector3 NodePosition(int index)
    {
        if (nodePositions == null || index < 0 || index >= nodePositions.Length)
            return Vector3.zero;
        return nodePositions[index];
    }

    // ------------------------- Graph authoring -------------------------

    /// <summary>
    /// Installs the node/edge data. <paramref name="edges"/> accepts either
    /// convention a layout author might reasonably use:
    /// - adjacency lists: edges.Length == nodes.Length, edges[i] = neighbors of node i;
    /// - edge list: each inner array is an edge pair (or a longer chain whose
    ///   consecutive entries are connected).
    /// The graph is stored undirected either way.
    /// </summary>
    public void SetGraph(Vector3[] nodes, int[][] edges)
    {
        nodePositions = nodes != null ? (Vector3[])nodes.Clone() : new Vector3[0];

        var from = new List<int>();
        var to = new List<int>();

        if (edges != null && nodePositions.Length > 0)
        {
            bool adjacencyShape = edges.Length == nodePositions.Length;
            for (int i = 0; i < edges.Length; i++)
            {
                int[] inner = edges[i];
                if (inner == null) continue;

                if (adjacencyShape)
                {
                    foreach (int neighbor in inner)
                        AddEdge(from, to, i, neighbor);
                }
                else
                {
                    for (int k = 0; k + 1 < inner.Length; k++)
                        AddEdge(from, to, inner[k], inner[k + 1]);
                }
            }
        }

        edgeFrom = from.ToArray();
        edgeTo = to.ToArray();
        adjacency = null; // rebuilt lazily
        BuildAdjacency();
    }

    void AddEdge(List<int> from, List<int> to, int a, int b)
    {
        if (a == b) return;
        if (a < 0 || b < 0 || a >= nodePositions.Length || b >= nodePositions.Length) return;

        // Dedupe both directions; the adjacency build mirrors every edge.
        for (int i = 0; i < from.Count; i++)
            if ((from[i] == a && to[i] == b) || (from[i] == b && to[i] == a))
                return;

        from.Add(a);
        to.Add(b);
    }

    void EnsureGraph()
    {
        if (NodeCount == 0)
            SetGraph(HouseLayout.WaypointNodes, HouseLayout.WaypointEdges);
        else if (adjacency == null)
            BuildAdjacency();
    }

    void BuildAdjacency()
    {
        adjacency = new List<int>[NodeCount];
        for (int i = 0; i < adjacency.Length; i++)
            adjacency[i] = new List<int>(4);

        if (edgeFrom == null || edgeTo == null) return;
        int count = Mathf.Min(edgeFrom.Length, edgeTo.Length);
        for (int i = 0; i < count; i++)
        {
            int a = edgeFrom[i];
            int b = edgeTo[i];
            if (a < 0 || b < 0 || a >= NodeCount || b >= NodeCount || a == b) continue;
            if (!adjacency[a].Contains(b)) adjacency[a].Add(b);
            if (!adjacency[b].Contains(a)) adjacency[b].Add(a);
        }
    }

    // ------------------------- Queries -------------------------

    /// <summary>Index of the graph node nearest to a world position; -1 when the graph is empty.</summary>
    public int NearestNode(Vector3 position)
    {
        EnsureGraph();
        int best = -1;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < NodeCount; i++)
        {
            float sqr = (nodePositions[i] - position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// A* route from one world position to another. Returns the node positions
    /// to walk through, ending with the exact destination. Never returns null;
    /// with no usable graph (or no route) it returns the destination alone and
    /// the bot walks straight at it.
    /// </summary>
    public List<Vector3> FindPath(Vector3 from, Vector3 to)
    {
        EnsureGraph();
        var path = new List<Vector3>();

        int start = NearestNode(from);
        int goal = NearestNode(to);
        if (start < 0 || goal < 0 || start == goal)
        {
            path.Add(to);
            return path;
        }

        List<int> nodeRoute = AStar(start, goal);
        if (nodeRoute == null)
        {
            path.Add(to);
            return path;
        }

        foreach (int index in nodeRoute)
            path.Add(nodePositions[index]);

        // Drop a first node that would mean walking backward: if the second
        // waypoint is already as close as the first, skip straight to it.
        if (path.Count >= 2 &&
            Vector3.Distance(from, path[1]) <= Vector3.Distance(from, path[0]) + 0.5f)
            path.RemoveAt(0);

        // The final node is near the destination, not on it.
        if (path.Count == 0 || (path[path.Count - 1] - to).sqrMagnitude > 0.04f)
            path.Add(to);

        return path;
    }

    List<int> AStar(int start, int goal)
    {
        int n = NodeCount;
        var gScore = new float[n];
        var fScore = new float[n];
        var cameFrom = new int[n];
        var closed = new bool[n];
        var inOpen = new bool[n];
        var open = new List<int>(n);

        for (int i = 0; i < n; i++)
        {
            gScore[i] = float.MaxValue;
            fScore[i] = float.MaxValue;
            cameFrom[i] = -1;
        }

        gScore[start] = 0f;
        fScore[start] = Heuristic(start, goal);
        open.Add(start);
        inOpen[start] = true;

        while (open.Count > 0)
        {
            // ~25 nodes: a linear min-scan beats any heap bookkeeping.
            int current = open[0];
            for (int i = 1; i < open.Count; i++)
                if (fScore[open[i]] < fScore[current])
                    current = open[i];

            if (current == goal)
                return Reconstruct(cameFrom, goal);

            open.Remove(current);
            inOpen[current] = false;
            closed[current] = true;

            foreach (int neighbor in adjacency[current])
            {
                if (closed[neighbor]) continue;

                float tentative = gScore[current] + Heuristic(current, neighbor);
                if (tentative >= gScore[neighbor]) continue;

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentative;
                fScore[neighbor] = tentative + Heuristic(neighbor, goal);
                if (!inOpen[neighbor])
                {
                    open.Add(neighbor);
                    inOpen[neighbor] = true;
                }
            }
        }
        return null; // disconnected
    }

    float Heuristic(int a, int b) => Vector3.Distance(nodePositions[a], nodePositions[b]);

    static List<int> Reconstruct(int[] cameFrom, int goal)
    {
        var route = new List<int>();
        for (int node = goal; node >= 0; node = cameFrom[node])
            route.Add(node);
        route.Reverse();
        return route;
    }

    // ------------------------- Editor aid -------------------------

    void OnDrawGizmosSelected()
    {
        if (nodePositions == null) return;

        Gizmos.color = ScreamerPalette.GhostMint;
        foreach (Vector3 node in nodePositions)
            Gizmos.DrawWireSphere(node + Vector3.up * 0.2f, 0.3f);

        if (edgeFrom == null || edgeTo == null) return;
        Gizmos.color = ScreamerPalette.HauntedTeal;
        int count = Mathf.Min(edgeFrom.Length, edgeTo.Length);
        for (int i = 0; i < count; i++)
        {
            if (edgeFrom[i] < 0 || edgeTo[i] < 0 ||
                edgeFrom[i] >= nodePositions.Length || edgeTo[i] >= nodePositions.Length) continue;
            Gizmos.DrawLine(nodePositions[edgeFrom[i]] + Vector3.up * 0.2f,
                            nodePositions[edgeTo[i]] + Vector3.up * 0.2f);
        }
    }
}
