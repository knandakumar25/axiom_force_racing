using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Maps authored landmarks and places additional scenery away from the race routes.</summary>
    internal static class TrialScenery
    {
        /// <summary>Returns the render model for each replaceable authored landmark.</summary>
        public static string ModelFor(string landmark) => landmark switch
        {
            "Skyport terminal" => "office",
            "Terminal canopy" => "office-roof",
            "Market stall" => "shop",
            "Market awning" => "awning",
            "Elevated viaduct deck" or "Viaduct bend span" or "Service tunnel roof" => "bridge-deck",
            "Viaduct crossbeam" or "Viaduct bend crossbeam" or "Cargo gantry" => "bridge-beam",
            "Viaduct support" or "Viaduct bend support" or "Tunnel support" or
                "Gantry west leg" or "Gantry east leg" => "bridge-pillar",
            "Loading container" => "freight",
            "Quay bollard" => "bollard",
            _ => null
        };

        /// <summary>Defines a rotated scenery footprint in course coordinates.</summary>
        internal readonly struct Placement
        {
            public readonly string Model;
            public readonly Vector3 Position, Size;
            public readonly float Yaw;

            /// <summary>Places the model on the chosen base elevation.</summary>
            public Placement(string model, float x, float z, float width, float height, float depth,
                float yaw = 0, float bottom = -2.5f)
            {
                Model = model;
                Position = new Vector3(x, bottom + height * 0.5f, z);
                Size = new Vector3(width, height, depth);
                Yaw = yaw;
            }
        }

        /// <summary>Returns the additional render-only scenery for a circuit.</summary>
        public static Placement[] Dressing(CourseDefinition circuit)
        {
            if (circuit == CourseDefinition.Docks)
                return new Placement[]
                {
                    new("quay", -108, -12, 4, 3, 35),
                    new("quay", -108, -49, 4, 3, 35),
                    new("quay", 110, -10, 4, 3, 26),
                    new("pallet", 59, -45, 6, 1.4f, 5, 15, -0.35f),
                    new("freight", 27, -48, 9, 4, 4, 90, -0.35f)
                };
            if (circuit == CourseDefinition.Rustrail)
                return new Placement[]
                {
                    new("pipe", 22, -55, 14, 9, 6, 90),
                    new("vent", 66, -28, 8, 7, 10),
                    new("freight", 65, 24, 8, 5, 9, 90),
                    new("pipe", 28, 57, 18, 10, 7),
                    new("vent", -102, 1, 6, 6, 10),
                    new("freight", -103, -28, 5, 6, 12),
                    new("vent", -25, -11, 9, 7, 14)
                };
            if (circuit == CourseDefinition.Foundry)
                return new Placement[]
                {
                    new("foundry-wall", 22, -51, 15, 13, 5),
                    new("foundry-wall", 48, -38, 17, 16, 8, 20),
                    new("foundry-wall", 57, 0, 8, 18, 24, 90),
                    new("foundry-wall", 29, 41, 17, 16, 7, 180),
                    new("pipe", -12, 41, 20, 11, 8, 180),
                    new("vent", -101, 0, 5, 7, 10),
                    new("pipe", -102, -28, 5, 5, 12, 90)
                };
            if (circuit == CourseDefinition.CyberGrave)
                return new Placement[]
                {
                    new("salvage", 20, -48, 13, 7, 5, 15),
                    new("scrap-bin", 58, -27, 9, 8, 8, 30),
                    new("salvage", 58, 9, 10, 6, 8, 160),
                    new("scrap-bin", 28, 37, 12, 8, 10),
                    new("salvage", -21, 35, 12, 7, 6, 135),
                    new("scrap-bin", -87, -2, 6, 7, 11),
                    new("salvage", -87, -23, 5, 6, 9, 90)
                };
            if (circuit == CourseDefinition.SpireHull)
                return new Placement[]
                {
                    new("spire", 19, -42, 8, 24, 8),
                    new("spire", 41, -38, 10, 32, 9),
                    new("spire", 57, 6, 8, 36, 10),
                    new("spire", 29, 37, 10, 27, 10),
                    new("spire", -13, 38, 11, 33, 11),
                    new("spire", -34, -4, 9, 20, 10),
                    new("spire", -101, -12, 7, 18, 8)
                };
            if (circuit == CourseDefinition.ApexDescent)
                return new Placement[]
                {
                    new("keep", 20, -45, 10, 19, 9),
                    new("castle-wall", 36, -44, 15, 12, 4),
                    new("keep", 58, 5, 9, 22, 9),
                    new("castle-wall", 24, 43, 18, 12, 5),
                    new("keep", -12, 42, 9, 18, 8),
                    new("castle-wall", -99, -6, 6, 11, 21, 90),
                    new("keep", -41, -11, 7, 14, 7)
                };
            return System.Array.Empty<Placement>();
        }
    }
}
