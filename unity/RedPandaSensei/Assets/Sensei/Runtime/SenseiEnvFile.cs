#nullable enable
using System.Collections.Generic;
using System.IO;

namespace Sensei.Unity
{
    public static class SenseiEnvFile
    {
        public static IReadOnlyDictionary<string, string?> Load(string path)
        {
            var values = new Dictionary<string, string?>();
            if (!File.Exists(path)) return values;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var key = line.Substring(0, separator).Trim();
                if (key.Length != 0) values[key] = line.Substring(separator + 1).Trim();
            }
            return values;
        }
    }
}
