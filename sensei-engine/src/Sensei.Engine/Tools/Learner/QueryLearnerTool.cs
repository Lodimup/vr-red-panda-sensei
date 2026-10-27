using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;

namespace Sensei.Engine.Tools.Learner;

/// <summary>Arguments of <c>query_learner</c> (S53).</summary>
public sealed class QueryLearnerArgs
{
    public string? TargetKey { get; set; }
}

/// <summary><c>query_learner</c> (S53): reads the learner model.</summary>
public sealed class QueryLearnerTool : SenseiTool<QueryLearnerArgs>
{
    public override string Name => "query_learner";

    public override string Description => "Read the learner model: band, hidden working level, support, labels per target, review queue, recommendations and notes.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "target_key": { "type": "string", "description": "Only report this target key." }
          },
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<QueryLearnerArgs> ArgsTypeInfo => SenseiJsonContext.Default.QueryLearnerArgs;

    public override Task<ToolResult> ExecuteAsync(QueryLearnerArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Locked(() =>
        {
            var profile = context.Profile;
            var targets = EvidenceRules.ComputeLabels(profile.Evidence).ToList();
            if (args.TargetKey is not null) targets = targets.Where(t => t.Key == args.TargetKey).ToList();
            var data = new JsonObject
            {
                ["starting_band"] = profile.StartingBand,
                ["working_level"] = profile.WorkingLevel,
                ["support"] = ToolContext.ToNode(profile.Support, SenseiJsonContext.Default.SupportSettings),
                ["pinned"] = ToolContext.ToNode(profile.Pinned, SenseiJsonContext.Default.ListString),
                ["targets"] = ToolContext.ToNode(targets, SenseiJsonContext.Default.ListTargetLabel),
                ["review_due"] = ToolContext.ToNode(EvidenceRules.ComputeReviewDue(profile.Evidence, context.SessionId).ToList(), SenseiJsonContext.Default.ListString),
                ["recommendations"] = ToolContext.ToNode(EvidenceRules.ComputeRecommendations(profile.Evidence).ToList(), SenseiJsonContext.Default.ListRecommendation),
                ["notes"] = ToolContext.ToNode(profile.Notes, SenseiJsonContext.Default.DictionaryStringString),
            };
            return context.Success(null, ToolContext.ToData(data));
        }));
}
