using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionPlanner.Warnings;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MissionPlanner.BSA.Alerts
{
    public sealed class AlertPresetRecord
    {
        public string Text { get; set; }
        public int RepeatTime { get; set; }
        public CustomWarning.Conditional ConditionType { get; set; }
        public double Warning { get; set; }
        public string Color { get; set; }
        public AlertOptions Alert { get; set; }
    }

    public sealed class AlertPresetFile
    {
        public int Schema { get; set; } = AlertPresetStore.CurrentSchema;
        public List<AlertPresetRecord> Presets { get; set; } = new List<AlertPresetRecord>();
    }

    public sealed class AlertPresetLoadResult
    {
        public List<CustomWarning> Presets { get; set; } = new List<CustomWarning>();
        public string Error { get; set; }
    }

    public static class AlertPresetStore
    {
        public const int CurrentSchema = 1;

        static JsonSerializerSettings JsonSettings() => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Converters = { new StringEnumConverter() }
        };

        public static List<CustomWarning> Parse(string json)
        {
            AlertPresetFile file;
            try
            {
                file = JsonConvert.DeserializeObject<AlertPresetFile>(json ?? "", JsonSettings());
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("The alert presets are not valid JSON: " + ex.Message, ex);
            }
            if (file == null) throw new InvalidDataException("The alert presets file is empty.");
            if (file.Schema < 1 || file.Schema > CurrentSchema)
                throw new InvalidDataException("The alert presets use schema " + file.Schema + ", which this version cannot read.");

            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var presets = new List<CustomWarning>();
            foreach (var record in file.Presets ?? new List<AlertPresetRecord>())
            {
                if (record?.Alert == null) throw new InvalidDataException("An alert preset has no alert settings.");
                if (string.IsNullOrWhiteSpace(record.Alert.Id) || string.IsNullOrWhiteSpace(record.Alert.Title))
                    throw new InvalidDataException("Every alert preset needs an id and a name.");
                if (!titles.Add(record.Alert.Title.Trim()))
                    throw new InvalidDataException("Two alert presets are called '" + record.Alert.Title.Trim() + "'.");
                presets.Add(ToWarning(record));
            }
            return presets;
        }

        public static string Serialize(IEnumerable<CustomWarning> presets)
        {
            var file = new AlertPresetFile
            {
                Presets = (presets ?? Enumerable.Empty<CustomWarning>()).Where(p => p?.Alert != null).Select(ToRecord).ToList()
            };
            return JsonConvert.SerializeObject(file, JsonSettings());
        }

        public static AlertPresetLoadResult Load(string path)
        {
            var result = new AlertPresetLoadResult();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return result;
            try
            {
                result.Presets = Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                result.Error = "The saved alert presets in " + path + " could not be read: " + ex.Message;
            }
            return result;
        }

        public static void Save(string path, IEnumerable<CustomWarning> presets)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.", nameof(path));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, Serialize(presets));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static string Normalize(string json) => Serialize(Parse(json));

        public static bool SameContent(string json, string otherJson)
        {
            if (json == null || otherJson == null) return json == otherJson;
            try
            {
                return Normalize(json) == Normalize(otherJson);
            }
            catch
            {
                return false;
            }
        }

        static AlertPresetRecord ToRecord(CustomWarning warning) => new AlertPresetRecord
        {
            Text = warning.Text ?? "",
            RepeatTime = warning.RepeatTime,
            ConditionType = warning.ConditionType,
            Warning = warning.Warning,
            Color = warning.color,
            Alert = CopyOptions(warning.Alert)
        };

        static CustomWarning ToWarning(AlertPresetRecord record) => new CustomWarning
        {
            Text = record.Text ?? "",
            RepeatTime = record.RepeatTime,
            ConditionType = record.ConditionType,
            Warning = record.Warning,
            color = record.Color,
            Alert = CopyOptions(record.Alert)
        };

        static AlertOptions CopyOptions(AlertOptions alert)
        {
            var copy = JsonConvert.DeserializeObject<AlertOptions>(JsonConvert.SerializeObject(alert, JsonSettings()), JsonSettings());
            if (copy.SourceNames == null) copy.SourceNames = new List<string>();
            return copy;
        }
    }
}
