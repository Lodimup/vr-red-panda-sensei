using System;
using System.Collections.Generic;
using System.Linq;
using Sensei.Engine.Tools.Learner;
using Sensei.Engine.Tools.Speech;
using Sensei.Engine.Tools.World;

namespace Sensei.Engine.Tools;

/// <summary>The 16 game-master tools in the exact order of S10, plus any test-only extras.</summary>
public sealed class ToolRegistry
{
    private readonly List<SenseiTool> _standard;
    private readonly Dictionary<string, SenseiTool> _byName = new(StringComparer.Ordinal);

    /// <summary>S10 order.</summary>
    public static IReadOnlyList<string> StandardNames { get; } = new[]
    {
        "inspect_scene", "spawn_objects", "move_object", "remove_objects", "highlight_objects", "animate_sensei",
        "speak", "start_challenge", "update_challenge", "show_hint", "set_support", "record_attempt",
        "end_activity", "query_learner", "replay_speech", "remember_note",
    };

    internal ToolRegistry(IEnumerable<SenseiTool>? extraTools = null)
    {
        _standard = CreateStandardTools();
        foreach (var t in _standard) _byName.Add(t.Name, t);
        foreach (var t in extraTools ?? Array.Empty<SenseiTool>())
        {
            if (_byName.ContainsKey(t.Name)) throw new ArgumentException($"duplicate tool name '{t.Name}'");
            _byName.Add(t.Name, t);
        }
    }

    /// <summary>A fresh instance of each standard tool, in S10 order.</summary>
    public static List<SenseiTool> CreateStandardTools() => new()
    {
        new InspectSceneTool(),
        new SpawnObjectsTool(),
        new MoveObjectTool(),
        new RemoveObjectsTool(),
        new HighlightObjectsTool(),
        new AnimateSenseiTool(),
        new SpeakTool(),
        new StartChallengeTool(),
        new UpdateChallengeTool(),
        new ShowHintTool(),
        new SetSupportTool(),
        new RecordAttemptTool(),
        new EndActivityTool(),
        new QueryLearnerTool(),
        new ReplaySpeechTool(),
        new RememberNoteTool(),
    };

    /// <summary>The 16 standard tools in S10 order (what the agent offers the model).</summary>
    public IReadOnlyList<SenseiTool> Standard => _standard;

    /// <summary>Names of the standard tools in S10 order.</summary>
    public IReadOnlyList<string> Names => _standard.Select(t => t.Name).ToArray();

    /// <summary>Every invocable tool (standard plus extras).</summary>
    public IEnumerable<SenseiTool> All => _byName.Values;

    public SenseiTool? Find(string? name) => name is not null && _byName.TryGetValue(name, out var t) ? t : null;

    internal void Attach(ToolPipeline pipeline)
    {
        foreach (var t in _byName.Values) t.Pipeline = pipeline;
    }
}
