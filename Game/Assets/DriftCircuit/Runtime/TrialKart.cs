using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Applies arcade handling, tire grip, drift charge, and boost to the kart body.</summary>
    internal sealed class TrialKart
    {
        private const float BoostDuration = 1f;
        private const float NormalSpeed = 25f;
        private readonly BoxCollider collider;
        private float charge;
        private double boost;
        private bool driftAllowed;
        private bool drifting;

        public Rigidbody Body { get; }
        internal RaceItem HeldItem { get; private set; }
        internal bool RelayBoostHeld => HeldItem == RaceItem.RelayBoost;
        internal double ShieldRemaining { get; private set; }
        internal double GripLossRemaining { get; private set; }
        internal double ImmunityRemaining { get; private set; }
        internal double AccelTapRemaining { get; private set; }
        internal double SlipstreamRemaining { get; private set; }
        internal bool ShieldActive => ShieldRemaining > 0.0000001d;
        internal bool DisruptionImmune => ImmunityRemaining > 0.0000001d;
        internal bool AccelTapped => AccelTapRemaining > 0.0000001d;
        internal bool SlipstreamActive => SlipstreamRemaining > 0.0000001d;
        internal float GripMultiplier => GripLossRemaining > 0.0000001d ? 0.55f : 1f;
        internal int ItemsCollected { get; private set; }
        internal int ItemsUsed { get; private set; }
        internal int ItemsUsedMask { get; private set; }
        internal int PlacementVersion { get; private set; }
        public bool BoostReady => charge >= 0.49999f;
        public bool BoostActive => boost > 0.0000001d;
        public float ForwardSpeed => Vector3.Dot(Body.linearVelocity, Body.rotation * Vector3.forward);
        public bool OutOfBounds => Body.position.y < -1.35f || Body.position.sqrMagnitude > 125f * 125f;
        public bool Grounded => Physics.Raycast(Body.position - Vector3.up * 0.20f, Vector3.down,
            0.55f, 1 << 0, QueryTriggerInteraction.Ignore);

        /// <summary>Wraps the scene's interpolated kart body.</summary>
        public TrialKart(Rigidbody body, BoxCollider collider)
        {
            Body = body;
            this.collider = collider;
        }

        public bool OverlapsAt(Vector3 position, Quaternion rotation, TrialKart other) =>
            other != null && other.Body.gameObject.activeInHierarchy &&
            Physics.ComputePenetration(collider, position, rotation, other.collider,
                other.Body.position, other.Body.rotation, out _, out _);

        public float ClearanceAhead(Vector3 origin, Vector3 forward)
        {
            Vector3 offset = Body.position - origin;
            float ahead = Vector3.Dot(offset, forward);
            if (ahead <= 0f || Mathf.Abs(offset.y) >= collider.size.y)
                return float.PositiveInfinity;

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 otherRight = Body.rotation * Vector3.right;
            Vector3 otherForward = Body.rotation * Vector3.forward;
            Vector3 half = collider.size * 0.5f;
            float lateralExtent = half.x * Mathf.Abs(Vector3.Dot(right, otherRight))
                + half.z * Mathf.Abs(Vector3.Dot(right, otherForward));
            if (Mathf.Abs(Vector3.Dot(offset, right)) >= half.x + lateralExtent + 0.15f)
                return float.PositiveInfinity;

            float forwardExtent = half.x * Mathf.Abs(Vector3.Dot(forward, otherRight))
                + half.z * Mathf.Abs(Vector3.Dot(forward, otherForward));
            return ahead - forwardExtent - half.z;
        }

        public void Freeze()
        {
            if (Body.isKinematic)
                return;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
        }

        // The chase view uses interpolation. The crossing needs the current physics pose,
        // including a reset performed in this same fixed step.
        internal Bounds PhysicsBounds
        {
            get
            {
                Quaternion rotation = Body.rotation;
                Vector3 size = collider.size;
                Vector3 x = rotation * Vector3.right * size.x;
                Vector3 y = rotation * Vector3.up * size.y;
                Vector3 z = rotation * Vector3.forward * size.z;
                return new Bounds(Body.position + rotation * collider.center, new Vector3(
                    Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
            }
        }

        /// <summary>Discards an unfinished drift when its control is released in a paused trial.</summary>
        public void CancelDrift()
        {
            charge = 0f;
            drifting = false;
        }

        /// <summary>Teleports the body and clears motion and transient handling state.</summary>
        public void Place(Vector3 position, Vector3 forward)
        {
            PlacementVersion++;
            if (Body.isKinematic)
                Body.isKinematic = false;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.position = position;
            Body.rotation = Quaternion.LookRotation(forward);
            Body.transform.SetPositionAndRotation(Body.position, Body.rotation);
            charge = 0f;
            boost = 0d;
            ClearEffects();
            drifting = driftAllowed = false;
        }

        /// <summary>Fills the single slot without replacing an item already held.</summary>
        internal void CollectRelayBoost()
        {
            CollectItem(RaceItem.RelayBoost);
        }

        /// <summary>Collects into the shared slot; full slots keep their existing item.</summary>
        internal void CollectItem(RaceItem item)
        {
            if (HeldItem != RaceItem.None || item == RaceItem.None)
                return;
            HeldItem = item;
            ItemsCollected++;
        }

        /// <summary>Retry clears inventories and telemetry; ordinary placement preserves them.</summary>
        internal void ClearItems()
        {
            HeldItem = RaceItem.None;
            ClearEffects();
            ItemsCollected = ItemsUsed = 0;
            ItemsUsedMask = 0;
        }

        /// <summary>Consumes a held item only for forward, grounded driving without braking.</summary>
        internal bool TryUseRelayBoost(bool grounded, float brake)
        {
            if (!RelayBoostHeld || !grounded || ForwardSpeed <= 4f || brake > 0.02f)
                return false;
            ConsumeItem();
            ActivateBoost();
            return true;
        }

        /// <summary>Both boost sources refresh one timer and share the existing speed and acceleration.</summary>
        internal void ActivateBoost() => boost = BoostDuration;

        /// <summary>Consumes only a successfully activated item, recording one use.</summary>
        internal void ConsumeItem() { ItemsUsedMask |= 1 << (int)HeldItem; HeldItem = RaceItem.None; ItemsUsed++; }

        /// <summary>Shield works even at rest or under braking; an active shield preserves the slot.</summary>
        internal bool TryUseShield()
        {
            if (HeldItem != RaceItem.PulseShield || ShieldActive) return false;
            ConsumeItem();
            ShieldRemaining = 3d;
            return true;
        }

        /// <summary>Opens a four-second slipstream window; assist applies only while holding the draft line.</summary>
        internal bool TryUseSlipstream()
        {
            if (HeldItem != RaceItem.SlipstreamLink || SlipstreamActive) return false;
            ConsumeItem();
            SlipstreamRemaining = 4d;
            return true;
        }

        /// <summary>Spends a contact immediately; immunity precedes shield absorption and grip loss.</summary>
        internal void ReceiveRibbonHit()
        {
            if (DisruptionImmune) return;
            if (ShieldActive)
            {
                ShieldRemaining = 0d;
                ImmunityRemaining = 1.5d;
            }
            else if (GripLossRemaining <= 0.0000001d) GripLossRemaining = 1d;
        }

        /// <summary>Applies a brief acceleration reduction unless immune or absorbed by Pulse Shield.</summary>
        internal void ReceiveSignalTapHit()
        {
            if (DisruptionImmune) return;
            if (ShieldActive)
            {
                ShieldRemaining = 0d;
                ImmunityRemaining = 1.5d;
            }
            else if (!AccelTapped) AccelTapRemaining = 1d;
        }

        /// <summary>Recovery clears active effects and grants protection without changing inventory.</summary>
        internal void RecoverEffects() { ClearEffects(); ImmunityRemaining = 1.5d; }

        /// <summary>Clears transient effects for placement and Retry; pickup history belongs to the sampler.</summary>
        private void ClearEffects() { boost = ShieldRemaining = GripLossRemaining = ImmunityRemaining = AccelTapRemaining = SlipstreamRemaining = 0d; }

        /// <summary>Advances effects after handling; only the time after disruptions end consumes new immunity.</summary>
        internal void AdvanceEffects(float dt)
        {
            boost = System.Math.Max(0d, boost - dt);
            ShieldRemaining = System.Math.Max(0d, ShieldRemaining - dt);
            SlipstreamRemaining = System.Math.Max(0d, SlipstreamRemaining - dt);
            ImmunityRemaining = System.Math.Max(0d, ImmunityRemaining - dt);
            if (GripLossRemaining > 0.0000001d)
            {
                double left = GripLossRemaining - dt;
                GripLossRemaining = System.Math.Max(0d, left);
                if (left <= 0.0000001d) ImmunityRemaining = System.Math.Max(0d, 1.5d + System.Math.Min(0d, left));
            }
            if (AccelTapRemaining > 0.0000001d)
            {
                double left = AccelTapRemaining - dt;
                AccelTapRemaining = System.Math.Max(0d, left);
                if (left <= 0.0000001d) ImmunityRemaining = System.Math.Max(0d, 1.5d + System.Math.Min(0d, left));
            }
        }

        /// <summary>Applies one grounded handling step and tracks boost in simulation seconds.</summary>
        public void Step(float throttle, float brake, float steer, bool driftHeld, bool grounded, float dt,
            bool advanceEffects = true, bool inSlipstream = false)
        {
            Vector3 forward = Body.rotation * Vector3.forward;
            Vector3 right = Body.rotation * Vector3.right;
            Vector3 velocity = Body.linearVelocity;
            float speed = Vector3.Dot(velocity, forward);

            // A release after placement arms drift, so a button held through reset grants no charge.
            if (!driftHeld)
            {
                if (drifting && BoostReady && grounded && speed > 4f)
                    ActivateBoost();
                driftAllowed = true;
            }
            float sideways = Vector3.Dot(velocity, right);
            bool sliding = Mathf.Abs(steer) > 0.1f || Mathf.Abs(sideways) > 0.8f;
            drifting = driftHeld && driftAllowed && grounded && speed > 4f && sliding;
            charge = drifting ? charge + dt : 0f;

            if (!grounded)
            {
                if (advanceEffects) AdvanceEffects(dt);
                return;
            }

            bool drafting = !BoostActive && SlipstreamActive && inSlipstream && throttle > 0.02f && brake <= 0.02f;
            float target = BoostActive ? 34f : drafting ? 30f : throttle * NormalSpeed;
            float acceleration = BoostActive ? 29f : drafting ? 21f : 13f;
            if (!BoostActive && AccelTapped && throttle > 0.02f && brake <= 0.02f)
                acceleration = 5.5f;
            if (brake > 0.02f)
            {
                target = speed > 0.35f ? 0f : -6f * brake;
                acceleration = Mathf.Lerp(10f, 32f, brake);
            }
            else if (!BoostActive && throttle < 0.02f)
                acceleration = 5f;

            speed = Mathf.MoveTowards(speed, target, acceleration * dt);
            float lateral = sideways * Mathf.Exp(-(drifting ? 2.8f : 10f) * GripMultiplier * dt);
            Body.linearVelocity = forward * speed + right * lateral + Vector3.up * velocity.y;
            float steeringRate = Mathf.Lerp(140f, 74f, Mathf.Abs(speed) / NormalSpeed);
            float motion = Mathf.Clamp(speed / 2.5f, -1f, 1f);
            Body.MoveRotation(Body.rotation * Quaternion.Euler(0, steer * motion * steeringRate * (drifting ? 1.28f : 1f) * dt, 0));
            if (advanceEffects) AdvanceEffects(dt);
        }
    }
}
