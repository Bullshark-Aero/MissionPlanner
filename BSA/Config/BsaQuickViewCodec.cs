using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.BSA.Config
{
    /// <summary>
    /// Converts Flight Data's flat settings into a portable QuickView profile. Named values are
    /// persisted by MAV_* identity, never by the session-dependent customfield slot.
    /// </summary>
    public static class BsaQuickViewCodec
    {
        const string Prefix = "quickView";

        public static BsaQuickViewProfile Export(IReadOnlyDictionary<string, string> settings,
            IReadOnlyDictionary<string, string> customFieldNames)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (customFieldNames == null) throw new ArgumentNullException(nameof(customFieldNames));

            var rows = ReadPositiveInt(settings, "quickViewRows", 6);
            var columns = ReadPositiveInt(settings, "quickViewCols", 5);
            var profile = new BsaQuickViewProfile { Rows = rows, Columns = columns };

            for (var position = 1; position <= rows * columns; position++)
            {
                var key = Prefix + position;
                settings.TryGetValue(key, out var source);
                source = StableSourceId(source, customFieldNames);
                profile.Cells.Add(new BsaQuickViewCell
                {
                    Position = position,
                    SourceId = source,
                    Label = ValueOrNull(settings, key + "_label"),
                    LabelColor = ValueOrNull(settings, key + "_labelcolor"),
                    ValueColor = ValueOrNull(settings, key + "_valuecolor"),
                    Visible = !ReadBoolean(settings, key + "_blank")
                });
            }

            Validate(profile);
            return profile;
        }

        public static List<string> Apply(IDictionary<string, string> settings, BsaQuickViewProfile profile)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            Validate(profile);
            var changed = new List<string>();

            Set(settings, "quickViewRows", profile.Rows.ToString(), changed);
            Set(settings, "quickViewCols", profile.Columns.ToString(), changed);
            foreach (var cell in profile.Cells.OrderBy(c => c.Position))
            {
                var key = Prefix + cell.Position;
                SetOrRemove(settings, key, cell.SourceId, changed);
                SetOrRemove(settings, key + "_label", cell.Label, changed);
                SetOrRemove(settings, key + "_labelcolor", cell.LabelColor, changed);
                SetOrRemove(settings, key + "_valuecolor", cell.ValueColor, changed);
                Set(settings, key + "_blank", (!cell.Visible).ToString(), changed);
            }

            return changed;
        }

        public static bool OwnsSetting(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (key == "quickViewRows" || key == "quickViewCols" || key.StartsWith("quickViewLabel_", StringComparison.Ordinal))
                return true;
            if (!key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var suffix = key.Substring(Prefix.Length);
            var digitCount = suffix.TakeWhile(char.IsDigit).Count();
            if (digitCount == 0 || !int.TryParse(suffix.Substring(0, digitCount), out _)) return false;
            var tail = suffix.Substring(digitCount);
            return tail.Length == 0 || tail == "_label" || tail == "_labelcolor" || tail == "_valuecolor" || tail == "_blank";
        }

        public static void Validate(BsaQuickViewProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.Rows <= 0 || profile.Columns <= 0 || profile.Rows * profile.Columns > 100)
                throw new InvalidOperationException("QuickView dimensions must contain between 1 and 100 cells.");
            if (profile.Cells == null || profile.Cells.Count != profile.Rows * profile.Columns)
                throw new InvalidOperationException("QuickView dimensions do not match the cell count.");
            var expected = Enumerable.Range(1, profile.Cells.Count).ToArray();
            if (!profile.Cells.Select(c => c.Position).OrderBy(p => p).SequenceEqual(expected))
                throw new InvalidOperationException("QuickView positions must be unique and contiguous from one.");
            foreach (var cell in profile.Cells)
            {
                if (cell.SourceId != null && cell.SourceId.StartsWith("customfield", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("QuickView cell " + cell.Position + " has an unstable customfield binding.");
            }
        }

        static string StableSourceId(string source, IReadOnlyDictionary<string, string> customFieldNames)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            if (!source.StartsWith("customfield", StringComparison.OrdinalIgnoreCase)) return source;
            if (customFieldNames.TryGetValue(source, out var name) && name != null && name.StartsWith("MAV_", StringComparison.Ordinal))
                return name;
            throw new InvalidOperationException("QuickView source '" + source + "' has no stable MAV_* identity.");
        }

        static int ReadPositiveInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
            values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

        static bool ReadBoolean(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;

        static string ValueOrNull(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        static void Set(IDictionary<string, string> settings, string key, string value, ICollection<string> changed)
        {
            if (settings.TryGetValue(key, out var current) && string.Equals(current, value, StringComparison.Ordinal)) return;
            settings[key] = value;
            changed.Add(key);
        }

        static void SetOrRemove(IDictionary<string, string> settings, string key, string value, ICollection<string> changed)
        {
            if (value == null)
            {
                if (settings.Remove(key)) changed.Add(key);
                return;
            }
            Set(settings, key, value, changed);
        }
    }
}
