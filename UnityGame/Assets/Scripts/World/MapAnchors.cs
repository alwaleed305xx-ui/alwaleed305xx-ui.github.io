using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The placeholder contract's attachment points (GDD section 13): one named
/// empty child Transform per future art asset. Asset packs drop their meshes
/// onto these anchors without ever touching triggers, spawns or scripts.
///
/// HouseFactory creates the GameObject and its children; this component only
/// resolves names. Multi-instance anchors are numbered from 1
/// ("ArmchairAnchor1", "GarageJunkAnchor2", ...).
/// </summary>
public class MapAnchors : MonoBehaviour
{
    public static MapAnchors Instance { get; private set; }

    readonly Dictionary<string, Transform> lookup = new Dictionary<string, Transform>();
    bool built;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// The anchor Transform for a GDD section 13 name (e.g. "FireplaceAnchor"),
    /// or null (with one logged warning) when no child carries that name.
    /// </summary>
    public Transform Get(string anchorName)
    {
        if (string.IsNullOrEmpty(anchorName)) return null;

        if (!built) BuildLookup();

        if (lookup.TryGetValue(anchorName, out Transform anchor) && anchor != null)
            return anchor;

        // Children may have been added after the first lookup (editor tooling,
        // late factories) - rebuild once before giving up.
        BuildLookup();
        if (lookup.TryGetValue(anchorName, out anchor) && anchor != null)
            return anchor;

        Debug.LogWarning($"MapAnchors: no anchor named '{anchorName}'.", this);
        return null;
    }

    void BuildLookup()
    {
        lookup.Clear();
        foreach (Transform child in transform)
        {
            if (!lookup.ContainsKey(child.name))
                lookup.Add(child.name, child);
        }
        built = true;
    }
}
