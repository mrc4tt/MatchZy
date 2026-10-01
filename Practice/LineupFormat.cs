using System;
using System.Globalization;

namespace MatchZy
{
    // How saved lineups (savednades.json, grenadelibrary.json) store a position or an angle: three
    // numbers separated by spaces. Written with a '.' decimal separator so the file reads the same on
    // every server locale. Older files written on a ',' locale (e.g. "123,5 -40,25 8") still load.
    public static class LineupFormat
    {
        public static string Format(float x, float y, float z)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{x} {y} {z}");
        }

        public static bool TryParseNumber(string? text, out float value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            // No thousands separators are ever written, so a ',' can only be a decimal separator.
            // Some locales (sv, nb, fi, ...) wrote U+2212 as the minus sign in older files.
            return float.TryParse(text.Trim().Replace(',', '.').Replace('\u2212', '-'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && float.IsFinite(value);
        }

        public static bool TryParse(string? text, out float x, out float y, out float z)
        {
            x = y = z = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                return false;
            if (TryParseNumber(parts[0], out x) && TryParseNumber(parts[1], out y) && TryParseNumber(parts[2], out z))
                return true;
            x = y = z = 0;
            return false;
        }
    }
}
