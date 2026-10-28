#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Sensei.App.Core;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.World;
using UnityEngine;

namespace Sensei.App
{
    public sealed class UnityEngineEvents : IEngineEvents
    {
        private readonly MainThreadQueue queue;
        private readonly object panelLock = new object();
        private Sensei.Engine.Events.HintCard? pendingHint; private TaskView? pendingTask;
        private bool hintQueued, taskQueued; private int panelEpoch;
        public void ResetPanels() { lock (panelLock) { panelEpoch++; pendingHint = null; pendingTask = null; } }
        private readonly PracticeAreaView area;
        private readonly SenseiView sensei;
        private readonly Action<string> paceChanged;
        private readonly Action<string> stateChanged;
        public UnityEngineEvents(MainThreadQueue queue, PracticeAreaView area, SenseiView sensei, Action<string> paceChanged, Action<string>? stateChanged = null) { this.queue = queue; this.area = area; this.sensei = sensei; this.paceChanged = paceChanged; this.stateChanged = stateChanged ?? (_ => { }); }
        public Action<Sensei.Engine.Events.HintCard>? Hint; public Action<TaskView>? Task; public Action<SupportSettings>? Support;
        public void OnSceneChanged(SceneEvent e, SceneSnapshot scene) => queue.Post(() => { Debug.Log($"RPS_EVENT OnSceneChanged kind={e.Kind} revision={scene.Revision}"); area.Render(scene); });
        public void OnHighlight(IReadOnlyList<string> ids, int durationMs) { var copy = ids.ToArray(); queue.Post(() => area.Highlight(copy, durationMs)); }
        public void OnAnimate(string animation, string? targetId) => queue.Post(() => sensei.Animate(animation, targetId));
        public void OnHint(Sensei.Engine.Events.HintCard card) { lock (panelLock) { pendingHint = card.Clone(); if (hintQueued) return; hintQueued = true; queue.Post(FlushHint); } }
        private void FlushHint() { Sensei.Engine.Events.HintCard? value; int epoch; lock (panelLock) { value = pendingHint; pendingHint = null; hintQueued = false; epoch = panelEpoch; } lock (panelLock) { if (epoch == panelEpoch && value != null) Hint?.Invoke(value); } }
        public void OnSupportChanged(SupportSettings settings) { var pace = settings.Pace; var copy = settings.Clone(); queue.Post(() => { paceChanged(pace); Support?.Invoke(copy); }); }
        public void OnTaskChanged(TaskView task) { lock (panelLock) { pendingTask = task?.Clone(); if (taskQueued) return; taskQueued = true; queue.Post(FlushTask); } }
        private void FlushTask() { TaskView? value; int epoch; lock (panelLock) { value = pendingTask; pendingTask = null; taskQueued = false; epoch = panelEpoch; } lock (panelLock) { if (epoch == panelEpoch && value != null) Task?.Invoke(value); } }
        public void OnSummary(SessionSummary summary) { }
        public void OnSenseiState(string state) => queue.Post(() => { sensei.SetState(state); stateChanged(state); });
    }
}
