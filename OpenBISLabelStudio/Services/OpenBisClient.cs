using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OpenBISLabelStudio.Services
{
    using OpenBISLabelStudio.Models;

    /// <summary>
    /// Executes openbis_fetch.py in a subprocess with an isolated temp --out JSON file
    /// and deserializes openBIS responses or surfaces diagnostic process errors.
    /// </summary>
    public class OpenBisClient
    {
        private readonly string _pythonExecutable;
        private readonly string _scriptPath;

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
        /// Verifies openBIS URL reachability and token session validity (--check) without listing object types.
        /// </summary>
        public Task<OpenBisConnectionResult> CheckConnectionAsync(string url, string token)
        {
            return RunScriptAsync(
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
            return RunScriptAsync(
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
            return RunScriptAsync(
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
            var args = skipElnPreview
                ? new[] { url ?? "", token ?? "", "--details", permId ?? "", "--skip-eln" }
                : new[] { url ?? "", token ?? "", "--details", permId ?? "" };

            return RunScriptAsync(
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
