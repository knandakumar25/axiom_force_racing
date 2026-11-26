using DriftCircuit;
using NUnit.Framework;
using UnityEngine;

/// <summary>Deterministic coverage for Docks item placement, effects, swept contacts and rival decisions.</summary>
public sealed class RaceItemTests
{
    private GameObject floor, first, second;
    private TrialKart owner, target;
    private RaceItems items;

    /// <summary>Uses disposable native bodies over a real ground collider for grounded prediction checks.</summary>
    [SetUp]
    public void SetUp()
    {
        floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new(0, -0.5f, 0);
        floor.transform.localScale = new(220, 1, 160);
        owner = MakeKart(out first);
        target = MakeKart(out second);
        items = new RaceItems();
        Put(owner, new(30, 0.58f, -64), Vector3.right, 15);
        Put(target, new(10, 0.58f, -64), Vector3.right, 15);
    }

    /// <summary>Destroys every test object rather than leaving orphan native resources.</summary>
    [TearDown]
    public void TearDown()
    {
        items.Clear();
        Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(floor);
    }

    /// <summary>Creates a kart on the non-ground layer so its collider cannot satisfy another's grounding ray.</summary>
    private static TrialKart MakeKart(out GameObject obj)
    {
        obj = new GameObject("Item test kart") { layer = 2 };
        var body = obj.AddComponent<Rigidbody>(); body.useGravity = false;
        return new TrialKart(body, obj.AddComponent<BoxCollider>());
    }

    /// <summary>Places without a physical sweep and explicitly synchronizes subsequent ground queries.</summary>
    private static void Put(TrialKart kart, Vector3 position, Vector3 forward, float speed)
    { kart.Place(position, forward); kart.Body.linearVelocity = forward * speed; Physics.SyncTransforms(); }

    /// <summary>All six gates enforce direction, stationary suppression, swept width and height constraints.</summary>
    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
    public void EveryGateHasSweptBounds(int index)
    {
        Checkpoint gate = RelayBoostPickups.Gates[index];
        Vector3 side = Vector3.Cross(Vector3.up, gate.Forward);
        Vector3 from = gate.Center - gate.Forward * 60 + Vector3.up * 0.58f;
        Vector3 to = gate.Center + gate.Forward * 60 + Vector3.up * 0.58f;
        Assert.That(RelayBoostPickups.Crossed(gate, from, to), Is.True);
        Assert.That(RelayBoostPickups.Crossed(gate, to, from), Is.False);
        Assert.That(RelayBoostPickups.Crossed(gate, from, from), Is.False);
        Assert.That(RelayBoostPickups.Crossed(gate, from + side * 7.8f, to + side * 7.8f), Is.False);
        Assert.That(RelayBoostPickups.Crossed(gate, from + Vector3.up, to + Vector3.up), Is.False);
        Assert.That(RelayBoostPickups.Crossed(gate, from - Vector3.up, to - Vector3.up), Is.False);
    }

    /// <summary>Each gate/racer has an independent allowance; full slots spend it and Retry restores it.</summary>
    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
    public void EveryGateKeepsFullSlotsAndIndependentHistory(int index)
    {
        Checkpoint gate = RelayBoostPickups.Gates[index];
        var rules = new TrialRules(CourseDefinition.Docks.Gates, owner.Body.position); rules.Tick(3, owner.Body.position);
        var pickups = new RelayBoostPickups(); var other = new RelayBoostPickups();
        owner.CollectItem(RaceItem.PulseShield);
        Cross(pickups, owner, rules, gate);
        Assert.That(owner.HeldItem, Is.EqualTo(RaceItem.PulseShield));
        owner.ConsumeItem(); Cross(pickups, owner, rules, gate);
        Assert.That(owner.HeldItem, Is.EqualTo(RaceItem.None));
        Cross(other, target, rules, gate);
        Assert.That(target.HeldItem, Is.EqualTo(RelayBoostPickups.Items[index]));
        // Only an ordered lap, rather than driving the gate again or recovering, renews the allowance.
        foreach (Checkpoint checkpoint in CourseDefinition.Docks.Gates)
        { rules.Rebase(checkpoint.Center - checkpoint.Forward); rules.Tick(0.02, checkpoint.Center + checkpoint.Forward); }
        Cross(pickups, owner, rules, gate);
        Assert.That(owner.HeldItem, Is.EqualTo(RelayBoostPickups.Items[index]));
        owner.ClearItems(); pickups.Restart(); Cross(pickups, owner, rules, gate);
        Assert.That(owner.HeldItem, Is.EqualTo(RelayBoostPickups.Items[index]));
    }

    /// <summary>Isolates a single gate crossing, including the recovery generation rebase.</summary>
    private static void Cross(RelayBoostPickups pickups, TrialKart kart, TrialRules rules, Checkpoint gate)
    {
        Put(kart, gate.Center - gate.Forward + Vector3.up * 0.58f, gate.Forward, 10);
        pickups.Sample(kart, rules);
        kart.Body.position += gate.Forward * 2; pickups.Sample(kart, rules);
    }

    /// <summary>Authoring limits include direction, road offset and protected grid/checkpoint/recovery poses.</summary>
    [TestCase(30, -64, true, true)] [TestCase(-40, -64, true, true)]
    [TestCase(0, 62, false, true)] [TestCase(-40, 62, false, true)]
    [TestCase(30, 62, false, false)] [TestCase(20, -64, true, false)]
    [TestCase(0, -64, true, false)] [TestCase(51, -64, true, false)]
    [TestCase(-51, -64, true, false)] [TestCase(-40, -59.49f, true, false)]
    [TestCase(30, -64, false, false)] [TestCase(0, 62, true, false)]
    public void PlacementLimits(float x, float z, bool east, bool expected)
    {
        Put(owner, new(x, 0.58f, z), east ? Vector3.right : Vector3.left, 15);
        Assert.That(RaceItems.TryPlacement(owner, out _, out _), Is.EqualTo(expected));
    }

    /// <summary>Invalid drops retain the slot and an owner can deploy again only after expiry or recovery.</summary>
    [Test]
    public void RibbonActivationRetentionAndLifetime()
    {
        owner.CollectItem(RaceItem.FluxRibbon);
        Assert.That(items.Activate(owner, false, 0), Is.False);
        Assert.That(items.Activate(owner, true, 1), Is.False);
        owner.Body.linearVelocity = Vector3.right * 4;
        Assert.That(items.Activate(owner, true, 0), Is.False);
        owner.Body.linearVelocity = Vector3.right * 15;
        Assert.That(items.Activate(owner, true, 0), Is.True);
        Assert.That(items.Ribbons[0].Center.x, Is.EqualTo(26.2f).Within(0.001));
        Assert.That(items.Ribbons[0].Armed, Is.False);
        owner.CollectItem(RaceItem.FluxRibbon);
        Assert.That(items.Activate(owner, true, 0), Is.False);
        for (int i = 0; i < 19; i++) items.Advance(0.02f);
        Assert.That(items.Ribbons[0].Armed, Is.False);
        items.Advance(0.02f); Assert.That(items.Ribbons[0].Armed, Is.True);
        for (int i = 20; i < 300; i++) items.Advance(0.02f);
        Assert.That(items.Ribbons[0].Armed, Is.False);
        items.Advance(0.02f); // Final expired rectangle is retained for one last pre-expiry motion sample.
        Assert.That(items.Ribbons, Is.Empty);
        Assert.That(items.Activate(owner, true, 0), Is.True);
        items.Recover(owner);
        Assert.That(items.Ribbons, Is.Empty);
        Assert.That(owner.DisruptionImmune, Is.True);
    }

    /// <summary>Grounded fast passage hits once; ownership, height, lateral misses and placement suppress contacts.</summary>
    [TestCase(0, 0.58f, false, true)] [TestCase(2.26f, 0.58f, false, false)]
    [TestCase(0, 2f, false, false)] [TestCase(0, 0.58f, true, false)]
    public void SweptContact(float lateral, float height, bool teleport, bool hit)
    {
        Deploy();
        Put(target, new(0, height, -64 + lateral), Vector3.right, 20);
        items.Resolve(target);
        if (teleport) Put(target, new(60, height, -64 + lateral), Vector3.right, 20);
        else { target.Body.position = new(60, height, -64 + lateral); Physics.SyncTransforms(); }
        items.Resolve(target);
        Assert.That(target.GripMultiplier, Is.EqualTo(hit ? 0.55f : 1f));
        owner.Body.position = new(26.2f, 0.58f, -64); Physics.SyncTransforms();
        items.Resolve(owner); items.Resolve(owner);
        Assert.That(owner.GripMultiplier, Is.EqualTo(1));
        target.AdvanceEffects(3);
        items.Resolve(target);
        Assert.That(target.GripMultiplier, Is.EqualTo(1));
    }

    /// <summary>Same-step shield absorbs one hit; immunity consumes another patch's opportunity without delay.</summary>
    [Test]
    public void ShieldContactPrecedenceAndImmunity()
    {
        Deploy(); items.Resolve(target);
        target.CollectItem(RaceItem.PulseShield);
        Assert.That(items.Activate(target, false, 1), Is.True);
        target.Body.position = new(26.2f, 0.58f, -64); Physics.SyncTransforms();
        items.Resolve(target);
        Assert.That(target.ShieldActive, Is.False);
        Assert.That(target.GripMultiplier, Is.EqualTo(1));
        Assert.That(target.ImmunityRemaining, Is.EqualTo(1.5d));
        items.Recover(owner); owner.CollectItem(RaceItem.FluxRibbon);
        Assert.That(items.Activate(owner, true, 0), Is.True); items.Advance(0.4f);
        items.Resolve(target);
        target.AdvanceEffects(1.5f); items.Resolve(target);
        Assert.That(target.GripMultiplier, Is.EqualTo(1));
        Assert.That(items.Ribbons[0].Spent.Contains(target), Is.True);
    }

    /// <summary>Effect lifetimes are exact fixed-step counts; shield activation does not cure existing grip loss.</summary>
    [Test]
    public void ExactEffectTimersAndShieldRetention()
    {
        target.ReceiveRibbonHit(); target.CollectItem(RaceItem.PulseShield);
        Assert.That(target.TryUseShield(), Is.True);
        Assert.That(target.GripMultiplier, Is.EqualTo(0.55f));
        target.CollectItem(RaceItem.PulseShield);
        Assert.That(target.TryUseShield(), Is.False);
        Assert.That(target.HeldItem, Is.EqualTo(RaceItem.PulseShield));
        for (int i = 0; i < 50; i++) target.AdvanceEffects(0.02f);
        Assert.That(target.GripMultiplier, Is.EqualTo(1));
        Assert.That(target.ImmunityRemaining, Is.EqualTo(1.5d).Within(0.000001));
        for (int i = 0; i < 75; i++) target.AdvanceEffects(0.02f);
        Assert.That(target.DisruptionImmune, Is.False);
        for (int i = 0; i < 25; i++) target.AdvanceEffects(0.02f);
        Assert.That(target.ShieldActive, Is.False);
        target.RecoverEffects(); Assert.That(target.DisruptionImmune, Is.True);
        Assert.That(target.HeldItem, Is.EqualTo(RaceItem.PulseShield));
        target.ClearItems(); Assert.That(target.DisruptionImmune || target.ShieldActive, Is.False);
    }

    /// <summary>A new shield absorbs a distinct hit during existing grip loss without curing or refreshing that loss.</summary>
    [Test]
    public void ShieldAbsorbsWhileExistingGripLossContinues()
    {
        target.ReceiveRibbonHit(); target.AdvanceEffects(0.2f);
        double remaining = target.GripLossRemaining;
        target.CollectItem(RaceItem.PulseShield); target.TryUseShield(); target.ReceiveRibbonHit();
        Assert.That(target.ShieldActive, Is.False);
        Assert.That(target.ImmunityRemaining, Is.EqualTo(1.5d));
        Assert.That(target.GripLossRemaining, Is.EqualTo(remaining));
        Assert.That(target.GripMultiplier, Is.EqualTo(0.55f));
        target.AdvanceEffects(0.8f);
        Assert.That(target.GripMultiplier, Is.EqualTo(1f));
        Assert.That(target.ImmunityRemaining, Is.EqualTo(1.5d).Within(0.000001d));
    }

    /// <summary>Disruption multiplies normal and drifting grip while leaving longitudinal and steering inputs intact.</summary>
    [TestCase(false)] [TestCase(true)]
    public void GripLossPreservesHandling(bool drift)
    {
        Put(owner, new(30, 0.58f, -64), Vector3.forward, 12);
        Put(target, new(10, 0.58f, -64), Vector3.forward, 12);
        owner.Step(1, 0, 0, false, true, 0.02f); target.Step(1, 0, 0, false, true, 0.02f);
        owner.Body.linearVelocity = target.Body.linearVelocity = new(5, 0, 12);
        target.ReceiveRibbonHit();
        owner.Step(1, 0, 0.3f, drift, true, 0.02f); target.Step(1, 0, 0.3f, drift, true, 0.02f);
        Assert.That(target.Body.linearVelocity.z, Is.EqualTo(owner.Body.linearVelocity.z).Within(0.001));
        float coefficient = drift ? 2.8f : 10f;
        Assert.That(target.Body.linearVelocity.x, Is.EqualTo(5 * Mathf.Exp(-coefficient * 0.55f * 0.02f)).Within(0.001));
        target.ActivateBoost(); float before = target.ForwardSpeed;
        target.Step(1, 1, 0.3f, drift, true, 0.02f);
        Assert.That(target.ForwardSpeed, Is.LessThan(before));
        Assert.That(target.BoostActive, Is.True);
    }

    /// <summary>Rival drops require every eligibility predicate; path prediction handles arming, expiry and misses.</summary>
    [Test]
    public void RivalOffenseAndThreatPrediction()
    {
        owner.CollectItem(RaceItem.FluxRibbon);
        Assert.That(items.RivalShouldDrop(owner, target, false, false), Is.True);
        Assert.That(items.RivalShouldDrop(owner, target, true, false), Is.False);
        Assert.That(items.RivalShouldDrop(owner, target, false, true), Is.False);
        target.RecoverEffects(); Assert.That(items.RivalShouldDrop(owner, target, false, false), Is.False);
        target.ClearItems(); target.CollectItem(RaceItem.PulseShield); target.TryUseShield();
        Assert.That(items.RivalShouldDrop(owner, target, false, false), Is.False);
        target.ClearItems();
        items.Activate(owner, true, 0);
        Assert.That(items.Incoming(target), Is.True);
        target.Body.linearVelocity = Vector3.zero; Assert.That(items.Incoming(target), Is.False);
        target.Body.linearVelocity = Vector3.right * 15;
        target.Body.position += Vector3.forward * 3; Physics.SyncTransforms();
        Assert.That(items.Incoming(target), Is.False);
        target.Body.position -= Vector3.forward * 3; Physics.SyncTransforms();
        items.Advance(5.9f); Assert.That(items.Incoming(target), Is.False);
        items.RemoveOwned(owner); Assert.That(items.Ribbons, Is.Empty);
    }

    /// <summary>Deploys a valid, armed owner patch for swept-contact tests.</summary>
    private void Deploy()
    { owner.CollectItem(RaceItem.FluxRibbon); Assert.That(items.Activate(owner, true, 0), Is.True); items.Advance(0.4f); }

    /// <summary>Uses nonzero sample intervals to verify actual passage time at arming and expiry boundaries.</summary>
    [TestCase(0.38f, 0.04f, 20, 60, false)]
    [TestCase(0.38f, 0.04f, 0, 28, true)]
    [TestCase(5.98f, 0.04f, 20, 60, true)]
    [TestCase(5.98f, 0.04f, 0, 28, false)]
    [TestCase(0f, 0.02f, 20, 60, false)]
    public void SweptContactsClipToArmedLifetime(float startAge, float duration, float from, float to, bool hit)
    {
        owner.CollectItem(RaceItem.FluxRibbon); items.Activate(owner, true, 0);
        items.Advance(startAge);
        Put(target, new(from, 0.58f, -64), Vector3.right, 15); items.Resolve(target);
        items.Advance(duration);
        target.Body.position = new(to, 0.58f, -64); Physics.SyncTransforms(); items.Resolve(target);
        Assert.That(target.GripMultiplier, Is.EqualTo(hit ? 0.55f : 1f));
    }

    /// <summary>Varies one offensive eligibility condition at a time and verifies rejected decisions retain Ribbon.</summary>
    [TestCase("owner-airborne")] [TestCase("owner-slow")] [TestCase("target-airborne")]
    [TestCase("target-reverse")] [TestCase("target-stationary")] [TestCase("too-close")]
    [TestCase("too-far")] [TestCase("target-angle")] [TestCase("owner-angle")]
    [TestCase("early-arrival")] [TestCase("late-arrival")] [TestCase("lateral-miss")]
    [TestCase("live-owned")] [TestCase("invalid-straight")]
    public void RivalOffenseRetainsOnEveryRejection(string condition)
    {
        owner.CollectItem(RaceItem.FluxRibbon);
        Assert.That(items.RivalShouldDrop(owner, target, false, false), Is.True);
        switch (condition)
        {
            case "owner-airborne": owner.Body.position += Vector3.up * 2; break;
            case "owner-slow": owner.Body.linearVelocity = Vector3.right * 3.9f; break;
            case "target-airborne": target.Body.position += Vector3.up * 2; break;
            case "target-reverse": target.Body.linearVelocity = Vector3.left; break;
            case "target-stationary": target.Body.linearVelocity = Vector3.zero; break;
            case "too-close": target.Body.position = new(22.01f, 0.58f, -64); break;
            case "too-far": target.Body.position = new(-0.01f, 0.58f, -64); break;
            case "target-angle": target.Body.rotation = Quaternion.Euler(0, 106, 0); break;
            case "owner-angle": owner.Body.rotation = Quaternion.Euler(0, 96, 0); break;
            case "early-arrival": target.Body.position = new(22, 0.58f, -64); target.Body.linearVelocity = Vector3.right * 40; break;
            case "late-arrival": target.Body.position = new(0, 0.58f, -64); target.Body.linearVelocity = Vector3.right * 10; break;
            case "lateral-miss": target.Body.position += Vector3.forward * 3; break;
            case "live-owned": items.Activate(owner, true, 0); owner.CollectItem(RaceItem.FluxRibbon); break;
            case "invalid-straight": owner.Body.position = new(0, 0.58f, -64); break;
        }
        Physics.SyncTransforms();
        Assert.That(items.RivalShouldDrop(owner, target, false, false), Is.False, condition);
        Assert.That(owner.HeldItem, Is.EqualTo(RaceItem.FluxRibbon));
    }

    /// <summary>Rival defense consumes only for a real threat and retains a second Shield while shielded or immune.</summary>
    [TestCase("valid", true)] [TestCase("no-threat", false)]
    [TestCase("shielded", false)] [TestCase("immune", false)]
    public void RivalShieldPolicy(string condition, bool used)
    {
        Put(target, new(30, 0.58f, -64), Vector3.right, 15);
        Put(owner, new(10, 0.58f, -64), Vector3.right, 15);
        target.CollectItem(RaceItem.FluxRibbon); items.Activate(target, true, 0);
        owner.CollectItem(RaceItem.PulseShield);
        if (condition == "no-threat") owner.Body.linearVelocity = Vector3.zero;
        if (condition == "shielded") { owner.TryUseShield(); owner.CollectItem(RaceItem.PulseShield); }
        if (condition == "immune") owner.RecoverEffects();
        var lights = new Material(Shader.Find("Hidden/InternalErrorShader"));
        try
        {
            var rival = new TrialRival(CourseDefinition.Docks, owner, lights);
            int before = owner.ItemsUsed;
            rival.ActivateItem(target, items);
            Assert.That(owner.ItemsUsed - before, Is.EqualTo(used ? 1 : 0));
            Assert.That(owner.HeldItem, Is.EqualTo(used ? RaceItem.None : RaceItem.PulseShield));
        }
        finally { Object.DestroyImmediate(lights); }
    }

    /// <summary>Signal Tap fires straight ahead, hits only the first opponent, respects Shield/immunity, and expires at 45 m.</summary>
    [Test]
    public void SignalTapPulseTravelHitShieldAndExpiry()
    {
        Put(owner, new(0, 0.58f, -64), Vector3.right, 15);
        Put(target, new(12, 0.58f, -64), Vector3.right, 15);
        owner.CollectItem(RaceItem.SignalTap);
        Assert.That(items.Activate(owner, true, 0), Is.True);
        Assert.That(owner.HeldItem, Is.EqualTo(RaceItem.None));
        Assert.That(items.Pulses.Count, Is.EqualTo(1));
        Assert.That(items.IncomingPulse(target), Is.True);

        // Lateral lane change dodges the non-tracking pulse.
        target.Body.position = new(12, 0.58f, -61.5f); Physics.SyncTransforms();
        Assert.That(items.IncomingPulse(target), Is.False);
        target.Body.position = new(12, 0.58f, -64); Physics.SyncTransforms();

        items.Resolve(target);
        items.Advance(0.35f);
        target.Body.position = new(17.25f, 0.58f, -64); Physics.SyncTransforms();
        items.Resolve(target);
        Assert.That(items.Pulses, Is.Empty, "First hit must consume the pulse.");
        Assert.That(target.AccelTapped, Is.True);

        // Reduced acceleration applies during tap without blocking steering or braking.
        float before = target.ForwardSpeed;
        target.Step(1f, 0f, 0f, false, true, 0.2f);
        Assert.That(target.ForwardSpeed - before, Is.EqualTo(5.5f * 0.2f).Within(0.05f));
        target.AdvanceEffects(0.8f);
        Assert.That(target.AccelTapped, Is.False);
        Assert.That(target.DisruptionImmune, Is.True);

        // Shield absorbs an opposing Signal Tap and grants immunity.
        target.ClearItems();
        target.CollectItem(RaceItem.PulseShield);
        Assert.That(target.TryUseShield(), Is.True);
        target.ReceiveSignalTapHit();
        Assert.That(target.ShieldActive, Is.False);
        Assert.That(target.AccelTapped, Is.False);
        Assert.That(target.DisruptionImmune, Is.True);

        // Pulse expires after traveling 45 m with no target hit.
        owner.CollectItem(RaceItem.SignalTap);
        items.Activate(owner, true, 0);
        for (int i = 0; i < 60; i++) items.Advance(0.02f);
        Assert.That(items.Pulses, Is.Empty);
    }

    /// <summary>Slipstream Link boosts only while inside the leader's draft cone and stops immediately on exit.</summary>
    [Test]
    public void SlipstreamDraftConeAndHandlingAssist()
    {
        Put(target, new(30, 0.58f, -64), Vector3.right, 25);
        Put(owner, new(15, 0.58f, -64), Vector3.right, 25);
        Assert.That(RaceItems.IsInSlipstream(owner, target), Is.True);
        Assert.That(RaceItems.IsInSlipstream(target, owner), Is.False);

        owner.CollectItem(RaceItem.SlipstreamLink);
        Assert.That(items.Activate(owner, true, 0), Is.True);
        Assert.That(owner.SlipstreamActive, Is.True);

        // Inside draft: accelerates past normal 25 m/s top speed toward 30 m/s.
        owner.Step(1f, 0f, 0f, false, true, 0.2f, true, true);
        Assert.That(owner.ForwardSpeed, Is.GreaterThan(25.5f));

        // Leaving draft cone immediately drops the assist back toward normal 25 m/s.
        owner.Body.position = new(15, 0.58f, -61f); Physics.SyncTransforms();
        Assert.That(RaceItems.IsInSlipstream(owner, target), Is.False);
        float outside = owner.ForwardSpeed;
        owner.Step(1f, 0f, 0f, false, true, 0.2f, true, false);
        Assert.That(owner.ForwardSpeed, Is.LessThan(outside));
    }

    /// <summary>Rival fires Signal Tap only when player is straight ahead and activates Slipstream only in draft.</summary>
    [Test]
    public void RivalSignalTapAndSlipstreamPolicies()
    {
        Put(owner, new(10, 0.58f, -64), Vector3.right, 20);
        Put(target, new(25, 0.58f, -64), Vector3.right, 20);

        owner.CollectItem(RaceItem.SignalTap);
        Assert.That(items.RivalShouldFireTap(owner, target), Is.True);
        target.RecoverEffects();
        Assert.That(items.RivalShouldFireTap(owner, target), Is.False);
        target.ClearItems();
        target.Body.position = new(25, 0.58f, -61.5f); Physics.SyncTransforms();
        Assert.That(items.RivalShouldFireTap(owner, target), Is.False);

        target.Body.position = new(25, 0.58f, -64); Physics.SyncTransforms();
        owner.ClearItems();
        owner.CollectItem(RaceItem.SlipstreamLink);
        Assert.That(items.RivalShouldUseSlipstream(owner, target, false, false), Is.True);
        Assert.That(items.RivalShouldUseSlipstream(owner, target, true, false), Is.False);
        Assert.That(items.RivalShouldUseSlipstream(owner, target, false, true), Is.False);
        owner.Body.position = new(25, 0.58f, -64); Physics.SyncTransforms();
        Assert.That(items.RivalShouldUseSlipstream(owner, target, false, false), Is.False);
    }

    /// <summary>ItemPool draws varied items from a seeded bag and weights trailing karts toward catch-up items.</summary>
    [Test]
    public void ItemPoolDistributionAndTrailingWeighting()
    {
        var pool = new ItemPool(42);
        var seen = new System.Collections.Generic.HashSet<RaceItem>();
        for (int i = 0; i < 6; i++)
            seen.Add(pool.Next(false));
        Assert.That(seen.Count, Is.EqualTo(6));

        pool.Reset();
        int catchUp = 0;
        for (int i = 0; i < 24; i++)
        {
            RaceItem item = pool.Next(true);
            if (item == RaceItem.RelayBoost || item == RaceItem.SlipstreamLink)
                catchUp++;
        }
        Assert.That(catchUp, Is.EqualTo(12));
    }

    /// <summary>Route Beacon activates for 6 seconds and Flux Ribbon placement works on Tokyo and Viaduct straights.</summary>
    [Test]
    public void RouteBeaconAndMultiCircuitPlacement()
    {
        owner.CollectItem(RaceItem.RouteBeacon);
        Assert.That(items.Activate(owner, true, 0), Is.True);
        Assert.That(owner.BeaconActive, Is.True);
        owner.AdvanceEffects(6f);
        Assert.That(owner.BeaconActive, Is.False);

        Put(owner, new(-25, 0.58f, -38), Vector3.right, 15);
        Assert.That(RaceItems.TryPlacement(owner, CourseDefinition.Tokyo, out _, out _), Is.True);
        Put(owner, new(-25, 0.58f, -40), Vector3.right, 15);
        Assert.That(RaceItems.TryPlacement(owner, CourseDefinition.Viaduct, out _, out _), Is.True);
    }

    /// <summary>InMemoryRacingSaveStore mirrors the Express/PostgreSQL save contract when signed in.</summary>
    [Test]
    public void InMemoryRacingSaveStoreSyncsRecordsAndSettings()
    {
        var store = new InMemoryRacingSaveStore();
        var record = new TrialRecord();
        Assert.That(store.SyncRecordAsync(CourseDefinition.Tokyo, record).Result, Is.False);

        store.SignInForTests("CyberDriver");
        Assert.That(store.SyncRecordAsync(CourseDefinition.Tokyo, record).Result, Is.True);
        Assert.That(store.SyncedRecords.ContainsKey("little-tokyo-skyport-r1"), Is.True);
        Assert.That(store.SyncSettingsAsync(true, true).Result, Is.True);
        Assert.That(store.LastGhostSetting && store.LastBeaconSetting, Is.True);

        store.SeedLeaderboard("CyberDriver", 86860);
        Assert.That(store.FetchLeaderboardAsync().Result, Does.Contain("CyberDriver"));

        Assert.That(store.StartBrowserSignInAsync().Result, Does.Contain("game-auth"));
        Assert.That(store.SignInPending, Is.True);
        Assert.That(store.PollBrowserSignInAsync().Result, Is.True);
        Assert.That(store.IsSignedIn, Is.True);
        store.SignOut();
        Assert.That(store.IsSignedIn, Is.False);
    }

    /// <summary>Skyline Cup circuits have valid banking interpolation, driving lines, respawns, and pickup gates.</summary>
    [Test]
    public void SkylineCupCoursesHaveValidStationsBankingAndPickups()
    {
        Assert.That(CourseDefinition.Skyline.Length, Is.EqualTo(4));
        Assert.That(CourseDefinition.AllCircuits.Length, Is.EqualTo(8));
        Station lerped = Station.Lerp(new Station(0, 0, 6f, 0f), new Station(10, 0, 8f, 12f), 0.5f);
        Assert.That(lerped.Bank, Is.EqualTo(6f).Within(0.001f));

        foreach (CourseDefinition circuit in CourseDefinition.Skyline)
        {
            Assert.That(circuit.CircuitCup, Is.EqualTo(CourseDefinition.Cup.Skyline));
            Assert.That(TrialCourse.DrivingLine(circuit, false).Length, Is.GreaterThan(10));
            Assert.That(TrialCourse.DrivingLine(circuit, true).Length, Is.GreaterThan(10));
            Assert.That(circuit.Respawn(-1).Position, Is.EqualTo(circuit.Start));
        }
        Assert.That(RelayBoostPickups.GatesFor(CourseDefinition.Foundry), Is.Empty);
        Assert.That(RelayBoostPickups.GatesFor(CourseDefinition.CyberGrave).Length, Is.EqualTo(2));
        Assert.That(RelayBoostPickups.GatesFor(CourseDefinition.SpireHull).Length, Is.EqualTo(2));
        Assert.That(RelayBoostPickups.GatesFor(CourseDefinition.ApexDescent).Length, Is.EqualTo(2));
    }
}
