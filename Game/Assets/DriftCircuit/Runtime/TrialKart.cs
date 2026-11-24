using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Applies arcade handling, tire grip, drift charge, and boost to the kart body.</summary>
    internal sealed class TrialKart
    {
        private const float BoostDuration = 1f;
        private const float NormalSpeed = 25f;
        private float charge;
        private float boost;
        private bool driftAllowed;
        private bool drifting;

        public Rigidbody Body { get; }
        public bool BoostReady => charge >= 0.49999f;
        public bool BoostActive => boost > 0f;
        public float ForwardSpeed => Vector3.Dot(Body.linearVelocity, Body.rotation * Vector3.forward);

        /// <summary>Wraps the scene's interpolated kart body.</summary>
        public TrialKart(Rigidbody body) => Body = body;

        /// <summary>Discards an unfinished drift when its control is released in a paused trial.</summary>
        public void CancelDrift()
        {
            charge = 0f;
            drifting = false;
        }

        /// <summary>Teleports the body and clears motion and transient handling state.</summary>
        public void Place(Vector3 position, Vector3 forward)
        {
            if (Body.isKinematic)
                Body.isKinematic = false;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.position = position;
            Body.rotation = Quaternion.LookRotation(forward);
            Body.transform.SetPositionAndRotation(Body.position, Body.rotation);
            charge = boost = 0;
            drifting = driftAllowed = false;
        }

        /// <summary>Applies one grounded handling step and tracks boost in simulation seconds.</summary>
        public void Step(float throttle, float brake, float steer, bool driftHeld, bool grounded, float dt)
        {
            Vector3 forward = Body.rotation * Vector3.forward;
            Vector3 right = Body.rotation * Vector3.right;
            Vector3 velocity = Body.linearVelocity;
            float speed = Vector3.Dot(velocity, forward);

            // A release after placement arms drift, so a button held through reset grants no charge.
            if (!driftHeld)
            {
                if (drifting && BoostReady && grounded && speed > 4f)
                    boost = BoostDuration;
                driftAllowed = true;
            }
            float sideways = Vector3.Dot(velocity, right);
            bool sliding = Mathf.Abs(steer) > 0.1f || Mathf.Abs(sideways) > 0.8f;
            drifting = driftHeld && driftAllowed && grounded && speed > 4f && sliding;
            charge = drifting ? charge + dt : 0f;

            if (!grounded)
            {
                boost = Mathf.Max(0f, boost - dt);
                return;
            }

            float target = BoostActive ? 34f : throttle * NormalSpeed;
            float acceleration = BoostActive ? 29f : 13f;
            if (brake > 0.02f)
            {
                target = speed > 0.35f ? 0f : -6f * brake;
                acceleration = Mathf.Lerp(10f, 32f, brake);
            }
            else if (!BoostActive && throttle < 0.02f)
                acceleration = 5f;

            speed = Mathf.MoveTowards(speed, target, acceleration * dt);
            float lateral = sideways * Mathf.Exp(-(drifting ? 2.8f : 10f) * dt);
            Body.linearVelocity = forward * speed + right * lateral + Vector3.up * velocity.y;
            float steeringRate = Mathf.Lerp(140f, 74f, Mathf.Abs(speed) / NormalSpeed);
            float motion = Mathf.Clamp(speed / 2.5f, -1f, 1f);
            Body.MoveRotation(Body.rotation * Quaternion.Euler(0, steer * motion * steeringRate * (drifting ? 1.28f : 1f) * dt, 0));
            boost = Mathf.Max(0f, boost - dt);
        }
    }
}
