using System.Text.Json;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Learner.LearnerKit;

namespace Sensei.Engine.Tests.Learner.ReviewFixes;

/// <summary>S46: the evidence record always contains <c>task_id</c>, null when no task is bound.</summary>
public sealed class S46TaskIdTests
{
    [Fact]
    public async Task S46_the_returned_record_contains_task_id_as_null_without_a_bound_task()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("no_task", Str(r, "engine_outcome"));
        Assert.True(DataOf(r).TryGetProperty("task_id", out var taskId), "task_id must be present, not omitted");
        Assert.Equal(JsonValueKind.Null, taskId.ValueKind);
    }

    [Fact]
    public async Task S46_the_duplicate_response_contains_task_id_as_null_without_a_bound_task()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        Assert.True((await Attempt(t, "production", "ringo", "りんご")).IsSuccess);
        var again = await Attempt(t, "production", "ringo", "りんご");
        Assert.True(DataOf(again).GetProperty("duplicate").GetBoolean());
        Assert.True(DataOf(again).TryGetProperty("task_id", out var taskId), "task_id must be present, not omitted");
        Assert.Equal(JsonValueKind.Null, taskId.ValueKind);
    }

    [Fact]
    public async Task S46_the_stored_record_contains_task_id_as_null_without_a_bound_task()
    {
        await using var t = await TestEngine.CreateAsync();
        Assert.True((await SayAndAttempt(t, "production", "ringo", "りんごをください")).IsSuccess);
        using var doc = JsonDocument.Parse(t.Store.RawJson("default")!);
        var record = Assert.Single(doc.RootElement.GetProperty("evidence").EnumerateArray());
        Assert.True(record.TryGetProperty("task_id", out var taskId), "task_id must be present in the stored record, not omitted");
        Assert.Equal(JsonValueKind.Null, taskId.ValueKind);
    }

    [Fact]
    public async Task S46_a_bound_task_keeps_its_id_in_the_returned_and_stored_record()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        var (task, _) = StartTask(t, "production", "ringo", "りんご");
        var r = await Attempt(t, "production", "ringo", "りんご");
        Assert.Equal(task.TaskId, Str(r, "task_id"));
        using var doc = JsonDocument.Parse(t.Store.RawJson("default")!);
        var record = Assert.Single(doc.RootElement.GetProperty("evidence").EnumerateArray());
        Assert.Equal(task.TaskId, record.GetProperty("task_id").GetString());
    }
}
