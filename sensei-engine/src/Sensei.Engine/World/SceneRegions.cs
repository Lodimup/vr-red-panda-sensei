using System;
using System.Collections.Generic;
using System.Linq;

namespace Sensei.Engine.World;

/// <summary>Scene regions (SPEC §5). <c>left</c>, <c>center</c>, <c>right</c> form the ordered practice area.</summary>
public static class SceneRegions
{
    public const string Left = "left";
    public const string Center = "center";
    public const string Right = "right";
    public const string LearnerHand = "learner_hand";
    public const string SenseiSide = "sensei_side";

    /// <summary>All five regions.</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Left, Center, Right, LearnerHand, SenseiSide };

    /// <summary>The ordered practice area, left to right.</summary>
    public static IReadOnlyList<string> PracticeArea { get; } = new[] { Left, Center, Right };

    /// <summary>Regions <c>spawn_objects</c> accepts.</summary>
    public static IReadOnlyList<string> Spawnable { get; } = new[] { Left, Center, Right, SenseiSide };

    public static bool IsRegion(string? region) => region is not null && All.Contains(region, StringComparer.Ordinal);

    public static bool IsPracticeArea(string? region) => region is not null && PracticeArea.Contains(region, StringComparer.Ordinal);

    /// <summary>Index of a practice-area region (0..2), or -1.</summary>
    public static int PracticeIndex(string? region) => region switch
    {
        Left => 0,
        Center => 1,
        Right => 2,
        _ => -1,
    };
}

/// <summary>Who caused a scene event.</summary>
public static class SceneActors
{
    public const string Sensei = "sensei";
    public const string Player = "player";
}

/// <summary>Scene event kinds (S17, S104).</summary>
public static class SceneEventKinds
{
    public const string Spawned = "spawned";
    public const string Moved = "moved";
    public const string Removed = "removed";
    public const string Selected = "selected";
    public const string TaskStarted = "task_started";
    public const string TaskRevised = "task_revised";
    public const string TaskAbandoned = "task_abandoned";
    public const string TaskEnded = "task_ended";
    public const string TaskCompleted = "task_completed";
}

/// <summary>Purposes allowed for Sensei's <c>move_object</c> (S31).</summary>
public static class MovePurposes
{
    public const string FulfillRequest = "fulfill_request";
    public const string Demonstrate = "demonstrate";
    public const string Arrange = "arrange";

    public static IReadOnlyList<string> All { get; } = new[] { FulfillRequest, Demonstrate, Arrange };
}
