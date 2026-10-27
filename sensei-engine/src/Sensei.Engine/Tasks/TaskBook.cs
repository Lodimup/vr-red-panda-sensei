using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sensei.Engine.Catalog;
using Sensei.Engine.Tools;
using Sensei.Engine.World;

namespace Sensei.Engine.Tasks;

/// <summary>
/// The task lifecycle (S35, S36, S96, S104, S107, S108). The task is never persisted (S8).
/// Use every member under the engine lock; commits queue <c>OnTaskChanged</c>/<c>OnSceneChanged</c>.
/// </summary>
public sealed class TaskBook
{
    private readonly EngineCore _core;
    private readonly List<TaskView> _history = new();
    private int _taskCounter;

    internal TaskBook(EngineCore core)
    {
        _core = core;
    }

    /// <summary>The task whose status is <c>active</c>, or null (S96).</summary>
    public TaskView? Active => _history.Count > 0 && _history[_history.Count - 1].Status == TaskStatuses.Active ? _history[_history.Count - 1] : null;

    /// <summary>Every task of this engine instance (since the last reset), oldest first.</summary>
    public IReadOnlyList<TaskView> History => _history;

    public TaskView? Find(string? taskId) => taskId is null ? null : _history.FirstOrDefault(t => t.TaskId == taskId);

    private SceneWorld World => _core.World;

    /// <summary>Semantic checks of <c>start_challenge</c> after argument validation: S35a <c>task_already_active</c>, then S35b/S104/S106.</summary>
    public ToolFailure? ValidateStart(IReadOnlyList<TaskPredicate> predicates, IReadOnlyList<ScenarioProperty> scenarioProperties)
    {
        if (Active is not null) return new ToolFailure(ToolErrorCodes.TaskAlreadyActive, $"task '{Active.TaskId}' is already active");
        return TaskValidator.AsImpossible(TaskValidator.ValidateFeasibility(World, predicates, scenarioProperties));
    }

    /// <summary>
    /// Semantic checks of <c>update_challenge</c> (S36): <c>no_active_task</c>; for <c>revise</c> instruction and/or predicates
    /// required, then feasibility of the new predicates against the task's scenario properties.
    /// </summary>
    public ToolFailure? ValidateUpdate(string taskId, string action, TaskInstruction? instruction, IReadOnlyList<TaskPredicate>? predicates)
    {
        var active = Active;
        if (active is null) return new ToolFailure(ToolErrorCodes.NoActiveTask, "there is no active task");
        if (active.TaskId != taskId) return new ToolFailure(ToolErrorCodes.NoActiveTask, $"task '{taskId}' is not the active task ({active.TaskId})");
        if (action != "revise") return null;
        if (instruction is null && predicates is null)
            return new ToolFailure(ToolErrorCodes.InvalidArguments, "revise needs instruction and/or predicates");
        if (predicates is null) return null;
        return TaskValidator.AsImpossible(TaskValidator.ValidateFeasibility(World, predicates, active.ScenarioProperties));
    }

    /// <summary>Starts a task (S35c, S104): <c>task_started</c> event, no revision change, then evaluation. Validate first.</summary>
    public TaskView CommitStart(string family, TaskInstruction instruction, IEnumerable<SkillTarget> skillTargets, IEnumerable<TaskPredicate> predicates,
        IEnumerable<ScenarioProperty> scenarioProperties, string? role, bool isFreshVariation, long turnId)
    {
        if (Active is not null) throw new InvalidOperationException("task_already_active: validate before committing");
        _taskCounter++;
        var task = new TaskView
        {
            TaskId = "t" + _taskCounter.ToString(CultureInfo.InvariantCulture),
            Status = TaskStatuses.Active,
            Family = family,
            SkillTargets = skillTargets.Select(t => t.Clone()).ToList(),
            Instruction = instruction.Clone(),
            Predicates = predicates.Select(p => p.Clone()).ToList(),
            ScenarioProperties = scenarioProperties.Select(p => p.Clone()).ToList(),
            Role = role,
            IsFreshVariation = isFreshVariation,
            SourceRevision = World.Revision,
            AssistanceBaseline = _core.Learner.Profile.Support.Clone(),
        };
        _history.Add(task);
        var e = World.AppendTaskEvent(SceneEventKinds.TaskStarted, task.TaskId, SceneActors.Sensei, turnId);
        task.StartEventId = e.EventId;
        task.PredicatesSinceEventId = e.EventId;
        task.StartSequence = e.Sequence;
        _core.QueueTaskChanged(task);
        Evaluate(e);
        return task.Clone();
    }

    /// <summary>
    /// Revises the active task (S36, S108): keeps <c>task_id</c>, <c>start_event_id</c> (the assistance boundary),
    /// <c>assistance_baseline</c> and <c>is_fresh_variation</c>. A predicate revise sets <c>predicates_since_event_id</c>
    /// to the <c>task_revised</c> event and <c>source_revision</c> to now; an instruction-only revise changes nothing else.
    /// </summary>
    public TaskView CommitRevise(TaskInstruction? instruction, IEnumerable<TaskPredicate>? predicates, long turnId)
    {
        var task = Active ?? throw new InvalidOperationException("no_active_task: validate before committing");
        if (instruction is not null) task.Instruction = instruction.Clone();
        var e = World.AppendTaskEvent(SceneEventKinds.TaskRevised, task.TaskId, SceneActors.Sensei, turnId);
        if (predicates is not null)
        {
            task.Predicates = predicates.Select(p => p.Clone()).ToList();
            task.PredicatesSinceEventId = e.EventId;
            task.SourceRevision = World.Revision;
        }
        _core.QueueTaskChanged(task);
        Evaluate(e);
        return task.Clone();
    }

    /// <summary>Abandons the active task (S36): status <c>abandoned</c>, <c>task_abandoned</c> event.</summary>
    public TaskView CommitAbandon(long turnId) => Close(TaskStatuses.Abandoned, SceneEventKinds.TaskAbandoned, turnId);

    /// <summary>Ends the active task for <c>end_activity</c> (S55): status <c>ended</c>, <c>task_ended</c> event. Null when none is active.</summary>
    public TaskView? CommitEnd(long turnId) => Active is null ? null : Close(TaskStatuses.Ended, SceneEventKinds.TaskEnded, turnId);

    private TaskView Close(string status, string eventKind, long turnId)
    {
        var task = Active ?? throw new InvalidOperationException("no_active_task: validate before committing");
        task.Status = status;
        task.ClosedTurnId = turnId;
        World.AppendTaskEvent(eventKind, task.TaskId, SceneActors.Sensei, turnId);
        _core.QueueTaskChanged(task);
        return task.Clone();
    }

    /// <summary>
    /// <c>remove_objects</c> feasibility (S32a, S107): ids referenced by the active task, removals that leave too few
    /// selector matches, or objects named in <c>scenario_properties</c> → <c>object_in_active_task</c>. Null when allowed.
    /// </summary>
    public ToolFailure? CheckRemoval(IReadOnlyCollection<string> ids)
    {
        var task = Active;
        if (task is null) return null;
        var removing = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (var id in TaskValidator.ReferencedIds(task.Predicates))
            if (removing.Contains(id)) return Blocked($"'{id}' is referenced by task {task.TaskId}");
        foreach (var sp in task.ScenarioProperties)
            if (removing.Contains(sp.ObjectId)) return Blocked($"'{sp.ObjectId}' has a scenario property in task {task.TaskId}");

        var remaining = World.Instances.Where(i => !removing.Contains(i.Id)).ToList();
        foreach (var p in task.Predicates)
        {
            if (p.Selector is null) continue;
            var left = PredicateEvaluator.MatchesInScene(p.Selector, task.ScenarioProperties, remaining).Count();
            if (p.Type == PredicateTypes.Quantity && p.Count is not null)
            {
                var comparison = p.Comparison ?? QuantityComparisons.Eq;
                if (comparison != QuantityComparisons.Lte && left < p.Count.Value)
                    return Blocked($"removal would leave {left} {p.Selector.CatalogId} but task {task.TaskId} needs {p.Count.Value}");
            }
            else if ((p.Type == PredicateTypes.InRegion || p.Type == PredicateTypes.ContainedIn) && left == 0)
            {
                return Blocked($"removal would leave no {p.Selector.CatalogId} for task {task.TaskId}");
            }
        }
        return null;
    }

    /// <summary>S33: is <paramref name="instanceId"/> referenced by an active-task predicate, directly or through a selector match?</summary>
    public bool IsReferencedByActiveTask(string instanceId)
    {
        var task = Active;
        if (task is null) return false;
        foreach (var p in task.Predicates)
        {
            if (p.ObjectId == instanceId || p.ReferenceId == instanceId || p.ContainerId == instanceId) return true;
            if (p.Selector is not null && World.Find(instanceId) is { } inst && PredicateEvaluator.Matches(inst, p.Selector, task.ScenarioProperties))
                return true;
        }
        return false;
    }

    /// <summary>
    /// S96 binding for <c>record_attempt</c>: the named task if it is active or closed in the current or previous turn
    /// (else null → <c>no_active_task</c>); otherwise the active task; otherwise the latest task closed in the current or
    /// previous turn; otherwise null (no task). <paramref name="found"/> is false only when a named task cannot be bound.
    /// </summary>
    public TaskView? FindBindable(string? taskId, long currentTurnId, out bool found)
    {
        found = true;
        bool Recent(TaskView t) => t.ClosedTurnId is { } c && (c == currentTurnId || c == currentTurnId - 1);
        if (taskId is not null)
        {
            var t = Find(taskId);
            if (t is not null && (t.Status == TaskStatuses.Active || ((t.Status == TaskStatuses.Completed || t.Status == TaskStatuses.Ended) && Recent(t))))
                return t;
            found = false;
            return null;
        }
        if (Active is { } active) return active;
        return _history.LastOrDefault(t => (t.Status == TaskStatuses.Completed || t.Status == TaskStatuses.Ended) && Recent(t));
    }

    /// <summary>S35d: after a committed mutation, completes the active task when every predicate holds.</summary>
    internal void Evaluate(SceneEvent trigger)
    {
        var task = Active;
        if (task is null || !PredicateEvaluator.AllTrue(task, World)) return;
        task.Status = TaskStatuses.Completed;
        task.CompletedBy = trigger.Actor;
        task.CompletedEventId = trigger.EventId;
        task.ClosedTurnId = _core.CurrentTurnId;
        World.AppendTaskEvent(SceneEventKinds.TaskCompleted, task.TaskId, trigger.Actor, trigger.TurnId);
        _core.QueueTaskChanged(task);
    }

    /// <summary>S87: drops every task (the counter keeps going).</summary>
    internal void Clear() => _history.Clear();

    private static ToolFailure Blocked(string detail) => new(ToolErrorCodes.ObjectInActiveTask, detail);

    /// <summary>True when <paramref name="catalogId"/> names a container (convenience for validators).</summary>
    public static bool IsContainerCatalog(string catalogId) => ObjectCatalog.IsContainer(catalogId);
}
