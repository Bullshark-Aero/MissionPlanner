using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using MissionPlanner.Warnings;

namespace MissionPlanner.BSA.Alerts
{
    public static class AlertColours
    {
        public const string CustomChoice = "Custom...";
        public const string DefaultColour = "Red";

        public static IReadOnlyList<string> Named { get; } =
            Enum.GetNames(typeof(CustomWarning.WarningColors)).Where(n => n != AlertEvaluator.NoColour).ToList();

        public static bool TryParse(string text, out Color colour)
        {
            colour = Color.Empty;
            var value = (text ?? "").Trim();
            if (value.Length == 7 && value[0] == '#' &&
                int.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                colour = Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                return true;
            }
            if (value.Length == 0 || value == AlertEvaluator.NoColour || value[0] == '#') return false;
            var named = Color.FromName(value);
            if (!named.IsKnownColor) return false;
            colour = named;
            return true;
        }

        public static string ToText(Color colour) =>
            "#" + colour.R.ToString("X2", CultureInfo.InvariantCulture) +
            colour.G.ToString("X2", CultureInfo.InvariantCulture) +
            colour.B.ToString("X2", CultureInfo.InvariantCulture);

        public static string Normalize(string text)
        {
            if (!TryParse(text, out var colour)) return DefaultColour;
            var value = text.Trim();
            return value[0] == '#' ? ToText(colour) : value;
        }

        public static Color ForQuickView(string text) =>
            TryParse(text, out var colour) ? colour : Color.FromName(text ?? "");
    }
}
