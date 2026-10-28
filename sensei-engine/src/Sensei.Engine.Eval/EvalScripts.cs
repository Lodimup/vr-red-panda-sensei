using System.Text;

namespace Sensei.Engine.Eval;

/// <summary>One learner line of a scripted conversation.</summary>
/// <param name="Text">What the learner types.</param>
/// <param name="JapaneseAttempt">True when the learner really tries Japanese on this line, so Sensei should record an attempt.</param>
internal sealed record ScriptTurn(string Text, bool JapaneseAttempt);

/// <summary>A fixed scripted conversation.</summary>
internal sealed record Conversation(string Id, string Description, string? Band, IReadOnlyList<ScriptTurn> Turns);

/// <summary>
/// The scripts file of the live evaluation. Plain text, one directive per line:
/// <c>#</c> comment; <c>@conversation &lt;id&gt; | &lt;description&gt;</c> starts a conversation (id: lowercase letters, digits, underscore, at most 40);
/// <c>@band &lt;band&gt;</c> the player picks this starting band before the first line (optional);
/// <c>&gt; &lt;text&gt;</c> a learner line; <c>&gt;* &lt;text&gt;</c> a learner line that is a Japanese attempt (record_attempt expected).
/// Each conversation has 3 to 5 learner lines.
/// </summary>
internal static class EvalScripts
{
    public const int MinTurns = 3;
    public const int MaxTurns = 5;

    public static IReadOnlyList<Conversation> Load(string path) => Parse(File.ReadAllLines(path, Encoding.UTF8), path);

    public static IReadOnlyList<Conversation> Parse(IReadOnlyList<string> lines, string source)
    {
        var result = new List<Conversation>();
        string? id = null;
        string description = "";
        string? band = null;
        var turns = new List<ScriptTurn>();

        void Finish()
        {
            if (id is null) return;
            if (turns.Count < MinTurns || turns.Count > MaxTurns)
                throw new FormatException($"{source}: conversation '{id}' has {turns.Count} learner lines; expected {MinTurns} to {MaxTurns}");
            result.Add(new Conversation(id, description, band, turns.ToArray()));
            id = null;
            description = "";
            band = null;
            turns = new List<ScriptTurn>();
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var where = $"{source}:{i + 1}";

            if (line.StartsWith("@conversation ", StringComparison.Ordinal))
            {
                Finish();
                var rest = line["@conversation ".Length..].Trim();
                var bar = rest.IndexOf('|');
                id = (bar < 0 ? rest : rest[..bar]).Trim();
                description = bar < 0 ? "" : rest[(bar + 1)..].Trim();
                if (id.Length is 0 or > 40 || !id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                    throw new FormatException($"{where}: bad conversation id '{id}'");
                if (result.Any(c => c.Id == id)) throw new FormatException($"{where}: duplicate conversation id '{id}'");
            }
            else if (line.StartsWith("@band ", StringComparison.Ordinal))
            {
                if (id is null) throw new FormatException($"{where}: @band outside a conversation");
                band = line["@band ".Length..].Trim();
            }
            else if (line.StartsWith('>'))
            {
                if (id is null) throw new FormatException($"{where}: learner line outside a conversation");
                var attempt = line.StartsWith(">*", StringComparison.Ordinal);
                var text = line[(attempt ? 2 : 1)..].Trim();
                if (text.Length == 0) throw new FormatException($"{where}: empty learner line");
                turns.Add(new ScriptTurn(text, attempt));
            }
            else
            {
                throw new FormatException($"{where}: unrecognised line '{line}'");
            }
        }
        Finish();
        if (result.Count == 0) throw new FormatException($"{source}: no conversations");
        return result;
    }
}
