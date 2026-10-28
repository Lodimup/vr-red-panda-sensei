using NUnit.Framework;
using Sensei.App.Core;
using Sensei.Engine.Learner;

public sealed class MenuTests
{
    [Test] public void ToggleAndResetConfirmation() { var state = new MenuState(); Assert.False(state.IsOpen); state.Toggle(); Assert.True(state.IsOpen); Assert.False(state.PressReset()); Assert.True(state.ResetPending); state.CancelReset(); Assert.False(state.ResetPending); Assert.False(state.PressReset()); state.Toggle(); Assert.False(state.ResetPending); state.Toggle(); Assert.False(state.PressReset()); Assert.True(state.PressReset()); Assert.False(state.ResetPending); }
    [Test] public void StartingBandsMatchLabels() { CollectionAssert.AreEqual(new[] { "I know no Japanese", "A few words", "Beginner", "Intermediate", "Advanced" }, MenuState.Labels); CollectionAssert.AreEqual(new[] { "zero", "early_beginner", "developing_beginner", "intermediate", "advanced" }, MenuState.Bands); for (var i = 0; i < 5; i++) Assert.AreEqual(MenuState.Bands[i], MenuState.BandFor(i)); }
    [Test] public void SummaryContainsCountsAndNoWorkingLevel() { var summary = new SessionSummary(); summary.Practiced.Add(new PracticedTarget { Label = "apple", Independent = 2, Supported = 3 }); var text = MenuState.FormatSummary(summary); StringAssert.Contains("apple: 2 independent, 3 supported", text); StringAssert.DoesNotContain("level", text.ToLowerInvariant()); Assert.AreEqual("Nothing practiced yet.", MenuState.FormatSummary(new SessionSummary())); }
}
