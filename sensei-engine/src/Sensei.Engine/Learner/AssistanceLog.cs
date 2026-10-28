using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Sensei.Engine.Tasks;

namespace Sensei.Engine.Learner;

/// <summary>Assistance kinds (SPEC §9).</summary>
public static class AssistanceKinds
{
    public const string TranscriptVisible = "transcript_visible";
    public const string TranslationVisible = "translation_visible";
    public const string PartialPhrase = "partial_phrase";
    public const string ModelAnswer = "model_answer";
    public const string Explanation = "explanation";
    public const string VocabularyCard = "vocabulary_card";
    public const string TranslationHint = "translation_hint";
    public const string HighlightAnswer = "highlight_answer";
    public const string Demonstration = "demonstration";
    public const string Repetition = "repetition";
    public const string AudioModel = "audio_model";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        TranscriptVisible, TranslationVisible, PartialPhrase, ModelAnswer, Explanation, VocabularyCard,
        TranslationHint, HighlightAnswer, Demonstration, Repetition, AudioModel,
    };
}

/// <summary>One assistance record (SPEC §9): <c>kind, target_key, task_id, turn_id, event_id</c>.</summary>
public sealed class AssistanceRecord
{
    public string Kind { get; set; } = "";
    public string TargetKey { get; set; } = "";

    /// <summary>Dimension of the target when known (S97); null when recorded by key only (e.g. <c>show_hint target_key</c>), matching every dimension of that key.</summary>
    public string? TargetDimension { get; set; }

    public string? TaskId { get; set; }
    public long TurnId { get; set; }

    /// <summary>Last scene event id when recorded.</summary>
    public long EventId { get; set; }

    /// <summary>Position in the engine-wide interaction sequence (S92).</summary>
    [JsonIgnore]
    public long Sequence { get; set; }

    /// <summary>True when this record applies to <paramref name="dimension"/>/<paramref name="key"/>.</summary>
    public bool AppliesTo(string dimension, string key) =>
        TargetKey == key && (TargetDimension is null || TargetDimension == dimension);
}

/// <summary>In-memory assistance log of this engine instance (cleared on reset). Access under the engine lock.</summary>
public sealed class AssistanceLog
{
    private readonly List<AssistanceRecord> _records = new();

    public IReadOnlyList<AssistanceRecord> Records => _records;

    internal void Add(AssistanceRecord record) => _records.Add(record);

    internal void Clear() => _records.Clear();

    public IEnumerable<AssistanceRecord> For(string dimension, string key) => _records.Where(r => r.AppliesTo(dimension, key));

    /// <summary>Convenience for tests and rules: targets of a task as (dimension, key) pairs.</summary>
    public static IEnumerable<(string Dimension, string Key)> TargetsOf(TaskView task) => task.SkillTargets.Select(t => (t.Dimension, t.Key));
}
