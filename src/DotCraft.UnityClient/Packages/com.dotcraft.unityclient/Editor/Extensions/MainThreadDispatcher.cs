using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace DotCraft.Editor.Extensions
{
    /// <summary>
    /// Dispatches actions to the Unity main thread.
    /// Unity API must be called from the main thread, but ACP message handling
    /// runs on background threads. This class bridges the gap.
    /// </summary>
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> Queue = new();

        // Maximum time per frame for processing queue (in milliseconds)
        private const int MaxFrameTimeMs = 16; // ~60fps target, leave room for other work

        static MainThreadDispatcher()
        {
            EditorApplication.update += ProcessQueue;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Queues an action to run on the main thread.
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;
            Queue.Enqueue(action);
        }

        /// <summary>
        /// Queues a function to run on the main thread and returns the result.
        /// </summary>
        /// <param name="func">The function to execute on the main thread.</param>
        /// <param name="timeoutMs">Optional timeout in milliseconds (default: 30000ms = 30s).</param>
        public static Task<T> RunOnMainThread<T>(Func<T> func, int timeoutMs = 30000)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Queue.Enqueue(() =>
            {
                try
                {
                    var result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            // Apply timeout
            var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

            return tcs.Task;
        }

        /// <summary>
        /// Queues an async function to run on the main thread.
        /// </summary>
        /// <param name="func">The async function to execute on the main thread.</param>
        /// <param name="timeoutMs">Optional timeout in milliseconds (default: 30000ms = 30s).</param>
        public static Task RunOnMainThread(Func<Task> func, int timeoutMs = 30000)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Queue.Enqueue(() =>
            {
                RunAsyncOnMainThread(func, tcs);
            });

            // Apply timeout
            var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

            return tcs.Task;
        }

        private static async void RunAsyncOnMainThread(Func<Task> func, TaskCompletionSource<bool> tcs)
        {
            try
            {
                await func();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Queues an async function to run on the main thread and returns the result.
        /// </summary>
        /// <param name="func">The async function to execute on the main thread.</param>
        /// <param name="timeoutMs">Optional timeout in milliseconds (default: 30000ms = 30s).</param>
        public static Task<T> RunOnMainThread<T>(Func<Task<T>> func, int timeoutMs = 30000)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Queue.Enqueue(() =>
            {
                RunAsyncOnMainThread(func, tcs);
            });

            // Apply timeout
            var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

            return tcs.Task;
        }

        private static async void RunAsyncOnMainThread<T>(Func<Task<T>> func, TaskCompletionSource<T> tcs)
        {
            try
            {
                var result = await func();
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Checks if currently on the main thread.
        /// </summary>
        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == 1;

        /// <summary>
        /// Runs an action on the main thread, executing immediately if already on main thread.
        /// </summary>
        public static void RunOrEnqueue(Action action)
        {
            if (IsMainThread)
            {
                action?.Invoke();
            }
            else
            {
                Enqueue(action);
            }
        }

        private static void ProcessQueue()
        {
            // Process actions with both count and time limits to avoid blocking
            var stopwatch = Stopwatch.StartNew();
            int processed = 0;
            const int maxActionsPerFrame = 100;

            while (Queue.TryDequeue(out var action) && processed < maxActionsPerFrame)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
                processed++;

                // Check if we've exceeded the time budget
                if (stopwatch.ElapsedMilliseconds >= MaxFrameTimeMs)
                {
                    // Yield to prevent frame blocking
                    break;
                }
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Clear queue when entering play mode
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                while (Queue.TryDequeue(out _)) { }
            }
        }
    }
}
