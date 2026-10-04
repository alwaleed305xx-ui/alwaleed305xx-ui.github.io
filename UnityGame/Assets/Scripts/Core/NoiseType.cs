/// <summary>
/// Every category of racket a player (or the house itself) can make.
/// The monster's HUD draws a differently shaped ping per type, so the
/// monster reads WHAT it hears, not just where it came from.
/// <see cref="NoiseType.DeathScream"/> is special: it is always visible
/// in the world (rings, self-noise meter) but is NEVER relayed to the
/// monster - silence after a kill is the rule (GDD 9.8).
/// </summary>
public enum NoiseType : byte
{
    Footsteps,
    Scream,
    Music,
    Alarm,
    Chicken,
    Plumbing,
    Cooking,
    Slap,
    Boo,
    DeathScream
}
