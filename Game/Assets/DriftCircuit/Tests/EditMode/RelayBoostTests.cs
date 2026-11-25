using DriftCircuit;
using NUnit.Framework;
using UnityEngine;

/// <summary>Checks swept pickup eligibility and shared boost handling without frame timing.</summary>
public sealed class RelayBoostTests
{
    private GameObject obj;
    private TrialKart kart;
    private TrialRules rules;
    private RelayBoostPickups pickups;

    /// <summary>Creates a disposable kart and starts its independent valid-lap clock.</summary>
    [SetUp]
    public void SetUp()
    {
        obj = new GameObject("Relay test kart");
        var body = obj.AddComponent<Rigidbody>();
        body.useGravity = false;
        kart = new TrialKart(body, obj.AddComponent<BoxCollider>());
        kart.Place(new(18, 0.58f, -64), Vector3.right);
        rules = new TrialRules(CourseDefinition.Docks.Gates, body.position);
        rules.Tick(3, body.position);
        pickups = new RelayBoostPickups();
        pickups.Sample(kart, rules);
    }

    /// <summary>Destroys all native objects made by a test.</summary>
    [TearDown]
    public void TearDown() => Object.DestroyImmediate(obj);

    /// <summary>Rejects reverse, stationary, lateral and high/low passes while accepting a long sweep.</summary>
    [TestCase(18, 22, -64, 0.58f, true)]
    [TestCase(-50, 70, -64, 0.58f, true)]
    [TestCase(22, 18, -64, 0.58f, false)]
    [TestCase(20, 20, -64, 0.58f, false)]
    [TestCase(18, 22, -56.2f, 0.58f, false)]
    [TestCase(18, 22, -71.8f, 0.58f, false)]
    [TestCase(18, 22, -64, 1.31f, false)]
    [TestCase(18, 22, -64, 0.14f, false)]
    public void SweptGateLimits(float from, float to, float z, float y, bool expected) =>
        Assert.That(RelayBoostPickups.Crossed(RelayBoostPickups.Gates[0], new(from, y, z), new(to, y, z)), Is.EqualTo(expected));

    /// <summary>Each gate is spent even with a full slot; only an ordered valid lap restores it.</summary>
    [Test]
    public void FullSlotSpendsGateAndValidLapRestoresIt()
    {
        kart.CollectRelayBoost();
        CrossFirstGate();
        kart.Body.linearVelocity = Vector3.right * 10;
        Assert.That(kart.TryUseRelayBoost(true, 0), Is.True);
        CrossFirstGate();
        Assert.That(kart.RelayBoostHeld, Is.False);
        foreach (Checkpoint gate in CourseDefinition.Docks.Gates)
        {
            rules.Rebase(gate.Center - gate.Forward + Vector3.up * 0.58f);
            rules.Tick(0.02, gate.Center + gate.Forward + Vector3.up * 0.58f);
        }
        Assert.That(rules.Laps, Is.EqualTo(1));
        CrossFirstGate();
        Assert.That(kart.RelayBoostHeld, Is.True);
    }

    /// <summary>Placement preserves held items and gate history but cannot collect its teleport path.</summary>
    [Test]
    public void RecoveryRebasesAndRetryRestoresAllowances()
    {
        kart.Place(new(22, 0.58f, -64), Vector3.right);
        pickups.Sample(kart, rules);
        Assert.That(kart.RelayBoostHeld, Is.False);
        CrossFirstGate();
        kart.ActivateBoost();
        kart.Place(new(18, 0.58f, -64), Vector3.right);
        Assert.That(kart.RelayBoostHeld, Is.True);
        Assert.That(kart.BoostActive, Is.False);
        kart.ClearItems();
        pickups.Sample(kart, rules);
        CrossFirstGate();
        Assert.That(kart.RelayBoostHeld, Is.False);
        pickups.Restart();
        pickups.Sample(kart, rules);
        CrossFirstGate();
        Assert.That(kart.RelayBoostHeld, Is.True);
    }

    /// <summary>Two independent samplers grant the same gate, while non-racing motion is discarded.</summary>
    [Test]
    public void IndependentRacersAndCountdownSuppression()
    {
        var other = new RelayBoostPickups();
        rules.Restart(kart.Body.position);
        CrossFirstGate();
        Assert.That(kart.RelayBoostHeld, Is.False);
        rules.Tick(3, kart.Body.position);
        CrossFirstGate();
        Assert.That(kart.RelayBoostHeld, Is.True);
        kart.ClearItems();
        kart.Body.position = new(18, 0.58f, -64);
        other.Sample(kart, rules);
        kart.Body.position = new(22, 0.58f, -64);
        other.Sample(kart, rules);
        Assert.That(kart.RelayBoostHeld, Is.True);
    }

    /// <summary>The westbound gate has its own allowance and requires its authored direction.</summary>
    [Test]
    public void WestboundGateIsIndependentAndDirectional()
    {
        CrossFirstGate();
        kart.Body.linearVelocity = Vector3.right * 10;
        Assert.That(kart.TryUseRelayBoost(true, 0), Is.True);
        kart.Place(new(22, 0.58f, 62), Vector3.left);
        pickups.Sample(kart, rules);
        kart.Body.position = new(18, 0.58f, 62);
        pickups.Sample(kart, rules);
        Assert.That(kart.RelayBoostHeld, Is.True);
        Assert.That(kart.ItemsCollected, Is.EqualTo(2));
        Assert.That(RelayBoostPickups.Crossed(RelayBoostPickups.Gates[1], new(18, 0.58f, 62),
            new(22, 0.58f, 62)), Is.False);
    }

    /// <summary>Invalid use preserves the slot; successful use has exactly 50 boosted 20 ms steps.</summary>
    [Test]
    public void ActivationValidityDurationAndBraking()
    {
        kart.CollectRelayBoost();
        kart.Body.linearVelocity = Vector3.right * 3.99f;
        Assert.That(kart.TryUseRelayBoost(true, 0), Is.False);
        kart.Body.linearVelocity = Vector3.left * 10;
        Assert.That(kart.TryUseRelayBoost(true, 0), Is.False);
        kart.Body.linearVelocity = Vector3.right * 10;
        Assert.That(kart.TryUseRelayBoost(false, 0), Is.False);
        Assert.That(kart.TryUseRelayBoost(true, 1), Is.False);
        Assert.That(kart.RelayBoostHeld, Is.True);
        Assert.That(kart.TryUseRelayBoost(true, 0), Is.True);
        for (int step = 0; step < 50; step++)
        {
            Assert.That(kart.BoostActive, Is.True, $"Step {step}");
            kart.Step(1, 0, 0, false, true, 0.02f);
        }
        Assert.That(kart.BoostActive, Is.False);
        Assert.That(kart.ForwardSpeed, Is.EqualTo(34).Within(0.001));
        kart.ActivateBoost();
        kart.Step(1, 1, 0, false, true, 0.02f);
        Assert.That(kart.ForwardSpeed, Is.LessThan(34), "Braking overrides an active boost.");
    }

    /// <summary>Simultaneous drift release and item use refresh one timer and apply one acceleration.</summary>
    [Test]
    public void DriftAndItemNeverStack()
    {
        kart.Body.linearVelocity = Vector3.right * 10;
        kart.Step(1, 0, 0, false, true, 0.02f);
        for (int i = 0; i < 25; i++)
            kart.Step(1, 0, 0.2f, true, true, 0.02f);
        Assert.That(kart.BoostReady, Is.True);
        kart.CollectRelayBoost();
        Assert.That(kart.TryUseRelayBoost(true, 0), Is.True);
        float before = kart.ForwardSpeed;
        kart.Step(1, 0, 0, false, true, 0.02f);
        Assert.That(kart.ForwardSpeed, Is.EqualTo(Mathf.Min(34, before + 29 * 0.02f)).Within(0.001));
        for (int i = 1; i < 50; i++)
            kart.Step(1, 0, 0, false, true, 0.02f);
        Assert.That(kart.BoostActive, Is.False);
    }

    /// <summary>Rebases behind the plane without changing placement, then samples a forward crossing.</summary>
    private void CrossFirstGate()
    {
        kart.Body.position = new(18, 0.58f, -64);
        pickups.Sample(kart, rules);
        kart.Body.position = new(22, 0.58f, -64);
        pickups.Sample(kart, rules);
    }
}
