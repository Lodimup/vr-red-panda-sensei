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
        private readonly PracticeAreaView area;
        private readonly SenseiView sensei;
        private readonly Action<string> paceChanged;
        private readonly Action<string> stateChanged;
        public UnityEngineEvents(MainThreadQueue queue, PracticeAreaView area, SenseiView sensei, Action<string> paceChanged, Action<string>? stateChanged = null) { this.queue = queue; this.area = area; this.sensei = sensei; this.paceChanged = paceChanged; this.stateChanged = stateChanged ?? (_ => { }); }
        public void OnSceneChanged(SceneEvent e, SceneSnapshot scene) => queue.Post(() => { Debug.Log($"RPS_EVENT OnSceneChanged kind={e.Kind} revision={scene.Revision}"); area.Render(scene); });
        public void OnHighlight(IReadOnlyList<string> ids, int durationMs) { var copy = ids.ToArray(); queue.Post(() => area.Highlight(copy, durationMs)); }
        public void OnAnimate(string animation, string? targetId) => queue.Post(() => sensei.Animate(animation, targetId));
        public void OnHint(HintCard card) { }
        public void OnSupportChanged(SupportSettings settings) { var pace = settings.Pace; queue.Post(() => paceChanged(pace)); }
        public void OnTaskChanged(TaskView task) { }
        public void OnSummary(SessionSummary summary) { }
        public void OnSenseiState(string state) => queue.Post(() => { sensei.SetState(state); stateChanged(state); });
    }
}
