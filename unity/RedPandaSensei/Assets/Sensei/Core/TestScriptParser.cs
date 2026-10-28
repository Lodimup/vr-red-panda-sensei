#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Sensei.App.Core
{
    public sealed class TestStep
    {
        public string Verb { get; }
        public string[] Arguments { get; }
        public TestStep(string verb, string[] arguments) { Verb = verb; Arguments = arguments; }
    }
    public static class TestScriptParser
    {
        public static bool Enabled(string? filename) => !string.IsNullOrWhiteSpace(filename);
        public static IReadOnlyList<TestStep> Parse(string text)
        {
            var steps = new List<TestStep>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var verb = parts[0]; int n = parts.Length - 1;
                bool valid = verb switch { "wait_idle" => n <= 1, "repeat" => n == 0, "wav" or "event" or "select" or "sleep" or "log" => n == 1, "move" or "interrupt_after_ms" => n == 2, _ => false };
                if (!valid) throw new FormatException("Invalid step: " + verb);
                var args = new string[n]; Array.Copy(parts, 1, args, 0, n);
                if ((verb == "sleep" || verb == "wait_idle") && n > 0 && (!double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)) throw new FormatException("Invalid duration");
                if (verb == "interrupt_after_ms" && (!int.TryParse(args[0], out var ms) || ms < 0)) throw new FormatException("Invalid interrupt delay");
                var wav = verb == "wav" ? args[0] : verb == "interrupt_after_ms" ? args[1] : null;
                if (wav != null && (Path.GetFileName(wav) != wav || wav == "." || wav == ".." || wav.Contains("\\"))) throw new FormatException("Invalid WAV name");
                steps.Add(new TestStep(verb, args));
            }
            return steps;
        }
    }
}
