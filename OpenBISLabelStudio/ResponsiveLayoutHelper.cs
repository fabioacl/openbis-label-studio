using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenBISLabelStudio
{
    /// <summary>
    /// Shared geometry calculations for proportional multi-column card and footer layouts across WinForms windows.
    /// </summary>
    internal static class ResponsiveLayoutHelper
    {
        /// <summary>
        /// Splits the horizontal span [<paramref name="left"/>, <paramref name="right"/>) into
        /// <paramref name="columnCount"/> equal-width slots separated by <paramref name="gap"/>,
        /// distributing any remainder pixels cleanly so the final slot aligns with <paramref name="right"/>.
        /// </summary>
        public static (int Left, int Width)[] ComputeHorizontalColumns(int left, int right, int gap, int columnCount)
        {
            if (columnCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columnCount));
            }

            int safeGap = Math.Max(0, gap);
            int totalWidth = Math.Max(0, right - left);
            int totalGaps = (columnCount - 1) * safeGap;
            int available = Math.Max(columnCount, totalWidth - totalGaps);
            int baseWidth = available / columnCount;
            int remainder = available % columnCount;

            var columns = new (int Left, int Width)[columnCount];
            int cursorX = left;
            for (int i = 0; i < columnCount; i++)
            {
                int colWidth = baseWidth + (i < remainder ? 1 : 0);
                columns[i] = (cursorX, colWidth);
                cursorX += colWidth + safeGap;
            }

            return columns;
        }

        /// <summary>
        /// Sizes and positions a row of <paramref name="controls"/> to fill [<paramref name="left"/>, <paramref name="right"/>)
        /// evenly at the specified vertical span.
        /// </summary>
        public static void LayoutEqualRow(int left, int right, int top, int height, int gap, params Control[] controls)
        {
            if (controls == null || controls.Length == 0)
            {
                return;
            }

            (int Left, int Width)[] columns = ComputeHorizontalColumns(left, right, gap, controls.Length);
            int safeHeight = Math.Max(1, height);
            for (int i = 0; i < controls.Length; i++)
            {
                controls[i].Bounds = new Rectangle(columns[i].Left, top, columns[i].Width, safeHeight);
            }
        }

        /// <summary>
        /// Horizontally centers a row of <paramref name="buttons"/> within <paramref name="containerWidth"/>
        /// while preserving each button's DPI-scaled width and height.
        /// </summary>
        public static void CenterHorizontalRow(int containerWidth, int top, int gap, params Control[] buttons)
        {
            if (buttons == null || buttons.Length == 0)
            {
                return;
            }

            int safeGap = Math.Max(0, gap);
            int totalWidth = (buttons.Length - 1) * safeGap;
            for (int i = 0; i < buttons.Length; i++)
            {
                totalWidth += buttons[i].Width;
            }

            int cursorX = Math.Max(0, (containerWidth - totalWidth) / 2);
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Location = new Point(cursorX, top);
                cursorX += buttons[i].Width + safeGap;
            }
        }

        /// <summary>
        /// Splits <paramref name="totalAvailableHeight"/> between two vertically stacked panels
        /// according to their baseline ratio while honoring minimum heights.
        /// </summary>
        public static (int FirstHeight, int SecondHeight) ComputeResponsiveSplit(
            int totalAvailableHeight,
            int baselineFirstHeight,
            int baselineSecondHeight,
            int minFirstHeight,
            int minSecondHeight)
        {
            int safeTotal = Math.Max(minFirstHeight + minSecondHeight, totalAvailableHeight);
            int baselineSum = Math.Max(1, baselineFirstHeight + baselineSecondHeight);
            double firstRatio = (double)baselineFirstHeight / baselineSum;

            int first = (int)Math.Round(safeTotal * firstRatio);
            int maxFirst = Math.Max(minFirstHeight, safeTotal - minSecondHeight);
            first = Math.Clamp(first, minFirstHeight, maxFirst);
            int second = Math.Max(minSecondHeight, safeTotal - first);
            return (first, second);
        }
    }
}
