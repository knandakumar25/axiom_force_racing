using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Per-kart, per-valid-lap allowances for all six shared Docks pickup planes.</summary>
    internal sealed class RelayBoostPickups
    {
        internal static readonly Checkpoint[] Gates =
        {
            new("RELAY BOOST", new(20, 0, -64), Vector3.right, 7.7f),
            new("RELAY BOOST", new(20, 0, 62), Vector3.left, 7.7f),
            new("PULSE SHIELD", new(60, 0, 62), Vector3.left, 7.7f),
            new("FLUX RIBBON", new(-20, 0, 62), Vector3.left, 7.7f),
            new("SIGNAL TAP", new(-20, 0, -64), Vector3.right, 7.7f),
            new("SLIPSTREAM LINK", new(50, 0, -64), Vector3.right, 7.7f)
        };

        internal static readonly RaceItem[] Items =
        {
            RaceItem.RelayBoost, RaceItem.RelayBoost, RaceItem.PulseShield,
            RaceItem.FluxRibbon, RaceItem.SignalTap, RaceItem.SlipstreamLink
        };

        private readonly int[] spentLap = { -1, -1, -1, -1, -1, -1 };
        private Vector3 previous;
        private int placementVersion = -1;

        /// <summary>Retry restores every allowance and discards the prior run's motion segment.</summary>
        internal void Restart()
        {
            for (int i = 0; i < spentLap.Length; i++) spentLap[i] = -1;
            placementVersion = -1;
        }

        /// <summary>Accepts forward swept passages at kart height, including fast crossings.</summary>
        internal static bool Crossed(Checkpoint gate, Vector3 from, Vector3 to)
        {
            if (!gate.Crossed(from, to))
                return false;
            float start = Vector3.Dot(from - gate.Center, gate.Forward);
            float end = Vector3.Dot(to - gate.Center, gate.Forward);
            float height = Mathf.LerpUnclamped(from.y, to.y, -start / (end - start));
            return height >= 0.15f && height <= 1.3f;
        }

        /// <summary>Samples only racing motion since the last placement; a full slot still spends eligibility.</summary>
        internal void Sample(TrialKart kart, TrialRules rules)
        {
            Vector3 current = kart.Body.position;
            if (placementVersion == kart.PlacementVersion && rules.Phase == TrialPhase.Racing && !kart.OutOfBounds)
            {
                for (int i = 0; i < Gates.Length; i++)
                {
                    if (spentLap[i] == rules.Laps || !Crossed(Gates[i], previous, current))
                        continue;
                    spentLap[i] = rules.Laps;
                    kart.CollectItem(Items[i]);
                }
            }
            previous = current;
            placementVersion = kart.PlacementVersion;
        }
    }
}
