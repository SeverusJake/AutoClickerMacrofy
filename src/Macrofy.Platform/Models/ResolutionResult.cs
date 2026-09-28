namespace Macrofy.Platform.Models;

public abstract record ResolutionResult
{
    public sealed record Missing : ResolutionResult;
    public sealed record Ambiguous(IReadOnlyList<TargetWindow> Candidates) : ResolutionResult;
    public sealed record Matched(TargetWindow Window) : ResolutionResult;
}
