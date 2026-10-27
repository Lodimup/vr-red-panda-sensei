using Sensei.Engine.Speech;
using Sensei.Engine.Tests.TestKit;
using Xunit.Abstractions;

namespace Sensei.Engine.Tests.Integration;

public class ScratchHangProbe
{
    private readonly ITestOutputHelper _out;
    public ScratchHangProbe(ITestOutputHelper o) { _out = o; }

    private static async Task<T> Within<T>(Task<T> t, string what)
    {
        var done = await Task.WhenAny(t, Task.Delay(5000));
        if (done != t) throw new TimeoutException("HANG: " + what);
        return await t;
    }

    [Fact]
    public async Task Probe_S21_inspect_scene_unknown_property()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("hello");
        var r = await Within(t.Engine.InvokeToolAsync("inspect_scene", "{\"bogus\":1}"), "inspect_scene bogus");
        _out.WriteLine(r.Status + " " + r.ErrorCode + " " + r.Detail);
        r = await Within(t.Engine.InvokeToolAsync("inspect_scene", "{\"bogus\":1}"), "inspect_scene bogus 2");
        _out.WriteLine(r.Status + " " + r.ErrorCode + " " + r.Detail);
    }

    [Fact]
    public async Task Probe_S35_unchanged_without_object_id()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("hello");
        var s = await Within(t.Engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}"), "spawn");
        _out.WriteLine(s.Status + " " + s.ErrorCode + " " + s.Detail);
        t.Engine.MarkRoundStart();
        var r = await Within(t.Engine.InvokeToolAsync("start_challenge", "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"x\"},\"skill_targets\":[{\"dimension\":\"vocabulary\",\"key\":\"cup\"}],\"predicates\":[{\"type\":\"unchanged\"}]}"), "start_challenge unchanged");
        _out.WriteLine(r.Status + " " + r.ErrorCode + " " + r.Detail);
    }
}

public class ScratchWireProbe
{
    private readonly ITestOutputHelper _out;
    public ScratchWireProbe(ITestOutputHelper o) { _out = o; }

    private static async Task<T> Within<T>(Task<T> t, string what)
    {
        var done = await Task.WhenAny(t, Task.Delay(8000));
        if (done != t) throw new TimeoutException("HANG: " + what);
        return await t;
    }

    [Theory]
    [InlineData("inspect_scene", "{\"bogus\":1}")]
    [InlineData("inspect_scene", "{}")]
    [InlineData("spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}],\"bogus\":1}")]
    public async Task Probe_wire_tool(string tool, string args)
    {
        using var server = new Sensei.Engine.Tests.Agent.FakeChatServer().EnqueueToolCalls(("call_1", tool, args)).EnqueueText("done");
        var client = Sensei.Engine.Llm.ChatClientFactory.Create(new Sensei.Engine.Llm.LlmSettings("openai_compatible", "key-1234567890", "m", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client);
        var r = await Within(t.Engine.RunTurnAsync("hello"), tool);
        _out.WriteLine(r.Status + " rounds=" + r.Rounds + " calls=" + r.ToolCalls.Count + " " + r.ToolCalls[0].Result?.Status + " " + r.ToolCalls[0].Result?.Detail);
    }
}

public class ScratchHammer
{
    private readonly ITestOutputHelper _out;
    public ScratchHammer(ITestOutputHelper o) { _out = o; }

    [Fact]
    public async Task Hammer_threads()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("hello");
        var s = await t.Engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\",\"quantity\":3},{\"catalog_id\":\"pen\",\"quantity\":3}]}");
        t.Engine.MarkRoundStart();
        _out.WriteLine(s.Status + " " + s.ErrorCode + " " + s.Detail);
        var ids = s.AffectedIds.ToList();
        var st = await t.Engine.InvokeToolAsync("start_challenge", "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"x\"},\"skill_targets\":[{\"dimension\":\"vocabulary\",\"key\":\"cup\"}],\"predicates\":[{\"type\":\"selected\",\"object_id\":\"" + ids[0] + "\"}]}");
        _out.WriteLine(st.Status + " " + st.Detail);
        var threads = new List<Thread>();
        var errors = new List<Exception>();
        for (var k = 0; k < 16; k++)
        {
            var kk = k;
            var th = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < 500; i++)
                    {
                        switch ((i + kk) % 5)
                        {
                            case 0: t.Engine.PlayerSelect(ids[(i + kk) % ids.Count]); break;
                            case 1: t.Engine.GetScene(); break;
                            case 2: t.Engine.PlayerMove(ids[i % ids.Count], new[] { "left", "right", "center" }[i % 3], null); break;
                            case 3: t.Engine.GetProfile(); t.Engine.GetActiveTask(); break;
                            default: t.Engine.InvokeToolAsync("inspect_scene", "{}", "c" + kk + "_" + i).GetAwaiter().GetResult(); break;
                        }
                    }
                }
                catch (Exception e) { lock (errors) errors.Add(e); }
            });
            th.Start();
            threads.Add(th);
        }
        foreach (var th in threads) Assert.True(th.Join(30000), "HANG");
        Assert.Empty(errors);
    }
}

public class ScratchEvents
{
    private readonly ITestOutputHelper _out;
    public ScratchEvents(ITestOutputHelper o) { _out = o; }

    [Fact]
    public async Task Events_flow()
    {
        var ev = new RecordingEvents();
        await using var t = await TestEngine.CreateAsync(events: ev);
        t.Engine.BeginTurn("hello");
        var s = await t.Engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"},{\"catalog_id\":\"apple\"}]}");
        t.Engine.MarkRoundStart();
        var st = await t.Engine.InvokeToolAsync("start_challenge", "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"x\"},\"skill_targets\":[{\"dimension\":\"vocabulary\",\"key\":\"cup\"},{\"dimension\":\"vocabulary\",\"key\":\"apple\"}],\"predicates\":[{\"type\":\"in_region\",\"object_id\":\"cup_1\",\"region\":\"left\"}]}");
        _out.WriteLine("start: " + st.Status + " " + st.Detail);
        var mv = await t.Engine.InvokeToolAsync("move_object", "{\"object_id\":\"cup_1\",\"destination\":{\"region\":\"left\"},\"purpose\":\"demonstrate\"}");
        _out.WriteLine("move: " + mv.Status + " " + mv.Detail + " " + mv.Data);
        foreach (var e in ev.SceneEvents) _out.WriteLine($"cb event {e.EventId} {e.Kind} rev={e.Revision} actor={e.Actor}");
        foreach (var e in t.Core.World.Events) _out.WriteLine($"world event {e.EventId} {e.Kind} rev={e.Revision}");
        _out.WriteLine(string.Join(",", ev.Methods));
    }
}

public class ScratchBlankName
{
    private readonly ITestOutputHelper _out;
    public ScratchBlankName(ITestOutputHelper o) { _out = o; }

    [Fact]
    public async Task Blank_name()
    {
        using var server = new Sensei.Engine.Tests.Agent.FakeChatServer().EnqueueToolCalls(("c1", "", "{}")).EnqueueText("done");
        var client = Sensei.Engine.Llm.ChatClientFactory.Create(new Sensei.Engine.Llm.LlmSettings("openai_compatible", "key-1234567890", "m", server.BaseUrl));
        await using var t = await TestEngine.CreateAsync(chatClient: client);
        var r = await t.Engine.RunTurnAsync("hello");
        _out.WriteLine($"{r.Status} {r.ErrorCode} {r.ErrorDetail}");
        foreach (var req in server.Requests) _out.WriteLine(req.Body.Length > 1500 ? req.Body[^900..] : req.Body);
    }
}

public class ScratchReflect
{
    private readonly ITestOutputHelper _out;
    public ScratchReflect(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Reflect()
    {
        var t = typeof(Microsoft.Agents.AI.ChatClientAgent);
        _out.WriteLine($"sealed={t.IsSealed} abstract={t.IsAbstract}");
        foreach (var c in t.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            _out.WriteLine(c.ToString() + " public=" + c.IsPublic + " family=" + c.IsFamily);
        var asm = typeof(Sensei.Engine.SenseiEngine).Assembly;
        foreach (var x in asm.GetTypes().Where(x => x.Name.Contains("Agent") || x.Name.Contains("Context"))) _out.WriteLine(x.FullName + " : " + x.BaseType);
    }
}

public class ScratchExcerpt
{
    private readonly ITestOutputHelper _out;
    public ScratchExcerpt(ITestOutputHelper o) { _out = o; }

    private const string Rec = "{\"target\":{\"dimension\":\"vocabulary\",\"key\":\"cup\"},\"transcript_excerpt\":\"コップをください\",\"communicative_success\":true,\"language_assessment\":\"correct\"}";

    [Fact]
    public async Task Flows()
    {
        // 1 BeginTurn
        await using (var t = await TestEngine.CreateAsync())
        {
            t.Engine.BeginTurn("コップをください");
            var r = await t.Engine.InvokeToolAsync("record_attempt", Rec);
            _out.WriteLine("begin: " + r.Status + " " + r.ErrorCode + " " + r.Detail);
        }
        // 2 RunTurnAsync without a client
        await using (var t = await TestEngine.CreateAsync())
        {
            var turn = await t.Engine.RunTurnAsync("コップをください");
            var r = await t.Engine.InvokeToolAsync("record_attempt", Rec);
            _out.WriteLine("run-null: " + turn.Status + " " + turn.ErrorCode + " / " + r.Status + " " + r.ErrorCode + " " + r.Detail);
        }
        // 3 RunTurnAsync with scripted client, tools after
        {
            var chat = new Sensei.Engine.Tests.TestKit.ScriptedChatClient().EnqueueText("ok");
            await using var t = await TestEngine.CreateAsync(chat: chat);
            var turn = await t.Engine.RunTurnAsync("コップをください");
            var r = await t.Engine.InvokeToolAsync("record_attempt", Rec);
            _out.WriteLine("run-chat: " + turn.Status + " / " + r.Status + " " + r.ErrorCode + " " + r.Detail);
        }
        // 4 two turns
        await using (var t = await TestEngine.CreateAsync())
        {
            t.Engine.BeginTurn("コップをください");
            t.Engine.BeginTurn("second");
            var r = await t.Engine.InvokeToolAsync("record_attempt", Rec);
            _out.WriteLine("prev-turn: " + r.Status + " " + r.ErrorCode + " " + r.Detail);
        }
        // 5 after player actions, spawn
        await using (var t = await TestEngine.CreateAsync())
        {
            t.Engine.BeginTurn("コップをください");
            var s = await t.Engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}");
            t.Engine.PlayerMove("cup_1", "left", null);
            var s2 = await t.Engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"apple\"}]}");
            _out.WriteLine("stale-after-player: " + s2.Status + " " + s2.ErrorCode);
            t.Engine.MarkRoundStart();
            var s3 = await t.Engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"apple\"}]}");
            _out.WriteLine("after MarkRoundStart: " + s3.Status + " " + s3.ErrorCode);
        }
    }
}

public class ScratchAgentFlow
{
    private readonly ITestOutputHelper _out;
    public ScratchAgentFlow(ITestOutputHelper o) { _out = o; }

    private const string Rec = "{\"target\":{\"dimension\":\"vocabulary\",\"key\":\"cup\"},\"transcript_excerpt\":\"コップをください\",\"communicative_success\":true,\"language_assessment\":\"correct\"}";
    private const string Start = "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"x\"},\"skill_targets\":[{\"dimension\":\"vocabulary\",\"key\":\"cup\"}],\"predicates\":[{\"type\":\"in_region\",\"object_id\":\"cup_1\",\"region\":\"left\"}]}";

    [Fact]
    public async Task Agent_flow_with_empty_call_ids()
    {
        var chat = new Sensei.Engine.Tests.TestKit.ScriptedChatClient()
            .EnqueueFunctionCalls(("", "spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}"))
            .EnqueueFunctionCalls(("", "start_challenge", Start))
            .EnqueueFunctionCalls(("", "update_challenge", "{\"task_id\":\"t1\",\"action\":\"abandon\",\"reason\":\"x\"}"), ("", "record_attempt", Rec))
            .EnqueueText("done");
        await using var t = await TestEngine.CreateAsync(chat: chat);
        var r = await t.Engine.RunTurnAsync("コップをください");
        _out.WriteLine(r.Status + " rounds=" + r.Rounds);
        foreach (var c in r.ToolCalls) _out.WriteLine($"{c.CallId} {c.Tool} {c.Status} {c.ErrorCode} {c.Result?.Detail}");
    }
}

public class ScratchEventFuzz
{
    private readonly ITestOutputHelper _out;
    public ScratchEventFuzz(ITestOutputHelper o) { _out = o; }

    [Fact]
    public async Task Callbacks_match_world_events()
    {
        var rnd = new Random(7);
        for (var run = 0; run < 50; run++)
        {
            var ev = new RecordingEvents();
            await using var t = await TestEngine.CreateAsync(events: ev);
            t.Engine.BeginTurn("hello");
            await t.Engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"},{\"catalog_id\":\"box\"},{\"catalog_id\":\"apple\"}]}");
            t.Engine.MarkRoundStart();
            for (var i = 0; i < 25; i++)
            {
                switch (rnd.Next(9))
                {
                    case 0: t.Engine.PlayerSelect(new[] { "cup_1", "box_1", "apple_1", "nope" }[rnd.Next(4)]); break;
                    case 1: t.Engine.PlayerMove(new[] { "cup_1", "box_1", "apple_1" }[rnd.Next(3)], new[] { "left", "center", "right" }[rnd.Next(3)], null); break;
                    case 2: t.Engine.PlayerMove("cup_1", null, "box_1"); break;
                    case 3: await t.Engine.InvokeToolAsync("move_object", "{\"object_id\":\"apple_1\",\"destination\":{\"region\":\"" + new[] { "left", "center", "right" }[rnd.Next(3)] + "\"},\"purpose\":\"arrange\"}"); break;
                    case 4: await t.Engine.InvokeToolAsync("start_challenge", "{\"family\":\"discover_request\",\"instruction\":{\"english\":\"x\"},\"skill_targets\":[{\"dimension\":\"vocabulary\",\"key\":\"cup\"}],\"predicates\":[{\"type\":\"in_region\",\"object_id\":\"apple_1\",\"region\":\"" + new[] { "left", "center", "right" }[rnd.Next(3)] + "\"}]}"); break;
                    case 5: await t.Engine.InvokeToolAsync("update_challenge", "{\"task_id\":\"t" + (1 + rnd.Next(3)) + "\",\"action\":\"abandon\",\"reason\":\"x\"}"); break;
                    case 6: t.Engine.MarkRoundStart(); break;
                    case 7: await t.Engine.InvokeToolAsync("end_activity", "{\"next_suggestion\":\"more\"}"); t.Engine.BeginTurn("again"); break;
                    default: await t.Engine.InvokeToolAsync("remove_objects", "{\"object_ids\":[\"apple_1\"]}"); break;
                }
            }
            var cb = ev.SceneEvents.Select(e => e.EventId).ToList();
            var world = t.Core.World.Events.Select(e => e.EventId).ToList();
            if (!cb.SequenceEqual(world)) { _out.WriteLine($"run {run}: callbacks {string.Join(",", cb)} vs world {string.Join(",", world)}"); }
        }
    }
}
