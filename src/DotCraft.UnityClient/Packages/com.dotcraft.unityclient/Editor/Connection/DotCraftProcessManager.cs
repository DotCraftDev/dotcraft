using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Settings;
using Debug = UnityEngine.Debug;

namespace DotCraft.Editor.Connection
{
    /// <summary>
    /// Manages the DotCraft subprocess lifecycle.
    /// Handles starting, stopping, monitoring, and restarting the process.
    /// </summary>
    public sealed class DotCraftProcessManager : IDisposable
    {
        private Process _process;
        private readonly StringBuilder _errorOutput = new();
        private Task _errorReadTask;
        private CancellationTokenSource _errorReadCts;

        public event Action OnProcessExited;
        public event Action<string> OnErrorOutput;

        public bool IsAlive => _process != null && !_process.HasExited;
        public Process Process => _process;
        public int? ProcessId => _process?.Id;
        public DateTime? StartTime { get; private set; }

        /// <summary>
        /// Starts the DotCraft process with redirected stdio.
        /// </summary>
        public bool Start(DotCraftSettings settings)
        {
            if (IsAlive)
            {
                Debug.LogWarning("[DotCraft] Process is already running.");
                return true;
            }

            _errorOutput.Clear();

            try
            {
                var startInfo = BuildProcessStartInfo(settings);
                _process = Process.Start(startInfo);

                if (_process == null)
                {
                    Debug.LogError("[DotCraft] Failed to start process.");
                    return false;
                }

                StartTime = DateTime.Now;

                // Start reading stderr in background
                _errorReadCts = new CancellationTokenSource();
                _errorReadTask = ReadErrorOutputAsync(_errorReadCts.Token);

                // Set up exit handler
                _process.EnableRaisingEvents = true;
                _process.Exited += HandleProcessExited;

                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"[DotCraft] Process started (PID: {_process.Id})");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Failed to start process: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stops the DotCraft process gracefully.
        /// </summary>
        public async Task StopAsync(TimeSpan? timeout = null)
        {
            if (_process == null || _process.HasExited)
                return;

            timeout ??= TimeSpan.FromSeconds(3);

            try
            {
                // Close stdin to signal graceful shutdown
                _process.StandardInput.Close();

                // Wait for graceful exit
                if (await WaitForExitAsync(_process, timeout.Value))
                {
                    if (DotCraftSettings.Instance.VerboseLogging)
                    {
                        Debug.Log("[DotCraft] Process exited gracefully.");
                    }
                }
                else
                {
                    // Force kill if timeout
                    _process.Kill();
                    Debug.LogWarning("[DotCraft] Process was force-killed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotCraft] Error stopping process: {ex.Message}");
                try
                {
                    _process?.Kill();
                }
                catch
                {
                    // ignored
                }
            }
            finally
            {
                CleanupProcess();
            }
        }

        /// <summary>
        /// Kills the process immediately.
        /// </summary>
        public void Kill()
        {
            if (_process == null || _process.HasExited)
                return;

            try
            {
                _process.Kill();
                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log("[DotCraft] Process killed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotCraft] Error killing process: {ex.Message}");
            }
            finally
            {
                CleanupProcess();
            }
        }

        /// <summary>
        /// Restarts the DotCraft process.
        /// </summary>
        public async Task<bool> RestartAsync(DotCraftSettings settings, CancellationToken ct = default)
        {
            await StopAsync();
            await Task.Delay(500, ct);
            return Start(settings);
        }

        private ProcessStartInfo BuildProcessStartInfo(DotCraftSettings settings)
        {
            var startInfo = new ProcessStartInfo
            {
#if UNITY_EDITOR_OSX
                // macOS: Use zsh -cl to properly load PATH
                FileName = "/bin/zsh",
                Arguments = $"-cl '{settings.DotCraftCommand} {settings.DotCraftArguments}'",
#else
                FileName = settings.DotCraftCommand,
                Arguments = settings.DotCraftArguments,
#endif
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = settings.EffectiveWorkspacePath
            };

            // Inject environment variables
            foreach (var kv in settings.EnvironmentVariables)
            {
                if (!string.IsNullOrEmpty(kv.Key))
                {
                    startInfo.EnvironmentVariables[kv.Key] = kv.Value ?? "";
                }
            }

            return startInfo;
        }

        private async Task ReadErrorOutputAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _process != null && !_process.HasExited)
                {
                    var line = await _process.StandardError.ReadLineAsync();
                    if (line == null) break;

                    _errorOutput.AppendLine(line);
                    OnErrorOutput?.Invoke(line);

                    if (DotCraftSettings.Instance.VerboseLogging)
                    {
                        Debug.Log($"[DotCraft stderr] {line}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Suppress I/O exceptions that result from the process/stream being
                // disposed during an intentional shutdown (CTS already cancelled).
                if (!ct.IsCancellationRequested)
                    Debug.LogWarning($"[DotCraft] Error reading stderr: {ex.Message}");
            }
        }

        private void HandleProcessExited(object sender, EventArgs e)
        {
            if (DotCraftSettings.Instance.VerboseLogging)
            {
                Debug.Log("[DotCraft] Process exited.");
            }

            OnProcessExited?.Invoke();
        }

        private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();

            process.Exited += OnExited;

            if (process.HasExited)
            {
                process.Exited -= OnExited;
                return true;
            }

            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(timeout)
            );

            return completedTask == tcs.Task;

            void OnExited(object sender, EventArgs e)
            {
                process.Exited -= OnExited;
                tcs.TrySetResult(true);
            }
        }

        private void CleanupProcess()
        {
            if (_errorReadCts != null)
            {
                _errorReadCts.Cancel();
                _errorReadCts.Dispose();
                _errorReadCts = null;
            }

            try
            {
                _errorReadTask?.Wait(TimeSpan.FromMilliseconds(200));
            }
            catch
            {
                // ignored
            }

            if (_process != null)
            {
                _process.Exited -= HandleProcessExited;
                _process.Dispose();
                _process = null;
            }

            StartTime = null;
        }

        public void Dispose()
        {
            StopAsync().Wait(TimeSpan.FromSeconds(5));
            CleanupProcess();
        }
    }
}
