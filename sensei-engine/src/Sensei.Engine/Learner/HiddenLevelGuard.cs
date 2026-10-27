using Sensei.Engine.Text;

namespace Sensei.Engine.Learner;

/// <summary>
/// S80: model-written presentation text (<c>speak</c> text, <c>show_hint</c> content, <c>next_suggestion</c>) must not
/// mention the hidden working level. RULING(Handler 2026-10-27): hidden JLPT working level is never shown to the learner.
/// </summary>
public static class HiddenLevelGuard
{
    /// <summary>The detail of the <c>invalid_arguments</c> failure.</summary>
    public const string Detail = "hidden_level";

    /// <summary>True when <paramref name="text"/> contains <c>working level</c> or <c>working_level</c> (case-insensitive) and must be refused.</summary>
    public static bool Check(string? text) =>
        TextRules.ContainsIgnoreCase(text, "working level") || TextRules.ContainsIgnoreCase(text, "working_level");
}
