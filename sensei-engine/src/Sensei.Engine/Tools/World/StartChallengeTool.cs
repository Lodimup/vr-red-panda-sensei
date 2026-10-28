using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;

namespace Sensei.Engine.Tools.World;

/// <summary>Arguments of <c>start_challenge</c> (S35).</summary>
public sealed class StartChallengeArgs
{
    public string Family { get; set; } = "";
    public TaskInstruction? Instruction { get; set; }
    public List<SkillTarget> SkillTargets { get; set; } = new();
    public List<TaskPredicate> Predicates { get; set; } = new();
    public List<ScenarioProperty> ScenarioProperties { get; set; } = new();
    public string? Role { get; set; }
    public bool IsFreshVariation { get; set; }
}

/// <summary><c>start_challenge</c> (S35): starts a task whose predicates the engine checks.</summary>
public sealed class StartChallengeTool : SenseiTool<StartChallengeArgs>
{
    public override string Name => "start_challenge";

    public override string Description => "Start a practice task: an instruction, the skills it trains and the scene conditions that complete it.";

    public override bool IsMutating => true;

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "family": { "type": "string", "enum": ["discover_request", "listen_arrange", "converse_roleplay"], "description": "Kind of task." },
            "instruction": {
              "type": "object",
              "description": "What Sensei asks the learner to do; at least one language.",
              "properties": {
                "japanese": { "type": "string", "maxLength": 80, "description": "Instruction in Japanese, at most 80 characters." },
                "english": { "type": "string", "maxLength": 200, "description": "Instruction in English, at most 200 characters." }
              },
              "additionalProperties": false
            },
            "skill_targets": {
              "type": "array",
              "description": "Skills the task trains, 1 to 3.",
              "minItems": 1,
              "maxItems": 3,
              "items": {
                "type": "object",
                "description": "One skill target.",
                "properties": {
                  "dimension": { "type": "string", "enum": ["vocabulary", "listening", "production", "reading", "interaction", "register"], "description": "Skill dimension." },
                  "key": { "type": "string", "minLength": 1, "maxLength": 40, "description": "Short stable key of the target, for example cup or kudasai." },
                  "japanese": { "type": "string", "maxLength": 30, "description": "The Japanese form the learner should hear or say." }
                },
                "required": ["dimension", "key"],
                "additionalProperties": false
              }
            },
            "predicates": {
              "type": "array",
              "description": "Scene conditions that complete the task, 1 to 3.",
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
            },
            "scenario_properties": {
              "type": "array",
              "description": "Pretend properties of scene objects for this task, 0 to 4.",
              "maxItems": 4,
              "items": {
                "type": "object",
                "description": "One scenario property.",
                "properties": {
                  "object_id": { "type": "string", "description": "Instance id the property belongs to." },
                  "name": { "type": "string", "pattern": "^[a-z_]{1,24}$", "description": "Property name, lowercase letters and underscores." },
                  "value": { "type": "string", "minLength": 1, "maxLength": 24, "description": "Property value." }
                },
                "required": ["object_id", "name", "value"],
                "additionalProperties": false
              }
            },
            "role": { "type": "string", "maxLength": 40, "description": "Role Sensei plays in a roleplay." },
            "is_fresh_variation": { "type": "boolean", "description": "True when this task varies an earlier one in a new way." }
          },
          "required": ["family", "instruction", "skill_targets", "predicates"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<StartChallengeArgs> ArgsTypeInfo => SenseiJsonContext.Default.StartChallengeArgs;

    public override string? ValidateArguments(StartChallengeArgs args) =>
        HiddenLevelGuard.Check(args.Instruction?.Japanese) || HiddenLevelGuard.Check(args.Instruction?.English) ||
        HiddenLevelGuard.Check(args.Role) || args.ScenarioProperties.Any(p => HiddenLevelGuard.Check(p.Value)) ||
        args.SkillTargets.Any(t => HiddenLevelGuard.Check(t.Japanese))
            ? HiddenLevelGuard.Detail
            : TaskValidator.ValidateShape(args.Instruction, args.Predicates);

    public override IEnumerable<string> ReferencedInstanceIds(StartChallengeArgs args) => TaskValidator.ReferencedIds(args.Predicates, args.ScenarioProperties);

    public override Task<ToolResult> ExecuteAsync(StartChallengeArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Locked(() =>
        {
            var failure = context.Tasks.ValidateStart(args.Predicates, args.ScenarioProperties);
            if (failure is not null) return context.Failure(failure);

            var task = context.Tasks.CommitStart(args.Family, args.Instruction!, args.SkillTargets, args.Predicates, args.ScenarioProperties,
                args.Role, args.IsFreshVariation, context.TurnId);
            return context.Success(null, ToolContext.ToData(task, SenseiJsonContext.Default.TaskView));
        }));
}
