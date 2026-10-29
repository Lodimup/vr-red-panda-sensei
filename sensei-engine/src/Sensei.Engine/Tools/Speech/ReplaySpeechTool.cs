using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Speech;

namespace Sensei.Engine.Tools.Speech;

/// <summary>Arguments of <c>replay_speech</c> (S39, S90).</summary>
public sealed class ReplaySpeechArgs
{
    public string? UtteranceId { get; set; }
    public string? Pace { get; set; }
}

/// <summary><c>replay_speech</c> (S39, S90): says a stored utterance again.</summary>
public sealed class ReplaySpeechTool : SenseiTool<ReplaySpeechArgs>
{
    public override string Name => "replay_speech";

    public override string Description => "Replay speech; this counts as help.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "utterance_id": { "type": "string", "description": "Utterance id to replay; defaults to the latest." },
            "pace": { "type": "string", "enum": ["normal", "slow"], "description": "Speaking pace; defaults to the learner's pace setting." }
          },
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<ReplaySpeechArgs> ArgsTypeInfo => SenseiJsonContext.Default.ReplaySpeechArgs;

    public override async Task<ToolResult> ExecuteAsync(ReplaySpeechArgs args, ToolContext context, CancellationToken ct)
    {
        SpeechPlan? plan = null;
        var gate = context.Read(() =>
        {
            var entry = args.UtteranceId is null ? context.SpeechHistory.Latest : context.SpeechHistory.Find(args.UtteranceId);
            if (entry is null)
                return context.Failure(ToolErrorCodes.NothingToReplay,
                    args.UtteranceId is null ? "nothing has been said yet" : $"no utterance '{args.UtteranceId}' in the last {SpeechHistory.Capacity}");
            // S39: a replay is not counted against the 480 spoken characters of the turn.
            plan = SpeechPresenter.Plan(context, entry.Segments, args.Pace, 0, SpeechPresenter.ActiveTaskTargets(context), isReplay: true);
            return context.Success();
        });
        if (plan is null) return gate;
        return await SpeechPresenter.PresentAsync(context, plan, ct).ConfigureAwait(false);
    }
}
