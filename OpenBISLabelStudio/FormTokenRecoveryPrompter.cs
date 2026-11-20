using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenBISLabelStudio.Backend.Contracts;
using OpenBISLabelStudio.Models;

namespace OpenBISLabelStudio
{
    /// <summary>
    /// Prompts for replacement openBIS credentials by opening <see cref="SetupSettingsForm"/>
    /// in expired-token recovery mode on the owner form's UI thread.
    /// </summary>
    public sealed class FormTokenRecoveryPrompter : ITokenRecoveryPrompter
    {
        private readonly Form _owner;
        private readonly ILabelStudioBackend _backend;

        /// <summary>
        /// Initializes a prompter bound to <paramref name="owner"/> and <paramref name="backend"/>.
        /// </summary>
        public FormTokenRecoveryPrompter(Form owner, ILabelStudioBackend backend)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <inheritdoc />
        public Task<bool> PromptForNewTokenAsync(
            string currentUrl,
            string warningMessage,
            CancellationToken ct = default)
        {
            if (_owner.IsDisposed || _owner.Disposing || !_owner.IsHandleCreated || ct.IsCancellationRequested)
            {
                return Task.FromResult(false);
            }

            if (!_owner.InvokeRequired)
            {
                return Task.FromResult(ShowRecoveryDialogOnUiThread(warningMessage, ct));
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration ctReg = default;
            if (ct.CanBeCanceled)
            {
                ctReg = ct.Register(() => tcs.TrySetResult(false));
            }

            try
            {
                _owner.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        bool recovered = ShowRecoveryDialogOnUiThread(warningMessage, ct);
                        tcs.TrySetResult(recovered);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                    finally
                    {
                        ctReg.Dispose();
                    }
                }));
            }
            catch (ObjectDisposedException)
            {
                ctReg.Dispose();
                tcs.TrySetResult(false);
            }
            catch (InvalidOperationException)
            {
                ctReg.Dispose();
                tcs.TrySetResult(false);
            }

            return tcs.Task;
        }

        private bool ShowRecoveryDialogOnUiThread(string warningMessage, CancellationToken ct)
        {
            if (_owner.IsDisposed || _owner.Disposing || ct.IsCancellationRequested)
            {
                return false;
            }

            using var recoveryForm = new SetupSettingsForm(
                _backend,
                SetupDialogMode.ExpiredTokenRecovery,
                SetupWizardStep.Connection,
                initialWarning: warningMessage);

            DialogResult result = recoveryForm.ShowDialog(_owner);
            return !_owner.IsDisposed && !_owner.Disposing && result == DialogResult.OK;
        }
    }
}
