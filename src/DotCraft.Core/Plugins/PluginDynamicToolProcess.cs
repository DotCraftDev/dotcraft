using System.Diagnostics;
using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Plugins;

/// <summary>
/// Manages stdio JSON-RPC processes used by plugin dynamic tools.
/// </summary>
public sealed class PluginDynamicToolProcessManager : IAsyncDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, PluginDynamicToolProcessRuntime> _runtimes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Invokes a plugin dynamic tool through its configured external process.
    /// </summary>
    public async ValueTask<PluginFunctionInvocationResult> InvokeAsync(
        PluginManifest manifest,
        PluginManifestProcess process,
        string toolName,
        PluginFunctionInvocationContext context,
        CancellationToken cancellationToken)
    {
        var runtime = GetRuntime(manifest, process, context.Execution.WorkspacePath);
        return await runtime.InvokeAsync(manifest, process, toolName, context, cancellationToken);
    }

    private PluginDynamicToolProcessRuntime GetRuntime(
        PluginManifest manifest,
        PluginManifestProcess process,
        string workspaceRoot)
    {
        var key = manifest.ManifestPath + "\u001f" + process.Id + "\u001f" + Path.GetFullPath(workspaceRoot);
        lock (_lock)
        {
            if (!_runtimes.TryGetValue(key, out var runtime))
            {
                runtime = new PluginDynamicToolProcessRuntime(workspaceRoot);
                _runtimes[key] = runtime;
            }

            return runtime;
        }
    }

    public async ValueTask DisposeAsync()
    {
        PluginDynamicToolProcessRuntime[] runtimes;
        lock (_lock)
        {
            runtimes = _runtimes.Values.ToArray();
            _runtimes.Clear();
        }

        foreach (var runtime in runtimes)
            await runtime.DisposeAsync();
    }
}

internal sealed class PluginDynamicToolProcessInvoker(
    PluginDynamicToolProcessManager manager,
    PluginManifest manifest,
    PluginManifestProcess process,
    string toolName) : IPluginFunctionInvoker
{
    public ValueTask<PluginFunctionInvocationResult> InvokeAsync(
        PluginFunctionInvocationContext context,
        CancellationToken cancellationToken)
        => manager.InvokeAsync(manifest, process, toolName, context, cancellationToken);
}

internal sealed class PluginDynamicToolProcessRuntime(string workspaceRoot) : IAsyncDisposable
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _process;
    private StdioTransport? _transport;
    private Task? _stderrForwarder;

    public async ValueTask<PluginFunctionInvocationResult> InvokeAsync(
        PluginManifest manifest,
        PluginManifestProcess process,
        string toolName,
        PluginFunctionInvocationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var transport = await EnsureStartedAsync(manifest, process, cancellationToken);
            var response = await transport.SendClientRequestAsync(
                "plugin/toolCall",
                new
                {
                    pluginId = manifest.Id,
                    pluginRoot = manifest.RootPath,
                    workspaceRoot = context.Execution.WorkspacePath,
                    threadId = context.Execution.ThreadId,
                    turnId = context.Execution.TurnId,
                    callId = context.CallId,
                    tool = toolName,
                    @namespace = context.Descriptor.Namespace,
                    arguments = context.Arguments
                },
                cancellationToken,
                TimeoutFromSeconds(process.ToolTimeoutSeconds, TimeSpan.FromSeconds(20)));
            return ParseToolResponse(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RestartAfterFailureAsync();
            return PluginFunctionInvocationResult.Failed(
                "PluginDynamicToolTimeout",
                $"Plugin dynamic tool '{toolName}' timed out while waiting for process '{process.Id}'.");
        }
        catch (Exception ex)
        {
            await RestartAfterFailureAsync();
            return PluginFunctionInvocationResult.Failed(
                "PluginDynamicToolFailed",
                $"Plugin dynamic tool '{toolName}' failed: {ex.Message}");
        }
    }

    private async Task<StdioTransport> EnsureStartedAsync(
        PluginManifest manifest,
        PluginManifestProcess process,
        CancellationToken cancellationToken)
    {
        if (_transport != null && _process is { HasExited: false })
            return _transport;

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (_transport != null && _process is { HasExited: false })
                return _transport;

            await DisposeRuntimeAsync();
            var startInfo = CreateStartInfo(manifest, process);
            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start plugin process '{process.Id}'.");

            _transport = StdioTransport.Create(
                _process.StandardOutput.BaseStream,
                _process.StandardInput.BaseStream);
            _transport.Start();
            _stderrForwarder = ForwardStderrAsync(_process, process.Id);

            var response = await _transport.SendClientRequestAsync(
                "plugin/initialize",
                new
                {
                    pluginId = manifest.Id,
                    pluginRoot = manifest.RootPath,
                    workspaceRoot,
                    tools = manifest.Functions
                        .Where(function =>
                            function.Backend.Kind.Equals("process", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(function.Backend.ProcessId, process.Id, StringComparison.OrdinalIgnoreCase))
                        .Select(function => new
                        {
                            @namespace = function.Namespace,
                            name = function.Backend.ToolName ?? function.Name,
                            functionName = function.Name,
                            description = function.Description,
                            inputSchema = function.InputSchema,
                            outputSchema = function.OutputSchema,
                            deferLoading = function.DeferLoading,
                            requiresChatContext = function.RequiresChatContext
                        })
                        .ToArray()
                },
                cancellationToken,
                TimeoutFromSeconds(process.StartupTimeoutSeconds, TimeSpan.FromSeconds(10)));

            if (response.Error is { } error)
                throw new InvalidOperationException($"Initialize failed: {error.GetRawText()}");
            if (InitializeReportedFailure(response, out var initializeError))
                throw new InvalidOperationException(initializeError);

            return _transport;
        }
        finally
        {
            _startLock.Release();
        }
    }

    private static ProcessStartInfo CreateStartInfo(PluginManifest manifest, PluginManifestProcess process)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveCommand(manifest.RootPath, process.Command),
            WorkingDirectory = ResolveWorkingDirectory(manifest.RootPath, process.WorkingDirectory),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in process.Args)
            startInfo.ArgumentList.Add(ResolveArgument(manifest.RootPath, arg));

        foreach (var (key, value) in process.Env)
            startInfo.Environment[key] = value;

        return startInfo;
    }

    private static string ResolveCommand(string pluginRoot, string command)
        => command.StartsWith("./", StringComparison.Ordinal)
            ? Path.GetFullPath(Path.Combine(pluginRoot, command[2..]))
            : command;

    private static string ResolveArgument(string pluginRoot, string arg)
        => arg.StartsWith("./", StringComparison.Ordinal)
            ? Path.GetFullPath(Path.Combine(pluginRoot, arg[2..]))
            : arg;

    private static string ResolveWorkingDirectory(string pluginRoot, string? workingDirectory)
        => string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetFullPath(pluginRoot)
            : Path.GetFullPath(Path.Combine(pluginRoot, workingDirectory[2..]));

    private static TimeSpan TimeoutFromSeconds(double? seconds, TimeSpan fallback)
        => seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : fallback;

    private static bool InitializeReportedFailure(
        AppServerIncomingMessage response,
        out string error)
    {
        error = string.Empty;
        if (response.Result is not { ValueKind: JsonValueKind.Object } result
            || !result.TryGetProperty("success", out var success)
            || success.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        string? errorCode = null;
        string? errorMessage = null;
        if (result.TryGetProperty("errorCode", out var codeElement) && codeElement.ValueKind == JsonValueKind.String)
            errorCode = codeElement.GetString();
        if (result.TryGetProperty("errorMessage", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            errorMessage = messageElement.GetString();

        error = "Initialize failed";
        if (!string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(errorMessage))
            error += $": {errorCode ?? "PluginDynamicToolInitializeFailed"} {errorMessage}".TrimEnd();

        return true;
    }

    private static PluginFunctionInvocationResult ParseToolResponse(AppServerIncomingMessage response)
    {
        if (response.Error is { } error)
        {
            return PluginFunctionInvocationResult.Failed(
                "PluginDynamicToolProtocolError",
                "Plugin process returned a JSON-RPC error: " + error.GetRawText());
        }

        if (response.Result is not { } result || result.ValueKind != JsonValueKind.Object)
        {
            return PluginFunctionInvocationResult.Failed(
                "PluginDynamicToolProtocolViolation",
                "Plugin process returned an invalid tool response payload.");
        }

        var parsed = JsonSerializer.Deserialize<PluginFunctionInvocationResult>(
            result.GetRawText(),
            SessionWireJsonOptions.Default);
        if (parsed == null)
        {
            return PluginFunctionInvocationResult.Failed(
                "PluginDynamicToolProtocolViolation",
                "Plugin process returned an empty tool response payload.");
        }

        if (!parsed.Success && string.IsNullOrWhiteSpace(parsed.ErrorCode))
            parsed = parsed with { ErrorCode = "PluginDynamicToolFailed" };
        if (!parsed.Success && string.IsNullOrWhiteSpace(parsed.ErrorMessage))
            parsed = parsed with { ErrorMessage = "Plugin process reported a failed tool call." };

        return parsed;
    }

    private async Task RestartAfterFailureAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            await DisposeRuntimeAsync();
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task DisposeRuntimeAsync()
    {
        if (_transport != null)
        {
            try { await _transport.DisposeAsync(); }
            catch { }
            _transport = null;
        }

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch { }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        if (_stderrForwarder != null)
        {
            try { await _stderrForwarder; }
            catch { }
            _stderrForwarder = null;
        }
    }

    private static async Task ForwardStderrAsync(Process process, string processId)
    {
        try
        {
            while (!process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line == null)
                    break;
                await Console.Error.WriteLineAsync($"[Plugin:{processId}] {line}");
            }
        }
        catch
        {
            // Best-effort diagnostics only.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            await DisposeRuntimeAsync();
        }
        finally
        {
            _startLock.Release();
            _startLock.Dispose();
        }
    }
}
