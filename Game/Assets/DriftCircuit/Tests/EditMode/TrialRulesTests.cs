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
