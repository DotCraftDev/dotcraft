using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Wraps an <see cref="AIFunction"/> to normalize empty results and enforce per-tool result size limits
/// (spill-to-disk with preview when exceeded). Intended as the outermost wrapper so hooks see full results.
/// </summary>
internal sealed class ResultSizeLimitingFunction : DelegatingAIFunction
{
    private readonly int _maxResultChars;
    private readonly string _workspacePath;
    private readonly int _previewLines;

    public ResultSizeLimitingFunction(
        AIFunction innerFunction,
        int maxResultChars,
        string workspacePath,
        int previewLines)
        : base(innerFunction)
    {
        _maxResultChars = maxResultChars;
        _workspacePath = workspacePath;
        _previewLines = previewLines;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var result = await base.InvokeCoreAsync(arguments, cancellationToken);
        var sessionId = TracingChatClient.CurrentSessionKey;

        return ToolResultProcessor.Process(
            InnerFunction.Name,
            result,
            _maxResultChars,
            _workspacePath,
            sessionId,
            _previewLines);
    }
}
