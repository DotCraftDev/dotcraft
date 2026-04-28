using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

public sealed class PreSamplingCompactionRuntimeContext
{
    public required Func<
        IReadOnlyList<ChatMessage>,
        CancellationToken,
        Task<IReadOnlyList<ChatMessage>?>> TryCompactAsync { get; init; }
}

public static class PreSamplingCompactionRuntimeScope
{
    private static readonly AsyncLocal<PreSamplingCompactionRuntimeContext?> CurrentContext = new();

    public static PreSamplingCompactionRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(PreSamplingCompactionRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(PreSamplingCompactionRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
