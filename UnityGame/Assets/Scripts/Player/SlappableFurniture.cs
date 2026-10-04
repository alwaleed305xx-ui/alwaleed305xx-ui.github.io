using UnityEngine;

/// <summary>
/// Marker for innocent furniture a survivor can slap with [F]. HouseFactory places
/// this on the couch, armchairs, lamps, and the rest of the den dressing.
///
/// A slap on a marked prop emits a loudness 0.45 noise ping (you snitched on
/// yourself) and feeds the FURNITURE ABUSER award. The Mimic's prop proxy
/// intentionally does NOT carry this marker: the slap code checks for a disguised
/// MimicDisguise first and the context prompt reads the same for both, so the
/// prompt can never be used as a free Mimic detector.
/// </summary>
public class SlappableFurniture : MonoBehaviour
{
}
