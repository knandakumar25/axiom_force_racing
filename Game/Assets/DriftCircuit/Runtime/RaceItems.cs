using System.Collections.Generic;
using UnityEngine;

namespace DriftCircuit
{
    /// <summary>The single internal Docks inventory slot; never serialized into trial records.</summary>
    internal enum RaceItem { None, RelayBoost, FluxRibbon, PulseShield }

    /// <summary>Runs non-solid ribbon placement, swept contacts and prediction using simulation time.</summary>
    internal sealed class RaceItems
    {
        // Float fixed steps accumulate in double timers; one microsecond covers six seconds of 20 ms steps.
        private const double TimerTolerance = 0.000001d;
        /// <summary>A fixed road rectangle, its owner's placement generation and one-hit eligibility.</summary>
        internal sealed class Ribbon
        {
            internal readonly TrialKart Owner;
            internal readonly Vector3 Center, Forward;
            internal readonly int PlacementVersion;
            internal readonly double DeployedAt;
            internal readonly HashSet<TrialKart> Spent = new();
            internal double Age;
            internal GameObject View;
            internal bool Armed => Age >= 0.4d - TimerTolerance && Age < 6d - TimerTolerance;

            /// <summary>Snapshots the owner generation so recovery invalidates its patch.</summary>
            internal Ribbon(TrialKart owner, Vector3 center, Vector3 forward, double deployedAt = 0)
            { Owner = owner; Center = center; Forward = forward; PlacementVersion = owner.PlacementVersion; DeployedAt = deployedAt; }
        }

        private readonly List<Ribbon> ribbons = new();
        private readonly Dictionary<TrialKart, (Vector3 Position, int Version, double Time)> previous = new();
        private double clock;
        private readonly TrialCourse course;
        private readonly Material dormant, armed, stripe;
        internal IReadOnlyList<Ribbon> Ribbons => ribbons;

        /// <summary>Optional course supplies shared primitive materials; pure rule tests need no visuals.</summary>
        internal RaceItems(TrialCourse course = null)
        {
            this.course = course;
            if (course == null) return;
            dormant = course.Paint(0x704383, true);
            armed = course.Paint(0xf04be8, true);
            stripe = course.Paint(0xffdf83, true);
        }

        /// <summary>Clears all world effects and motion samples for Retry and Track Select.</summary>
        internal void Clear()
        {
            foreach (Ribbon patch in ribbons) RemoveView(patch);
            ribbons.Clear();
            previous.Clear();
            clock = 0;
        }

        /// <summary>Removes owned hazards and prevents a recovery teleport from being swept.</summary>
        internal void Recover(TrialKart kart)
        {
            RemoveOwned(kart);
            previous.Remove(kart);
            kart.RecoverEffects();
        }

        /// <summary>Retirement immediately removes the racer's deployed patches.</summary>
        internal void RemoveOwned(TrialKart kart)
        {
            for (int i = ribbons.Count - 1; i >= 0; i--)
                if (ribbons[i].Owner == kart) { RemoveView(ribbons[i]); ribbons.RemoveAt(i); }
        }

        /// <summary>Dispatches the held item after both racers have evaluated their controls.</summary>
        internal bool Activate(TrialKart kart, bool grounded, float brake)
        {
            if (kart.HeldItem == RaceItem.RelayBoost) return kart.TryUseRelayBoost(grounded, brake);
            if (kart.HeldItem == RaceItem.PulseShield) return kart.TryUseShield();
            if (kart.HeldItem != RaceItem.FluxRibbon || !grounded || brake > 0.02f ||
                kart.ForwardSpeed <= 4.000001f || !TryPlacement(kart, out Vector3 center, out Vector3 forward)) return false;
            foreach (Ribbon patch in ribbons)
                if (Live(patch) && patch.Owner == kart) return false;
            var ribbon = new Ribbon(kart, center, forward, clock);
            if (course != null)
            {
                ribbon.View = course.Cube("Flux Ribbon arming", center + Vector3.up * 0.05f,
                    new(3, 0.035f, 4), Quaternion.LookRotation(forward), dormant);
                for (int i = 0; i < 5; i++)
                    course.Cube("Ribbon stripe", new(0, 0.6f, -0.4f + i * 0.2f), new(1, 0.3f, 0.07f),
                        Quaternion.identity, stripe, parent: ribbon.View.transform);
            }
            ribbons.Add(ribbon);
            kart.ConsumeItem();
            return true;
        }

        /// <summary>Requires a shared straight and measures the entire patch against every protected pose.</summary>
        internal static bool TryPlacement(TrialKart kart, out Vector3 center, out Vector3 forward)
        {
            Vector3 position = kart.Body.position;
            bool east = Mathf.Abs(position.z + 64f) <= 4.5f &&
                (position.x is >= -50f and <= -20f or >= 20f and <= 50f);
            bool west = Mathf.Abs(position.z - 62f) <= 4.5f && position.x is >= -40f and <= 30f;
            forward = east ? Vector3.right : Vector3.left;
            center = position - forward * 3.8f;
            center.y = 0f;
            if ((!east && !west) || Vector3.Angle(kart.Body.rotation * Vector3.forward, forward) > 5f) return false;
            CourseDefinition docks = CourseDefinition.Docks;
            foreach (Checkpoint checkpoint in docks.Gates)
                if (DistanceToPatch(center, checkpoint.Center) < 12f) return false;
            Vector3 gridSide = Vector3.Cross(Vector3.up, docks.StartForward) * TrialRival.GridOffset;
            if (DistanceToPatch(center, docks.Start + gridSide) < 12f ||
                DistanceToPatch(center, docks.Start - gridSide) < 12f) return false;
            for (int gate = -1; gate < docks.Gates.Length; gate++)
            {
                var pose = docks.Respawn(gate);
                Vector3 side = Vector3.Cross(Vector3.up, pose.Forward) * 2.4f;
                if (DistanceToPatch(center, pose.Position) < 12f ||
                    DistanceToPatch(center, pose.Position + side) < 12f ||
                    DistanceToPatch(center, pose.Position - side) < 12f) return false;
            }
            return true;
        }

        /// <summary>Both straights lie along x; closest rectangle point gives the true whole-patch clearance.</summary>
        private static float DistanceToPatch(Vector3 center, Vector3 point) => new Vector2(
            Mathf.Max(0, Mathf.Abs(point.x - center.x) - 2f),
            Mathf.Max(0, Mathf.Abs(point.z - center.z) - 1.5f)).magnitude;

        /// <summary>Predicts contact in a bounded future window, including arming and expiry at contact time.</summary>
        internal static bool Predict(Ribbon patch, TrialKart target, float earliest, float latest)
        {
            if (!Live(patch) || patch.Owner == target || patch.Spent.Contains(target) || !target.Grounded) return false;
            float enter = Mathf.Max(earliest, (float)(0.4d - TimerTolerance - patch.Age));
            float exit = Mathf.Min(latest, (float)(6d - TimerTolerance - patch.Age));
            return enter <= exit && Intersects(patch, target.Body.position, target.Body.linearVelocity, ref enter, ref exit);
        }

        /// <summary>The HUD and rival shield decision deliberately share the same one-second prediction.</summary>
        internal bool Incoming(TrialKart target)
        {
            foreach (Ribbon patch in ribbons) if (Predict(patch, target, 0, 1)) return true;
            return false;
        }

        /// <summary>Uses the player's current velocity to justify an offensive drop, with no lane avoidance.</summary>
        internal bool RivalShouldDrop(TrialKart rival, TrialKart player, bool brake, bool yielding)
        {
            if (rival.HeldItem != RaceItem.FluxRibbon || brake || yielding || !rival.Grounded || rival.ForwardSpeed <= 4f ||
                !player.Grounded || player.ForwardSpeed <= 0 || player.ShieldActive || player.DisruptionImmune ||
                !TryPlacement(rival, out Vector3 center, out Vector3 forward)) return false;
            foreach (Ribbon patch in ribbons) if (Live(patch) && patch.Owner == rival) return false;
            float behind = Vector3.Dot(rival.Body.position - player.Body.position, forward);
            return behind >= 8f && behind <= 30f &&
                Vector3.Angle(player.Body.rotation * Vector3.forward, forward) <= 15f &&
                Predict(new Ribbon(rival, center, forward), player, 0.4f, 1.5f);
        }

        /// <summary>Resolves movement since the prior physics sample; placements discard teleport sweeps.</summary>
        internal void Resolve(TrialKart kart)
        {
            Vector3 current = kart.Body.position;
            if (previous.TryGetValue(kart, out var sample) && sample.Version == kart.PlacementVersion && kart.Grounded)
            {
                foreach (Ribbon patch in ribbons)
                {
                    if (!OwnerValid(patch) || patch.Owner == kart || patch.Spent.Contains(kart)) continue;
                    float enter = 0, exit = 1;
                    double duration = clock - sample.Time;
                    if (duration > 0.0000001d)
                    {
                        // Clip the actual passage instant: an armed endpoint cannot retroactively hit an earlier pass.
                        enter = Mathf.Max(0, (float)((patch.DeployedAt + 0.4d - TimerTolerance - sample.Time) / duration));
                        exit = Mathf.Min(1, (float)((patch.DeployedAt + 6d - TimerTolerance - sample.Time) / duration));
                        if (enter > exit) continue;
                    }
                    else if (!patch.Armed) continue;
                    if (!Intersects(patch, sample.Position, current - sample.Position, ref enter, ref exit)) continue;
                    patch.Spent.Add(kart); // Protected contact spends eligibility, so there is no delayed hit.
                    kart.ReceiveRibbonHit();
                }
            }
            previous[kart] = (current, kart.PlacementVersion, clock);
        }

        /// <summary>Clips a point path to the rectangle expanded by the kart centre contact margins and height.</summary>
        private static bool Intersects(Ribbon patch, Vector3 origin, Vector3 velocity, ref float enter, ref float exit)
        {
            Vector3 offset = origin - patch.Center;
            Vector3 right = Vector3.Cross(Vector3.up, patch.Forward);
            return Slab(Vector3.Dot(offset, right), Vector3.Dot(velocity, right), -2.25f, 2.25f, ref enter, ref exit) &&
                Slab(Vector3.Dot(offset, patch.Forward), Vector3.Dot(velocity, patch.Forward), -3.25f, 3.25f, ref enter, ref exit) &&
                Slab(origin.y, velocity.y, 0.15f, 1.3f, ref enter, ref exit);
        }

        /// <summary>Clips one axis, handling stationary paths without division by zero.</summary>
        private static bool Slab(float position, float velocity, float min, float max, ref float enter, ref float exit)
        {
            if (Mathf.Abs(velocity) < 0.000001f) return position >= min && position <= max;
            float a = (min - position) / velocity, b = (max - position) / velocity;
            enter = Mathf.Max(enter, Mathf.Min(a, b)); exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        /// <summary>Rejects expired, recovered or retired owners, even before the cleanup step runs.</summary>
        private static bool Live(Ribbon patch) => patch.Age < 6d - TimerTolerance && OwnerValid(patch);

        /// <summary>Recovery and retirement invalidate contacts regardless of the sampled time interval.</summary>
        private static bool OwnerValid(Ribbon patch) => patch.Owner.PlacementVersion == patch.PlacementVersion &&
            patch.Owner.Body.gameObject.activeInHierarchy;

        /// <summary>Advances patch age after contacts and changes the visible arming state without wall-clock timers.</summary>
        internal void Advance(float dt)
        {
            clock += dt;
            for (int i = ribbons.Count - 1; i >= 0; i--)
            {
                Ribbon patch = ribbons[i];
                // Retain an expired rectangle for the next sample's final pre-expiry sweep, but hide it at six seconds.
                if (patch.Age >= 6d - TimerTolerance || !OwnerValid(patch))
                { RemoveView(patch); ribbons.RemoveAt(i); continue; }
                patch.Age += dt;
                if (!Live(patch) && patch.View) patch.View.SetActive(false);
                if (patch.View && patch.Armed)
                {
                    patch.View.name = "Flux Ribbon ARMED";
                    patch.View.GetComponent<Renderer>().sharedMaterial = armed;
                }
            }
        }

        /// <summary>Hides immediately before Unity's deferred destruction releases primitive renderers.</summary>
        private static void RemoveView(Ribbon patch)
        { if (patch.View) { patch.View.SetActive(false); Object.Destroy(patch.View); } }
    }
}
