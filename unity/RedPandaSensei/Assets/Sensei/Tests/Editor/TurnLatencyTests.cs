#nullable enable
using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Sensei.App;
using UnityEngine.TestTools;

public sealed class TurnLatencyTests
{
    [Test]
    public async Task ReleaseClockFlowsToEngineWorkAndLogsFirstPlaybackOnce()
    {
        var type = typeof(EngineHost).Assembly.GetType("Sensei.App.TurnLatency", true)!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var latency = Activator.CreateInstance(type, true)!;
        var active = type.GetProperty("Active", BindingFlags.Static | BindingFlags.NonPublic)!;
        var mark = type.GetMethod("Mark", flags)!;
        var playback = type.GetMethod("PlaybackStarted", flags)!;
        var firstSynth = type.GetMethod("FirstSynthesis", flags)!;
        try
        {
            active.SetValue(null, latency);
            Assert.That(await Task.Run(() => active.GetValue(null)), Is.SameAs(latency));
            Assert.That(firstSynth.Invoke(latency, null), Is.True);
            Assert.That(firstSynth.Invoke(latency, null), Is.False);
            LogAssert.Expect(UnityEngine.LogType.Log, new Regex(@"^RPS_LAT turn=\d+ phase=asr_done ms=\d+$"));
            mark.Invoke(latency, new object[] { "asr_done", "" });
            LogAssert.Expect(UnityEngine.LogType.Log, new Regex(@"^RPS_LAT turn=\d+ phase=first_audio_playing ms=\d+$"));
            playback.Invoke(latency, null);
            playback.Invoke(latency, null);
            LogAssert.NoUnexpectedReceived();
        }
        finally { active.SetValue(null, null); }
    }
}
