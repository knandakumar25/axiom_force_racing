using System;
using System.Collections.Generic;

namespace DriftCircuit
{
    /// <summary>Bag-shuffled, trailing-weighted Quick Race item pool with deterministic seeding.</summary>
    internal sealed class ItemPool
    {
        private static readonly RaceItem[] BaseItems =
        {
            RaceItem.RelayBoost,
            RaceItem.FluxRibbon,
            RaceItem.PulseShield,
            RaceItem.SignalTap,
            RaceItem.SlipstreamLink,
            RaceItem.RouteBeacon
        };

        private readonly Random rng;
        private readonly List<RaceItem> bag = new();

        /// <summary>Creates a deterministic or time-seeded pool for Quick Race item gates.</summary>
        internal ItemPool(int seed = 1337)
        {
            rng = new Random(seed);
        }

        /// <summary>Resets the shuffled bag on Retry so runs start cleanly.</summary>
        internal void Reset() => bag.Clear();

        /// <summary>Draws the next item; trailing karts gain extra weight toward catch-up items without guaranteed leader hits.</summary>
        internal RaceItem Next(bool isTrailing)
        {
            if (bag.Count == 0)
                Refill(isTrailing);
            int index = rng.Next(bag.Count);
            RaceItem item = bag[index];
            bag.RemoveAt(index);
            return item;
        }

        private void Refill(bool isTrailing)
        {
            bag.AddRange(BaseItems);
            if (isTrailing)
            {
                bag.Add(RaceItem.RelayBoost);
                bag.Add(RaceItem.SlipstreamLink);
            }
        }
    }
}
