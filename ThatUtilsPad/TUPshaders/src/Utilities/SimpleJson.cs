using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TUPshaders.Configuration;

namespace TUPshaders.Utilities
{
    /// <summary>
    /// Zero-dependency JSON serializer for <see cref="GraphicsConfig"/>.
    /// Avoids System.Text.Json / Newtonsoft conflicts with IL2CPP BepInEx.
    /// </summary>
    public static class SimpleJson
    {
        public static string SerializeConfig(GraphicsConfig cfg)
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\n");
            AppendString(sb, "Name", cfg.Name, true);
            AppendString(sb, "Version", cfg.Version, true);
            AppendDictBool(sb, "Bools", cfg.Bools);
            AppendDictFloat(sb, "Floats", cfg.Floats);
            AppendDictInt(sb, "Ints", cfg.Ints);
            AppendDictString(sb, "Strings", cfg.Strings);
            AppendDictColors(sb, "Colors", cfg.Colors);
            // remove trailing comma/newline carefully
            TrimTrailingComma(sb);
            sb.Append("\n}");
            return sb.ToString();
        }

        public static bool TryDeserializeConfig(string json, out GraphicsConfig? config)
        {
            config = new GraphicsConfig();
            if (string.IsNullOrWhiteSpace(json)) return false;

            try
            {
                config.Name = ExtractString(json, "Name") ?? "Imported";
                config.Version = ExtractString(json, "Version") ?? PluginInfo.Version;
                ParseBoolObject(json, "Bools", config.Bools);
                ParseFloatObject(json, "Floats", config.Floats);
                ParseIntObject(json, "Ints", config.Ints);
                ParseStringObject(json, "Strings", config.Strings);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"JSON parse failed: {ex.Message}");
                config = null;
                return false;
            }
        }

        private static void AppendString(StringBuilder sb, string key, string value, bool comma)
        {
            sb.Append("  \"").Append(key).Append("\": \"").Append(Escape(value)).Append('"');
            if (comma) sb.Append(",\n");
        }

        private static void AppendDictBool(StringBuilder sb, string name, Dictionary<string, bool> d)
        {
            sb.Append("  \"").Append(name).Append("\": {\n");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    \"").Append(Escape(kv.Key)).Append("\": ").Append(kv.Value ? "true" : "false");
            }
            sb.Append("\n  },\n");
        }

        private static void AppendDictFloat(StringBuilder sb, string name, Dictionary<string, float> d)
        {
            sb.Append("  \"").Append(name).Append("\": {\n");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    \"").Append(Escape(kv.Key)).Append("\": ")
                  .Append(kv.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            sb.Append("\n  },\n");
        }

        private static void AppendDictInt(StringBuilder sb, string name, Dictionary<string, int> d)
        {
            sb.Append("  \"").Append(name).Append("\": {\n");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    \"").Append(Escape(kv.Key)).Append("\": ").Append(kv.Value);
            }
            sb.Append("\n  },\n");
        }

        private static void AppendDictString(StringBuilder sb, string name, Dictionary<string, string> d)
        {
            sb.Append("  \"").Append(name).Append("\": {\n");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    \"").Append(Escape(kv.Key)).Append("\": \"").Append(Escape(kv.Value)).Append('"');
            }
            sb.Append("\n  },\n");
        }

        private static void AppendDictColors(StringBuilder sb, string name, Dictionary<string, float[]> d)
        {
            sb.Append("  \"").Append(name).Append("\": {\n");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    \"").Append(Escape(kv.Key)).Append("\": [");
                if (kv.Value != null)
                {
                    for (int i = 0; i < kv.Value.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(kv.Value[i].ToString("R", CultureInfo.InvariantCulture));
                    }
                }
                sb.Append(']');
            }
            sb.Append("\n  }\n");
        }

        private static void TrimTrailingComma(StringBuilder sb)
        {
            // Colors section already ends without comma; nothing required.
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string? ExtractString(string json, string key)
        {
            var token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i);
            if (i < 0) return null;
            i = json.IndexOf('"', i);
            if (i < 0) return null;
            int j = json.IndexOf('"', i + 1);
            if (j < 0) return null;
            return json.Substring(i + 1, j - i - 1);
        }

        private static void ParseBoolObject(string json, string section, Dictionary<string, bool> dest)
        {
            foreach (var (k, v) in ExtractObjectEntries(json, section))
            {
                if (bool.TryParse(v.Trim(), out var b))
                    dest[k] = b;
            }
        }

        private static void ParseFloatObject(string json, string section, Dictionary<string, float> dest)
        {
            foreach (var (k, v) in ExtractObjectEntries(json, section))
            {
                if (float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    dest[k] = f;
            }
        }

        private static void ParseIntObject(string json, string section, Dictionary<string, int> dest)
        {
            foreach (var (k, v) in ExtractObjectEntries(json, section))
            {
                if (int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    dest[k] = n;
            }
        }

        private static void ParseStringObject(string json, string section, Dictionary<string, string> dest)
        {
            foreach (var (k, v) in ExtractObjectEntries(json, section))
            {
                var t = v.Trim();
                if (t.StartsWith("\"") && t.EndsWith("\"") && t.Length >= 2)
                    dest[k] = t.Substring(1, t.Length - 2);
                else
                    dest[k] = t;
            }
        }

        private static IEnumerable<(string key, string value)> ExtractObjectEntries(string json, string section)
        {
            var token = "\"" + section + "\"";
            int start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0) yield break;
            start = json.IndexOf('{', start);
            if (start < 0) yield break;
            int depth = 0;
            int end = start;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }

            string body = json.Substring(start + 1, Math.Max(0, end - start - 1));
            int idx = 0;
            while (idx < body.Length)
            {
                int k0 = body.IndexOf('"', idx);
                if (k0 < 0) yield break;
                int k1 = body.IndexOf('"', k0 + 1);
                if (k1 < 0) yield break;
                string key = body.Substring(k0 + 1, k1 - k0 - 1);
                int colon = body.IndexOf(':', k1);
                if (colon < 0) yield break;
                int vStart = colon + 1;
                while (vStart < body.Length && char.IsWhiteSpace(body[vStart])) vStart++;
                int vEnd = vStart;
                if (vStart < body.Length && body[vStart] == '"')
                {
                    vEnd = body.IndexOf('"', vStart + 1);
                    if (vEnd < 0) yield break;
                    vEnd++;
                }
                else
                {
                    while (vEnd < body.Length && body[vEnd] != ',' && body[vEnd] != '}')
                        vEnd++;
                }
                string value = body.Substring(vStart, vEnd - vStart);
                yield return (key, value);
                idx = vEnd + 1;
            }
        }
    }
}
