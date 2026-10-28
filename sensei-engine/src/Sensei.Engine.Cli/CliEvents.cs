using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.World;

namespace Sensei.Engine.Cli;

/// <summary>Prints what the learner would see: hint cards (S70) and the end-of-activity summary.</summary>
internal sealed class CliEvents : IEngineEvents
{
    private readonly CliOutput _output;

    public CliEvents(CliOutput output)
    {
        _output = output;
    }

    public void OnHint(HintCard card) => _output.Line(FormatCard(card));

    public void OnSummary(SessionSummary summary)
    {
        var practiced = summary.Practiced.Count == 0 ? "nothing yet" : string.Join(", ", summary.Practiced.Select(p => $"{p.Key} ({p.Label})"));
        var line = $"Summary: practiced {practiced}";
        if (summary.UnresolvedKeys.Count > 0) line += $"; still unclear: {string.Join(", ", summary.UnresolvedKeys)}";
        if (!string.IsNullOrEmpty(summary.NextSuggestion)) line += $"; next: {summary.NextSuggestion}";
        _output.Line(line);
    }

    public void OnSceneChanged(SceneEvent e, SceneSnapshot scene)
    {
    }

    public void OnHighlight(IReadOnlyList<string> ids, int durationMs)
    {
    }

    public void OnAnimate(string animation, string? targetId)
    {
    }

    public void OnSupportChanged(SupportSettings settings)
    {
    }

    public void OnTaskChanged(TaskView task)
    {
    }

    public void OnSenseiState(string state)
    {
    }

    /// <summary><c>Card [&lt;kind&gt;]: &lt;fields&gt;</c> — the fields Sensei chose to show, separated by <c> / </c>.</summary>
    internal static string FormatCard(HintCard card)
    {
        var fields = new[] { card.Japanese, card.Reading, card.Romaji, card.English }.Where(f => !string.IsNullOrEmpty(f));
        return $"Card [{card.Kind}]: {string.Join(" / ", fields)}";
    }
}
