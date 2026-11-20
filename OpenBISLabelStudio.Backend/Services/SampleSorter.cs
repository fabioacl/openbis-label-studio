using System;
using System.Collections.Generic;
using System.Globalization;
using OpenBISLabelStudio.Backend.Contracts;

namespace OpenBISLabelStudio.Backend.Services
{
    /// <summary>
    /// Pure in-memory sorter for loaded openBIS <see cref="SampleSummaryDto"/> entries.
    /// </summary>
    public static class SampleSorter
    {
        /// <summary>
        /// Returns a sorted copy of <paramref name="samples"/> without mutating the caller's collection.
        /// </summary>
        public static IReadOnlyList<SampleSummaryDto> Sort(
            IReadOnlyList<SampleSummaryDto>? samples,
            SampleSortField sortBy,
            SampleSortDirection direction)
        {
            if (samples == null || samples.Count == 0)
            {
                return Array.Empty<SampleSummaryDto>();
            }

            var result = new List<SampleSummaryDto>(samples.Count);
            foreach (SampleSummaryDto? sample in samples)
            {
                if (sample != null)
                {
                    result.Add(sample);
                }
            }

            if (result.Count == 0)
            {
                return Array.Empty<SampleSummaryDto>();
            }

            result.Sort((left, right) => CompareSamples(left, right, sortBy, direction));
            return result.AsReadOnly();
        }

        internal static int CompareNatural(string? left, string? right)
        {
            ReadOnlySpan<char> spanA = (left ?? "").AsSpan();
            ReadOnlySpan<char> spanB = (right ?? "").AsSpan();
            int i = 0;
            int j = 0;

            while (i < spanA.Length && j < spanB.Length)
            {
                bool digitA = char.IsAsciiDigit(spanA[i]);
                bool digitB = char.IsAsciiDigit(spanB[j]);

                if (digitA && digitB)
                {
                    int endA = i;
                    while (endA < spanA.Length && char.IsAsciiDigit(spanA[endA]))
                    {
                        endA++;
                    }

                    int endB = j;
                    while (endB < spanB.Length && char.IsAsciiDigit(spanB[endB]))
                    {
                        endB++;
                    }

                    ReadOnlySpan<char> significantA = spanA[i..endA].TrimStart('0');
                    ReadOnlySpan<char> significantB = spanB[j..endB].TrimStart('0');

                    int lenCmp = significantA.Length.CompareTo(significantB.Length);
                    if (lenCmp != 0)
                    {
                        return lenCmp;
                    }

                    int valCmp = significantA.CompareTo(significantB, StringComparison.Ordinal);
                    if (valCmp != 0)
                    {
                        return valCmp;
                    }

                    i = endA;
                    j = endB;
                    continue;
                }

                if (!digitA && !digitB)
                {
                    int endA = i;
                    while (endA < spanA.Length && !char.IsAsciiDigit(spanA[endA]))
                    {
                        endA++;
                    }

                    int endB = j;
                    while (endB < spanB.Length && !char.IsAsciiDigit(spanB[endB]))
                    {
                        endB++;
                    }

                    int textCmp = CultureInfo.CurrentCulture.CompareInfo.Compare(
                        spanA[i..endA],
                        spanB[j..endB],
                        CompareOptions.IgnoreCase);
                    if (textCmp != 0)
                    {
                        return textCmp;
                    }

                    int ordinalCmp = spanA[i..endA].CompareTo(spanB[j..endB], StringComparison.OrdinalIgnoreCase);
                    if (ordinalCmp != 0)
                    {
                        return ordinalCmp;
                    }

                    i = endA;
                    j = endB;
                    continue;
                }

                int mixedCmp = CultureInfo.CurrentCulture.CompareInfo.Compare(
                    spanA.Slice(i, 1),
                    spanB.Slice(j, 1),
                    CompareOptions.IgnoreCase);
                if (mixedCmp != 0)
                {
                    return mixedCmp;
                }

                int mixedOrdinalCmp = spanA.Slice(i, 1).CompareTo(spanB.Slice(j, 1), StringComparison.OrdinalIgnoreCase);
                if (mixedOrdinalCmp != 0)
                {
                    return mixedOrdinalCmp;
                }

                i++;
                j++;
            }

            if (i < spanA.Length || j < spanB.Length)
            {
                return i < spanA.Length ? 1 : -1;
            }

            int rawIgnoreCaseCmp = spanA.CompareTo(spanB, StringComparison.OrdinalIgnoreCase);
            if (rawIgnoreCaseCmp != 0)
            {
                return rawIgnoreCaseCmp;
            }

            return spanA.CompareTo(spanB, StringComparison.Ordinal);
        }

        private static int CompareSamples(
            SampleSummaryDto left,
            SampleSummaryDto right,
            SampleSortField sortBy,
            SampleSortDirection direction)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            int dir = direction == SampleSortDirection.Descending ? -1 : 1;
            int primaryCmp = sortBy switch
            {
                SampleSortField.Name => CompareNonBlankThenNatural(left.Name, right.Name, dir),
                SampleSortField.PermId => CompareNonBlankThenNatural(left.PermId, right.PermId, dir),
                _ => CompareModificationDates(left.ModificationDate, right.ModificationDate, dir)
            };

            if (primaryCmp != 0)
            {
                return primaryCmp;
            }

            if (sortBy != SampleSortField.PermId)
            {
                int permTie = CompareNonBlankThenNatural(left.PermId, right.PermId, dir);
                if (permTie != 0)
                {
                    return permTie;
                }
            }

            int rawPermTie = string.Compare(left.PermId, right.PermId, StringComparison.Ordinal) * dir;
            if (rawPermTie != 0)
            {
                return rawPermTie;
            }

            return string.Compare(left.Name, right.Name, StringComparison.Ordinal) * dir;
        }

        private static int CompareNonBlankThenNatural(string? left, string? right, int dir)
        {
            bool leftBlank = string.IsNullOrWhiteSpace(left);
            bool rightBlank = string.IsNullOrWhiteSpace(right);
            if (leftBlank != rightBlank)
            {
                return leftBlank ? 1 : -1;
            }

            if (leftBlank)
            {
                return 0;
            }

            return CompareNatural(left, right) * dir;
        }

        private static int CompareModificationDates(string? left, string? right, int dir)
        {
            bool leftValid = TryParseModificationDate(left, out DateTimeOffset leftDate);
            bool rightValid = TryParseModificationDate(right, out DateTimeOffset rightDate);
            if (leftValid != rightValid)
            {
                return leftValid ? -1 : 1;
            }

            if (leftValid)
            {
                int dateCmp = leftDate.CompareTo(rightDate);
                return dateCmp == 0 ? 0 : dateCmp * dir;
            }

            bool leftBlank = string.IsNullOrWhiteSpace(left);
            bool rightBlank = string.IsNullOrWhiteSpace(right);
            if (leftBlank != rightBlank)
            {
                return leftBlank ? 1 : -1;
            }

            if (leftBlank)
            {
                return 0;
            }

            int invalidTextCmp = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return invalidTextCmp == 0 ? 0 : invalidTextCmp * dir;
        }

        private static bool TryParseModificationDate(string? rawValue, out DateTimeOffset parsed)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                parsed = default;
                return false;
            }

            return DateTimeOffset.TryParse(
                rawValue.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed);
        }
    }
}
