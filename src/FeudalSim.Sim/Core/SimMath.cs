namespace FeudalSim.Sim.Core;

/// <summary>
/// Transcendental functions for state-affecting code (20 §8.4). Delegates to <see cref="MathF"/> today; can switch to
/// portable software implementations if cross-platform replay is ever wanted. Sqrt and basic arithmetic are IEEE-exact.
/// </summary>
public static class SimMath
{
    public static float Sin(float x) => MathF.Sin(x);
    public static float Cos(float x) => MathF.Cos(x);
    public static float Exp(float x) => MathF.Exp(x);
    public static float Log(float x) => MathF.Log(x);
    public static float Pow(float x, float y) => MathF.Pow(x, y);
    public static float Tanh(float x) => MathF.Tanh(x);
    public static float Atan2(float y, float x) => MathF.Atan2(y, x);
}
