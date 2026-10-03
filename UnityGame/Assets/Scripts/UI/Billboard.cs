using UnityEngine;

/// <summary>يخلي اللافتات فوق المهام تواجه الكاميرا دايماً.</summary>
public class Billboard : MonoBehaviour
{
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
