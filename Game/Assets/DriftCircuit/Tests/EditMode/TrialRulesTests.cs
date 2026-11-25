using DriftCircuit;
using NUnit.Framework;
using UnityEngine;

/// <summary>Exercises progression without relying on collider order or render frames.</summary>
public sealed class TrialRulesTests
{
    private readonly Checkpoint[] gates =
    {
        new("First", new Vector3(0, 0, 10), Vector3.forward, 3),
        new("Finish", new Vector3(0, 0, 20), Vector3.forward, 3)
    };

    [TestCase(100.00, 100.02, 1, 2)]
    [TestCase(100.02, 100.00, 2, 1)]
    [TestCase(100.00, 100.00, 1, 1)]
    [TestCase(100.00, null, 1, 2)]
    public void QuickRacePlacesUseFixedStepFinishTimes(double playerTime, double? rivalTime,
        int playerPlace, int rivalPlace)
    {
        var result = new QuickRaceResult(playerTime, rivalTime);
        Assert.That(result.PlayerPlace, Is.EqualTo(playerPlace));
        Assert.That(result.RivalPlace, Is.EqualTo(rivalPlace));
        Assert.That(result.PlayerTime, Is.EqualTo(playerTime));
        Assert.That(result.RivalTime, Is.EqualTo(rivalTime));
    }

    [TestCase(0, 47, false, true)]
    [TestCase(6, 47, false, true)]
    [TestCase(7, 47, false, false)]
    [TestCase(12, 6, false, true)]
    [TestCase(12, 12, false, false)]
    [TestCase(14, 0, false, false)]
    [TestCase(15, 0, false, false)]
    [TestCase(23.99949, 0, false, false)]
    [TestCase(23.9995, 47, false, true)]
    [TestCase(24, 47, false, true)]
    [TestCase(0, 47, true, false)]
    public void RustrailRouteChoiceRequiresScheduledClearanceAndFreeApproach(
        double elapsed, float distance, bool occupied, bool expected)
    {
        Bounds player = new(occupied ? new Vector3(-90, 0.58f, 4) : Vector3.zero, new Vector3(1.46f, 0.71f, 2.45f));
        Assert.That(TrialRival.CanTakeCrossing(elapsed, distance, player), Is.EqualTo(expected));
        // Bounds touching the decision or barrier end still occupy the approach.
        player.center = new Vector3(-84, 0.58f, 33);
        Assert.That(TrialRival.CanTakeCrossing(0, 47, player), Is.False);
    }

    /// <summary>Checks both sides of the phase boundaries and the recurring cycle.</summary>
    [Test]
    public void CrossingScheduleRoundsToMillisecondsBeforeWrapping()
    {
        Assert.That(RailCrossing.ScheduledPhase(0), Is.EqualTo(CrossingPhase.Open));
        Assert.That(RailCrossing.ScheduledPhase(11.99949), Is.EqualTo(CrossingPhase.Open));
        Assert.That(RailCrossing.ScheduledPhase(11.9995), Is.EqualTo(CrossingPhase.Closing));
        Assert.That(RailCrossing.ScheduledPhase(12), Is.EqualTo(CrossingPhase.Closing));
        Assert.That(RailCrossing.ScheduledPhase(14.99949), Is.EqualTo(CrossingPhase.Closing));
        Assert.That(RailCrossing.ScheduledPhase(15), Is.EqualTo(CrossingPhase.Closed));
        Assert.That(RailCrossing.ScheduledPhase(23.99949), Is.EqualTo(CrossingPhase.Closed));
        Assert.That(RailCrossing.ScheduledPhase(23.9995), Is.EqualTo(CrossingPhase.Open));
        Assert.That(RailCrossing.ScheduledPhase(24), Is.EqualTo(CrossingPhase.Open));
        Assert.That(RailCrossing.ScheduledPhase(36), Is.EqualTo(CrossingPhase.Closing));
        Assert.That(RailCrossing.ScheduledPhase(39), Is.EqualTo(CrossingPhase.Closed));
        Assert.That(RailCrossing.ScheduledPhase(48), Is.EqualTo(CrossingPhase.Open));
    }

    /// <summary>Rejects wrong order, reverse, out-of-width and duplicate motion.</summary>
    [Test]
    public void OnlyAcceptsForwardCrossingOfExpectedGate()
    {
        var rules = StartRace();
        Move(rules, 18, 22);
        Move(rules, 12, 8);
        Move(rules, 8, 12, 4);
        Assert.That(rules.NextGate, Is.Zero);
        Move(rules, 6, 15);
        Assert.That(rules.NextGate, Is.EqualTo(1));
        Move(rules, 6, 15);
        Assert.That(rules.NextGate, Is.EqualTo(1));
        Assert.That(rules.Laps, Is.Zero);
    }

    /// <summary>Rebasing at a respawn preserves progression and awards no gate or lap.</summary>
    [Test]
    public void ResetRebasesTheMovementSample()
    {
        var rules = StartRace();
        Move(rules, 8, 12);
        double before = rules.Elapsed;
        rules.Rebase(new Vector3(0, 0.5f, 22));
        rules.Tick(1, new Vector3(0, 0.5f, 22));
        Assert.That(rules.NextGate, Is.EqualTo(1));
        Assert.That(rules.Laps, Is.Zero);
        Assert.That(rules.Elapsed, Is.EqualTo(before + 1));
    }

    /// <summary>Verifies countdown and pause boundaries, split totals, and a clean retry.</summary>
    [Test]
    public void PauseAndRetryFreezeAndClearTheRightClocks()
    {
        var rules = new TrialRules(gates, Vector3.zero);
        rules.Tick(1, Vector3.zero);
        rules.TogglePause();
        rules.Tick(10, Vector3.zero);
        Assert.That(rules.Countdown, Is.EqualTo(2));
        rules.TogglePause();
        rules.Tick(2, Vector3.zero);
        Assert.That(rules.Phase, Is.EqualTo(TrialPhase.Racing));
        Assert.That(rules.Elapsed, Is.Zero);
        Move(rules, 8, 12);
        rules.TogglePause();
        rules.Tick(10, new Vector3(0, 0, 24));
        Assert.That(rules.Elapsed, Is.EqualTo(1));
        rules.TogglePause();
        Move(rules, 18, 22);
        for (int lap = 0; lap < 2; lap++)
        {
            Move(rules, 8, 12);
            Move(rules, 18, 22);
        }
        Assert.That(rules.Phase, Is.EqualTo(TrialPhase.Finished));
        for (int lap = 0; lap < 3; lap++)
            Assert.That(rules.LapSplit(lap), Is.EqualTo(2));
        Move(rules, 8, 12);
        Move(rules, 18, 22);
        Assert.That(rules.Laps, Is.EqualTo(3));
        Assert.That(rules.Elapsed, Is.EqualTo(6));
        rules.Restart(Vector3.zero);
        Assert.That(rules.Phase, Is.EqualTo(TrialPhase.Countdown));
        Assert.That(rules.Countdown, Is.EqualTo(3));
        Assert.That(rules.Laps, Is.Zero);
        Assert.That(rules.Elapsed, Is.Zero);
        Assert.That(rules.LapSplit(0), Is.Zero);
    }

    /// <summary>Starts a race with a fully elapsed countdown.</summary>
    private TrialRules StartRace()
    {
        var rules = new TrialRules(gates, Vector3.zero);
        rules.Tick(3, Vector3.zero);
        return rules;
    }

    /// <summary>Supplies one motion sample from a known starting point.</summary>
    private static void Move(TrialRules rules, float from, float to, float x = 0)
    {
        rules.Rebase(new Vector3(x, 0.5f, from));
        rules.Tick(1, new Vector3(x, 0.5f, to));
    }
}
