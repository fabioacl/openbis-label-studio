using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenBISLabelStudio.Services
{
    /// <summary>
    /// Preprocesses ELN chemical structure images by trimming excessive outer whitespace,
    /// safely bounding oversized uploads, and converting colored/anti-aliased strokes into
    /// crisp dark ink so small 180-DPI labels and expanded high-DPI UI previews remain sharp.
    /// </summary>
    public static class ElnImageProcessor
    {
        private const int MaxScanDimensionPx = 1400;
        private const int BinaryPrintInkCutoff = 195;
        private const int PrintCanvasWidthPx = 720;

        private static readonly byte[] PreviewInkGammaLut = BuildPreviewInkGammaLut();

        /// <summary>
        /// Loads <paramref name="sourceImagePath"/>, trims surrounding white/transparent padding,
        /// converts colored heteroatom and thin strokes to crisp anti-aliased dark ink, and fits
        /// the result proportionally within <paramref name="targetBoxWidthPx"/> by
        /// <paramref name="targetBoxHeightPx"/>. Returns <c>null</c> if missing or blank.
        /// </summary>
        public static Bitmap? BuildPreparedElnPreviewBitmap(
            string sourceImagePath,
            int targetBoxWidthPx,
            int targetBoxHeightPx)
        {
            if (targetBoxWidthPx <= 0 || targetBoxHeightPx <= 0)
            {
                return null;
            }

            using Bitmap? trimmed = LoadAndTrimToContent(sourceImagePath);
            if (trimmed == null)
            {
                return null;
            }

            ApplyCrispMonochromeInkCurve(trimmed, binaryOutput: false);

            double fitScale = Math.Min(
                (double)targetBoxWidthPx / trimmed.Width,
                (double)targetBoxHeightPx / trimmed.Height);
            int destW = Math.Max(1, (int)Math.Round(trimmed.Width * fitScale));
            int destH = Math.Max(1, (int)Math.Round(trimmed.Height * fitScale));

            var output = new Bitmap(destW, destH, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(output);
            ConfigureHighQualityGraphics(g);
            g.Clear(Color.White);
            g.DrawImage(
                trimmed,
                new Rectangle(0, 0, destW, destH),
                0,
                0,
                trimmed.Width,
                trimmed.Height,
                GraphicsUnit.Pixel);
            return output;
        }

        /// <summary>
        /// Renders <paramref name="sourceImagePath"/> trimmed, vertically/horizontally centered on a
        /// white canvas matching <paramref name="targetWidthPt"/>:<paramref name="targetHeightPt"/>,
        /// and quantized to crisp black-on-white thermal print ink as a temporary PNG path.
        /// </summary>
        public static string? TryCreatePreparedPrintPng(
            string sourceImagePath,
            double targetWidthPt,
            double targetHeightPt)
        {
            if (targetWidthPt <= 0 || targetHeightPt <= 0)
            {
                return null;
            }

            using Bitmap? trimmed = LoadAndTrimToContent(sourceImagePath);
            if (trimmed == null)
            {
                return null;
            }

            int canvasW = PrintCanvasWidthPx;
            int canvasH = Math.Max(64, (int)Math.Round(PrintCanvasWidthPx * (targetHeightPt / targetWidthPt)));
            double innerW = canvasW * 0.94;
            double innerH = canvasH * 0.94;
            double fitScale = Math.Min(innerW / trimmed.Width, innerH / trimmed.Height);
            int drawW = Math.Max(1, (int)Math.Round(trimmed.Width * fitScale));
            int drawH = Math.Max(1, (int)Math.Round(trimmed.Height * fitScale));
            int drawX = (canvasW - drawW) / 2;
            int drawY = (canvasH - drawH) / 2;

            using var printCanvas = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(printCanvas))
            {
                ConfigureHighQualityGraphics(g);
                g.Clear(Color.White);
                g.DrawImage(
                    trimmed,
                    new Rectangle(drawX, drawY, drawW, drawH),
                    0,
                    0,
                    trimmed.Width,
                    trimmed.Height,
                    GraphicsUnit.Pixel);
            }

            // Threshold after resampling so 180-DPI thermal rasterization receives solid #000000 ink without dithering noise.
            ApplyCrispMonochromeInkCurve(printCanvas, binaryOutput: true);

            string tempPath = Path.Combine(Path.GetTempPath(), $"openbis_eln_print_{Guid.NewGuid():N}.png");
            try
            {
                printCanvas.Save(tempPath, ImageFormat.Png);
                return tempPath;
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                return null;
            }
        }

        private static Bitmap? LoadAndTrimToContent(string sourceImagePath)
        {
            using Bitmap? bounded = LoadBoundedWhiteBackedArgb(sourceImagePath);
            if (bounded == null)
            {
                return null;
            }

            Rectangle inkBounds = FindInkBounds(bounded);
            if (inkBounds.IsEmpty)
            {
                return null;
            }

            return bounded.Clone(inkBounds, PixelFormat.Format32bppArgb);
        }

        private static Bitmap? LoadBoundedWhiteBackedArgb(string sourceImagePath)
        {
            if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
            {
                return null;
            }

            try
            {
                using var fs = new FileStream(sourceImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using Image original = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: true);
                if (original.Width <= 0 || original.Height <= 0)
                {
                    return null;
                }

                int maxEdge = Math.Max(original.Width, original.Height);
                double scale = maxEdge > MaxScanDimensionPx ? (double)MaxScanDimensionPx / maxEdge : 1.0;
                int targetW = Math.Max(1, (int)Math.Round(original.Width * scale));
                int targetH = Math.Max(1, (int)Math.Round(original.Height * scale));

                var whiteCanvas = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
                using Graphics g = Graphics.FromImage(whiteCanvas);
                ConfigureHighQualityGraphics(g);
                g.Clear(Color.White);
                g.DrawImage(
                    original,
                    new Rectangle(0, 0, targetW, targetH),
                    0,
                    0,
                    original.Width,
                    original.Height,
                    GraphicsUnit.Pixel);
                return whiteCanvas;
            }
            catch
            {
                return null;
            }
        }

        private static Rectangle FindInkBounds(Bitmap whiteBackedArgb)
        {
            int w = whiteBackedArgb.Width;
            int h = whiteBackedArgb.Height;
            int minX = w;
            int minY = h;
            int maxX = -1;
            int maxY = -1;

            BitmapData? data = null;
            try
            {
                data = whiteBackedArgb.LockBits(
                    new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                int rowBytes = w * 4;
                byte[] rowBuffer = new byte[rowBytes];
                long scan0 = data.Scan0.ToInt64();

                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(scan0 + ((long)y * data.Stride)), rowBuffer, 0, rowBytes);
                    for (int x = 0; x < w; x++)
                    {
                        int offset = x * 4;
                        byte b = rowBuffer[offset];
                        byte g = rowBuffer[offset + 1];
                        byte r = rowBuffer[offset + 2];
                        if (Math.Min(r, Math.Min(g, b)) < 245)
                        {
                            if (x < minX) minX = x;
                            if (y < minY) minY = y;
                            if (x > maxX) maxX = x;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
            finally
            {
                if (data != null)
                {
                    whiteBackedArgb.UnlockBits(data);
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return Rectangle.Empty;
            }

            int contentSpan = Math.Max(maxX - minX + 1, maxY - minY + 1);
            int pad = Math.Clamp((int)Math.Round(contentSpan * 0.025), 2, 16);
            int cropX = Math.Max(0, minX - pad);
            int cropY = Math.Max(0, minY - pad);
            int cropRight = Math.Min(w, maxX + pad + 1);
            int cropBottom = Math.Min(h, maxY + pad + 1);
            return Rectangle.FromLTRB(cropX, cropY, cropRight, cropBottom);
        }

        private static void ApplyCrispMonochromeInkCurve(Bitmap whiteBackedArgb, bool binaryOutput)
        {
            int w = whiteBackedArgb.Width;
            int h = whiteBackedArgb.Height;

            BitmapData? data = null;
            try
            {
                data = whiteBackedArgb.LockBits(
                    new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadWrite,
                    PixelFormat.Format32bppArgb);

                int rowBytes = w * 4;
                byte[] rowBuffer = new byte[rowBytes];
                long scan0 = data.Scan0.ToInt64();

                for (int y = 0; y < h; y++)
                {
                    IntPtr rowPtr = new IntPtr(scan0 + ((long)y * data.Stride));
                    Marshal.Copy(rowPtr, rowBuffer, 0, rowBytes);

                    for (int x = 0; x < w; x++)
                    {
                        int offset = x * 4;
                        byte b = rowBuffer[offset];
                        byte g = rowBuffer[offset + 1];
                        byte r = rowBuffer[offset + 2];

                        byte minC = Math.Min(r, Math.Min(g, b));
                        byte maxC = Math.Max(r, Math.Max(g, b));
                        int lum = (30 * r + 59 * g + 11 * b) / 100;

                        // Weight lowest channel heavily when chromatic so blue/red/purple heteroatoms become dark ink.
                        int effectiveInk = (maxC - minC) > 25 ? (minC + (lum - minC) / 6) : lum;
                        effectiveInk = Math.Clamp(effectiveInk, 0, 255);

                        byte outVal = binaryOutput
                            ? (effectiveInk < BinaryPrintInkCutoff ? (byte)0 : (byte)255)
                            : PreviewInkGammaLut[effectiveInk];

                        rowBuffer[offset] = outVal;
                        rowBuffer[offset + 1] = outVal;
                        rowBuffer[offset + 2] = outVal;
                        rowBuffer[offset + 3] = 255;
                    }

                    Marshal.Copy(rowBuffer, 0, rowPtr, rowBytes);
                }
            }
            finally
            {
                if (data != null)
                {
                    whiteBackedArgb.UnlockBits(data);
                }
            }
        }

        private static byte[] BuildPreviewInkGammaLut()
        {
            var lut = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                if (i <= 28)
                {
                    lut[i] = 0;
                }
                else if (i >= 243)
                {
                    lut[i] = 255;
                }
                else
                {
                    double norm = (i - 28.0) / 215.0;
                    lut[i] = (byte)Math.Clamp((int)Math.Round(Math.Pow(norm, 1.35) * 255.0), 0, 255);
                }
            }

            return lut;
        }

        internal static void ConfigureHighQualityGraphics(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
    }
}
