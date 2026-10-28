using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;

namespace Sensei.Engine.Tools.World;

/// <summary>Arguments of <c>update_challenge</c> (S36).</summary>
public sealed class UpdateChallengeArgs
{
    public string TaskId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public TaskInstruction? Instruction { get; set; }
    public List<TaskPredicate>? Predicates { get; set; }
}

/// <summary><c>update_challenge</c> (S36): revises or abandons the active task.</summary>
public sealed class UpdateChallengeTool : SenseiTool<UpdateChallengeArgs>
{
    public override string Name => "update_challenge";

    public override string Description => "Revise the active task's instruction or conditions, or abandon it, giving a short reason.";

    public override bool IsMutating => true;

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "task_id": { "type": "string", "description": "Id of the active task." },
            "action": { "type": "string", "enum": ["revise", "abandon"], "description": "Revise the task or abandon it." },
            "reason": { "type": "string", "minLength": 1, "maxLength": 120, "description": "Why, in a short sentence." },
            "instruction": {
              "type": "object",
              "description": "What Sensei asks the learner to do; at least one language.",
              "properties": {
                "japanese": { "type": "string", "maxLength": 80, "description": "Instruction in Japanese, at most 80 characters." },
                "english": { "type": "string", "maxLength": 200, "description": "Instruction in English, at most 200 characters." }
              },
              "additionalProperties": false
            },
            "predicates": {
              "type": "array",
              "description": "New conditions replacing the old ones, 1 to 3.",
              "minItems": 1,
              "maxItems": 3,
              "items": {
                "type": "object",
                "description": "One condition of the task; type decides which other fields are allowed.",
                "properties": {
                  "type": { "type": "string", "enum": ["selected", "in_region", "contained_in", "quantity", "left_of", "right_of", "unchanged", "before"], "description": "Predicate type." },
                  "object_id": { "type": "string", "description": "Instance id the predicate is about." },
                  "selector": {
                    "type": "object",
                    "description": "Matches objects by catalog id and an optional scenario property.",
                    "properties": {
                      "catalog_id": { "type": "string", "description": "Catalog id to match." },
                      "property": {
                        "type": "object",
                        "description": "A scenario property declared by this task.",
                        "properties": {
                          "name": { "type": "string", "description": "Property name." },
                          "value": { "type": "string", "description": "Property value." }
                        },
                        "required": ["name", "value"],
                        "additionalProperties": false
                      }
                    },
                    "required": ["catalog_id"],
                    "additionalProperties": false
                  },
                  "region": { "type": "string", "enum": ["left", "center", "right", "learner_hand", "sensei_side"], "description": "Region for in_region or quantity." },
                  "container_id": { "type": "string", "description": "Container instance id for contained_in or quantity." },
                  "count": { "type": "integer", "minimum": 1, "maximum": 8, "description": "Number to compare for quantity, 1 to 8." },
                  "comparison": { "type": "string", "enum": ["eq", "gte", "lte"], "description": "Comparison for quantity; defaults to eq." },
                  "reference_id": { "type": "string", "description": "Second instance id for left_of, right_of and before." }
                },
                "required": ["type"],
                "additionalProperties": false
              }
            }
          },
          "required": ["task_id", "action", "reason"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<UpdateChallengeArgs> ArgsTypeInfo => SenseiJsonContext.Default.UpdateChallengeArgs;

    public override string? ValidateArguments(UpdateChallengeArgs args) =>
        HiddenLevelGuard.Check(args.Instruction?.Japanese) || HiddenLevelGuard.Check(args.Instruction?.English)
            ? HiddenLevelGuard.Detail
            : TaskValidator.ValidateShape(args.Instruction, args.Predicates, instructionRequired: false);

    public override IEnumerable<string> ReferencedInstanceIds(UpdateChallengeArgs args) => TaskValidator.ReferencedIds(args.Predicates);

    public override Task<ToolResult> ExecuteAsync(UpdateChallengeArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Locked(() =>
        {
            var failure = context.Tasks.ValidateUpdate(args.TaskId, args.Action, args.Instruction, args.Predicates);
            if (failure is not null) return context.Failure(failure);

            var task = args.Action == "revise"
                ? context.Tasks.CommitRevise(args.Instruction, args.Predicates, context.TurnId)
                : context.Tasks.CommitAbandon(context.TurnId);
            return context.Success(null, ToolContext.ToData(task, SenseiJsonContext.Default.TaskView));
        }));
}
