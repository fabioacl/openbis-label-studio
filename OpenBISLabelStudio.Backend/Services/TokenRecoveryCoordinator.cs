using System;
using System.Threading.Tasks;

namespace OpenBISLabelStudio.Services
{
    /// <summary>
    /// Coalesces concurrent expired-token recovery triggers so only one recovery prompt
    /// executes at a time while all in-flight callers await its shared outcome.
    /// </summary>
    public sealed class TokenRecoveryCoordinator
    {
        private readonly object _syncLock = new object();
        private Task<bool>? _inFlightRecovery;

        /// <summary>
        /// Runs <paramref name="promptAsync"/> if no recovery is currently active, or returns
        /// the shared in-flight recovery task for concurrent callers.
        /// </summary>
        public Task<bool> RecoverAsync(Func<Task<bool>> promptAsync)
        {
            ArgumentNullException.ThrowIfNull(promptAsync);

            TaskCompletionSource<bool> tcs;
            lock (_syncLock)
            {
                if (_inFlightRecovery != null)
                {
                    return _inFlightRecovery;
                }

                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlightRecovery = tcs.Task;
            }

            _ = ExecutePromptAsync(promptAsync, tcs);
            return tcs.Task;
        }

        private async Task ExecutePromptAsync(Func<Task<bool>> promptAsync, TaskCompletionSource<bool> tcs)
        {
            bool recovered = false;
            Exception? capturedException = null;
            try
            {
                recovered = await promptAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }

            lock (_syncLock)
            {
                if (ReferenceEquals(_inFlightRecovery, tcs.Task))
                {
                    _inFlightRecovery = null;
                }
            }

            if (capturedException != null)
            {
                tcs.TrySetException(capturedException);
            }
            else
            {
                tcs.TrySetResult(recovered);
            }
        }
    }
}
