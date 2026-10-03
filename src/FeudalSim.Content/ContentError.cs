namespace FeudalSim.Content;

/// <summary>A content problem with a location designers can jump to.</summary>
public sealed record ContentError(string File, long Line, long Column, string Message)
{
    public override string ToString() => $"{File}:{Line}:{Column}: {Message}";
}
