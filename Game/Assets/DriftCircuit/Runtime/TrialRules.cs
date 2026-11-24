using System;
using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Phases that govern input and the active race clock.</summary>
    public enum TrialPhase { Countdown, Racing, Paused, Finished }

    /// <summary>A bounded plane with an allowed direction of travel.</summary>
    public readonly struct Checkpoint
    {
        public readonly string Name;
        public readonly Vector3 Center;
        public readonly Vector3 Forward;
        public readonly float HalfWidth;

        /// <summary>Defines a gate at road height with a horizontal forward direction.</summary>
        public Checkpoint(string name, Vector3 center, Vector3 forward, float halfWidth)
        {
            Name = name;
            Center = center;
            Forward = forward.normalized;
            HalfWidth = halfWidth;
        }

        /// <summary>Checks the intersection within the gate even when a step spans the whole marker.</summary>
        public bool Crossed(Vector3 previous, Vector3 current)
        {
            float from = Vector3.Dot(previous - Center, Forward);
            float to = Vector3.Dot(current - Center, Forward);
            if (from >= 0f || to < 0f)
                return false;

            Vector3 hit = Vector3.LerpUnclamped(previous, current, -from / (to - from)) - Center;
            return Mathf.Abs(Vector3.Dot(hit, Vector3.Cross(Vector3.up, Forward))) <= HalfWidth
                && hit.y >= -0.25f && hit.y <= 3f;
        }
    }

    /// <summary>Tracks ordered gates, countdown, and lap times from fixed-step movement samples.</summary>
    public sealed class TrialRules
    {
        public const int LapCount = 3;
        public const float CountdownLength = 3f;

        private readonly Checkpoint[] gates;
        private readonly double[] splits = new double[LapCount];
        private TrialPhase beforePause;
        private Vector3 previous;
        private double lapStart;
        private double countdown;

        public TrialPhase Phase { get; private set; }
        public double Countdown => countdown;
        public double Elapsed { get; private set; }
        public int Laps { get; private set; }
        public int NextGate { get; private set; }
        public int LastGate { get; private set; }
        /// <summary>Returns the recorded duration for a zero-based lap.</summary>
        public double LapSplit(int index) => splits[index];
        public Checkpoint ExpectedGate => gates[NextGate];

        /// <summary>Starts a countdown with an ordered gate list ending at the finish plane.</summary>
        public TrialRules(Checkpoint[] gates, Vector3 start)
        {
            this.gates = gates;
            Restart(start);
        }

        /// <summary>Clears the result and starts a new countdown at the grid position.</summary>
        public void Restart(Vector3 start)
        {
            Phase = TrialPhase.Countdown;
            countdown = CountdownLength;
            Elapsed = lapStart = 0;
            Laps = NextGate = 0;
            LastGate = -1;
            Array.Clear(splits, 0, splits.Length);
            Rebase(start);
        }

        /// <summary>Discards the motion segment preceding a teleport.</summary>
        public void Rebase(Vector3 position) => previous = position;

        /// <summary>Freezes an active phase or resumes it with its original clock.</summary>
        public void TogglePause()
        {
            if (Phase == TrialPhase.Paused)
                Phase = beforePause;
            else if (Phase != TrialPhase.Finished)
            {
                beforePause = Phase;
                Phase = TrialPhase.Paused;
            }
        }

        /// <summary>Advances the active clock and accepts at most one gate per physics step.</summary>
        public void Tick(double seconds, Vector3 position)
        {
            if (Phase == TrialPhase.Countdown)
            {
                countdown = Math.Max(0, countdown - seconds);
                Rebase(position);
                if (countdown <= 0.000001)
                    Phase = TrialPhase.Racing;
                return;
            }
            if (Phase != TrialPhase.Racing)
                return;

            Elapsed += seconds;
            bool crossed = ExpectedGate.Crossed(previous, position);
            Rebase(position);
            if (!crossed)
                return;

            LastGate = NextGate++;
            if (NextGate < gates.Length)
                return;

            NextGate = 0;
            splits[Laps++] = Elapsed - lapStart;
            lapStart = Elapsed;
            if (Laps == LapCount)
                Phase = TrialPhase.Finished;
        }
    }
}
