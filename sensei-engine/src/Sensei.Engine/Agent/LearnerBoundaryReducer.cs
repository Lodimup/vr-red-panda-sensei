using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Sensei.Engine.Agent;

/// <summary>
/// S65: keeps the last N learner messages and everything after them. It cuts only at a learner (user) message, so an
/// assistant tool call is never separated from its result and a turn is never cut in the middle.
/// </summary>
internal sealed class LearnerBoundaryReducer : IChatReducer
{
    /// <summary>Learner turns the history keeps (S65).</summary>
    public const int MaxLearnerTurns = 12;

    private readonly int _keep;

    public LearnerBoundaryReducer(int keepLearnerMessages = MaxLearnerTurns)
    {
        _keep = keepLearnerMessages;
    }

    public Task<IEnumerable<ChatMessage>> ReduceAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<ChatMessage>>(Trim(messages.ToList(), _keep));

    /// <summary>The suffix of <paramref name="messages"/> starting at the learner message that makes the last <paramref name="keepLearnerMessages"/>.</summary>
    public static List<ChatMessage> Trim(IReadOnlyList<ChatMessage> messages, int keepLearnerMessages)
    {
        var learnerIndexes = new List<int>();
        for (var i = 0; i < messages.Count; i++)
            if (messages[i].Role == ChatRole.User && messages[i].Text != "<learner_data>\n</learner_data>") learnerIndexes.Add(i);
        if (learnerIndexes.Count <= keepLearnerMessages) return messages.ToList();
        var start = keepLearnerMessages <= 0 ? messages.Count : learnerIndexes[learnerIndexes.Count - keepLearnerMessages];
        return messages.Skip(start).ToList();
    }
}
