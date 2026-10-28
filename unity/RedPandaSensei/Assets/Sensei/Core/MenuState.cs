#nullable enable
using System;
using System.Linq;
using Sensei.Engine.Learner;

namespace Sensei.App.Core
{
    public sealed class MenuState
    {
        public bool IsOpen { get; private set; }
        public bool ResetPending { get; private set; }
        public void Toggle() { IsOpen = !IsOpen; ResetPending = false; }
        public void CancelReset() => ResetPending = false;
        public bool PressReset() { if (!ResetPending) { ResetPending = true; return false; } ResetPending = false; return true; }
        public static readonly string[] Labels = { "I know no Japanese", "A few words", "Beginner", "Intermediate", "Advanced" };
        public static readonly string[] Bands = { "zero", "early_beginner", "developing_beginner", "intermediate", "advanced" };
        public static string BandFor(int index) => Bands[index];
        public static string FormatSummary(SessionSummary summary)
        {
            var lines = summary.Practiced.Select(p => $"{p.Label}: {p.Independent} independent, {p.Supported} supported").ToArray();
            return lines.Length == 0 ? "Nothing practiced yet." : string.Join("\n", lines);
        }
    }
}
