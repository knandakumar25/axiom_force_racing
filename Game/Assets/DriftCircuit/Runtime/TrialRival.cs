using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Follows the loading-yard line using the same driving controls as the player.</summary>
    internal sealed class TrialRival
    {
        public const float GridOffset = 1.6f;
        private const float LookAhead = 3.8f;
        private const float StuckSeconds = 2f;
        private static readonly Color TailColor = new(0.55f, 0.08f, 0.035f);
        private static readonly Color BrakeColor = new(1f, 0.19f, 0.08f);
        private readonly Vector3[] line;
        private readonly float[] distances;
        private readonly Material lights;
        private readonly int segmentCount;
        private int segment;
        private float completedDistance;
        private float progress;
        private float windowStart;
        private float windowTime;
        private float brakeLightTime;
        private bool braking;

        public TrialKart Kart { get; }
        public TrialRules Rules { get; }
        public int Recoveries { get; private set; }
        public bool NeedsRecovery { get; private set; }

        public TrialRival(CourseDefinition circuit, TrialKart kart, Material lights)
        {
            Kart = kart;
            Rules = new TrialRules(circuit.Gates, circuit.Start);
            this.lights = lights;
            line = TrialCourse.DrivingLine(circuit, false);
            segmentCount = line.Length;
            distances = new float[segmentCount + 1];
            for (int i = 0; i < segmentCount; i++)
                distances[i + 1] = distances[i] + Vector3.Distance(line[i], EndOf(i));
        }

        public void Restart(Vector3 position, Vector3 forward)
        {
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
            Place(position, forward);
            Recoveries++;
        }

        private void Place(Vector3 position, Vector3 forward)
        {
            Kart.Place(position, forward);
            Rules.Rebase(position);
            position.y = 0f;
            segment = 0;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < segmentCount; i++)
            {
                float distance = (position - Project(i, position, out _)).sqrMagnitude;
                if (distance < nearest)
                {
                    segment = i;
                    nearest = distance;
                }
            }
            completedDistance = 0f;
            Project(segment, position, out progress);
            NeedsRecovery = false;
            RebaseProgress();
            brakeLightTime = 0f;
            ShowBrake(false, force: true);
        }

        public void Step(TrialKart player, float dt)
        {
            Vector3 position = Kart.Body.position;
            position.y = 0f;
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
            // Follow the grid lane on every lap so a parked starter leaves room to go by.
            if (target.z < -62f && target.x is > -15f and < 25f)
                target.z += GridOffset * Mathf.Clamp01(Mathf.Min((target.x + 15f) / 10f, (25f - target.x) / 15f));

            Vector3 forward = Kart.Body.rotation * Vector3.forward;
            float angle = Vector3.SignedAngle(forward, target - position, Vector3.up);
            float speed = Kart.ForwardSpeed;
            bool waterfront = position.z < -60f && position.x is > 0f and < 49f;
            // The tangent at the look-ahead point reveals the bend before the kart turns into it.
            float bend = Vector3.Angle(forward, EndOf(ahead) - line[ahead]);
            float turn = Mathf.Max(Mathf.Abs(angle), bend * 0.75f);
            float desired = Mathf.Lerp(waterfront ? 25f : 14f, 6f, turn / 37f);
            float gap = player.ClearanceAhead(Kart.Body.position, forward);
            float leadSpeed = Mathf.Max(0f, Vector3.Dot(player.Body.linearVelocity, forward));
            float clearanceSpeed = Mathf.Max(0f, leadSpeed + (gap - 2.4f) * 1.5f);
            bool yielding = desired > clearanceSpeed;
            desired = Mathf.Min(desired, clearanceSpeed);
            float throttle = Mathf.Clamp01(desired / 25f + (desired - speed) * 0.16f);
            float brakeMargin = yielding ? 0.35f : 0.55f;
            bool brake = speed > Mathf.Max(0.4f, desired + brakeMargin);
            Kart.Step(throttle, brake ? 0.75f : 0f, Mathf.Clamp(angle / 33f, -1f, 1f),
                false, Kart.Grounded, dt);
            // Keep a short brake tap visible in the chase view.
            brakeLightTime = brake ? 0.18f : Mathf.Max(0f, brakeLightTime - dt);
            ShowBrake(brakeLightTime > 0f);

            if ((yielding && desired < 1f) || progress - windowStart >= 1f)
                RebaseProgress();
            else if ((windowTime += dt) >= StuckSeconds - 0.00001f)
                NeedsRecovery = true;
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
