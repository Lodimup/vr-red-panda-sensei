using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tools;
using Sensei.Engine.Tools.Learner;
using Sensei.Engine.Tools.Speech;
using Sensei.Engine.Tools.World;
using Sensei.Engine.Trace;
using Sensei.Engine.World;

namespace Sensei.Engine.Json;

/// <summary>
/// The one source-generated JSON context (S3): snake_case lower names, nulls omitted when writing unless a property
/// says otherwise. Every type the engine serializes or deserializes is listed here, including the 16 tool argument classes.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
// tool arguments
[JsonSerializable(typeof(InspectSceneArgs))]
[JsonSerializable(typeof(SpawnObjectsArgs))]
[JsonSerializable(typeof(MoveObjectArgs))]
[JsonSerializable(typeof(RemoveObjectsArgs))]
[JsonSerializable(typeof(HighlightObjectsArgs))]
[JsonSerializable(typeof(AnimateSenseiArgs))]
[JsonSerializable(typeof(SpeakArgs))]
[JsonSerializable(typeof(StartChallengeArgs))]
[JsonSerializable(typeof(UpdateChallengeArgs))]
[JsonSerializable(typeof(ShowHintArgs))]
[JsonSerializable(typeof(SetSupportArgs))]
[JsonSerializable(typeof(RecordAttemptArgs))]
[JsonSerializable(typeof(EndActivityArgs))]
[JsonSerializable(typeof(QueryLearnerArgs))]
[JsonSerializable(typeof(ReplaySpeechArgs))]
[JsonSerializable(typeof(RememberNoteArgs))]
// results, scene, tasks
[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(SceneSnapshot))]
[JsonSerializable(typeof(SceneEvent))]
[JsonSerializable(typeof(List<SceneEvent>))]
[JsonSerializable(typeof(ObjectInstance))]
[JsonSerializable(typeof(TaskView))]
[JsonSerializable(typeof(TaskPredicate))]
[JsonSerializable(typeof(List<TaskPredicate>))]
// learner
[JsonSerializable(typeof(LearnerProfile))]
[JsonSerializable(typeof(LearnerProfileView))]
[JsonSerializable(typeof(SupportSettings))]
[JsonSerializable(typeof(SessionSummary))]
[JsonSerializable(typeof(EvidenceRecord))]
[JsonSerializable(typeof(AssistanceRecord))]
[JsonSerializable(typeof(TargetLabel))]
[JsonSerializable(typeof(List<TargetLabel>))]
[JsonSerializable(typeof(Recommendation))]
[JsonSerializable(typeof(List<Recommendation>))]
[JsonSerializable(typeof(PracticedTarget))]
[JsonSerializable(typeof(PreferenceChange))]
[JsonSerializable(typeof(LevelChange))]
// speech, events, trace, turns
[JsonSerializable(typeof(SpeechSegment))]
[JsonSerializable(typeof(List<SpeechSegment>))]
[JsonSerializable(typeof(HintCard))]
[JsonSerializable(typeof(TraceEntry))]
[JsonSerializable(typeof(TurnResult))]
[JsonSerializable(typeof(SpokenUtterance))]
[JsonSerializable(typeof(PlayerActionResult))]
// primitives and generic shapes used to compose `data`
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
public sealed partial class SenseiJsonContext : JsonSerializerContext
{
}
