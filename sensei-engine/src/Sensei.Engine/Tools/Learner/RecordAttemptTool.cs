using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Tasks;

namespace Sensei.Engine.Tools.Learner;

/// <summary>Arguments of <c>record_attempt</c> (S43–S47, S93–S98).</summary>
public sealed class RecordAttemptArgs
{
    public SkillTarget Target { get; set; } = new();
    public string? TranscriptExcerpt { get; set; }
    public bool CommunicativeSuccess { get; set; }
    public string LanguageAssessment { get; set; } = "";
    public List<long> EventIds { get; set; } = new();
    public string? Note { get; set; }
    public bool IsFreshVariation { get; set; }
    public string? TaskId { get; set; }
    public string? Uncertainty { get; set; }
}

/// <summary><c>record_attempt</c>: commits evidence of one learner attempt.</summary>
public sealed class RecordAttemptTool : SenseiTool<RecordAttemptArgs>
{
    public override string Name => "record_attempt";

    public override string Description => "Record evidence of one learner attempt at a skill target, quoting the learner's words; the engine decides the outcome from the help given.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "target": {
              "type": "object",
              "description": "The skill target attempted.",
              "properties": {
                "dimension": { "type": "string", "enum": ["vocabulary", "listening", "production", "reading", "interaction", "register"], "description": "Skill dimension." },
                "key": { "type": "string", "minLength": 1, "maxLength": 40, "description": "Target key." },
                "japanese": { "type": "string", "maxLength": 30, "description": "Japanese form of the target." }
              },
              "required": ["dimension", "key"],
              "additionalProperties": false
            },
            "transcript_excerpt": { "type": "string", "minLength": 1, "maxLength": 120, "description": "The learner's exact words from this or the previous turn; may be omitted for listening targets." },
            "communicative_success": { "type": "boolean", "description": "Whether the learner got the meaning across or understood." },
            "language_assessment": { "type": "string", "enum": ["correct", "minor_issue", "meaning_blocking_error", "unclear_input"], "description": "Language quality of the attempt." },
            "event_ids": { "type": "array", "description": "Scene event ids that show the attempt.", "items": { "type": "integer", "description": "Scene event id." } },
            "note": { "type": "string", "maxLength": 160, "description": "Short note for later sessions." },
            "is_fresh_variation": { "type": "boolean", "description": "True when the attempt was in a new variation of the task." },
            "task_id": { "type": "string", "description": "Task the attempt belongs to; defaults to the active or just-finished task." },
            "uncertainty": { "type": "string", "enum": ["low", "high"], "description": "How sure the assessment is; defaults to low." }
          },
          "required": ["target", "communicative_success", "language_assessment"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<RecordAttemptArgs> ArgsTypeInfo => SenseiJsonContext.Default.RecordAttemptArgs;

    public override Task<ToolResult> ExecuteAsync(RecordAttemptArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
