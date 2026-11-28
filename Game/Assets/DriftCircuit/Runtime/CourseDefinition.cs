using System.Collections.Generic;
using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Pairs a road center with the half width and banking angle at that point.</summary>
    internal readonly struct Station
    {
        public readonly Vector3 Center;
        public readonly float Width;
        public readonly float Bank;

        /// <summary>Defines a station in scene space.</summary>
        public Station(Vector3 center, float width, float bank = 0f) { Center = center; Width = width; Bank = bank; }

        /// <summary>Defines a station on the flat course.</summary>
        public Station(float x, float z, float width, float bank = 0f) : this(new Vector3(x, 0, z), width, bank) { }

        /// <summary>Interpolates the width, banking, and center along a road segment.</summary>
        public static Station Lerp(Station from, Station to, float t) =>
            new(Vector3.Lerp(from.Center, to.Center, t), Mathf.Lerp(from.Width, to.Width, t), Mathf.Lerp(from.Bank, to.Bank, t));
    }


    internal sealed class CourseDefinition
    {
        private static readonly Vector3 TokyoStart = new(4, 0.58f, -38);
        private static readonly Checkpoint[] TokyoGates =
        {
            new("Market", new(33, 0, -24), Vector3.forward, 4.3f),
            new("Skyport", new(5, 0, 49), Vector3.left, 7f),
            new("Maintenance split", new(-60, 0, 24), Vector3.back, 5.2f),
            new("Finish", new(0, 0, -38), Vector3.right, 5.2f)
        };

        private static readonly Station[] TokyoMain =
        {
            new(0, -38, 5.4f), new(24, -38, 5.4f), new(33, -31, 4.6f),
            new(33, -18, 4.6f), new(24, -10, 4.6f), new(24, 4, 4.6f),
            new(39, 17, 5.3f), new(39, 38, 6.2f), new(29, 49, 7.2f),
            new(-46, 49, 7.2f), new(-60, 36, 5.4f), new(-60, 14, 5.4f),
            new(-60, -24, 5.4f), new(-49, -38, 5.4f), new(-22, -38, 5.4f)
        };
        private static readonly Station[] TokyoShortcut =
        {
            new(-60, 14, 3.2f), new(-52, 4, 3.2f), new(-46, -11, 3.2f),
            new(-35, -28, 3.2f), new(-22, -38, 3.2f), new(-15, -38, 3.2f)
        };


        private static readonly Station[] ViaductMain =
        {
            new(0, -40, 6.8f), new(32, -40, 6.8f), new(48, -27, 6.4f),
            new(48, -10, 6.4f), new(35, 3, 6.4f), new(35, 17, 6.4f),
            new(50, 30, 6.4f), new(50, 49, 7f), new(32, 64, 7.8f),
            new(-43, 64, 7.8f), new(-65, 47, 6.8f), new(-65, 30, 6.8f),
            new(-65, 12, 6.8f), new(-65, -24, 6.8f), new(-51, -40, 6.8f),
            new(-23, -40, 6.8f)
        };

        private static readonly Station[] ViaductTunnel =
        {
            new(-65, 12, 3.7f), new(-54, 2, 3.7f), new(-51, -14, 3.7f),
            new(-36, -30, 3.7f), new(-23, -40, 3.7f), new(-14, -40, 3.7f)
        };

        public static readonly CourseDefinition Tokyo = new(
            "little-tokyo-skyport", "Little Tokyo–Skyport", 1, TokyoStart, Vector3.right,
            TokyoGates, TokyoMain, TokyoShortcut, 4, "Market and Skyport loop", "Maintenance shortcut", TokyoLandmarks());

        public static readonly CourseDefinition Viaduct = new(
            "concrete-wards-viaduct", "Concrete Wards Viaduct", 1, new(4, 0.58f, -40), Vector3.right,
            new Checkpoint[]
            {
                new("South approach", new(48, 0, -19), Vector3.forward, 6f),
                new("North viaduct", new(2, 0, 64), Vector3.left, 7.4f),
                new("Service split", new(-65, 0, 28), Vector3.back, 6.4f),
                new("Finish", new(0, 0, -40), Vector3.right, 6.4f)
            }, ViaductMain, ViaductTunnel, 4, "Viaduct boulevard", "Service tunnel", ViaductLandmarks());

        private static readonly Station[] DocksMain =
        {
            new(0, -64, 8f), new(77, -64, 8f), new(90, -50, 7.5f),
            new(90, -8, 7.5f), new(74, 6, 7f), new(74, 20, 7f),
            new(89, 34, 7f), new(89, 46, 7.5f), new(71, 62, 8f),
            new(-66, 62, 8f), new(-88, 45, 7.5f), new(-88, 20, 7.5f),
            new(-88, 11, 7.5f), new(-64, 0, 7f), new(-60, -15, 7f),
            new(-73, -28, 7f), new(-88, -33, 7.5f), new(-88, -43, 7.5f),
            new(-88, -51, 7.5f), new(-75, -64, 8f), new(-55, -64, 8f)
        };

        private static readonly Station[] DocksQuay =
        {
            new(-88, 20, 3.4f), new(-91, 7, 3.4f), new(-91, -28, 3.4f),
            new(-88, -43, 3.4f), new(-88, -49, 3.4f)
        };

        public static readonly CourseDefinition Docks = new(
            "stratos-docks", "Stratos Docks", 1, new(4, 0.58f, -64), Vector3.right,
            new Checkpoint[]
            {
                new("East Pier", new(90, 0, -31), Vector3.forward, 7.2f),
                new("Yard Exit", new(48, 0, 62), Vector3.left, 7.7f),
                new("Quay Approach", new(-88, 0, 32), Vector3.back, 7.2f),
                new("Finish", new(0, 0, -64), Vector3.right, 7.7f)
            }, DocksMain, DocksQuay, 3, "Loading yard", "Quay shortcut", DocksLandmarks());

        private static readonly Station[] RustrailMain =
        {
            new(0, -73, 7), new(58, -73, 7), new(78, -58, 7),
            new(84, -34, 7), new(84, 26, 7), new(70, 55, 7),
            new(42, 76, 7), new(-64, 76, 7), new(-84, 63, 7),
            new(-84, 20, 7), new(-84, 13, 7), new(-76, 4, 6),
            new(-52, 8, 6), new(-44, -2, 6), new(-52, -16, 6),
            new(-68, -17, 6), new(-72, -27, 6), new(-84, -38, 7),
            new(-84, -46, 7), new(-84, -57, 7), new(-69, -73, 7),
            new(-45, -73, 7)
        };

        private static readonly Station[] RustrailCrossing =
        {
            new(-84, 20, 3.6f), new(-90, 7, 3.6f), new(-90, -29, 3.6f),
            new(-84, -46, 3.6f), new(-84, -52, 3.6f)
        };

        /// <summary>Defines the shared checkpoint loop and its timed crossing.</summary>
        public static readonly CourseDefinition Rustrail = new(
            "rustrail-junction", "Rustrail Junction", 1, new(4, 0.58f, -73), Vector3.right,
            new Checkpoint[]
            {
                new("Rail Yard", new(84, 0, -5), Vector3.forward, 6.7f),
                new("Signal Approach", new(-16, 0, 76), Vector3.left, 6.7f),
                new("Crossing Approach", new(-84, 0, 43), Vector3.back, 6.7f),
                new("Finish", new(0, 0, -73), Vector3.right, 6.7f)
            }, RustrailMain, RustrailCrossing, 3, "Switchback", "Rail crossing",
            new Landmark[] { new("Rail yard foundation", new(0, -3, 0), new(224, 1, 189), Surface.Dark) });

        private static readonly Station[] FoundryMain =
        {
            new(0, -68, 7.2f), new(56, -68, 7.2f, 8f), new(82, -44, 7.2f, 12f),
            new(82, 24, 7.2f, 12f), new(56, 60, 7.4f, 10f), new(-56, 60, 7.4f, 6f),
            new(-84, 42, 7f), new(-84, 28, 7f), new(-84, 20, 7f), new(-84, 12, 7f), new(-62, 6, 6.2f),
            new(-52, -12, 6.2f), new(-64, -30, 6.2f), new(-84, -38, 7f), new(-84, -46, 7f),
            new(-84, -56, 7f), new(-66, -68, 7.2f), new(-36, -68, 7.2f)
        };

        private static readonly Station[] FoundryBypass =
        {
            new(-84, 20, 3.8f), new(-90, 7, 3.8f), new(-90, -29, 3.8f),
            new(-84, -46, 3.8f), new(-84, -56, 7f)
        };

        private static readonly Station[] CyberGraveMain =
        {
            new(0, -62, 6.8f), new(54, -62, 6.8f), new(76, -38, 6.5f),
            new(76, 18, 6.5f), new(52, 54, 7f), new(-48, 54, 7f),
            new(-68, 36, 6.6f), new(-68, 24, 6.6f), new(-68, 16, 6.6f), new(-68, 8, 6.6f), new(-48, 2, 6f),
            new(-48, -18, 6f), new(-68, -32, 6.6f), new(-68, -46, 6.6f), new(-68, -54, 6.6f),
            new(-46, -62, 6.8f), new(-22, -62, 6.8f)
        };

        private static readonly Station[] CyberGraveRamp =
        {
            new(-68, 16, 4.2f), new(new Vector3(-79, 1.4f, 3), 4.2f, 5f),
            new(new Vector3(-82, 2.8f, -11), 4.2f, 8f), new(new Vector3(-79, 1.6f, -27), 4.2f, 5f),
            new(-68, -46, 4.2f), new(-68, -54, 6.6f)
        };

        private static readonly Station[] SpireHullMain =
        {
            new(0, -60, 7f), new(new Vector3(52, 0.8f, -60), 7f), new(new Vector3(76, 2.2f, -32), 7f),
            new(new Vector3(76, 3.8f, 24), 7f), new(new Vector3(48, 5.0f, 58), 7.2f),
            new(new Vector3(-44, 4.2f, 58), 7.2f), new(new Vector3(-74, 3.5f, 44), 7f),
            new(new Vector3(-74, 3.0f, 34), 7f), new(new Vector3(-74, 2.5f, 24), 7f),
            new(new Vector3(-74, 1.5f, 4), 7f), new(-74, -28, 7f),
            new(-74, -42, 7f), new(-74, -50, 7f), new(-48, -60, 7f), new(-24, -60, 7f)
        };

        private static readonly Station[] SpireHullTransfer =
        {
            new(new Vector3(-74, 3.0f, 34), 3.8f), new(new Vector3(-56, 1.6f, 14), 3.8f),
            new(new Vector3(-56, 0.4f, -14), 3.8f), new(-74, -42, 3.8f), new(-74, -50, 7f)
        };

        private static readonly Station[] ApexBastionMain =
        {
            new(0, -64, 7.2f), new(new Vector3(54, 1.5f, -64), 7.2f), new(new Vector3(78, 3.5f, -34), 7.2f),
            new(new Vector3(78, 4.8f, 22), 7.4f), new(new Vector3(48, 4.2f, 60), 7.4f),
            new(new Vector3(-46, 2.8f, 60), 7.4f), new(new Vector3(-76, 2.0f, 44), 7.4f),
            new(new Vector3(-76, 1.6f, 34), 7.4f), new(new Vector3(-76, 1.2f, 24), 7.4f),
            new(new Vector3(-88, 1.15f, 9), 7.4f, -6f), new(new Vector3(-90, 0.75f, -9), 7.4f, -8f),
            new(new Vector3(-86, 0.25f, -29), 7.4f, -5f), new(-76, -44, 7.4f),
            new(-76, -54, 7.4f), new(-50, -64, 7.2f), new(-24, -64, 7.2f)
        };

        private static readonly Station[] ApexBastionInner =
        {
            new(new Vector3(-76, 1.6f, 34), 4.4f), new(new Vector3(-58, 0.9f, 10), 4.4f),
            new(new Vector3(-58, 0.3f, -18), 4.4f), new(-76, -44, 4.4f), new(-76, -54, 7.4f)
        };

        public static readonly CourseDefinition Foundry = new(
            "foundry-forge", "Foundry Forge", 1, new(4, 0.58f, -68), Vector3.right,
            new Checkpoint[]
            {
                new("Smelter Turn", new(82, 0, -10), Vector3.forward, 7f),
                new("Upper Catwalk", new(0, 0, 60), Vector3.left, 7.2f),
                new("Gate Approach", new(-84, 0, 32), Vector3.back, 6.8f),
                new("Finish", new(0, 0, -68), Vector3.right, 7f)
            }, FoundryMain, FoundryBypass, 3, "Factory loop", "Timed bypass",
            new Landmark[] { new("Foundry floor", new(0, -3, 0), new(224, 1, 189), Surface.Dark) }, Cup.Skyline);

        public static readonly CourseDefinition CyberGrave = new(
            "cyber-grave-circuit", "Cyber Grave Circuit", 2, new(4, 0.58f, -62), Vector3.right,
            new Checkpoint[]
            {
                new("Scrap Yard", new(76, 0, -10), Vector3.forward, 6.4f),
                new("North Ridge", new(0, 0, 54), Vector3.left, 6.8f),
                new("Ramp Approach", new(-68, 0, 26), Vector3.back, 6.4f),
                new("Finish", new(0, 0, -62), Vector3.right, 6.6f)
            }, CyberGraveMain, CyberGraveRamp, 4, "Salvage hairpin", "Ridge ramp",
            new Landmark[] { new("Salvage yard", new(0, -3, 0), new(210, 1, 175), Surface.Dark) }, Cup.Skyline);

        public static readonly CourseDefinition SpireHull = new(
            "spire-outer-hull", "Spire Outer Hull", 1, new(4, 0.58f, -60), Vector3.right,
            new Checkpoint[]
            {
                new("East Climb", new(76, 3.0f, 0), Vector3.forward, 6.8f),
                new("Summit", new(0, 4.6f, 58), Vector3.left, 7f),
                new("Transfer Entry", new(-74, 3.5f, 44), Vector3.back, 6.8f),
                new("Finish", new(0, 0, -60), Vector3.right, 6.8f)
            }, SpireHullMain, SpireHullTransfer, 3, "Spiral hull", "Lower transfer",
            new Landmark[] { new("Spire base", new(0, -3, 0), new(210, 1, 175), Surface.Dark) }, Cup.Skyline);

        public static readonly CourseDefinition ApexDescent = new(
            "apex-bastion-descent", "Apex Bastion Descent", 2, new(4, 0.58f, -64), Vector3.right,
            new Checkpoint[]
            {
                new("Bastion Climb", new(78, 4.2f, -4), Vector3.forward, 7f),
                new("High Crest", new(0, 3.5f, 60), Vector3.left, 7.2f),
                new("Descent Split", new(-76, 2.2f, 46), Vector3.back, 7.2f),
                new("Finish", new(0, 0, -64), Vector3.right, 7f)
            }, ApexBastionMain, ApexBastionInner, 3, "Outer descent", "Inner descent",
            new Landmark[] { new("Bastion plateau", new(0, -3, 0), new(220, 1, 185), Surface.Dark) }, Cup.Skyline);

        internal enum Cup { City, Skyline }

        public static readonly CourseDefinition[] All = { Tokyo, Viaduct, Docks, Rustrail };
        public static readonly CourseDefinition[] Skyline = { Foundry, CyberGrave, SpireHull, ApexDescent };
        public static readonly CourseDefinition[] AllCircuits = { Tokyo, Viaduct, Docks, Rustrail, Foundry, CyberGrave, SpireHull, ApexDescent };

        public readonly string Id, Name, MainName, ShortcutName;
        public readonly int Revision, ShortcutRejoin;
        public readonly Vector3 Start, StartForward;
        public readonly Checkpoint[] Gates;
        public readonly Station[] Main, Shortcut;
        public readonly Landmark[] Landmarks;
        public readonly Cup CircuitCup;

        private CourseDefinition(string id, string name, int revision, Vector3 start, Vector3 startForward,
            Checkpoint[] gates, Station[] main, Station[] shortcut, int shortcutRejoin,
            string mainName, string shortcutName, Landmark[] landmarks, Cup cup = Cup.City)
        {
            Id = id;
            Name = name;
            Revision = revision;
            Start = start;
            StartForward = startForward;
            Gates = gates;
            Main = main;
            Shortcut = shortcut;
            ShortcutRejoin = shortcutRejoin;
            MainName = mainName;
            ShortcutName = shortcutName;
            Landmarks = landmarks;
            CircuitCup = cup;
        }

        public (Vector3 Position, Vector3 Forward) Respawn(int lastGate)
        {
            if (lastGate < 0)
                return (Start, StartForward);
            Checkpoint gate = Gates[lastGate];
            return (gate.Center + gate.Forward * 3f + Vector3.up * 0.58f, gate.Forward);
        }

        internal enum Surface { Structure, Dark, Cyan, Amber }

        internal readonly struct Landmark
        {
            public readonly string Name;
            public readonly Vector3 Position, Scale;
            public readonly Surface Material;
            public readonly float Yaw;
            public readonly bool Solid;

            public Landmark(string name, Vector3 position, Vector3 scale, Surface material, float yaw = 0f, bool solid = false)
            {
                Name = name;
                Position = position;
                Scale = scale;
                Material = material;
                Yaw = yaw;
                Solid = solid;
            }
        }

        private static Landmark[] TokyoLandmarks()
        {
            var objects = new List<Landmark>();
            objects.Add(new("City deck", new(-10, -3.5f, 3), new(170, 1, 153), Surface.Dark));
            for (int i = 0; i < 8; i++)
            {
                float height = 4 + (i * 7 % 9);
                objects.Add(new("Skyport terminal", new(20 - i * 9, height / 2 - 0.5f, 63), new(6.5f, height, 8), Surface.Structure));
                objects.Add(new("Terminal canopy", new(20 - i * 9, height - 0.45f, 63), new(6.7f, 0.14f, 8.2f), Surface.Cyan));
            }
            for (int i = 0; i < 6; i++)
            {
                float z = -28 + i * 7.5f;
                objects.Add(new("Market stall", new(47, 1.35f, z), new(5, 2.7f, 4.9f), Surface.Structure));
                objects.Add(new("Market awning", new(46.5f, 2.75f, z), new(6.3f, 0.24f, 5.4f), Surface.Amber));
            }
            return objects.ToArray();
        }

        private static Landmark[] ViaductLandmarks()
        {
            var objects = new List<Landmark>
            {
                new("Concrete Wards foundation", new(-7, -3.5f, 10), new(159, 1, 150), Surface.Dark),
                new("Elevated viaduct deck", new(-6, 8.5f, 64), new(76, 1.2f, 20), Surface.Structure),
                new("Service tunnel roof", new(-52.5f, 4.65f, -6f), new(9.5f, 0.7f, 15), Surface.Dark, -10, solid: true),
                new("Service tunnel header", new(-54, 4.25f, 1f), new(9.5f, 0.25f, 0.35f), Surface.Amber, -10)
            };
            for (int i = 0; i < 5; i++)
            {
                float x = -36 + i * 15;
                objects.Add(new("Viaduct crossbeam", new(x, 7.6f, 64), new(2f, 0.8f, 21), Surface.Structure));
                for (int side = -1; side <= 1; side += 2)
                    objects.Add(new("Viaduct support", new(x, 3.8f, 64 + side * 9.4f), new(1.5f, 7.6f, 1.45f), Surface.Structure));
            }
            Vector3[] approach =
            {
                new(48, 0, -20), new(45, 0, -5), new(35, 0, 10), new(41, 0, 24), new(50, 0, 36)
            };
            for (int i = 1; i < approach.Length; i++)
            {
                Vector3 segment = approach[i] - approach[i - 1];
                Vector3 center = (approach[i] + approach[i - 1]) * 0.5f;
                Vector3 right = Vector3.Cross(Vector3.up, segment.normalized);
                float yaw = Mathf.Atan2(segment.x, segment.z) * Mathf.Rad2Deg;
                objects.Add(new("Viaduct bend span", center + Vector3.up * 8.5f,
                    new(12f, 1.2f, segment.magnitude + 1f), Surface.Structure, yaw));
                objects.Add(new("Viaduct bend crossbeam", center + Vector3.up * 7.6f,
                    new(19.7f, 0.8f, 2f), Surface.Structure, yaw));
                for (int side = -1; side <= 1; side += 2)
                    objects.Add(new("Viaduct bend support", center + right * (9.2f * side) + Vector3.up * 3.8f,
                        new(1.45f, 7.6f, 1.5f), Surface.Structure, yaw));
            }
            for (int i = 0; i < 3; i++)
            {
                float z = -1 - i * 5;
                float x = -53.5f + i * 0.85f;
                for (int side = -1; side <= 1; side += 2)
                    objects.Add(new("Tunnel support", new(x + side * 4.35f, 2.15f, z), new(0.6f, 4.3f, 0.8f), Surface.Structure));
            }
            return objects.ToArray();
        }

        private static Landmark[] DocksLandmarks()
        {
            var objects = new List<Landmark>
            {
                new("Harbor", new(0, -3, 0), new(245, 1, 208), Surface.Dark),
                new("Dock deck", new(0, -0.85f, 2), new(188, 1, 147), Surface.Structure),
                new("Cargo gantry", new(88, 7.2f, 43), new(23, 0.85f, 2), Surface.Structure),
                new("Gantry west leg", new(77, 3.6f, 43), new(1.1f, 7.2f, 2), Surface.Structure),
                new("Gantry east leg", new(99, 3.6f, 43), new(1.1f, 7.2f, 2), Surface.Structure)
            };
            for (int i = 0; i < 6; i++)
            {
                objects.Add(new("Loading container", new(56, 1.25f, -20 + i * 9),
                    new(4, 2.5f, 7), i % 2 == 0 ? Surface.Structure : Surface.Dark));
                objects.Add(new("Quay bollard", new(-96, 0.72f, 17 - i * 10),
                    new(0.5f, 1.44f, 0.5f), Surface.Amber));
            }
            for (int i = 0; i < 3; i++)
            {
                float z = -42 + i * 3f;
                objects.Add(new("Chicane braking stripe", new(90, 0.036f, z),
                    new(14.2f, 0.024f, 0.32f), Surface.Amber));
            }
            for (int i = 0; i < 4; i++)
                objects.Add(new("Quay entry marking", new(-88.8f - i * 0.65f, 0.045f, 29 - i * 3f),
                    new(0.32f, 0.025f, 2.3f), Surface.Amber, 12f));
            return objects.ToArray();
        }
    }
}
