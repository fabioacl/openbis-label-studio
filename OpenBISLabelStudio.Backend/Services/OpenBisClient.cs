using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OpenBISLabelStudio.Services
{
    using OpenBISLabelStudio.Models;

    /// <summary>
    /// Executes openbis_fetch.py via a persistent --worker stdio JSON loop when supported,
    /// falling back transparently to isolated one-shot --out CLI execution for stub or legacy scripts.
    /// </summary>
    public class OpenBisClient : IDisposable
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly object ActiveClientsLock = new object();
        private static readonly HashSet<OpenBisClient> ActiveWorkerClients = new HashSet<OpenBisClient>();

        private readonly string _pythonExecutable;
        private readonly string _scriptPath;
        private readonly SemaphoreSlim _workerGate = new SemaphoreSlim(1, 1);
        private readonly object _lifecycleLock = new object();

        private Process? _workerProcess;
        private StreamWriter? _workerIn;
        private StreamReader? _workerOut;
        private StringBuilder? _workerStdErr;
        private TaskCompletionSource<bool>? _workerStdErrClosed;
        private bool _workerUnsupported;
        private bool _workerConfirmed;
        private bool _disposed;

        static OpenBisClient()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutdownAllActiveWorkers();
        }

        /// <summary>
        /// Initializes a client rooted at <paramref name="baseDirectory"/> using <paramref name="pythonExecutable"/>.
        /// </summary>
        public OpenBisClient(string? baseDirectory = null, string? pythonExecutable = null)
        {
            string resolvedBaseDir = string.IsNullOrWhiteSpace(baseDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : Path.GetFullPath(baseDirectory);

            _pythonExecutable = string.IsNullOrWhiteSpace(pythonExecutable)
                ? "python"
                : pythonExecutable.Trim();

            _scriptPath = Path.Combine(resolvedBaseDir, "openbis_fetch.py");
        }

        public string ScriptPath => _scriptPath;

        /// <summary>
        /// Pre-warms the persistent Python --worker process asynchronously when the script is present.
        /// Errors are swallowed cleanly so subsequent queries recover on demand.
        /// </summary>
        public void WarmUpWorkerInBackground()
        {
            lock (_lifecycleLock)
            {
                if (_disposed || _workerUnsupported || _workerProcess != null || !File.Exists(_scriptPath))
                {
                    return;
                }
            }

            _ = Task.Run(() =>
            {
                try
                {
                    lock (_lifecycleLock)
                    {
                        if (_disposed || _workerUnsupported || !File.Exists(_scriptPath))
                        {
                            return;
                        }

                        if (_workerProcess != null && !_workerProcess.HasExited)
                        {
                            return;
                        }

                        TryStartWorkerUnsafe(out _);
                    }
                }
                catch
                {
                    // Best-effort warmup; queries recover cleanly on demand.
                }
            });
        }

        /// <summary>
        /// Stops and cleans up any active persistent Python --worker process.
        /// </summary>
        public void ShutdownWorker()
        {
            lock (_lifecycleLock)
            {
                ResetWorkerProcessUnsafe();
            }
        }

        /// <summary>
        /// Disposes the client and terminates any active persistent Python --worker process.
        /// </summary>
        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                ResetWorkerProcessUnsafe();
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Verifies openBIS URL reachability and token session validity (--check) without listing object types.
        /// </summary>
        public Task<OpenBisConnectionResult> CheckConnectionAsync(string url, string token)
        {
            var req = new WorkerRequest
            {
                Action = "check",
                Url = url ?? "",
                Token = token ?? ""
            };

            return RunViaWorkerOrFallbackAsync(
                req,
                new[] { url ?? "", token ?? "", "--check" },
                (errorType, error) => new OpenBisConnectionResult
                {
                    Success = false,
                    SessionActive = false,
                    ErrorType = errorType,
                    Error = error
                });
        }

        /// <summary>
        /// Queries all available object/sample types from openBIS (--types).
        /// </summary>
        public Task<OpenBisTypesResult> QueryTypesAsync(string url, string token)
        {
            var req = new WorkerRequest
            {
                Action = "types",
                Url = url ?? "",
                Token = token ?? ""
            };

            return RunViaWorkerOrFallbackAsync(
                req,
                new[] { url ?? "", token ?? "", "--types" },
                (errorType, error) => new OpenBisTypesResult
                {
                    Success = false,
                    ErrorType = errorType,
                    Error = error
                });
        }

        /// <summary>
        /// Queries samples belonging to the specified openBIS object type.
        /// </summary>
        public Task<OpenBisListResult> QuerySamplesByTypeAsync(string url, string token, string sampleType)
        {
            var req = new WorkerRequest
            {
                Action = "list",
                Url = url ?? "",
                Token = token ?? "",
                SampleType = sampleType ?? ""
            };

            return RunViaWorkerOrFallbackAsync(
                req,
                new[] { url ?? "", token ?? "", sampleType ?? "" },
                (errorType, error) => new OpenBisListResult
                {
                    Success = false,
                    ErrorType = errorType,
                    Error = error
                });
        }

        /// <summary>
        /// Queries detailed properties, icon flags, and preview paths for a single sample (--details).
        /// </summary>
        public Task<OpenBisDetailsResult> QuerySampleDetailsAsync(
            string url,
            string token,
            string permId,
            bool skipElnPreview = false)
        {
            var req = new WorkerRequest
            {
                Action = "details",
                Url = url ?? "",
                Token = token ?? "",
                PermId = permId ?? "",
                SkipEln = skipElnPreview
            };

            string[] args = skipElnPreview
                ? new[] { url ?? "", token ?? "", "--details", permId ?? "", "--skip-eln" }
                : new[] { url ?? "", token ?? "", "--details", permId ?? "" };

            return RunViaWorkerOrFallbackAsync(
                req,
                args,
                (errorType, error) => new OpenBisDetailsResult
                {
                    Success = false,
                    ErrorType = errorType,
                    Error = error
                });
        }

        /// <summary>
        /// Deserializes JSON output from openbis_fetch.py when present, or invokes
        /// <paramref name="errorFactory"/> with <c>"unknown_error"</c> and stderr/exit details.
        /// </summary>
        public static T DeserializeOrError<T>(
            string? jsonContent,
            int exitCode,
            string stdErr,
            Func<string, string, T> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(errorFactory);

            if (!string.IsNullOrWhiteSpace(jsonContent))
            {
                try
                {
                    T? parsed = JsonConvert.DeserializeObject<T>(jsonContent);
                    if (parsed != null)
                    {
                        return parsed;
                    }
                }
                catch (JsonException ex)
                {
                    string parseMessage = string.IsNullOrWhiteSpace(stdErr)
                        ? $"Failed to parse openBIS JSON output: {ex.Message}"
                        : $"{stdErr.Trim()} (Invalid JSON output: {ex.Message})";
                    return errorFactory("unknown_error", parseMessage);
                }
            }

            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                return errorFactory("unknown_error", stdErr.Trim());
            }

            string fallbackMessage = exitCode != 0
                ? $"Python script failed with exit code {exitCode} and produced no JSON output."
                : "Python script completed, but no JSON output was created.";
            return errorFactory("unknown_error", fallbackMessage);
        }

        private sealed class WorkerRequest
        {
            [JsonProperty("action")]
            public string Action { get; set; } = "";

            [JsonProperty("url")]
            public string Url { get; set; } = "";

            [JsonProperty("token")]
            public string Token { get; set; } = "";

            [JsonProperty("sampleType", NullValueHandling = NullValueHandling.Ignore)]
            public string? SampleType { get; set; }

            [JsonProperty("permId", NullValueHandling = NullValueHandling.Ignore)]
            public string? PermId { get; set; }

            [JsonProperty("skipEln")]
            public bool SkipEln { get; set; }
        }

        private sealed class WorkerHandle
        {
            public WorkerHandle(
                Process process,
                StreamWriter input,
                StreamReader output,
                StringBuilder stdErr,
                TaskCompletionSource<bool> stdErrClosed)
            {
                Process = process;
                Input = input;
                Output = output;
                StdErr = stdErr;
                StdErrClosed = stdErrClosed;
            }

            public Process Process { get; }
            public StreamWriter Input { get; }
            public StreamReader Output { get; }
            public StringBuilder StdErr { get; }
            public TaskCompletionSource<bool> StdErrClosed { get; }
        }

        private async Task<T> RunViaWorkerOrFallbackAsync<T>(
            WorkerRequest workerRequest,
            string[] cliArgs,
            Func<string, string, T> errorFactory)
        {
            if (IsDisposed())
            {
                return errorFactory("unknown_error", "OpenBIS client has been disposed.");
            }

            if (!File.Exists(_scriptPath))
            {
                return errorFactory("unknown_error", $"Script missing at '{_scriptPath}'.");
            }

            if (IsWorkerUnsupported())
            {
                return await RunScriptAsync(cliArgs, errorFactory).ConfigureAwait(false);
            }

            await _workerGate.WaitAsync().ConfigureAwait(false);
            bool gateHeld = true;
            try
            {
                if (!TryAcquireWorkerHandle(
                        out WorkerHandle? handle,
                        out bool shouldFallbackToCli,
                        out bool disposed,
                        out string? startError))
                {
                    if (disposed)
                    {
                        return errorFactory("unknown_error", "OpenBIS client has been disposed.");
                    }

                    if (shouldFallbackToCli)
                    {
                        gateHeld = false;
                        _workerGate.Release();
                        return await RunScriptAsync(cliArgs, errorFactory).ConfigureAwait(false);
                    }

                    return errorFactory("unknown_error", startError ?? $"Failed to start Python worker '{_pythonExecutable}'.");
                }

                string requestJson = JsonConvert.SerializeObject(workerRequest);
                string? responseLine = null;
                string? ioFailureMessage = null;

                try
                {
                    await handle!.Input.WriteLineAsync(requestJson).ConfigureAwait(false);
                    await handle.Input.FlushAsync().ConfigureAwait(false);
                    responseLine = await handle.Output.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    ioFailureMessage = ex.Message;
                }

                if (ioFailureMessage == null && !string.IsNullOrWhiteSpace(responseLine))
                {
                    try
                    {
                        T? parsed = JsonConvert.DeserializeObject<T>(responseLine);
                        if (parsed != null)
                        {
                            lock (_lifecycleLock)
                            {
                                _workerConfirmed = true;
                            }
                            return parsed;
                        }
                    }
                    catch (JsonException)
                    {
                        // Treated below as unsupported worker handshake or mid-session corrupt stdout.
                    }
                }

                bool fallbackAfterFailure = HandleFailedWorkerAttempt(
                    handle!,
                    out bool disposedDuringCall,
                    out int exitCode,
                    out string stdErrText);

                if (disposedDuringCall)
                {
                    return errorFactory("unknown_error", "OpenBIS client has been disposed.");
                }

                if (fallbackAfterFailure)
                {
                    gateHeld = false;
                    _workerGate.Release();
                    return await RunScriptAsync(cliArgs, errorFactory).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(stdErrText) && !string.IsNullOrWhiteSpace(ioFailureMessage))
                {
                    stdErrText = ioFailureMessage;
                }

                return DeserializeOrError(responseLine, exitCode, stdErrText, errorFactory);
            }
            finally
            {
                if (gateHeld)
                {
                    _workerGate.Release();
                }
            }
        }

        private bool IsDisposed()
        {
            lock (_lifecycleLock)
            {
                return _disposed;
            }
        }

        private bool IsWorkerUnsupported()
        {
            lock (_lifecycleLock)
            {
                return _workerUnsupported;
            }
        }

        private bool TryAcquireWorkerHandle(
            out WorkerHandle? handle,
            out bool shouldFallbackToCli,
            out bool disposed,
            out string? startError)
        {
            handle = null;
            shouldFallbackToCli = false;
            disposed = false;
            startError = null;

            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    disposed = true;
                    return false;
                }

                if (_workerUnsupported)
                {
                    shouldFallbackToCli = true;
                    return false;
                }

                if (_workerProcess != null && _workerProcess.HasExited)
                {
                    if (!_workerConfirmed)
                    {
                        _workerUnsupported = true;
                        ResetWorkerProcessUnsafe();
                        shouldFallbackToCli = true;
                        return false;
                    }

                    ResetWorkerProcessUnsafe();
                }

                if (_workerProcess == null && !TryStartWorkerUnsafe(out startError))
                {
                    if (!_workerConfirmed)
                    {
                        _workerUnsupported = true;
                        shouldFallbackToCli = true;
                        return false;
                    }

                    return false;
                }

                if (_workerProcess == null || _workerIn == null || _workerOut == null
                    || _workerStdErr == null || _workerStdErrClosed == null)
                {
                    startError = $"Failed to start Python worker '{_pythonExecutable}'.";
                    return false;
                }

                if (_workerConfirmed)
                {
                    lock (_workerStdErr)
                    {
                        _workerStdErr.Clear();
                    }
                }

                handle = new WorkerHandle(
                    _workerProcess,
                    _workerIn,
                    _workerOut,
                    _workerStdErr,
                    _workerStdErrClosed);
                return true;
            }
        }

        private bool HandleFailedWorkerAttempt(
            WorkerHandle handle,
            out bool disposed,
            out int exitCode,
            out string stdErrText)
        {
            disposed = false;
            exitCode = -1;
            stdErrText = "";

            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    disposed = true;
                    return false;
                }

                if (!_workerConfirmed)
                {
                    _workerUnsupported = true;
                    ResetWorkerProcessUnsafe();
                    return true;
                }

                if (ReferenceEquals(_workerProcess, handle.Process))
                {
                    try
                    {
                        if (!handle.Process.HasExited)
                        {
                            handle.Process.WaitForExit(250);
                        }
                    }
                    catch
                    {
                        // Ignore wait errors before teardown.
                    }

                    if (handle.Process.HasExited)
                    {
                        try
                        {
                            handle.StdErrClosed.Task.Wait(100);
                        }
                        catch
                        {
                            // Ignore wait errors.
                        }

                        try
                        {
                            exitCode = handle.Process.ExitCode;
                        }
                        catch
                        {
                            exitCode = -1;
                        }
                    }

                    lock (handle.StdErr)
                    {
                        stdErrText = handle.StdErr.ToString();
                    }

                    ResetWorkerProcessUnsafe();
                }

                return false;
            }
        }

        private bool TryStartWorkerUnsafe(out string? startError)
        {
            startError = null;
            ResetWorkerProcessUnsafe();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _pythonExecutable,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardInputEncoding = Utf8NoBom,
                    StandardOutputEncoding = Utf8NoBom,
                    StandardErrorEncoding = Utf8NoBom
                };
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
                startInfo.Environment["PYTHONUTF8"] = "1";
                startInfo.ArgumentList.Add(_scriptPath);
                startInfo.ArgumentList.Add("--worker");

                Process? proc = Process.Start(startInfo);
                if (proc == null)
                {
                    startError = $"Failed to start Python process '{_pythonExecutable}'.";
                    return false;
                }

                var errBuffer = new StringBuilder();
                var errClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                    {
                        errClosed.TrySetResult(true);
                        return;
                    }

                    lock (errBuffer)
                    {
                        if (errBuffer.Length < 65536)
                        {
                            if (errBuffer.Length > 0)
                            {
                                errBuffer.AppendLine();
                            }
                            errBuffer.Append(e.Data);
                        }
                    }
                };
                proc.BeginErrorReadLine();

                _workerProcess = proc;
                _workerIn = proc.StandardInput;
                _workerOut = proc.StandardOutput;
                _workerStdErr = errBuffer;
                _workerStdErrClosed = errClosed;

                lock (ActiveClientsLock)
                {
                    ActiveWorkerClients.Add(this);
                }

                return true;
            }
            catch (Exception ex)
            {
                startError = $"Failed to run '{_pythonExecutable}': {ex.Message}";
                ResetWorkerProcessUnsafe();
                return false;
            }
        }

        private void ResetWorkerProcessUnsafe()
        {
            lock (ActiveClientsLock)
            {
                ActiveWorkerClients.Remove(this);
            }

            if (_workerIn != null)
            {
                try
                {
                    _workerIn.Dispose();
                }
                catch
                {
                    // Ignore broken pipe flush errors during worker teardown.
                }
                _workerIn = null;
            }

            if (_workerProcess != null)
            {
                try
                {
                    if (!_workerProcess.HasExited && !_workerProcess.WaitForExit(300))
                    {
                        _workerProcess.Kill(entireProcessTree: true);
                        _workerProcess.WaitForExit(300);
                    }
                }
                catch
                {
                    // Ignore best-effort worker teardown errors.
                }

                try
                {
                    _workerProcess.Dispose();
                }
                catch
                {
                    // Ignore process disposal errors.
                }
                _workerProcess = null;
            }

            if (_workerOut != null)
            {
                try
                {
                    _workerOut.Dispose();
                }
                catch
                {
                    // Ignore pipe cleanup errors.
                }
                _workerOut = null;
            }

            _workerStdErr = null;
            _workerStdErrClosed = null;
        }

        private static void ShutdownAllActiveWorkers()
        {
            OpenBisClient[] snapshot;
            lock (ActiveClientsLock)
            {
                snapshot = new OpenBisClient[ActiveWorkerClients.Count];
                ActiveWorkerClients.CopyTo(snapshot);
            }

            foreach (OpenBisClient client in snapshot)
            {
                try
                {
                    client.ShutdownWorker();
                }
                catch
                {
                    // Best-effort ProcessExit cleanup.
                }
            }
        }

        private async Task<T> RunScriptAsync<T>(string[] args, Func<string, string, T> errorFactory)
        {
            if (!File.Exists(_scriptPath))
            {
                return errorFactory("unknown_error", $"Script missing at '{_scriptPath}'.");
            }

            string tempOut = Path.Combine(Path.GetTempPath(), $"openbis_results_{Guid.NewGuid():N}.json");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _pythonExecutable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

                startInfo.ArgumentList.Add(_scriptPath);
                foreach (string arg in args)
                {
                    startInfo.ArgumentList.Add(arg ?? "");
                }
                startInfo.ArgumentList.Add("--out");
                startInfo.ArgumentList.Add(tempOut);

                using Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    return errorFactory("unknown_error", $"Failed to start Python process '{_pythonExecutable}'.");
                }

                // Drain stdout and stderr before WaitForExitAsync to prevent OS pipe deadlocks.
                Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stdErrTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync().ConfigureAwait(false);

                string stdOut = await stdOutTask.ConfigureAwait(false);
                string stdErr = await stdErrTask.ConfigureAwait(false);

                string? jsonContent = File.Exists(tempOut)
                    ? await File.ReadAllTextAsync(tempOut, Encoding.UTF8).ConfigureAwait(false)
                    : null;

                string diagnosticErr = !string.IsNullOrWhiteSpace(stdErr) ? stdErr : stdOut;
                return DeserializeOrError(jsonContent, process.ExitCode, diagnosticErr, errorFactory);
            }
            catch (Exception ex)
            {
                return errorFactory("unknown_error", $"Failed to run '{_pythonExecutable}': {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempOut))
                {
                    try
                    {
                        File.Delete(tempOut);
                    }
                    catch
                    {
                        // Ignore best-effort cleanup failure for transient temp file.
                    }
                }
            }
        }
    }
}
