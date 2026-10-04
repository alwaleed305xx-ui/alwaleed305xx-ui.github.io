using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps world-space uGUI labels readable across scene saves. Fonts created
/// with Font.CreateDynamicFontFromOSFont are runtime-only objects: when the
/// editor wizard builds a label and saves the scene, the serialized font
/// reference dies and the Text renders nothing on the next boot.
/// <see cref="TaskFactory.BuildWorldLabel"/> attaches this to every world
/// label (station signs, door signs, the closet sign) so each one re-acquires
/// the font in Awake - including labels whose GameObject has no richer owner
/// component to do it for them.
/// </summary>
[RequireComponent(typeof(Text))]
public class WorldLabelFont : MonoBehaviour
{
    void Awake()
    {
        Text label = GetComponent<Text>();
        if (label != null && label.font == null)
            label.font = TaskFactory.RuntimeFont();
    }
}
