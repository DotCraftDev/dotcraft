using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotCraft.Editor.Connection
{
    /// <summary>
    /// Manages multiple terminal processes for the agent.
    /// This is a key difference from UnityAgentClient which has throw NotImplementedException for terminal methods.
    /// </summary>
    public sealed class TerminalExecutor : IDisposable
    {
        private readonly ConcurrentDictionary<string, TerminalInstance> _terminals = new();
        private int _nextId;

        /// <summary>
        /// Creates a new terminal and starts executing the command.
        /// </summary>
        public string Create(string command, string cwd = null, System.Collections.Generic.Dictionary<string, string> env = null)
        {
            var terminalId = Interlocked.Increment(ref _nextId).ToString();

            var startInfo = new ProcessStartInfo
            {
#if UNITY_EDITOR_WIN
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
#else
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
#endif
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            if (!string.IsNullOrEmpty(cwd))
            {
                startInfo.WorkingDirectory = cwd;
            }

            if (env != null)
            {
                foreach (var kv in env)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                    {
                        startInfo.EnvironmentVariables[kv.Key] = kv.Value ?? "";
                    }
                }
            }

            try
            {
                var process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start terminal process.");
                }

                var terminal = new TerminalInstance
                {
                    Process = process,
                    Output = new StringBuilder(),
                    StartTime = DateTime.Now
                };

                // Asynchronously collect output
                Task.Run(() => CollectOutputAsync(terminal, process.StandardOutput, process.StandardError));

                _terminals[terminalId] = terminal;

                return terminalId;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[DotCraft] Failed to create terminal: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets the output and exit code of a terminal.
        /// </summary>
        public (string output, int? exitCode) GetOutput(string terminalId)
        {
            if (!_terminals.TryGetValue(terminalId, out var terminal))
            {
                return ("Terminal not found", null);
            }

            lock (terminal.Output)
            {
                var output = terminal.Output.ToString();
                var exitCode = terminal.Process.HasExited ? terminal.Process.ExitCode : (int?)null;
                return (output, exitCode);
            }
        }

        /// <summary>
        /// Waits for a terminal to exit and returns the final output.
        /// </summary>
        public async Task<(string output, int? exitCode)> WaitForExitAsync(string terminalId, TimeSpan? timeout = null)
        {
            if (!_terminals.TryGetValue(terminalId, out var terminal))
            {
                return ("Terminal not found", null);
            }

            var process = terminal.Process;
            timeout ??= TimeSpan.FromSeconds(30);

            try
            {
                // Wait for process to exit
                if (!process.HasExited)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    process.EnableRaisingEvents = true;
                    process.Exited += (s, e) => tcs.TrySetResult(true);

                    if (process.HasExited)
                    {
                        tcs.TrySetResult(true);
                    }

                    using var cts = new CancellationTokenSource(timeout.Value);
                    using var reg = cts.Token.Register(() => tcs.TrySetCanceled());

                    await tcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout - return current output
            }

            lock (terminal.Output)
            {
                var output = terminal.Output.ToString();
                var exitCode = process.HasExited ? process.ExitCode : (int?)null;
                return (output, exitCode);
            }
        }

        /// <summary>
        /// Kills a terminal process.
        /// </summary>
        public void Kill(string terminalId)
        {
            if (!_terminals.TryGetValue(terminalId, out var terminal))
                return;

            try
            {
                if (!terminal.Process.HasExited)
                {
                    terminal.Process.Kill();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[DotCraft] Failed to kill terminal: {ex.Message}");
            }
        }

        /// <summary>
        /// Releases a terminal's resources.
        /// </summary>
        public void Release(string terminalId)
        {
            if (_terminals.TryRemove(terminalId, out var terminal))
            {
                try
                {
                    if (!terminal.Process.HasExited)
                    {
                        terminal.Process.Kill();
                    }
                    terminal.Process.Dispose();
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static async Task CollectOutputAsync(TerminalInstance terminal, StreamReader stdout, StreamReader stderr)
        {
            var process = terminal.Process;

            try
            {
                // Read stdout and stderr concurrently
                var stdoutTask = Task.Run(async () =>
                {
                    while (!process.HasExited || !stdout.EndOfStream)
                    {
                        var line = await stdout.ReadLineAsync();
                        if (line == null) break;

                        lock (terminal.Output)
                        {
                            terminal.Output.AppendLine(line);
                        }
                    }
                });

                var stderrTask = Task.Run(async () =>
                {
                    while (!process.HasExited || !stderr.EndOfStream)
                    {
                        var line = await stderr.ReadLineAsync();
                        if (line == null) break;

                        lock (terminal.Output)
                        {
                            terminal.Output.AppendLine(line);
                        }
                    }
                });

                await Task.WhenAll(stdoutTask, stderrTask);

                // Process may still have remaining output
                while (true)
                {
                    var line = await stdout.ReadLineAsync();
                    if (line == null) break;
                    lock (terminal.Output)
                    {
                        terminal.Output.AppendLine(line);
                    }
                }

                while (true)
                {
                    var line = await stderr.ReadLineAsync();
                    if (line == null) break;
                    lock (terminal.Output)
                    {
                        terminal.Output.AppendLine(line);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[DotCraft] Terminal output collection error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            foreach (var terminal in _terminals.Values)
            {
                try
                {
                    if (!terminal.Process.HasExited)
                    {
                        terminal.Process.Kill();
                    }
                    terminal.Process.Dispose();
                }
                catch
                {
                    // ignored
                }
            }
            _terminals.Clear();
        }

        private sealed class TerminalInstance
        {
            public Process Process { get; set; }
            public StringBuilder Output { get; set; }
            public DateTime StartTime { get; set; }
        }
    }
}
