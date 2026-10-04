namespace FeudalSim.Sim.World;

/// <summary>The player's body pose as last reported by the client (saved and hashed; the character itself is a Person row, M1-04b).</summary>
public struct PlayerState
{
    public bool Present;
    public float X, Z, Yaw;
}
