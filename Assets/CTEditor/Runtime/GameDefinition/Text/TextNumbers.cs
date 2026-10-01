using System;
using System.Globalization;

namespace CTEditor.GameDefinition.Text
{
    /// <summary>Numbers as people write them in Spanish sheets: «0,5» or «0.5».</summary>
    public static class TextNumbers
    {
        public static bool TryNumber(string s, out float value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return float.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryInt(string s, out int value)
        {
            value = 0;
            if (TryNumber(s, out var f) && Math.Abs(f - Math.Round(f)) < 1e-4) { value = (int)Math.Round(f); return true; }
            return false;
        }
    }
}
