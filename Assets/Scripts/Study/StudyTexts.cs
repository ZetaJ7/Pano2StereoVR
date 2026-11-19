using System.Collections.Generic;
using System.IO;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Text table with one Chinese and one English string per key. The session shows one language only,
    /// chosen on the first screen (<see cref="Language"/>). Defaults come from Resources/StudyTexts.json;
    /// an optional study_texts.json next to the executable overrides individual entries without rebuilding.
    /// Keys ending in "_note" hold short explanations shown as smaller secondary text.
    /// </summary>
    public static class StudyTexts
    {
        public const string Chinese = "zh";
        public const string English = "en";

        private static readonly Dictionary<string, KeyValuePair<string, string>> Map =
            new Dictionary<string, KeyValuePair<string, string>>();

        /// <summary>Draft notice of the table (written to the log, not shown on screen).</summary>
        public static string Status { get; private set; } = "";
        public static int Count => Map.Count;
        public static IEnumerable<string> Keys => Map.Keys;

        private static string _language = Chinese;

        /// <summary>"zh" or "en". Any other value falls back to "zh".</summary>
        public static string Language
        {
            get => _language;
            set => _language = value == English ? English : Chinese;
        }

        public static void Clear()
        {
            Map.Clear();
            Status = "";
        }

        /// <summary>Merge entries from a JSON table: {"status": "...", "entries": [{"key","zh","en"}]}.</summary>
        public static int LoadJson(string json)
        {
            Dictionary<string, object> root = MiniJson.AsObject(MiniJson.Parse(json), "texts");
            string status = MiniJson.GetString(root, "status");
            if (!string.IsNullOrEmpty(status))
            {
                Status = status;
            }
            int n = 0;
            foreach (object o in MiniJson.GetList(root, "entries"))
            {
                Dictionary<string, object> e = MiniJson.AsObject(o, "text entry");
                string key = MiniJson.GetString(e, "key");
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                Map[key] = new KeyValuePair<string, string>(MiniJson.GetString(e, "zh"), MiniJson.GetString(e, "en"));
                n++;
            }
            return n;
        }

        public static bool TryLoadOverride(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }
            LoadJson(File.ReadAllText(path));
            return true;
        }

        public static bool Has(string key)
        {
            return Map.ContainsKey(key);
        }

        public static string Zh(string key)
        {
            return Map.TryGetValue(key, out KeyValuePair<string, string> v) ? v.Key : key;
        }

        public static string En(string key)
        {
            return Map.TryGetValue(key, out KeyValuePair<string, string> v) ? v.Value : key;
        }

        /// <summary>Text in the selected language; falls back to the other language when empty.</summary>
        public static string Get(string key)
        {
            return Get(key, _language);
        }

        public static string Get(string key, string language)
        {
            if (!Map.TryGetValue(key, out KeyValuePair<string, string> v))
            {
                return "[" + key + "]";
            }
            string primary = language == English ? v.Value : v.Key;
            string other = language == English ? v.Key : v.Value;
            return string.IsNullOrEmpty(primary) ? (other ?? "") : primary;
        }

        /// <summary>Explanation for an item ("&lt;key&gt;_note"), or "" when there is none.</summary>
        public static string Note(string key)
        {
            string k = key + "_note";
            return Map.ContainsKey(k) ? Get(k) : "";
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(key), args);
        }
    }
}
