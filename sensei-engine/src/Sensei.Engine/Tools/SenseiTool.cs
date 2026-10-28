using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Sensei.Engine.Json;
using Sensei.Engine.Tools.Schema;

namespace Sensei.Engine.Tools;

/// <summary>
/// Base of every game-master tool (S3): an <see cref="AIFunction"/> subclass with an explicit, checked-in JSON schema.
/// Invocation always goes through the engine pipeline (S21–S28, S81–S85).
/// </summary>
public abstract class SenseiTool : AIFunction
{
    private ArgumentSchema? _schema;

    /// <summary>The tool name the model calls.</summary>
    public abstract override string Name { get; }

    /// <summary>One sentence telling the model what the tool does in the game (S112).</summary>
    public abstract override string Description { get; }

    /// <summary>The checked-in JSON schema text of the arguments object.</summary>
    protected abstract string SchemaJson { get; }

    /// <summary>The explicit schema sent to the model.</summary>
    public sealed override JsonElement JsonSchema => Schema.Root;

    /// <summary>True for the tools the stale-scene rule applies to (S26).</summary>
    public virtual bool IsMutating => false;

    internal ArgumentSchema Schema => _schema ??= new ArgumentSchema(SchemaJson);

    internal ToolPipeline? Pipeline { get; set; }

    internal abstract PreparedArguments Prepare(JsonElement arguments);

    internal abstract Task<ToolResult> ExecuteBoxedAsync(object arguments, ToolContext context, CancellationToken ct);

    /// <summary>MAF/MEAI entry point: forwards to the engine pipeline with the provider call id; returns the result JSON.</summary>
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var pipeline = Pipeline ?? throw new InvalidOperationException("tool is not attached to an engine");
        var json = ArgumentsToJson(arguments);
        var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;
        var result = await pipeline.InvokeAsync(Name, json, string.IsNullOrEmpty(callId) ? null : callId, null, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(result, SenseiJsonContext.Default.ToolResult);
    }

    /// <summary>Rebuilds a JSON object from MEAI's parsed argument dictionary.</summary>
    internal static string ArgumentsToJson(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var kv in arguments)
            {
                writer.WritePropertyName(kv.Key);
                switch (kv.Value)
                {
                    case null:
                        writer.WriteNullValue();
                        break;
                    case JsonElement e:
                        e.WriteTo(writer);
                        break;
                    case string s:
                        writer.WriteStringValue(s);
                        break;
                    case bool b:
                        writer.WriteBooleanValue(b);
                        break;
                    case int i:
                        writer.WriteNumberValue(i);
                        break;
                    case long l:
                        writer.WriteNumberValue(l);
                        break;
                    case double d:
                        writer.WriteNumberValue(d);
                        break;
                    case System.Text.Json.Nodes.JsonNode n:
                        n.WriteTo(writer);
                        break;
                    default:
                        writer.WriteStringValue(kv.Value.ToString());
                        break;
                }
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>Arguments after schema validation and typed deserialization.</summary>
internal sealed class PreparedArguments
{
    public object? Value { get; set; }
    public string? InvalidDetail { get; set; }
    public IReadOnlyList<string> ReferencedIds { get; set; } = Array.Empty<string>();
}

/// <summary>A tool with typed arguments <typeparamref name="TArgs"/> deserialized through <c>SenseiJsonContext</c>.</summary>
public abstract class SenseiTool<TArgs> : SenseiTool where TArgs : class
{
    /// <summary>The source-generated type info of the argument class.</summary>
    protected abstract JsonTypeInfo<TArgs> ArgsTypeInfo { get; }

    /// <summary>
    /// Argument rules a schema cannot express (exactly-one-of, predicate fields by type...).
    /// Return null when valid, else the <c>invalid_arguments</c> detail naming the property path.
    /// </summary>
    public virtual string? ValidateArguments(TArgs args) => null;

    /// <summary>Instance ids the call references, for the same-round visibility rule (S84).</summary>
    public virtual IEnumerable<string> ReferencedInstanceIds(TArgs args) => Array.Empty<string>();

    /// <summary>
    /// The tool's semantic checks and effect. Runs after the pipeline checks of S81.
    /// Must change nothing when it returns failure or cancelled (S22); commit through <see cref="ToolContext.Locked"/>.
    /// </summary>
    public abstract Task<ToolResult> ExecuteAsync(TArgs args, ToolContext context, CancellationToken ct);

    internal sealed override PreparedArguments Prepare(JsonElement arguments)
    {
        TArgs? value;
        try
        {
            value = arguments.Deserialize(ArgsTypeInfo);
        }
        catch (JsonException ex)
        {
            var path = string.IsNullOrEmpty(ex.Path) ? "arguments" : ex.Path!.TrimStart('$', '.');
            return new PreparedArguments { InvalidDetail = $"{path}: invalid value" };
        }
        catch (InvalidOperationException)
        {
            return new PreparedArguments { InvalidDetail = "arguments: invalid value" };
        }
        if (value is null) return new PreparedArguments { InvalidDetail = "arguments: must be an object" };
        var detail = ValidateArguments(value);
        if (detail is not null) return new PreparedArguments { InvalidDetail = detail };
        return new PreparedArguments
        {
            Value = value,
            ReferencedIds = ReferencedInstanceIds(value).Where(id => !string.IsNullOrEmpty(id)).ToArray(),
        };
    }

    internal sealed override Task<ToolResult> ExecuteBoxedAsync(object arguments, ToolContext context, CancellationToken ct) =>
        ExecuteAsync((TArgs)arguments, context, ct);
}
