using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Extensions;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.Settings;
using UnityEngine;

namespace DotCraft.Editor.Connection
{
    /// <summary>
    /// ACP client that manages the connection lifecycle with DotCraft agent.
    /// </summary>
    public sealed class AcpClient : IDisposable
    {
        private readonly DotCraftProcessManager _processManager;
        private readonly AcpTransportClient _transport;
        private readonly DotCraftSettings _settings;
        private readonly ExtensionMethodRouter _extensionRouter;

        private string _sessionId;
        private bool _isConnected;
        private bool _isRunning;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Current session ID.
        /// </summary>
        public string SessionId => _sessionId;

        /// <summary>
        /// Whether the client is connected to the agent.
        /// </summary>
        public bool IsConnected => _isConnected && _processManager.IsAlive;

        /// <summary>
        /// Whether a prompt is currently being processed.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Agent capabilities received during initialization.
        /// </summary>
        public AgentCapabilities AgentCapabilities { get; private set; }

        /// <summary>
        /// Agent info received during initialization.
        /// </summary>
        public AgentInfo AgentInfo { get; private set; }

        /// <summary>
        /// Whether the connected agent supports DotCraft's session delete extension.
        /// </summary>
        public bool SupportsSessionDelete => AgentCapabilities?.Meta?.DotCraft?.SessionDelete == true;

        /// <summary>
        /// Event raised when a session update is received.
        /// </summary>
        public event Action<AcpSessionUpdate> OnSessionUpdate;

        /// <summary>
        /// Event raised when a permission request is received.
        /// </summary>
        public event Action<RequestPermissionParams, Action<RequestPermissionResult>> OnPermissionRequest;

        /// <summary>
        /// Event raised when the connection state changes.
        /// </summary>
        public event Action<bool> OnConnectionStateChanged;

        /// <summary>
        /// Event raised when an error occurs.
        /// </summary>
        public event Action<string> OnError;

        /// <summary>
        /// Event raised when the process exits unexpectedly.
        /// </summary>
        public event Action OnProcessExited;

        /// <summary>
        /// Event raised when authentication is required.
        /// The handler should select an AuthMethod and call the callback with it.
        /// </summary>
        public event Action<AuthMethod[], Action<AuthMethod>> OnAuthenticationRequired;

        /// <summary>
        /// Config options (modes, models, etc.) from the session.
        /// </summary>
        public List<ConfigOption> ConfigOptions { get; private set; } = new();

        /// <summary>
        /// Available slash commands from the last session update.
        /// </summary>
        public List<AcpSlashCommand> AvailableCommands { get; private set; } = new();

        /// <summary>
        /// Event raised when config options are updated.
        /// </summary>
        public event Action<List<ConfigOption>> OnConfigOptionsUpdate;

        /// <summary>
        /// Event raised when available commands are updated.
        /// </summary>
        public event Action<List<AcpSlashCommand>> OnAvailableCommandsUpdate;

        public AcpClient(DotCraftSettings settings = null)
        {
            _settings = settings ?? DotCraftSettings.Instance;
            _processManager = new DotCraftProcessManager();
            _transport = new AcpTransportClient();
            _extensionRouter = new ExtensionMethodRouter();

            _processManager.OnProcessExited += HandleProcessExited;
            _processManager.OnErrorOutput += HandleErrorOutput;
            _transport.OnSessionUpdate += HandleSessionUpdate;
            _transport.OnError += HandleTransportError;
        }

        /// <summary>
        /// Connects to the DotCraft agent.
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            if (_isConnected)
            {
                Debug.LogWarning("[DotCraft] Already connected.");
                return true;
            }

            try
            {
                // Start the process
                if (!_processManager.Start(_settings))
                {
                    OnError?.Invoke("Failed to start DotCraft process.");
                    return false;
                }

                // Initialize transport
                _transport.Initialize(
                    _processManager.Process.StandardOutput.BaseStream,
                    _processManager.Process.StandardInput.BaseStream
                );

                // Register request handlers
                RegisterHandlers();

                // Start reading
                _transport.StartReaderLoop();

                // Send initialize request
                var initResult = await InitializeAsync(ct);
                if (initResult == null)
                {
                    await DisconnectAsync();
                    return false;
                }

                AgentCapabilities = initResult.AgentCapabilities;
                AgentInfo = initResult.AgentInfo;

                // Handle authentication if required
                if (initResult.AuthMethods != null && initResult.AuthMethods.Length > 0)
                {
                    var authResult = await HandleAuthenticationAsync(initResult.AuthMethods, ct);
                    if (!authResult)
                    {
                        await DisconnectAsync();
                        return false;
                    }
                }

                // Create new session
                var sessionResult = await NewSessionAsync(ct);
                if (sessionResult == null)
                {
                    await DisconnectAsync();
                    return false;
                }

                _sessionId = sessionResult.SessionId;
                ConfigOptions = sessionResult.ConfigOptions ?? new List<ConfigOption>();
                _isConnected = true;
                OnConnectionStateChanged?.Invoke(true);

                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"[DotCraft] Connected to {AgentInfo?.Name ?? "DotCraft"} (session: {_sessionId})");
                }

                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Connection failed: {ex.Message}");
                await DisconnectAsync();
                return false;
            }
        }

        /// <summary>
        /// Reconnects and loads an existing session.
        /// </summary>
        public async Task<bool> ReconnectAsync(string sessionId, CancellationToken ct = default)
        {
            if (_isConnected)
            {
                await DisconnectAsync();
            }

            try
            {
                // Start the process
                if (!_processManager.Start(_settings))
                {
                    return false;
                }

                // Initialize transport
                _transport.Initialize(
                    _processManager.Process.StandardOutput.BaseStream,
                    _processManager.Process.StandardInput.BaseStream
                );

                RegisterHandlers();
                _transport.StartReaderLoop();

                // Send initialize request
                var initResult = await InitializeAsync(ct);
                if (initResult == null)
                {
                    await DisconnectAsync();
                    return false;
                }

                AgentCapabilities = initResult.AgentCapabilities;
                AgentInfo = initResult.AgentInfo;

                // Load existing session
                if (!string.IsNullOrEmpty(sessionId) && AgentCapabilities.LoadSession)
                {
                    var loadResult = await LoadSessionAsync(sessionId, ct);
                    if (loadResult == null)
                    {
                        await DisconnectAsync();
                        return false;
                    }
                    _sessionId = loadResult.SessionId;
                    ConfigOptions = loadResult.ConfigOptions ?? new List<ConfigOption>();
                }
                else
                {
                    // Create new session if load not supported
                    var sessionResult = await NewSessionAsync(ct);
                    if (sessionResult == null)
                    {
                        await DisconnectAsync();
                        return false;
                    }
                    _sessionId = sessionResult.SessionId;
                    ConfigOptions = sessionResult.ConfigOptions ?? new List<ConfigOption>();
                }

                _isConnected = true;
                OnConnectionStateChanged?.Invoke(true);

                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"[DotCraft] Reconnected (session: {_sessionId})");
                }

                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Reconnect failed: {ex.Message}");
                await DisconnectAsync();
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the agent.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (!_isConnected && !_processManager.IsAlive)
                return;

            _isConnected = false;

            try
            {
                // Cancel the reader loop token BEFORE stopping the process so it sees
                // cancellation before the EOF from the closed pipe, preventing the
                // spurious "Connection closed by agent." error on intentional disconnect.
                _transport.CancelReaderLoop();

                await _processManager.StopAsync();
                await _transport.StopReaderLoopAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotCraft] Error during disconnect: {ex.Message}");
            }

            _sessionId = null;
            OnConnectionStateChanged?.Invoke(false);
        }

        /// <summary>
        /// Sends a prompt to the agent.
        /// </summary>
        public async Task<bool> PromptAsync(List<AcpContentBlock> prompt, CancellationToken ct = default)
        {
            if (!_isConnected)
            {
                OnError?.Invoke("Not connected.");
                return false;
            }

            _isRunning = true;
            try
            {
                var @params = new SessionPromptParams
                {
                    SessionId = _sessionId,
                    Prompt = prompt
                };

                var result = await _transport.SendRequestAsync(
                    AcpMethods.SessionPrompt,
                    @params,
                    ct,
                    TimeSpan.FromMinutes(10) // Long timeout for agent processing
                );

                return true;
            }
            catch (OperationCanceledException)
            {
                // Cancel the session
                _transport.SendNotification(AcpMethods.SessionCancel, new SessionCancelParams
                {
                    SessionId = _sessionId
                });
                return false;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Prompt failed: {ex.Message}");
                return false;
            }
            finally
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// Cancels the current prompt processing.
        /// </summary>
        public void Cancel()
        {
            if (!_isRunning || string.IsNullOrEmpty(_sessionId)) return;

            _transport.SendNotification(AcpMethods.SessionCancel, new SessionCancelParams
            {
                SessionId = _sessionId
            });
        }

        /// <summary>
        /// Sets a config option.
        /// </summary>
        public async Task<List<ConfigOption>> SetConfigOptionAsync(string configId, string value, CancellationToken ct = default)
        {
            if (!_isConnected) return null;

            try
            {
                var result = await _transport.SendRequestAsync(
                    AcpMethods.SessionSetConfigOption,
                    new SessionSetConfigOptionParams
                    {
                        SessionId = _sessionId,
                        ConfigId = configId,
                        Value = value
                    },
                    ct
                );

                var typed = result.Deserialize<SessionSetConfigOptionResult>(JsonOptions);
                return typed?.ConfigOptions;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Lists available sessions.
        /// </summary>
        public async Task<List<SessionListEntry>> ListSessionsAsync(CancellationToken ct = default)
        {
            if (!AgentCapabilities?.ListSessions ?? true) return null;

            try
            {
                var result = await _transport.SendRequestAsync(
                    AcpMethods.SessionList,
                    new SessionListParams { Cwd = _settings.EffectiveWorkspacePath },
                    ct
                );

                var typed = result.Deserialize<SessionListResult>(JsonOptions);
                return typed?.Sessions;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes a session using the DotCraft ACP extension method.
        /// </summary>
        public async Task<bool> DeleteSessionAsync(string sessionId, CancellationToken ct = default)
        {
            if (!_isConnected || !SupportsSessionDelete || string.IsNullOrWhiteSpace(sessionId))
                return false;

            try
            {
                var result = await _transport.SendRequestAsync(
                    AcpMethods.DotCraftSessionDelete,
                    new SessionDeleteParams { SessionId = sessionId },
                    ct
                );

                _ = result.Deserialize<SessionDeleteResult>(JsonOptions);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RegisterHandlers()
        {
            // Permission request handler
            _transport.RegisterHandler(AcpMethods.RequestPermission, async (paramsJson) =>
            {
                var @params = paramsJson.Deserialize<RequestPermissionParams>(JsonOptions);
                var tcs = new TaskCompletionSource<RequestPermissionResult>();

                OnPermissionRequest?.Invoke(@params, result => tcs.TrySetResult(result));

                return await tcs.Task;
            });

            // File handlers
            _transport.RegisterHandler(AcpMethods.FsReadTextFile, async (paramsJson) =>
            {
                var @params = paramsJson.Deserialize<FsReadTextFileParams>(JsonOptions);
                return await HandleReadTextFileAsync(@params);
            });

            _transport.RegisterHandler(AcpMethods.FsWriteTextFile, async (paramsJson) =>
            {
                var @params = paramsJson.Deserialize<FsWriteTextFileParams>(JsonOptions);
                return await HandleWriteTextFileAsync(@params);
            });

            // Terminal handlers
            _transport.RegisterHandler(AcpMethods.TerminalCreate, async (paramsJson) =>
            {
                var @params = paramsJson.Deserialize<TerminalCreateParams>(JsonOptions);
                return await HandleTerminalCreateAsync(@params);
            });

            _transport.RegisterHandler(AcpMethods.TerminalGetOutput, async (paramsJson) =>
            {
                var @params = paramsJson.Deserialize<TerminalGetOutputParams>(JsonOptions);
                return await HandleTerminalGetOutputAsync(@params);
            });

            _transport.RegisterHandler(AcpMethods.TerminalWaitForExit, async (paramsJson) =>
            {
                var @params = paramsJson.Deserialize<TerminalWaitForExitParams>(JsonOptions);
                return await HandleTerminalWaitForExitAsync(@params);
            });

            _transport.RegisterHandler(AcpMethods.TerminalKill, async (paramsJson) =>
            {
                var @params = paramsJson.Deserialize<TerminalKillParams>(JsonOptions);
                return await HandleTerminalKillAsync(@params);
            });

            _transport.RegisterHandler(AcpMethods.TerminalRelease, async (paramsJson) =>
            {
                var @params = paramsJson.Deserialize<TerminalReleaseParams>(JsonOptions);
                return await HandleTerminalReleaseAsync(@params);
            });

            // Extension method handler for _unity/*
            // Method name is passed separately - no longer injected into params
            // Only register if built-in Unity tools are enabled
            if (_settings.EnableBuiltinUnityTools)
            {
                _transport.RegisterExtensionHandler("_unity/", async (method, paramsJson) =>
                {
                    return await _extensionRouter.HandleAsync(method, paramsJson);
                });
            }
        }

        private async Task<InitializeResult> InitializeAsync(CancellationToken ct)
        {
            var @params = new InitializeParams
            {
                ProtocolVersion = 1,
                ClientCapabilities = new ClientCapabilities
                {
                    Fs = FsCapabilities.All,
                    Terminal = TerminalCapabilities.All,
                    Extensions = _settings.EnableBuiltinUnityTools ? new[] { "_unity" } : Array.Empty<string>()
                },
                ClientInfo = new ClientInfo
                {
                    Name = "DotCraft-Unity",
                    Version = "0.1.0"
                }
            };

            var result = await _transport.SendRequestAsync(AcpMethods.Initialize, @params, ct);
            return result.Deserialize<InitializeResult>(JsonOptions);
        }

        private async Task<SessionNewResult> NewSessionAsync(CancellationToken ct)
        {
            var @params = new SessionNewParams
            {
                Cwd = _settings.EffectiveWorkspacePath
            };

            var result = await _transport.SendRequestAsync(AcpMethods.SessionNew, @params, ct);
            return result.Deserialize<SessionNewResult>(JsonOptions);
        }

        private async Task<SessionLoadResult> LoadSessionAsync(string sessionId, CancellationToken ct)
        {
            var @params = new SessionLoadParams
            {
                SessionId = sessionId,
                Cwd = _settings.EffectiveWorkspacePath
            };

            var result = await _transport.SendRequestAsync(AcpMethods.SessionLoad, @params, ct);
            return result.Deserialize<SessionLoadResult>(JsonOptions);
        }

        private async Task<bool> HandleAuthenticationAsync(AuthMethod[] authMethods, CancellationToken ct)
        {
            if (OnAuthenticationRequired == null)
            {
                Debug.LogWarning("[DotCraft] Authentication required but no handler registered.");
                return false;
            }

            var tcs = new TaskCompletionSource<AuthMethod>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reg = ct.Register(() => tcs.TrySetCanceled());

            OnAuthenticationRequired.Invoke(authMethods, method => tcs.TrySetResult(method));

            var selectedMethod = await tcs.Task;
            if (selectedMethod == null) return false;

            try
            {
                await _transport.SendRequestAsync(
                    AcpMethods.Authenticate,
                    new AuthenticateParams { MethodId = selectedMethod.Id },
                    ct
                );

                Debug.Log($"[DotCraft] Authenticated with method: {selectedMethod.Id}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Authentication failed: {ex.Message}");
                return false;
            }
        }

        private void HandleSessionUpdate(JsonElement paramsJson)
        {
            var @params = paramsJson.Deserialize<SessionUpdateParams>(JsonOptions);
            var update = @params?.Update;
            if (update == null) return;

            // Track available commands
            if (update.SessionUpdate == AcpUpdateKind.AvailableCommandsUpdate && update.Commands != null)
            {
                AvailableCommands = update.Commands;
                OnAvailableCommandsUpdate?.Invoke(update.Commands);
            }

            // Track config option changes (mode, model, etc.)
            if (update.SessionUpdate == AcpUpdateKind.ConfigOptionsUpdate && update.ConfigOptions != null)
            {
                ConfigOptions = update.ConfigOptions;
                OnConfigOptionsUpdate?.Invoke(update.ConfigOptions);
            }

            OnSessionUpdate?.Invoke(update);
        }

        private void HandleProcessExited()
        {
            _isConnected = false;
            OnConnectionStateChanged?.Invoke(false);
            OnProcessExited?.Invoke();
        }

        private void HandleErrorOutput(string line)
        {
            if (_settings.VerboseLogging)
            {
                Debug.Log($"[DotCraft stderr] {line}");
            }
        }

        private void HandleTransportError(string error)
        {
            OnError?.Invoke(error);
        }

        #region File Handlers

        private async Task<FsReadTextFileResult> HandleReadTextFileAsync(FsReadTextFileParams @params)
        {
            try
            {
                var path = @params.Path;

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096);

                string content;

                if (@params.Offset.HasValue || @params.Limit.HasValue)
                {
                    content = await ReadLinesAsync(reader, @params.Offset ?? 1, @params.Limit);
                }
                else
                {
                    content = await reader.ReadToEndAsync();
                }

                return new FsReadTextFileResult { Content = content };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Failed to read file: {ex.Message}");
                return new FsReadTextFileResult { Content = "" };
            }
        }

        private async Task<string> ReadLinesAsync(StreamReader reader, int startLine, int? limit)
        {
            var sb = new System.Text.StringBuilder();
            int currentLine = 1;
            int linesRead = 0;

            while (currentLine < startLine && !reader.EndOfStream)
            {
                await reader.ReadLineAsync();
                currentLine++;
            }

            while (!reader.EndOfStream)
            {
                if (limit.HasValue && linesRead >= limit.Value) break;

                var line = await reader.ReadLineAsync();
                if (line == null) break;

                if (sb.Length > 0) sb.AppendLine();
                sb.Append(line);
                linesRead++;
            }

            return sb.ToString();
        }

        private async Task<FsWriteTextFileResult> HandleWriteTextFileAsync(FsWriteTextFileParams @params)
        {
            try
            {
                var path = @params.Path;
                var directory = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(path, @params.Content);

                return new FsWriteTextFileResult { Success = true };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Failed to write file: {ex.Message}");
                return new FsWriteTextFileResult { Success = false };
            }
        }

        #endregion

        #region Terminal Handlers

        private readonly TerminalExecutor _terminalExecutor = new();

        private Task<TerminalCreateResult> HandleTerminalCreateAsync(TerminalCreateParams @params)
        {
            var terminalId = _terminalExecutor.Create(@params.Command, @params.Cwd, @params.Env);
            return Task.FromResult(new TerminalCreateResult { TerminalId = terminalId });
        }

        private Task<TerminalGetOutputResult> HandleTerminalGetOutputAsync(TerminalGetOutputParams @params)
        {
            var (output, exitCode) = _terminalExecutor.GetOutput(@params.TerminalId);
            return Task.FromResult(new TerminalGetOutputResult { Output = output, ExitCode = exitCode });
        }

        private async Task<TerminalGetOutputResult> HandleTerminalWaitForExitAsync(TerminalWaitForExitParams @params)
        {
            var timeout = @params.Timeout.HasValue ? TimeSpan.FromSeconds(@params.Timeout.Value) : (TimeSpan?)null;
            var (output, exitCode) = await _terminalExecutor.WaitForExitAsync(@params.TerminalId, timeout);
            return new TerminalGetOutputResult { Output = output, ExitCode = exitCode };
        }

        private Task<object> HandleTerminalKillAsync(TerminalKillParams @params)
        {
            _terminalExecutor.Kill(@params.TerminalId);
            return Task.FromResult<object>(null);
        }

        private Task<object> HandleTerminalReleaseAsync(TerminalReleaseParams @params)
        {
            _terminalExecutor.Release(@params.TerminalId);
            return Task.FromResult<object>(null);
        }

        #endregion

        public void Dispose()
        {
            _isConnected = false;

            // Cancel the reader loop CTS first so it sees cancellation before EOF.
            // This must happen before Kill() to avoid a race where ReadLineAsync
            // returns null (stream closed) before the token is canceled, causing
            // a spurious "Connection closed by agent." error log.
            _transport?.CancelReaderLoop();

            // Kill process so pending stream I/O unblocks immediately.
            _processManager?.Kill();

            // Reader loop can now exit cleanly; Dispose calls won't deadlock.
            _transport?.Dispose();
            _processManager?.Dispose();
            _terminalExecutor?.Dispose();
        }
    }
}
