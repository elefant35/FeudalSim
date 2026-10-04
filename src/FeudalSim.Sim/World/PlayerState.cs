namespace FeudalSim.Sim.World;

/// <summary>The player's body pose as last reported by the client (M0: the player is not yet a Person row).</summary>
public struct PlayerState
{
    public bool Present;
    public float X, Z, Yaw;
}
