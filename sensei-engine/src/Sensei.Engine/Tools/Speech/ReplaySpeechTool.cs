using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

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

    public override string Description => "Say an earlier utterance again (the latest by default), optionally slower; a replay counts as repetition help.";

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

    public override Task<ToolResult> ExecuteAsync(ReplaySpeechArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
