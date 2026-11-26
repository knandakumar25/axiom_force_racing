using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Per-kart, per-valid-lap allowances for shared Quick Race pickup planes.</summary>
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

        private static readonly Checkpoint[] TokyoGates =
        {
            new("ITEM GATE", new(15, 0, -38), Vector3.right, 5.2f),
            new("ITEM GATE", new(-10, 0, 49), Vector3.left, 7.0f),
            new("ITEM GATE", new(-60, 0, 0), Vector3.back, 5.2f)
        };

        private static readonly Checkpoint[] ViaductGates =
        {
            new("ITEM GATE", new(18, 0, -40), Vector3.right, 6.4f),
            new("ITEM GATE", new(-10, 0, 64), Vector3.left, 7.4f),
            new("ITEM GATE", new(-65, 0, -5), Vector3.back, 6.4f)
        };

        private static readonly Checkpoint[] CyberGraveGates =
        {
            new("ITEM GATE", new(24, 0, -62), Vector3.right, 6.6f),
            new("ITEM GATE", new(-10, 0, 54), Vector3.left, 6.8f)
        };

        private static readonly Checkpoint[] SpireHullGates =
        {
            new("ITEM GATE", new(22, 0.3f, -60), Vector3.right, 6.8f),
            new("ITEM GATE", new(-10, 4.6f, 58), Vector3.left, 7.0f)
        };

        private static readonly Checkpoint[] ApexDescentGates =
        {
            new("ITEM GATE", new(22, 0.5f, -64), Vector3.right, 7.0f),
            new("ITEM GATE", new(-10, 3.4f, 60), Vector3.left, 7.2f)
        };

        private static readonly RaceItem[] RandomThree = { RaceItem.None, RaceItem.None, RaceItem.None };
        private static readonly RaceItem[] RandomTwo = { RaceItem.None, RaceItem.None };

        private readonly Checkpoint[] gates;
        private readonly RaceItem[] items;
        private readonly ItemPool pool;
        private readonly int[] spentLap;
        private Vector3 previous;
        private int placementVersion = -1;

        internal RelayBoostPickups(CourseDefinition circuit = null, ItemPool pool = null)
        {
            gates = GatesFor(circuit ?? CourseDefinition.Docks);
            items = ItemsFor(circuit ?? CourseDefinition.Docks);
            this.pool = pool;
            spentLap = new int[gates.Length];
            for (int i = 0; i < spentLap.Length; i++) spentLap[i] = -1;
        }

        internal static Checkpoint[] GatesFor(CourseDefinition circuit)
        {
            if (circuit == CourseDefinition.Tokyo) return TokyoGates;
            if (circuit == CourseDefinition.Viaduct) return ViaductGates;
            if (circuit == CourseDefinition.CyberGrave) return CyberGraveGates;
            if (circuit == CourseDefinition.SpireHull) return SpireHullGates;
            if (circuit == CourseDefinition.ApexDescent) return ApexDescentGates;
            if (circuit == CourseDefinition.Rustrail || circuit == CourseDefinition.Foundry)
                return System.Array.Empty<Checkpoint>();
            return Gates;
        }

        internal static RaceItem[] ItemsFor(CourseDefinition circuit)
        {
            if (circuit == CourseDefinition.Tokyo || circuit == CourseDefinition.Viaduct) return RandomThree;
            if (circuit == CourseDefinition.CyberGrave || circuit == CourseDefinition.SpireHull ||
                circuit == CourseDefinition.ApexDescent) return RandomTwo;
            if (circuit == CourseDefinition.Rustrail || circuit == CourseDefinition.Foundry)
                return System.Array.Empty<RaceItem>();
            return Items;
        }

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
        internal void Sample(TrialKart kart, TrialRules rules, bool isTrailing = false)
        {
            Vector3 current = kart.Body.position;
            if (placementVersion == kart.PlacementVersion && rules.Phase == TrialPhase.Racing && !kart.OutOfBounds)
            {
                for (int i = 0; i < gates.Length; i++)
                {
                    if (spentLap[i] == rules.Laps || !Crossed(gates[i], previous, current))
                        continue;
                    spentLap[i] = rules.Laps;
                    RaceItem granted = items[i] != RaceItem.None ? items[i] : (pool?.Next(isTrailing) ?? RaceItem.RelayBoost);
                    kart.CollectItem(granted);
                }
            }
            previous = current;
            placementVersion = kart.PlacementVersion;
        }
    }
}
