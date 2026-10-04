using UnityEngine;

/// <summary>
/// The house is a snitch (GDD 6.4): when the monster is within 15 units of a
/// room's center, that room's practicals desaturate 15% toward HauntedTeal,
/// and the den hearth additionally dims 35%. No UI, no text - players learn
/// the house's tells, streamers narrate them.
///
/// Implementation: a 4 Hz poll of MonsterController.ActiveMonster against each
/// registered LightFlicker's room center (planar XZ distance). The flickers
/// smooth the target themselves, so the tell breathes in and out instead of
/// snapping.
/// </summary>
public class HouseTells : MonoBehaviour
{
    [Tooltip("Polls per second (GDD 6.4 specifies 4 Hz).")]
    public float pollHz = 4f;

    [Tooltip("Monster-to-room-center distance that trips the tell.")]
    public float tellRadius = 15f;

    [Tooltip("How far each practical shifts toward HauntedTeal, 0..1.")]
    public float desaturation = 0.15f;

    [Tooltip("Extra intensity dim for the den hearth, 0..1.")]
    public float fireplaceDim = 0.35f;

    float pollTimer;

    void Update()
    {
        pollTimer -= Time.deltaTime;
        if (pollTimer > 0f) return;
        pollTimer = pollHz > 0.01f ? 1f / pollHz : 0.25f;

        Poll();
    }

    void Poll()
    {
        MonsterController monster = MonsterController.ActiveMonster;
        float radiusSqr = tellRadius * tellRadius;

        for (int i = 0; i < LightFlicker.Active.Count; i++)
        {
            LightFlicker flicker = LightFlicker.Active[i];

            bool near = false;
            if (monster != null)
            {
                Vector3 monsterPos = monster.transform.position;
                float dx = monsterPos.x - flicker.roomCenter.x;
                float dz = monsterPos.z - flicker.roomCenter.z;
                near = dx * dx + dz * dz <= radiusSqr;
            }

            if (near)
                flicker.SetTellTarget(desaturation, flicker.isFireplace ? fireplaceDim : 0f);
            else
                flicker.SetTellTarget(0f, 0f);
        }
    }
}
