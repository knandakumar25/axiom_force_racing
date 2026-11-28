using System.Collections.Generic;
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

            /// <summary>Stores a footprint whose final base elevation comes from the terrain.</summary>
            public Placement(string model, float x, float z, float width, float height, float depth,
                float yaw = 0)
            {
                Model = model;
                Position = new Vector3(x, 0, z);
                Size = new Vector3(width, height, depth);
                Yaw = yaw;
            }
        }

        /// <summary>Builds the distinctive trackside structures that need shared footings or stacked parts.</summary>
        internal static void BuildSetPieces(CourseDefinition circuit, TrialCourse course, TrialEnvironment environment)
        {
            if (circuit == CourseDefinition.Rustrail)
            {
                Material ballast = course.Paint(0x776d5b);
                Material sleepers = course.Paint(0x41372e);
                Material steel = course.Paint(0x858984);
                course.Cube("Rail ballast", new(-91, -0.406f, -12), new(25, 0.84f, 3.5f), Quaternion.identity, ballast);
                for (int i = 0; i < 25; i++)
                    course.Cube("Sleeper", new(-103 + i, 0.031f, -12), new(0.25f, 0.026f, 2.1f),
                        Quaternion.identity, sleepers);
                for (int side = -1; side <= 1; side += 2)
                    course.Cube("Crossing rail", new(-91, 0.055f, -12 + side * 0.69f), new(25, 0.035f, 0.16f),
                        Quaternion.identity, steel);
            }
            else if (circuit == CourseDefinition.Foundry)
            {
                Material lane = course.Paint(0x655d52);
                Material marking = course.Paint(0xb1a18d);
                Vector3 center = new(-13, 0, 8);
                Vector3 size = new(5.8f, 0.3f, 32);
                center.y = environment.GroundHeight(center.x, center.z) + size.y * 0.25f;
                course.Cube("Service lane", center, size, Quaternion.identity, lane);
                for (int i = 0; i < 7; i++)
                    course.Cube("Service lane paint", center + new Vector3(0, 0.16f, i * 4 - 12),
                        new(0.12f, 0.02f, 2.1f), Quaternion.identity, marking);
            }
            else if (circuit == CourseDefinition.CyberGrave)
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector3 p = new(-100 - i * 2, 0, -7 - i * 14);
                    Vector3 baseSize = new(7, 2.5f, 6);
                    Quaternion rotation = Quaternion.Euler(0, 18 + i * 22, 0);
                    float baseY = environment.FootingHeight(p, baseSize, rotation);
                    p.y = baseY + baseSize.y * 0.5f;
                    environment.Place("freight", p, baseSize, rotation);
                    p.y = baseY + baseSize.y + 0.63f;
                    environment.Place("salvage", p, new(5, 1.4f, 4), rotation);
                }
            }
            else if (circuit == CourseDefinition.SpireHull)
            {
                Material concrete = course.Paint(0x858d80);
                Material planting = course.Paint(0x54674a);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = new(-4 + i * 4, 0, 29 + i * 6);
                    Vector3 deck = new(20 - i * 2, 0.65f + i * 0.55f, 4.9f);
                    p.y = environment.FootingHeight(p, deck, Quaternion.identity) + deck.y * 0.5f;
                    course.Cube("Planted terrace deck", p, deck, Quaternion.identity, concrete);
                    Vector3 bed = p + Vector3.up * (deck.y * 0.5f + 0.08f);
                    course.Cube("Planted terrace bed", bed, new(deck.x - 2, 0.16f, 3.7f), Quaternion.identity, planting);
                    for (int side = -1; side <= 1; side += 2)
                        environment.Place("street-tree", bed + new Vector3(side * (deck.x * 0.3f), 2.18f, 0),
                            new(3.2f, 4.2f, 3.2f), Quaternion.identity);
                }
            }
        }

        /// <summary>Returns the additional render-only scenery for a circuit.</summary>
        public static Placement[] Dressing(CourseDefinition circuit)
        {
            if (circuit == CourseDefinition.Docks)
                return new Placement[]
                {
                    new("pallet", 59, -45, 6, 1.4f, 5, 15),
                    new("freight", 27, -48, 9, 4, 4, 90),
                    new("freight", 13, 31, 12, 7, 7),
                    new("freight", 27, 32, 12, 6, 7),
                    new("freight", 41, 34, 12, 5, 7),
                    new("freight", -76, -10, 4.4f, 5.5f, 9),
                    new("freight", -76, -20, 4.4f, 4f, 9),
                    new("pallet", -76, -4, 3.1f, 1f, 3.8f)
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

        /// <summary>Places the planting bands and distant silhouettes that identify each circuit.</summary>
        internal static IEnumerable<Placement> Landscape(CourseDefinition circuit)
        {
            bool upland = circuit == CourseDefinition.ApexDescent;
            bool city = circuit == CourseDefinition.Tokyo || circuit == CourseDefinition.Viaduct ||
                circuit == CourseDefinition.SpireHull;
            bool industrial = circuit == CourseDefinition.Foundry || circuit == CourseDefinition.Rustrail ||
                circuit == CourseDefinition.CyberGrave;
            if (city)
            {
                for (int i = 0; i < 9; i++)
                {
                    yield return new("street-tree", -48 + i * 12, circuit == CourseDefinition.Tokyo ? 30 : 40,
                        3.5f, 4.2f + i % 3, 3.5f, i * 21);
                    yield return new("sidewalk", -48 + i * 12, circuit == CourseDefinition.Tokyo ? 35 : 44, 11.8f, 0.25f, 3);
                    yield return new("gutter", -48 + i * 12, circuit == CourseDefinition.Tokyo ? 37 : 46, 11.8f, 0.22f, 0.65f);
                    yield return new("office", -125 + i * 29, 135 + i % 3 * 12, 19, 19 + i % 4 * 9, 18, i % 2 * 11);
                }
            }
            if (industrial)
                for (int i = 0; i < 8; i++)
                    yield return new(circuit == CourseDefinition.CyberGrave ? "salvage" : "foundry-wall",
                        -120 + 34 * i, -108 - 13 * (i % 2), 24, 13 + 5 * (i % 3), 8, i % 3 * 13);
            if (upland)
            {
                for (int i = 0; i < 7; i++)
                {
                    yield return new("retaining-wall", -108, -49 + i * 14, 14.3f, 4.2f, 3.4f, 90);
                    yield return new("mound", -145 - i % 2 * 15, -76 + i * 29, 42, 9 + i % 3 * 5, 34, i * 17);
                    yield return new("upland-tree", -116 - i % 3 * 5, -48 + i * 15, 4.8f, 8.5f, 4.8f, i * 33);
                    yield return new("pine", -128 + i % 2 * 5, -55 + i * 16, 3.6f, 7f, 3.6f, i * 21);
                    yield return new("grass", -103 - i % 3 * 2, -50 + i * 15, 3.1f, 0.8f, 2.7f, i * 23);
                    yield return new("upland-tree", -32 + i * 11, 26 + i % 2 * 5, 4.5f, 7.5f, 4.5f, i * 16);
                }
            }
            if (circuit == CourseDefinition.Docks)
            {
                for (int i = 0; i < 5; i++)
                    yield return new("road-patch", 17 + i * 12, 21, 8.5f, 0.12f, 5, i * 10);
                yield break;
            }
            var random = new System.Random(327);
            for (int i = 0; i < 68; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float radius = 30 + (float)random.NextDouble() * (upland ? 155 : 100);
                float x = Mathf.Cos(angle) * radius, z = Mathf.Sin(angle) * radius;
                float size = 1 + (float)random.NextDouble() * 2f;
                // The market stalls and their awnings occupy this strip beside the road.
                if (circuit == CourseDefinition.Tokyo && x is > 42 and < 52 && z is > -33 and < 14)
                    continue;
                string key = (i % 4) switch
                {
                    0 => upland ? "upland-tree" : city ? "street-tree" : "scrub",
                    1 => "grass",
                    2 => circuit == CourseDefinition.CyberGrave || upland ? "rock" : "scrub",
                    _ => "scrub"
                };
                yield return new(key, x, z, size, key is "upland-tree" or "street-tree" ? size * 2.6f : size * 0.65f,
                    size, angle * Mathf.Rad2Deg);
            }
        }
    }
}
