using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace OpenBISLabelStudio.Services
{
    using OpenBISLabelStudio.Models;

    /// <summary>
    /// Populates Brother b-PAC label templates for Molecule and Generic profiles, exports preview
    /// bitmaps, and provides COM-safe fallback images and realistic example sample payloads.
    /// </summary>
    public class LabelPreviewRenderer
    {
        private const string DefaultFreeTextPlaceholder = "Free text placeholder";
        private const string TransparentPngFileName = "openbis_transparent_1x1.png";
        private readonly AppConfigService _configService;
        private string _transparentPngPath = "";

        /// <summary>
        /// Initializes a renderer that resolves .lbx template paths via <paramref name="configService"/>.
        /// </summary>
        public LabelPreviewRenderer(AppConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _transparentPngPath = GetOrCreateTransparentPng();
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
            bpac.Document? doc = null;

            try
            {
                doc = new bpac.Document();
                if (!doc.Open(templatePath))
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

                if (!doc.Export((bpac.ExportType)4, tempPreviewFile, 360) || !File.Exists(tempPreviewFile))
                {
                    return CreateFallbackPreviewBitmap(
                        slot,
                        profile,
                        $"Could not export preview:\n{Path.GetFileName(templatePath)}");
                }

                using var fs = new FileStream(tempPreviewFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                using Image original = Image.FromStream(fs);
                return new Bitmap(original);
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

                if (!string.IsNullOrEmpty(tempPreviewFile) && File.Exists(tempPreviewFile))
                {
                    try
                    {
                        File.Delete(tempPreviewFile);
                    }
                    catch
                    {
                        // Ignore best-effort cleanup failure for temp preview PNG.
                    }
                }
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
            using var bodyBrush = new SolidBrush(Color.DimGray);

            g.DrawString($"{slotLabel} — {profileLabel}", titleFont, titleBrush, new RectangleF(10, 10, width - 20, 24));

            string safeReason = string.IsNullOrWhiteSpace(reason) ? "Preview unavailable." : reason.Trim();
            g.DrawString(safeReason, bodyFont, bodyBrush, new RectangleF(10, 38, width - 20, height - 48));

            return bitmap;
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
