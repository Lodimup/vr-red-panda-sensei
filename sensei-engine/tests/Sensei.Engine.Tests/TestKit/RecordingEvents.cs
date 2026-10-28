using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.TestKit;

/// <summary>Records every engine event with the thread it arrived on; can be told to throw from one method.</summary>
public sealed class RecordingEvents : IEngineEvents
{
    private readonly object _sync = new();
    private readonly List<(string Method, object? Payload, int ThreadId)> _all = new();

    /// <summary>Method name (e.g. <c>OnSceneChanged</c>) whose handler throws.</summary>
    public string? ThrowFrom { get; set; }

    public IReadOnlyList<(string Method, object? Payload, int ThreadId)> All
    {
        get { lock (_sync) return _all.ToList(); }
    }

    public IReadOnlyList<string> Methods => All.Select(a => a.Method).ToList();

    public IReadOnlyList<T> Of<T>(string method) => All.Where(a => a.Method == method).Select(a => (T)a.Payload!).ToList();

    public IReadOnlyList<SceneEvent> SceneEvents => Of<(SceneEvent, SceneSnapshot)>(nameof(OnSceneChanged)).Select(x => x.Item1).ToList();
    public IReadOnlyList<SceneSnapshot> SceneSnapshots => Of<(SceneEvent, SceneSnapshot)>(nameof(OnSceneChanged)).Select(x => x.Item2).ToList();
    public IReadOnlyList<TaskView> Tasks => Of<TaskView>(nameof(OnTaskChanged));
    public IReadOnlyList<SupportSettings> SupportChanges => Of<SupportSettings>(nameof(OnSupportChanged));
    public IReadOnlyList<HintCard> Hints => Of<HintCard>(nameof(OnHint));
    public IReadOnlyList<string> SenseiStates => Of<string>(nameof(OnSenseiState));
    public IReadOnlyList<SessionSummary> Summaries => Of<SessionSummary>(nameof(OnSummary));

    public void Clear()
    {
        lock (_sync) _all.Clear();
    }

    private void Add(string method, object? payload)
    {
        lock (_sync) _all.Add((method, payload, Environment.CurrentManagedThreadId));
        if (ThrowFrom == method) throw new InvalidOperationException("handler failure in " + method);
    }

    public void OnSceneChanged(SceneEvent e, SceneSnapshot scene) => Add(nameof(OnSceneChanged), (e, scene));
    public void OnHighlight(IReadOnlyList<string> ids, int durationMs) => Add(nameof(OnHighlight), (ids, durationMs));
    public void OnAnimate(string animation, string? targetId) => Add(nameof(OnAnimate), (animation, targetId));
    public void OnHint(HintCard card) => Add(nameof(OnHint), card);
    public void OnSupportChanged(SupportSettings settings) => Add(nameof(OnSupportChanged), settings);
    public void OnTaskChanged(TaskView task) => Add(nameof(OnTaskChanged), task);
    public void OnSummary(SessionSummary summary) => Add(nameof(OnSummary), summary);
    public void OnSenseiState(string state) => Add(nameof(OnSenseiState), state);
}
