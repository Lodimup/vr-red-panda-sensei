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
