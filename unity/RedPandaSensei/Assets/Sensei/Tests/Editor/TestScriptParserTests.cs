using System;
using NUnit.Framework;
using Sensei.App.Core;

public class TestScriptParserTests
{
    [Test] public void ParsesAllVerbsAndComments()
    {
        var steps = TestScriptParser.Parse("# test\nwait_idle\nwav hello\nevent help_hint\nselect apple\nmove apple left\ninterrupt_after_ms 1500 hello\nsleep 1.5\nlog ready\n");
        Assert.That(steps.Count, Is.EqualTo(8)); Assert.That(steps[5].Arguments[0], Is.EqualTo("1500"));
    }
    [TestCase("wav ../secret")][TestCase("wav a/b")][TestCase("event")][TestCase("sleep NaN")][TestCase("wait_idle -1")][TestCase("interrupt_after_ms nope a")][TestCase("unknown x")]
    public void RejectsInvalidSteps(string line) => Assert.Throws<FormatException>(() => TestScriptParser.Parse(line));
    [Test] public void MissingScriptDoesNotEnableDriver() { Assert.That(TestScriptParser.Enabled(null), Is.False); Assert.That(TestScriptParser.Enabled(""), Is.False); Assert.That(TestScriptParser.Enabled("  "), Is.False); Assert.That(TestScriptParser.Enabled("a01.txt"), Is.True); }
    [Test] public void EmptyScriptDoesNothing() => Assert.That(TestScriptParser.Parse("").Count, Is.Zero);
}
