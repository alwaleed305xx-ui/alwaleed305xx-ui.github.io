using UnityEngine;

/// <summary>
/// Keeps world-space labels (name tags, task station signs) facing the active
/// camera at all times. Attach to any floating text or sign root.
/// </summary>
public class Billboard : MonoBehaviour
{
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
