using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace OpenBISLabelStudio.Services
{
    using OpenBISLabelStudio.Models;

    /// <summary>
    /// Populates Brother b-PAC label templates for Molecule and Generic profiles, exports preview
    /// bitmaps (cropping oversized A4 fallback canvases to exact .lbx dimensions), and runs b-PAC
    /// COM calls in an isolated worker process so WinForms UI threads never freeze.
    /// </summary>
    public class LabelPreviewRenderer
    {
        public const int ExportResolutionDpi = 360;
        private const string DefaultFreeTextPlaceholder = "Free text placeholder";
        private const string TransparentPngFileName = "openbis_transparent_1x1.png";
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly SemaphoreSlim StaRenderGate = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim WorkerPipeGate = new SemaphoreSlim(1, 1);
        private static readonly object WorkerLifecycleLock = new object();
        private static Process? s_workerProcess;
        private static StreamWriter? s_workerIn;
        private static StreamReader? s_workerOut;

        static LabelPreviewRenderer()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutdownPreviewWorker();
        }
        private static readonly Regex PaperPrinterAttrRegex = new Regex(
            @"(<style:paper\b[^>]*?\bprinterName\s*=\s*"")([^""]*)("")",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex LengthValueRegex = new Regex(
            @"^\s*([0-9]+(?:\.[0-9]+)?)\s*(pt|mm|cm|in)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly AppConfigService _configService;
        private readonly Func<IReadOnlyList<string>>? _installedPrintersProvider;
        private string _transparentPngPath = "";

        /// <summary>
        /// Initializes a renderer that resolves .lbx template paths via <paramref name="configService"/>.
        /// </summary>
        public LabelPreviewRenderer(
            AppConfigService configService,
            Func<IReadOnlyList<string>>? installedPrintersProvider = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _installedPrintersProvider = installedPrintersProvider;
            _transparentPngPath = GetOrCreateTransparentPng();
        }

        /// <summary>
        /// Renders a preview image asynchronously without blocking the WinForms Main-STA UI thread.
        /// Uses an out-of-process worker when templates exist and the main app executable is available
        /// (because b-PAC's COM DLL has no ThreadingModel and otherwise marshals onto the UI STA thread).
        /// </summary>
        public async Task<Image> RenderTemplatePreviewAsync(
            LabelTemplateType slot,
            LabelTemplateProfile profile,
            SampleDetailsDto details,
            string freeText = "",
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(details);
            cancellationToken.ThrowIfCancellationRequested();

            string templatePath = _configService.GetTemplateFilePath(slot, profile);
            if (!File.Exists(templatePath))
            {
                return CreateFallbackPreviewBitmap(
                    slot,
                    profile,
                    $"Template missing:\n{Path.GetFileName(templatePath)}");
            }

            if (_installedPrintersProvider == null)
            {
                string? workerExe = ResolvePreviewWorkerExecutablePath();
                if (!string.IsNullOrEmpty(workerExe))
                {
                    return await RenderViaWorkerProcessAsync(
                        workerExe,
                        slot,
                        profile,
                        details,
                        freeText,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            await StaRenderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tcs = new TaskCompletionSource<Image>(TaskCreationOptions.RunContinuationsAsynchronously);
                var staThread = new Thread(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    try
                    {
                        Image image = RenderTemplatePreview(slot, profile, details, freeText);
                        tcs.TrySetResult(image);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                })
                {
                    IsBackground = true,
                    Name = $"bPACPreview_{slot}"
                };

                staThread.SetApartmentState(ApartmentState.STA);
                staThread.Start();

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                StaRenderGate.Release();
            }
        }

        private sealed class PreviewWorkerRequest
        {
            [JsonProperty("baseDir")]
            public string BaseDir { get; set; } = "";

            [JsonProperty("slot")]
            public LabelTemplateType Slot { get; set; }

            [JsonProperty("profile")]
            public LabelTemplateProfile Profile { get; set; }

            [JsonProperty("details")]
            public SampleDetailsDto? Details { get; set; }

            [JsonProperty("freeText")]
            public string FreeText { get; set; } = "";

            [JsonProperty("outPath")]
            public string OutPath { get; set; } = "";
        }

        private sealed class PreviewWorkerResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error")]
            public string Error { get; set; } = "";
        }

        /// <summary>
        /// Runs the headless single-request-per-line JSON preview loop in the worker child process.
        /// </summary>
        public static void RunPreviewWorkerLoop(TextReader input, TextWriter output)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(output);

            var renderersByBaseDir = new Dictionary<string, LabelPreviewRenderer>(StringComparer.OrdinalIgnoreCase);

            string? line;
            while ((line = input.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                PreviewWorkerResponse response;
                try
                {
                    PreviewWorkerRequest? req = JsonConvert.DeserializeObject<PreviewWorkerRequest>(line);
                    if (req == null || req.Details == null || string.IsNullOrWhiteSpace(req.OutPath))
                    {
                        response = new PreviewWorkerResponse { Success = false, Error = "Invalid preview worker request payload." };
                    }
                    else
                    {
                        string normalizedBaseDir = string.IsNullOrWhiteSpace(req.BaseDir)
                            ? AppDomain.CurrentDomain.BaseDirectory
                            : Path.GetFullPath(req.BaseDir);

                        if (!renderersByBaseDir.TryGetValue(normalizedBaseDir, out LabelPreviewRenderer? renderer))
                        {
                            renderer = new LabelPreviewRenderer(new AppConfigService(normalizedBaseDir));
                            renderersByBaseDir[normalizedBaseDir] = renderer;
                        }

                        string? outDir = Path.GetDirectoryName(req.OutPath);
                        if (!string.IsNullOrEmpty(outDir))
                        {
                            Directory.CreateDirectory(outDir);
                        }

                        using Image rendered = renderer.RenderTemplatePreview(
                            req.Slot,
                            req.Profile,
                            req.Details,
                            req.FreeText ?? "");
                        rendered.Save(req.OutPath, ImageFormat.Png);

                        response = new PreviewWorkerResponse { Success = true };
                    }
                }
                catch (Exception ex)
                {
                    response = new PreviewWorkerResponse { Success = false, Error = ex.Message };
                }

                output.WriteLine(JsonConvert.SerializeObject(response));
                output.Flush();
            }
        }

        private async Task<Image> RenderViaWorkerProcessAsync(
            string workerExePath,
            LabelTemplateType slot,
            LabelTemplateProfile profile,
            SampleDetailsDto details,
            string freeText,
            CancellationToken cancellationToken)
        {
            string outPath = Path.Combine(Path.GetTempPath(), $"openbis_worker_preview_{slot}_{Guid.NewGuid():N}.png");

            await WorkerPipeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                EnsurePreviewWorkerStarted(workerExePath);
                if (s_workerIn == null || s_workerOut == null)
                {
                    return CreateFallbackPreviewBitmap(slot, profile, "Preview worker failed to start.");
                }

                var request = new PreviewWorkerRequest
                {
                    BaseDir = Path.GetDirectoryName(_configService.TemplatesDirectoryPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    Slot = slot,
                    Profile = profile,
                    Details = details,
                    FreeText = freeText ?? "",
                    OutPath = outPath
                };

                await s_workerIn.WriteLineAsync(JsonConvert.SerializeObject(request)).ConfigureAwait(false);
                await s_workerIn.FlushAsync().ConfigureAwait(false);

                string? responseLine = await s_workerOut.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    ShutdownPreviewWorker();
                    return CreateFallbackPreviewBitmap(slot, profile, "Preview worker closed unexpectedly.");
                }

                PreviewWorkerResponse? response = JsonConvert.DeserializeObject<PreviewWorkerResponse>(responseLine);
                if (response == null || !response.Success || !File.Exists(outPath))
                {
                    string reason = string.IsNullOrWhiteSpace(response?.Error)
                        ? "Preview worker did not produce an image."
                        : response!.Error;
                    return CreateFallbackPreviewBitmap(slot, profile, reason);
                }

                using var fs = new FileStream(outPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using Image loaded = Image.FromStream(fs);
                return new Bitmap(loaded);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ShutdownPreviewWorker();
                return CreateFallbackPreviewBitmap(slot, profile, $"Preview unavailable: {ex.Message}");
            }
            finally
            {
                TryDeleteTempFile(outPath);
                WorkerPipeGate.Release();
            }
        }

        private static string? ResolvePreviewWorkerExecutablePath()
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath)
                && string.Equals(Path.GetFileName(processPath), "OpenBISLabelStudio.exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(processPath))
            {
                return processPath;
            }

            string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpenBISLabelStudio.exe");
            return File.Exists(candidate) ? candidate : null;
        }

        private static void EnsurePreviewWorkerStarted(string workerExePath)
        {
            lock (WorkerLifecycleLock)
            {
                if (s_workerProcess != null && !s_workerProcess.HasExited && s_workerIn != null && s_workerOut != null)
                {
                    return;
                }

                ShutdownPreviewWorkerUnsafe();

                var psi = new ProcessStartInfo
                {
                    FileName = workerExePath,
                    WorkingDirectory = Path.GetDirectoryName(workerExePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardInputEncoding = Utf8NoBom,
                    StandardOutputEncoding = Utf8NoBom
                };
                psi.ArgumentList.Add("--preview-worker");

                Process? proc = Process.Start(psi);
                if (proc == null)
                {
                    return;
                }

                proc.ErrorDataReceived += (_, _) => { };
                proc.BeginErrorReadLine();

                s_workerProcess = proc;
                s_workerIn = proc.StandardInput;
                s_workerOut = proc.StandardOutput;
            }
        }

        private static void ShutdownPreviewWorker()
        {
            lock (WorkerLifecycleLock)
            {
                ShutdownPreviewWorkerUnsafe();
            }
        }

        private static void ShutdownPreviewWorkerUnsafe()
        {
            try
            {
                s_workerIn?.Dispose();
            }
            catch
            {
                // Ignore pipe cleanup errors.
            }

            try
            {
                s_workerOut?.Dispose();
            }
            catch
            {
                // Ignore pipe cleanup errors.
            }

            if (s_workerProcess != null)
            {
                try
                {
                    if (!s_workerProcess.HasExited && !s_workerProcess.WaitForExit(150))
                    {
                        s_workerProcess.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore best-effort worker teardown errors.
                }

                try
                {
                    s_workerProcess.Dispose();
                }
                catch
                {
                    // Ignore process disposal errors.
                }
            }

            s_workerProcess = null;
            s_workerIn = null;
            s_workerOut = null;
        }

        /// <summary>
        /// Renders a preview image for <paramref name="slot"/> and <paramref name="profile"/>,
        /// returning a descriptive fallback bitmap when the template is missing or b-PAC fails.
        /// </summary>
        public Image RenderTemplatePreview(
            LabelTemplateType slot,
            LabelTemplateProfile profile,
            SampleDetailsDto details,
            string freeText = "")
        {
            ArgumentNullException.ThrowIfNull(details);

            string templatePath = _configService.GetTemplateFilePath(slot, profile);
            if (!File.Exists(templatePath))
            {
                return CreateFallbackPreviewBitmap(
                    slot,
                    profile,
                    $"Template missing:\n{Path.GetFileName(templatePath)}");
            }

            string? tempPreviewFile = null;
            string? tempOverrideLbx = null;
            bpac.Document? doc = null;

            try
            {
                bool hasPaperSize = TryReadLbxMetadata(
                    templatePath,
                    out int expectedWidthPx,
                    out int expectedHeightPx,
                    out string? designedPrinter);

                string effectiveTemplatePath = templatePath;
                IReadOnlyList<string> installedPrinters = _installedPrintersProvider?.Invoke() ?? GetSystemInstalledPrinters();
                string? overridePrinter = SelectPreviewPrinterOverride(designedPrinter, installedPrinters);
                if (!string.IsNullOrEmpty(overridePrinter))
                {
                    tempOverrideLbx = TryCreateTempLbxWithPrinter(templatePath, overridePrinter);
                    if (!string.IsNullOrEmpty(tempOverrideLbx))
                    {
                        effectiveTemplatePath = tempOverrideLbx;
                    }
                }

                doc = new bpac.Document();
                if (!doc.Open(effectiveTemplatePath))
                {
                    return CreateFallbackPreviewBitmap(
                        slot,
                        profile,
                        $"Could not open template:\n{Path.GetFileName(templatePath)}");
                }

                PopulateDocument(doc, slot, profile, details, freeText);

                tempPreviewFile = Path.Combine(
                    Path.GetTempPath(),
                    $"openbis_preview_{slot}_{Guid.NewGuid():N}.png");

                if (!doc.Export((bpac.ExportType)4, tempPreviewFile, ExportResolutionDpi) || !File.Exists(tempPreviewFile))
                {
                    return CreateFallbackPreviewBitmap(
                        slot,
                        profile,
                        $"Could not export preview:\n{Path.GetFileName(templatePath)}");
                }

                using var fs = new FileStream(tempPreviewFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                using Image original = Image.FromStream(fs);
                using var exportedBitmap = new Bitmap(original);

                return hasPaperSize
                    ? CropFallbackExportBitmap(exportedBitmap, expectedWidthPx, expectedHeightPx)
                    : new Bitmap(exportedBitmap);
            }
            catch (Exception ex)
            {
                return CreateFallbackPreviewBitmap(
                    slot,
                    profile,
                    $"Preview unavailable: {ex.Message}");
            }
            finally
            {
                if (doc != null)
                {
                    try
                    {
                        doc.Close();
                    }
                    catch
                    {
                        // Ignore b-PAC COM cleanup errors.
                    }
                }

                TryDeleteTempFile(tempPreviewFile);
                TryDeleteTempFile(tempOverrideLbx);
            }
        }

        /// <summary>
        /// Renders example label previews for all three slots under the specified profile.
        /// </summary>
        public Dictionary<LabelTemplateType, Image> RenderExamplePreviews(LabelTemplateProfile profile)
        {
            SampleDetailsDto example = BuildExampleSampleDetails(profile);
            string exampleFreeText = profile == LabelTemplateProfile.Generic ? "Batch #4 / Substrate" : "";

            var previews = new Dictionary<LabelTemplateType, Image>();
            foreach (LabelTemplateType slot in Enum.GetValues<LabelTemplateType>())
            {
                previews[slot] = RenderTemplatePreview(slot, profile, example, exampleFreeText);
            }

            return previews;
        }

        /// <summary>
        /// Builds realistic example sample data for previewing a template profile.
        /// </summary>
        public static SampleDetailsDto BuildExampleSampleDetails(LabelTemplateProfile profile)
        {
            return profile switch
            {
                LabelTemplateProfile.Molecule => new SampleDetailsDto
                {
                    PermId = "20261113-EX01",
                    SampleType = "SUBSTANCE",
                    Name = "CuPc-F16",
                    StudiedBy = "Jane Doe",
                    StudiedByName = "Jane Doe",
                    StudiedByUsername = "jdoe",
                    StorageLocation = "Cabinet A-3",
                    SupplierOwnName = "Sigma-Aldrich",
                    EvaporationTemperature = "520 K",
                    SpecialStorageConditions = new List<string> { "POISONOUS", "DRY" },
                    IconFlags = new IconFlagsDto
                    {
                        Poisonous = true,
                        Dry = true
                    }
                },
                LabelTemplateProfile.Generic => new SampleDetailsDto
                {
                    PermId = "20261113-EX02",
                    SampleType = "SAMPLE",
                    Name = "Sample-001",
                    StudiedBy = "Jane Doe",
                    StudiedByName = "Jane Doe",
                    StudiedByUsername = "jdoe"
                },
                _ => throw new ArgumentOutOfRangeException(nameof(profile), $"Unsupported template profile: {profile}")
            };
        }

        /// <summary>
        /// Draws a clear fallback bitmap showing the slot, profile, and reason when preview export fails.
        /// </summary>
        public static Bitmap CreateFallbackPreviewBitmap(
            LabelTemplateType slot,
            LabelTemplateProfile profile,
            string reason)
        {
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "Preview unavailable." : reason.Trim();
            return CreateSlotStatusBitmap(slot, profile, safeReason, Color.DimGray);
        }

        /// <summary>
        /// Draws a lightweight loading card while b-PAC renders the label preview off the UI thread.
        /// </summary>
        public static Bitmap CreateLoadingPreviewBitmap(
            LabelTemplateType slot,
            LabelTemplateProfile profile)
        {
            return CreateSlotStatusBitmap(slot, profile, "Rendering label preview...", Color.FromArgb(37, 99, 235));
        }

        /// <summary>
        /// Reads the designed label canvas dimensions (in pixels at 360 DPI) from an .lbx template archive.
        /// </summary>
        public static bool TryReadLbxPaperSizePx(string lbxPath, out int widthPx, out int heightPx)
        {
            return TryReadLbxMetadata(lbxPath, out widthPx, out heightPx, out _);
        }

        /// <summary>
        /// Selects a safe local preview printer when the template's saved Brother printer driver is missing,
        /// avoiding remote SMB spooler stalls and hardware margin clipping on fallback A4 printers.
        /// Returns null when the original .lbx should be opened unmodified.
        /// </summary>
        public static string? SelectPreviewPrinterOverride(string? designedPrinterName, IReadOnlyList<string>? installedPrinters)
        {
            if (installedPrinters == null || installedPrinters.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(designedPrinterName)
                && installedPrinters.Any(p => string.Equals(p?.Trim(), designedPrinterName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            string? brotherPrinter = installedPrinters.FirstOrDefault(
                p => !string.IsNullOrWhiteSpace(p) && p.Contains("Brother", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(brotherPrinter))
            {
                return brotherPrinter.Trim();
            }

            string? pdfPrinter = installedPrinters.FirstOrDefault(
                p => string.Equals(p?.Trim(), "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(pdfPrinter) ? null : pdfPrinter.Trim();
        }

        /// <summary>
        /// Creates a temporary copy of <paramref name="sourceLbxPath"/> whose paper printerName
        /// targets <paramref name="targetPrinterName"/>, leaving the original template file untouched.
        /// </summary>
        public static string? TryCreateTempLbxWithPrinter(string sourceLbxPath, string targetPrinterName)
        {
            if (string.IsNullOrWhiteSpace(sourceLbxPath)
                || !File.Exists(sourceLbxPath)
                || string.IsNullOrWhiteSpace(targetPrinterName))
            {
                return null;
            }

            string tempLbxPath = Path.Combine(Path.GetTempPath(), $"openbis_tpl_{Guid.NewGuid():N}.lbx");
            try
            {
                File.Copy(sourceLbxPath, tempLbxPath, overwrite: true);

                using (ZipArchive archive = ZipFile.Open(tempLbxPath, ZipArchiveMode.Update))
                {
                    ZipArchiveEntry? entry = archive.GetEntry("label.xml");
                    if (entry == null)
                    {
                        TryDeleteTempFile(tempLbxPath);
                        return null;
                    }

                    string xml;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        xml = reader.ReadToEnd();
                    }

                    string escapedPrinter = SecurityElement.Escape(targetPrinterName.Trim()) ?? "";
                    string updatedXml;
                    if (PaperPrinterAttrRegex.IsMatch(xml))
                    {
                        updatedXml = PaperPrinterAttrRegex.Replace(xml, m => m.Groups[1].Value + escapedPrinter + m.Groups[3].Value, 1);
                    }
                    else
                    {
                        TryDeleteTempFile(tempLbxPath);
                        return null;
                    }

                    entry.Delete();
                    ZipArchiveEntry updatedEntry = archive.CreateEntry("label.xml");
                    using var writer = new StreamWriter(updatedEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.Write(updatedXml);
                }

                return tempLbxPath;
            }
            catch
            {
                TryDeleteTempFile(tempLbxPath);
                return null;
            }
        }

        /// <summary>
        /// Crops oversized fallback exports (such as full-page A4 outputs when no Brother driver is installed)
        /// down to the template's designed label bounds while preserving native-sized exports untouched.
        /// </summary>
        public static Bitmap CropFallbackExportBitmap(Bitmap exportedBitmap, int expectedWidthPx, int expectedHeightPx)
        {
            ArgumentNullException.ThrowIfNull(exportedBitmap);

            if (expectedWidthPx <= 0 || expectedHeightPx <= 0)
            {
                return new Bitmap(exportedBitmap);
            }

            bool isOversizedFallback = exportedBitmap.Width > expectedWidthPx * 1.35
                                       || exportedBitmap.Height > expectedHeightPx * 1.35;
            if (!isOversizedFallback)
            {
                return new Bitmap(exportedBitmap);
            }

            int cropW = Math.Min(exportedBitmap.Width, expectedWidthPx);
            int cropH = Math.Min(exportedBitmap.Height, expectedHeightPx);

            int scanW = Math.Min(exportedBitmap.Width, expectedWidthPx * 2);
            int scanH = Math.Min(exportedBitmap.Height, expectedHeightPx * 2);

            BitmapData? bmpData = null;
            try
            {
                bmpData = exportedBitmap.LockBits(
                    new Rectangle(0, 0, scanW, scanH),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                int rowBytes = scanW * 4;
                byte[] rowBuffer = new byte[rowBytes];
                long scan0 = bmpData.Scan0.ToInt64();
                int maxInkX = -1;
                int maxInkY = -1;

                for (int y = 0; y < scanH; y++)
                {
                    Marshal.Copy(new IntPtr(scan0 + ((long)y * bmpData.Stride)), rowBuffer, 0, rowBytes);

                    for (int x = 0; x < scanW; x++)
                    {
                        int offset = x * 4;
                        byte b = rowBuffer[offset];
                        byte g = rowBuffer[offset + 1];
                        byte r = rowBuffer[offset + 2];
                        byte a = rowBuffer[offset + 3];

                        if (a > 10 && (r < 248 || g < 248 || b < 248))
                        {
                            if (x > maxInkX) maxInkX = x;
                            if (y > maxInkY) maxInkY = y;
                        }
                    }
                }

                if (maxInkX >= cropW)
                {
                    cropW = Math.Min(exportedBitmap.Width, maxInkX + 16);
                }

                if (maxInkY >= cropH)
                {
                    cropH = Math.Min(exportedBitmap.Height, maxInkY + 16);
                }
            }
            catch
            {
                // Fall back to designed paper crop bounds if LockBits fails.
            }
            finally
            {
                if (bmpData != null)
                {
                    exportedBitmap.UnlockBits(bmpData);
                }
            }

            Rectangle cropRect = new Rectangle(0, 0, Math.Max(1, cropW), Math.Max(1, cropH));
            return exportedBitmap.Clone(cropRect, PixelFormat.Format32bppArgb);
        }

        private static Bitmap CreateSlotStatusBitmap(
            LabelTemplateType slot,
            LabelTemplateProfile profile,
            string message,
            Color messageColor)
        {
            (int width, int height) = slot switch
            {
                LabelTemplateType.SmallBox12mm => (300, 120),
                LabelTemplateType.Magnet12mm => (320, 120),
                LabelTemplateType.Flask24mm => (320, 150),
                _ => (320, 120)
            };

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            using var borderPen = new Pen(Color.DarkGray, 1.5f);
            g.DrawRectangle(borderPen, 1, 1, width - 3, height - 3);

            string slotLabel = slot switch
            {
                LabelTemplateType.SmallBox12mm => "Small Box (12mm)",
                LabelTemplateType.Magnet12mm => "Magnet (12mm)",
                LabelTemplateType.Flask24mm => "Flask (24mm)",
                _ => slot.ToString()
            };

            string profileLabel = profile == LabelTemplateProfile.Molecule
                ? "Molecule / Substance"
                : "Generic / Free-text";

            using var titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            using var bodyFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            using var titleBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
            using var bodyBrush = new SolidBrush(messageColor);

            g.DrawString($"{slotLabel} — {profileLabel}", titleFont, titleBrush, new RectangleF(10, 10, width - 20, 24));
            g.DrawString(message, bodyFont, bodyBrush, new RectangleF(10, 38, width - 20, height - 48));

            return bitmap;
        }

        private static bool TryReadLbxMetadata(string lbxPath, out int widthPx, out int heightPx, out string? printerName)
        {
            widthPx = 0;
            heightPx = 0;
            printerName = null;

            if (string.IsNullOrWhiteSpace(lbxPath) || !File.Exists(lbxPath))
            {
                return false;
            }

            try
            {
                using ZipArchive archive = ZipFile.OpenRead(lbxPath);
                ZipArchiveEntry? entry = archive.GetEntry("label.xml");
                if (entry == null)
                {
                    return false;
                }

                using Stream stream = entry.Open();
                XDocument doc = XDocument.Load(stream);
                XElement? paper = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "paper");
                if (paper == null)
                {
                    return false;
                }

                printerName = paper.Attribute("printerName")?.Value;

                if (!TryParseLengthToPoints(paper.Attribute("width")?.Value, out double rawW)
                    || !TryParseLengthToPoints(paper.Attribute("height")?.Value, out double rawH))
                {
                    return false;
                }

                string orientation = paper.Attribute("orientation")?.Value ?? "";
                bool isLandscape = string.Equals(orientation, "landscape", StringComparison.OrdinalIgnoreCase);

                double horizontalPt = isLandscape ? rawH : rawW;
                double verticalPt = isLandscape ? rawW : rawH;

                widthPx = Math.Max(1, (int)Math.Round(horizontalPt * ExportResolutionDpi / 72.0, MidpointRounding.AwayFromZero));
                heightPx = Math.Max(1, (int)Math.Round(verticalPt * ExportResolutionDpi / 72.0, MidpointRounding.AwayFromZero));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseLengthToPoints(string? raw, out double points)
        {
            points = 0;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            Match match = LengthValueRegex.Match(raw);
            if (!match.Success
                || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || value <= 0)
            {
                return false;
            }

            string unit = match.Groups[2].Value.ToLowerInvariant();
            points = unit switch
            {
                "" or "pt" => value,
                "in" => value * 72.0,
                "mm" => value * 72.0 / 25.4,
                "cm" => value * 72.0 / 2.54,
                _ => 0
            };

            return points > 0;
        }

        private static IReadOnlyList<string> GetSystemInstalledPrinters()
        {
            var printers = new List<string>();
            try
            {
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    if (!string.IsNullOrWhiteSpace(printer))
                    {
                        printers.Add(printer);
                    }
                }
            }
            catch
            {
                // Return empty list if spooler enumeration fails.
            }

            return printers;
        }

        private static void TryDeleteTempFile(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // Ignore best-effort cleanup failure for transient temp files.
            }
        }

        /// <summary>
        /// Returns the path to a 1x1 transparent PNG in the temp directory used to hide disabled b-PAC icons.
        /// </summary>
        public string GetOrCreateTransparentPng()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), TransparentPngFileName);
                if (!File.Exists(path))
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), $"openbis_transparent_1x1_{Guid.NewGuid():N}.png");
                    try
                    {
                        using (var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
                        {
                            bmp.SetPixel(0, 0, Color.FromArgb(0, 255, 255, 255));
                            bmp.Save(tempPath, ImageFormat.Png);
                        }

                        try
                        {
                            File.Move(tempPath, path, overwrite: false);
                        }
                        catch (IOException) when (File.Exists(path))
                        {
                            // Another caller created the shared 1x1 transparent PNG concurrently.
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                        {
                            try
                            {
                                File.Delete(tempPath);
                            }
                            catch
                            {
                                // Ignore transient cleanup errors.
                            }
                        }
                    }
                }

                _transparentPngPath = path;
                return path;
            }
            catch
            {
                return "";
            }
        }

        private string EnsureTransparentPngPath()
        {
            if (!string.IsNullOrEmpty(_transparentPngPath) && File.Exists(_transparentPngPath))
            {
                return _transparentPngPath;
            }

            return GetOrCreateTransparentPng();
        }

        private void PopulateDocument(
            bpac.Document doc,
            LabelTemplateType slot,
            LabelTemplateProfile profile,
            SampleDetailsDto details,
            string freeText)
        {
            string transparentPngPath = EnsureTransparentPngPath();

            // 1. Common Name object
            bpac.Object? nameObj = doc.GetObject("name");
            if (nameObj != null)
            {
                nameObj.Text = details.Name ?? "";
            }

            // 2. Common Permid QR Code object
            bpac.Object? qrObj = doc.GetObject("PermidQRCode") ?? doc.GetObject("PermidCode");
            if (qrObj != null)
            {
                qrObj.Text = details.PermId ?? "";
            }

            if (profile == LabelTemplateProfile.Molecule)
            {
                // 3. Studied By (Small Box uses Username, Magnet uses Full Name)
                bpac.Object? studiedByObj = doc.GetObject("studiedBy");
                if (studiedByObj != null)
                {
                    if (slot == LabelTemplateType.SmallBox12mm)
                    {
                        studiedByObj.Text = !string.IsNullOrEmpty(details.StudiedByUsername)
                            ? details.StudiedByUsername
                            : (details.StudiedBy ?? "");
                    }
                    else if (slot == LabelTemplateType.Magnet12mm)
                    {
                        studiedByObj.Text = !string.IsNullOrEmpty(details.StudiedByName)
                            ? details.StudiedByName
                            : (details.StudiedBy ?? "");
                    }
                    else
                    {
                        studiedByObj.Text = details.StudiedBy ?? "";
                    }
                }

                // 4. Evaporation Temperature (populated for molecule templates that include it)
                bpac.Object? evapTempObj = doc.GetObject("evaporationTemperature");
                if (evapTempObj != null)
                {
                    evapTempObj.Text = details.EvaporationTemperature ?? "";
                }

                // 5. Magnet Icons for special storage conditions
                if (slot == LabelTemplateType.Magnet12mm)
                {
                    IconFlagsDto flags = details.IconFlags ?? new IconFlagsDto();
                    var iconMappings = new (string ObjectName, bool IsVisible)[]
                    {
                        ("poisonousIcon", flags.Poisonous),
                        ("flammableIcon", flags.Flammable),
                        ("darkIcon", flags.Dark),
                        ("dryIcon", flags.Dry),
                        ("freezerIcon", flags.Freezer),
                        ("fridgeIcon", flags.Fridge),
                        ("noOxygenIcon", flags.NoOxygen),
                        // Support both XML naming variations
                        ("tightlyCloseIcon", flags.TightlyClosed),
                        ("tightlyClosedIcon", flags.TightlyClosed)
                    };

                    foreach (var (objectName, isVisible) in iconMappings)
                    {
                        SetIconVisibility(doc, objectName, isVisible, transparentPngPath);
                    }
                }

                // 6. Flask 24mm Specific Fields
                if (slot == LabelTemplateType.Flask24mm)
                {
                    bpac.Object? storageLocObj = doc.GetObject("storageLocation");
                    if (storageLocObj != null)
                    {
                        storageLocObj.Text = details.StorageLocation ?? "";
                    }

                    bpac.Object? supplierNameObj = doc.GetObject("supplierOwnName");
                    if (supplierNameObj != null)
                    {
                        supplierNameObj.Text = details.SupplierOwnName ?? "";
                    }

                    bpac.Object? elnObj = doc.GetObject("elnPreview");
                    if (elnObj != null)
                    {
                        if (!string.IsNullOrEmpty(details.ElnPreviewPath) && File.Exists(details.ElnPreviewPath))
                        {
                            elnObj.SetData(0, details.ElnPreviewPath, 4);
                        }
                        else if (!string.IsNullOrEmpty(transparentPngPath) && File.Exists(transparentPngPath))
                        {
                            elnObj.SetData(0, transparentPngPath, 4);
                        }
                    }
                }
            }
            else
            {
                // Generic profile handling (FreeText support)
                string effectiveFreeText = string.IsNullOrWhiteSpace(freeText)
                    ? DefaultFreeTextPlaceholder
                    : freeText;

                if (slot == LabelTemplateType.Magnet12mm)
                {
                    bpac.Object? freeTextObj = doc.GetObject("freeText") ?? doc.GetObject("samplePlate");
                    if (freeTextObj != null)
                    {
                        freeTextObj.Text = effectiveFreeText;
                    }
                }
                else if (slot == LabelTemplateType.Flask24mm)
                {
                    bpac.Object? freeTextObj = doc.GetObject("freeText");
                    if (freeTextObj != null)
                    {
                        freeTextObj.Text = effectiveFreeText;
                    }
                }
            }
        }

        private static void SetIconVisibility(
            bpac.Document doc,
            string objectName,
            bool isVisible,
            string transparentPngPath)
        {
            bpac.Object? iconObj = doc.GetObject(objectName);
            if (iconObj != null && !isVisible && !string.IsNullOrEmpty(transparentPngPath) && File.Exists(transparentPngPath))
            {
                iconObj.SetData(0, transparentPngPath, 4);
            }
        }
    }
}
