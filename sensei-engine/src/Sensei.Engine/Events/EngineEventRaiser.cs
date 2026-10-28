using System;
using System.Collections.Generic;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.World;

namespace Sensei.Engine.Events;

/// <summary>
/// Raises <see cref="IEngineEvents"/> safely (S69): a handler exception is caught, traced as
/// <c>event_handler_error</c> with tool <c>event:&lt;method&gt;</c> (S86), and never undoes the change.
/// Call only after the change committed and outside the engine lock.
/// </summary>
public sealed class EngineEventRaiser
{
    private readonly IEngineEvents? _events;
    private readonly Action<string, Exception> _onHandlerError;

    internal EngineEventRaiser(IEngineEvents? events, Action<string, Exception> onHandlerError)
    {
        _events = events;
        _onHandlerError = onHandlerError;
    }

    public void SceneChanged(SceneEvent e, SceneSnapshot scene) => Raise(nameof(IEngineEvents.OnSceneChanged), h => h.OnSceneChanged(e, scene));

    public void Highlight(IReadOnlyList<string> ids, int durationMs) => Raise(nameof(IEngineEvents.OnHighlight), h => h.OnHighlight(ids, durationMs));

    public void Animate(string animation, string? targetId) => Raise(nameof(IEngineEvents.OnAnimate), h => h.OnAnimate(animation, targetId));

    public void Hint(HintCard card) => Raise(nameof(IEngineEvents.OnHint), h => h.OnHint(card));

    public void SupportChanged(SupportSettings settings) => Raise(nameof(IEngineEvents.OnSupportChanged), h => h.OnSupportChanged(settings));

    public void TaskChanged(TaskView task) => Raise(nameof(IEngineEvents.OnTaskChanged), h => h.OnTaskChanged(task));

    public void Summary(SessionSummary summary) => Raise(nameof(IEngineEvents.OnSummary), h => h.OnSummary(summary));

    public void SenseiState(string state) => Raise(nameof(IEngineEvents.OnSenseiState), h => h.OnSenseiState(state));

    private void Raise(string method, Action<IEngineEvents> raise)
    {
        if (_events is null) return;
        try
        {
            raise(_events);
        }
        catch (Exception ex)
        {
            try
            {
                _onHandlerError(method, ex);
            }
            catch (Exception)
            {
                // Reporting the handler's failure is best effort: it must not escape into the committed call that raised the event.
            }
        }
    }
}
