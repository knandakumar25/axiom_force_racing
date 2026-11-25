using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Follows a sampled course line using the same driving controls as the player.</summary>
    internal sealed class TrialRival
    {
        public const float GridOffset = 1.6f;
        private const float LookAhead = 3.8f;
        private const float StuckSeconds = 2f;
        private static readonly Color TailColor = new(0.55f, 0.08f, 0.035f);
        private static readonly Color BrakeColor = new(1f, 0.19f, 0.08f);
        private Vector3[] line;
        private readonly CourseDefinition circuit;
        private readonly Vector3[][] lines;
        private readonly float[][] routeDistances;
        private float previousZ;
        private float[] distances;
        private readonly Material lights;
        private int segmentCount;
        private int segment;
        private float completedDistance;
        private float progress;
        private float windowStart;
        private float windowTime;
        private float brakeLightTime;
        private bool braking;
        private float throttleIntent, brakeIntent, steerIntent;
        private bool yieldingIntent;

        public TrialKart Kart { get; }
        public TrialRules Rules { get; }
        public int Recoveries { get; private set; }
        public bool NeedsRecovery { get; private set; }
        internal bool? CrossingChoice { get; private set; }
        internal bool WaitingForCrossing { get; private set; }

        public TrialRival(CourseDefinition circuit, TrialKart kart, Material lights)
        {
            Kart = kart;
            Rules = new TrialRules(circuit.Gates, circuit.Start);
            this.lights = lights;
            this.circuit = circuit;
            lines = new[] { TrialCourse.DrivingLine(circuit, false), TrialCourse.DrivingLine(circuit, true) };
            routeDistances = new float[2][];
            for (int route = 0; route < lines.Length; route++)
            {
                Vector3[] points = lines[route];
                var lengths = new float[points.Length + 1];
                for (int i = 0; i < points.Length; i++)
                    lengths[i + 1] = lengths[i] + Vector3.Distance(points[i], points[(i + 1) % points.Length]);
                routeDistances[route] = lengths;
            }
            SelectLine(false, kart.Body.position);
        }

        public void Restart(Vector3 position, Vector3 forward)
        {
            CrossingChoice = null;
            WaitingForCrossing = false;
            SelectLine(false, position);
            Kart.Body.gameObject.SetActive(true);
            Rules.Restart(position);
            Place(position, forward);
            Kart.Freeze();
            Recoveries = 0;
        }

        public void Retire()
        {
            Kart.Freeze();
            Kart.Body.gameObject.SetActive(false);
        }

        public void Recover(Vector3 position, Vector3 forward)
        {
            CrossingChoice = null;
            WaitingForCrossing = false;
            SelectLine(false, position);
            Place(position, forward);
            Recoveries++;
        }

        private void Place(Vector3 position, Vector3 forward)
        {
            Kart.Place(position, forward);
            Rules.Rebase(position);
            previousZ = position.z;
            RebaseLine(position);
            NeedsRecovery = false;
            brakeLightTime = 0f;
            ShowBrake(false, force: true);
        }

        /// <summary>Follows the current route, yielding to traffic and using items only on eligible straights.</summary>
        public void Step(TrialKart player, float dt, RailCrossing crossing = null, bool useItems = false)
        {
            Decide(player, dt, crossing, useItems);
            if (useItems && CanUseRelayBoost(player, brakeIntent > 0, yieldingIntent))
                Kart.TryUseRelayBoost(Kart.Grounded, 0f);
            Apply(dt);
        }

        /// <summary>Computes driving intent without changing velocity, preserving a shared item/contact snapshot.</summary>
        internal void Decide(TrialKart player, float dt, RailCrossing crossing = null, bool useItems = false)
        {
            Vector3 position = Kart.Body.position;
            position.y = 0f;
            if (circuit == CourseDefinition.Rustrail)
            {
                if (CrossingChoice.HasValue && position.z < -46f && position.x < -78f)
                    CrossingChoice = null;
                if (!CrossingChoice.HasValue && previousZ > 32f && position.z <= 32f &&
                    position.x is > -91f and < -77f)
                {
                    Bounds bounds = Kart.PhysicsBounds;
                    float clearZ = RailCrossing.BarrierCenter.z - RailCrossing.BarrierSize.z * 0.5f
                        - Mathf.Max(bounds.extents.x, bounds.extents.z);
                    CrossingChoice = CanTakeCrossing(Rules.Elapsed, CrossingDistance(position, clearZ), player.PhysicsBounds);
                    SelectLine(CrossingChoice.Value, position);
                }
            }
            previousZ = position.z;
            for (int i = 0; i < 8; i++)
            {
                int next = (segment + 1) % segmentCount;
                Vector3 tangent = EndOf(next) - line[segment];
                if (Vector3.Dot(position - EndOf(segment), tangent) < 0f)
                    break;
                segment = next;
                if (segment == 0)
                    completedDistance += distances[^1];
            }

            Vector3 target = Project(segment, position, out float distance);
            progress = completedDistance + distance;
            float lookAhead = LookAhead;
            int ahead = segment;
            while (Vector3.Distance(target, EndOf(ahead)) < lookAhead)
            {
                lookAhead -= Vector3.Distance(target, EndOf(ahead));
                target = EndOf(ahead);
                ahead = (ahead + 1) % segmentCount;
            }
            target = Vector3.MoveTowards(target, EndOf(ahead), lookAhead);
            // Derive the lane from the start pose rather than Docks world coordinates.
            Vector3 relative = target - circuit.Start;
            float along = Vector3.Dot(relative, circuit.StartForward);
            Vector3 side = Vector3.Cross(Vector3.up, circuit.StartForward);
            if (Mathf.Abs(Vector3.Dot(relative, side)) < 2f && along is > -19f and < 21f)
                target -= side * GridOffset * Mathf.Clamp01(Mathf.Min((along + 19f) / 10f, (21f - along) / 15f));

            Vector3 forward = Kart.Body.rotation * Vector3.forward;
            float angle = Vector3.SignedAngle(forward, target - position, Vector3.up);
            float speed = Kart.ForwardSpeed;
            bool waterfront = circuit == CourseDefinition.Docks && position.z < -60f && position.x is > 0f and < 49f;
            // The tangent at the look-ahead point reveals the bend before the kart turns into it.
            float bend = Vector3.Angle(forward, EndOf(ahead) - line[ahead]);
            float turn = Mathf.Max(Mathf.Abs(angle), bend * 0.75f);
            float desired = Mathf.Lerp(waterfront ? 25f : 14f, 6f, turn / 37f);
            // Allow the shared boost to accelerate on clear straights instead of braking
            // against its higher target. Retain a 25 m braking approach to the next bend.
            if (useItems && Kart.BoostActive && HasStraightAhead(25f))
                desired = 34f;
            float gap = player.ClearanceAhead(Kart.Body.position, forward);
            float leadSpeed = Mathf.Max(0f, Vector3.Dot(player.Body.linearVelocity, forward));
            float clearanceSpeed = Mathf.Max(0f, leadSpeed + (gap - 2.4f) * 1.5f);
            bool yielding = desired > clearanceSpeed;
            desired = Mathf.Min(desired, clearanceSpeed);
            WaitingForCrossing = CrossingChoice == true && crossing != null && crossing.Closed &&
                position.x < -85f && Kart.PhysicsBounds.min.z > RailCrossing.BarrierCenter.z;
            if (WaitingForCrossing)
            {
                float gapToBarrier = Kart.PhysicsBounds.min.z -
                    (RailCrossing.BarrierCenter.z + RailCrossing.BarrierSize.z * 0.5f);
                desired = Mathf.Min(desired, Mathf.Sqrt(2f * 12f * Mathf.Max(0f, gapToBarrier - 1f)));
            }
            float throttle = Mathf.Clamp01(desired / 25f + (desired - speed) * 0.16f);
            float brakeMargin = yielding ? 0.35f : 0.55f;
            bool brake = speed > Mathf.Max(0.4f, desired + brakeMargin);
            throttleIntent = throttle;
            brakeIntent = brake ? 0.75f : 0f;
            steerIntent = Mathf.Clamp(angle / 33f, -1f, 1f);
            yieldingIntent = yielding;
            // Keep a short brake tap visible in the chase view.
            brakeLightTime = brake ? 0.18f : Mathf.Max(0f, brakeLightTime - dt);
            ShowBrake(brakeLightTime > 0f);

            if (WaitingForCrossing || (yielding && desired < 1f) || progress - windowStart >= 1f)
                RebaseProgress();
            else if ((windowTime += dt) >= StuckSeconds - 0.00001f)
                NeedsRecovery = true;
        }

        /// <summary>Uses evaluated traffic intent for Relay/Ribbon/Slipstream and current prediction for Shield/Tap.</summary>
        internal void ActivateItem(TrialKart player, RaceItems items)
        {
            bool use = Kart.HeldItem switch
            {
                RaceItem.RelayBoost => CanUseRelayBoost(player, brakeIntent > 0, yieldingIntent),
                RaceItem.PulseShield => !Kart.ShieldActive && !Kart.DisruptionImmune &&
                    (items.Incoming(Kart) || items.IncomingPulse(Kart)),
                RaceItem.FluxRibbon => items.RivalShouldDrop(Kart, player, brakeIntent > 0, yieldingIntent),
                RaceItem.SignalTap => items.RivalShouldFireTap(Kart, player),
                RaceItem.SlipstreamLink => items.RivalShouldUseSlipstream(Kart, player, brakeIntent > 0, yieldingIntent),
                RaceItem.RouteBeacon => true,
                _ => false
            };
            if (use) items.Activate(Kart, Kart.Grounded, brakeIntent);
        }

        /// <summary>Applies the previously evaluated controls; runtime advances effect timers after both karts.</summary>
        internal void Apply(float dt, bool advanceEffects = true, bool inSlipstream = false) =>
            Kart.Step(throttleIntent, brakeIntent, steerIntent, false, Kart.Grounded, dt, advanceEffects, inSlipstream);

        /// <summary>Requires room for the whole boost and checks every sampled tangent for 45 metres.</summary>
        internal bool CanUseRelayBoost(TrialKart player, bool brake, bool yielding)
        {
            if (!Kart.RelayBoostHeld || !Kart.Grounded || Kart.ForwardSpeed <= 8f || brake || yielding ||
                Vector3.Distance(Kart.Body.position, player.Body.position) < 40f)
                return false;
            return HasStraightAhead(45f);
        }

        /// <summary>Checks route alignment and all tangent samples over the requested clear distance.</summary>
        internal bool HasStraightAhead(float metres)
        {
            Vector3 forward = Kart.Body.rotation * Vector3.forward;
            Vector3 point = Project(segment, Kart.Body.position, out _);
            float remaining = metres;
            int index = segment;
            for (int visited = 0; visited < segmentCount; visited++)
            {
                Vector3 end = EndOf(index);
                if (Vector3.Angle(forward, end - line[index]) > 5f)
                    return false;
                remaining -= Vector3.Distance(point, end);
                if (remaining <= 0f)
                    return true;
                point = end;
                index = (index + 1) % segmentCount;
            }
            return false;
        }

        // Use the schedule, never a closure deferred by another kart, to commit at the fork.
        internal static bool CanTakeCrossing(double elapsed, float remainingDistance, Bounds playerBounds)
        {
            var approach = new Bounds(new Vector3(-87, 1f, 10f), new Vector3(13.2f, 4f, 44f));
            if (approach.Intersects(playerBounds) || RailCrossing.ScheduledPhase(elapsed) == CrossingPhase.Closed)
                return false;
            double cycle = System.Math.Round(elapsed * 1000d, System.MidpointRounding.AwayFromZero) % 24000 / 1000d;
            return cycle + remainingDistance / 6d + 1d < 15d;
        }

        private float CrossingDistance(Vector3 position, float clearZ)
        {
            Vector3[] points = lines[1];
            int closest = 0;
            float nearest = float.PositiveInfinity;
            Vector3 start = position;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 edge = points[(i + 1) % points.Length] - points[i];
                Vector3 projected = points[i] + edge * Mathf.Clamp01(Vector3.Dot(position - points[i], edge) / edge.sqrMagnitude);
                float distance = (position - projected).sqrMagnitude;
                if (distance < nearest) { nearest = distance; closest = i; start = projected; }
            }
            float remaining = 0f;
            for (int step = 0; step < points.Length; step++)
            {
                Vector3 end = points[(closest + step + 1) % points.Length];
                if (start.z >= clearZ && end.z <= clearZ)
                    return remaining + Vector3.Distance(start, Vector3.Lerp(start, end, (start.z - clearZ) / (start.z - end.z)));
                remaining += Vector3.Distance(start, end);
                start = end;
            }
            return float.PositiveInfinity;
        }

        private void SelectLine(bool shortcut, Vector3 position)
        {
            int route = shortcut ? 1 : 0;
            line = lines[route];
            distances = routeDistances[route];
            segmentCount = line.Length;
            RebaseLine(position);
        }

        private void RebaseLine(Vector3 position)
        {
            position.y = 0f;
            segment = 0;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < segmentCount; i++)
            {
                float distance = (position - Project(i, position, out _)).sqrMagnitude;
                if (distance < nearest) { segment = i; nearest = distance; }
            }
            completedDistance = 0f;
            Project(segment, position, out progress);
            RebaseProgress();
        }

        private Vector3 EndOf(int index) => line[(index + 1) % segmentCount];

        private Vector3 Project(int index, Vector3 position, out float distance)
        {
            Vector3 edge = EndOf(index) - line[index];
            float fraction = Mathf.Clamp01(Vector3.Dot(position - line[index], edge) / edge.sqrMagnitude);
            distance = Mathf.Lerp(distances[index], distances[index + 1], fraction);
            return line[index] + edge * fraction;
        }

        private void RebaseProgress()
        {
            windowStart = progress;
            windowTime = 0f;
        }

        private void ShowBrake(bool active, bool force = false)
        {
            if (!force && braking == active)
                return;
            braking = active;
            lights.color = braking ? BrakeColor : TailColor;
            lights.SetColor("_EmissionColor", lights.color * (braking ? 2.5f : 0.4f));
        }
    }
}
