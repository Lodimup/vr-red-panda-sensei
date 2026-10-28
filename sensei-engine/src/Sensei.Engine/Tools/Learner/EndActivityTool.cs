using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;

namespace Sensei.Engine.Tools.Learner;

/// <summary>Arguments of <c>end_activity</c> (S55).</summary>
public sealed class EndActivityArgs
{
    public string NextSuggestion { get; set; } = "";
}

/// <summary><c>end_activity</c> (S55, S88): ends the activity and shows the session summary.</summary>
public sealed class EndActivityTool : SenseiTool<EndActivityArgs>
{
    public override string Name => "end_activity";

    public override string Description => "End the current activity and show the learner a summary of what they practised; speak any recap before calling this.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "next_suggestion": { "type": "string", "minLength": 1, "maxLength": 160, "description": "What to practise next time, in one sentence." }
          },
          "required": ["next_suggestion"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<EndActivityArgs> ArgsTypeInfo => SenseiJsonContext.Default.EndActivityArgs;

    /// <summary>S80: model-written presentation text must not mention the hidden working level.</summary>
    public override string? ValidateArguments(EndActivityArgs args) => HiddenLevelGuard.Check(args.NextSuggestion) ? HiddenLevelGuard.Detail : null;

    public override async Task<ToolResult> ExecuteAsync(EndActivityArgs args, ToolContext context, CancellationToken ct)
    {
        SessionSummary? summary = null;
        var result = context.Locked(() =>
        {
            if (context.Secrets.Check(args.NextSuggestion))
                return context.Failure(ToolErrorCodes.LooksLikeCredential, "next_suggestion: looks like a credential");
            var profile = context.Profile;
            var session = context.Learner.CurrentSession(context.SessionId);
            if (session is null)
            {
                session = new SessionRecord { SessionId = context.SessionId, StartedAt = context.Clock.GetUtcNow() };
                profile.Sessions.Add(session);
            }
            session.NextSuggestion = args.NextSuggestion;
            context.Tasks.CommitEnd(context.TurnId);
            summary = SessionSummaryFactory.Build(profile, context.SessionId);
            // S88: the generation ends once this call has returned success.
            context.EndGenerationAfterCall();
            return context.Success(null, ToolContext.ToData(summary, SenseiJsonContext.Default.SessionSummary));
        });
        if (!result.IsSuccess || summary is null) return result;
        context.Events.Summary(summary);
        // Committed: save without the call's token so a late cancel cannot turn it into `cancelled` (S22).
        await context.SaveProfileAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }
}
