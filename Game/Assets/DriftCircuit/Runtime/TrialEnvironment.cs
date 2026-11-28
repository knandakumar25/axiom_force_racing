using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriftCircuit
{
    /// <summary>Owns a circuit's palette, render-only ground, road finish, and race lighting.</summary>
    internal sealed class TrialEnvironment
    {
        private readonly CourseDefinition circuit;
        private readonly TrialCourse course;
        private readonly TrialVisualCatalog catalog;
        private readonly List<Object> owned;
        private readonly Look look;
        private readonly Dictionary<string, Material> fallback = new();
        private List<Station> main, branch;
        private List<float> terrainAxis;
        private Vector3[,] terrainPoints;
        private SceneLighting previous;

        /// <summary>Groups the horizon, light, haze, and ground colors authored for a circuit.</summary>
        private readonly struct Look
        {
            public readonly uint Sky, Horizon, Sun, Ground;
            public readonly float Pitch, Yaw, Intensity, Haze, Relief;
            public readonly bool Concrete;

            public Look(uint sky, uint horizon, uint sun, uint ground, float pitch, float yaw,
                float intensity, float haze, float relief, bool concrete = false)
            {
                Sky = sky; Horizon = horizon; Sun = sun; Ground = ground;
                Pitch = pitch; Yaw = yaw; Intensity = intensity; Haze = haze; Relief = relief;
                Concrete = concrete;
            }
        }

        /// <summary>Captures the handful of scene settings temporarily overridden by a race.</summary>
        private sealed class SceneLighting
        {
            public readonly AmbientMode Ambient;
            public readonly Color Sky, Equator, Ground, FogColor;
            public readonly bool Fog;
            public readonly FogMode FogMode;
            public readonly float Density;
            public readonly Light Sun;
            public readonly Material Skybox;

            public SceneLighting()
            {
                Ambient = RenderSettings.ambientMode;
                Sky = RenderSettings.ambientSkyColor;
                Equator = RenderSettings.ambientEquatorColor;
                Ground = RenderSettings.ambientGroundColor;
                Skybox = RenderSettings.skybox;
                Fog = RenderSettings.fog;
                FogColor = RenderSettings.fogColor;
                FogMode = RenderSettings.fogMode;
                Density = RenderSettings.fogDensity;
                Sun = RenderSettings.sun;
            }

            /// <summary>Returns the menu or next scene to its original lighting settings.</summary>
            public void Restore()
            {
                RenderSettings.ambientMode = Ambient;
                RenderSettings.ambientSkyColor = Sky;
                RenderSettings.ambientEquatorColor = Equator;
                RenderSettings.ambientGroundColor = Ground;
                RenderSettings.skybox = Skybox;
                RenderSettings.fog = Fog;
                RenderSettings.fogColor = FogColor;
                RenderSettings.fogMode = FogMode;
                RenderSettings.fogDensity = Density;
                RenderSettings.sun = Sun;
            }
        }

        public TrialEnvironment(CourseDefinition circuit, TrialCourse course, TrialVisualCatalog catalog, List<Object> owned)
        {
            this.circuit = circuit;
            this.course = course;
            this.catalog = catalog;
            this.owned = owned;
            look = circuit.Id switch
            {
                "little-tokyo-skyport" => new(0x408aca, 0xd1e1df, 0xffedca, 0x657267, 48, -36, 1.65f, 0.004f, 15),
                "concrete-wards-viaduct" => new(0x818fa0, 0xc9d1ce, 0xf1f5ff, 0x696f61, 61, -20, 0.95f, 0.006f, 17, true),
                "stratos-docks" => new(0x5899b4, 0xc9dedb, 0xffefd8, 0x6b766e, 48, 42, 1.43f, 0.0065f, 9, true),
                "rustrail-junction" => new(0x7388a4, 0xe5bd8d, 0xffcd92, 0x827560, 28, -70, 1.55f, 0.0048f, 17),
                "foundry-forge" => new(0x434767, 0xd09068, 0xffad73, 0x61584f, 19, -55, 1.45f, 0.0065f, 15, true),
                "cyber-grave-circuit" => new(0x303a65, 0x969db3, 0xb3c7ff, 0x5e5864, 18, 37, 1.15f, 0.005f, 25),
                "spire-outer-hull" => new(0x4684b9, 0xd1dfdf, 0xffdba9, 0x627c70, 30, 55, 1.5f, 0.0046f, 16, true),
                _ => new(0x438fc1, 0xc9e1dc, 0xffebbb, 0x658450, 53, -45, 1.65f, 0.0039f, 43)
            };
        }

        /// <summary>Shares one sky, sun, and fog profile across the race cameras.</summary>
        internal void ApplyLighting()
        {
            previous = new SceneLighting();
            var sun = new GameObject("Circuit light");
            sun.transform.SetParent(course.Root, false);
            sun.transform.rotation = Quaternion.Euler(look.Pitch, look.Yaw, 0);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Rgb(look.Sun);
            light.intensity = look.Intensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = circuit == CourseDefinition.Viaduct ? 0.35f : 0.78f;

            Material sky = Resources.Load<Shader>("CircuitSky") is Shader shader ? new Material(shader) : null;
            if (sky)
            {
                owned.Add(sky);
                sky.SetColor("_Zenith", Rgb(look.Sky));
                sky.SetColor("_Horizon", Rgb(look.Horizon));
                sky.SetColor("_SunColor", Rgb(look.Sun));
                Vector3 direction = -sun.transform.forward;
                sky.SetVector("_SunDirection", direction);
                sky.SetFloat("_SunStrength", circuit == CourseDefinition.Viaduct ? 0f : 1f);
                RenderSettings.skybox = sky;
            }
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(Rgb(look.Sky), Color.white, 0.48f);
            RenderSettings.ambientEquatorColor = Color.Lerp(Rgb(look.Horizon), Color.gray, 0.52f);
            RenderSettings.ambientGroundColor = Rgb(look.Ground) * 0.55f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Rgb(look.Horizon);
            RenderSettings.fogDensity = look.Haze;
        }

        /// <summary>Restores render settings before the race materials are released.</summary>
        internal void RestoreLighting()
        {
            previous?.Restore();
            previous = null;
        }

        /// <summary>Builds tileable aggregate with enough contrast to read as the kart moves.</summary>
        internal Material RoadSurface(bool alternate)
        {
            uint color = look.Concrete ? (alternate ? 0x656661u : 0x828883u) :
                alternate ? 0x545356u : 0x383d43u;
            Material material = course.Paint(color);
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, true)
            {
                name = look.Concrete ? "Circuit concrete aggregate" : "Circuit asphalt aggregate",
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8
            };
            var pixels = new Color32[128 * 128];
            var random = new System.Random(713);
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                double variation = random.NextDouble() * (look.Concrete ? 33 : 61);
                float aggregate = Mathf.Sin(x * Mathf.PI / 16) * Mathf.Sin(y * Mathf.PI / 32);
                // Concrete expansion joints meet at the texture boundary.
                int joint = look.Concrete && (y < 2 || x < 1) ? 25 : 0;
                byte shade = (byte)(224 - (int)variation - joint + 6 * aggregate);
                pixels[y * 128 + x] = new Color32(shade, shade, shade, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            owned.Add(texture);
            material.mainTexture = texture;
            return material;
        }

        /// <summary>Builds the visual ribbon with an extra UV seam vertex pair on closed loops.</summary>
        internal void BuildRoadView(GameObject road, Vector3[] edges, List<Station> stations, bool[] barrierEdges,
            bool closed, Material surface, Material routeColor, Material rail)
        {
            var trim = new RoadMesh();
            int count = stations.Count;
            int segments = closed ? count : count - 1;
            int rejoin = closed ? segments : TrialCourse.ClosestStation(stations, circuit.Shortcut[circuit.ShortcutRejoin].Center);
            int split = closed ? TrialCourse.ClosestStation(stations, circuit.Shortcut[0].Center) : 0;
            bool descent = closed && circuit == CourseDefinition.ApexDescent;
            int outerMerge = descent ? TrialCourse.ClosestStation(stations, circuit.Shortcut[circuit.ShortcutRejoin].Center) : -1;
            var roadVertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[roadVertices.Length];
            var faces = new int[segments * 6];
            System.Array.Copy(edges, roadVertices, edges.Length);
            if (closed)
            {
                roadVertices[^2] = edges[0];
                roadVertices[^1] = edges[1];
            }
            float distance = 0;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % count;
                Vector3 a = edges[i * 2], b = edges[i * 2 + 1], c = edges[next * 2], d = edges[next * 2 + 1];
                float end = distance + Vector3.Distance(stations[i].Center, stations[next].Center);
                int v = i * 2, face = i * 6;
                uv[v] = new(0, distance / 4);
                uv[v + 1] = new(stations[i].Width / 2f, distance / 4);
                faces[face] = v; faces[face + 1] = v + 2; faces[face + 2] = v + 1;
                faces[face + 3] = v + 1; faces[face + 4] = v + 2; faces[face + 5] = v + 3;
                Vector3 acrossA = (b - a).normalized, acrossB = (d - c).normalized;
                Vector3 up = Vector3.Cross(c - a, acrossA).normalized;
                Vector3 lift = up * 0.046f;
                for (int side = 0; side < 2; side++)
                {
                    Vector3 p = side == 0 ? a : b, q = side == 0 ? c : d;
                    if (!barrierEdges[v + side]) continue;
                    Vector3 middle = (p + q) * 0.5f;
                    Quaternion rotation = Quaternion.LookRotation(q - p);
                    float length = Vector3.Distance(p, q);
                    trim.Box(middle + Vector3.up * 0.38f, rotation, new(0.45f, 0.76f, length + 0.16f), 5);
                    trim.Box(middle + Vector3.up * 0.78f, rotation, new(0.12f, 0.06f, length * 0.78f), 4);
                    Vector3 insideA = side == 0 ? acrossA : -acrossA;
                    Vector3 insideB = side == 0 ? acrossB : -acrossB;
                    trim.Quad(p - insideA * 1.4f - lift, p + insideA * 0.65f - lift,
                        q - insideB * 1.4f - lift, q + insideB * 0.65f - lift, 0, reverse: side == 1);
                    trim.Quad(p + insideA * 0.29f + lift, p + insideA * 0.65f + lift,
                        q + insideB * 0.29f + lift, q + insideB * 0.65f + lift, (i / 2) % 2 == 0 ? 1 : 2, reverse: side == 1);
                    // The skirt meets the ground beneath elevated and banked road edges.
                    Vector3 outsideP = p - insideA * 1.39f - lift, outsideQ = q - insideB * 1.39f - lift;
                    Vector3 baseP = outsideP, baseQ = outsideQ;
                    baseP.y = Mathf.Min(outsideP.y - 0.12f, GroundHeight(outsideP.x, outsideP.z) - 0.12f);
                    baseQ.y = Mathf.Min(outsideQ.y - 0.12f, GroundHeight(outsideQ.x, outsideQ.z) - 0.12f);
                    if (side == 0) trim.Quad(baseP, outsideP, baseQ, outsideQ, 3);
                    else trim.Quad(outsideP, baseP, outsideQ, baseQ, 3);
                }

                Vector3 center = (a + b) * 0.5f, nextCenter = (c + d) * 0.5f;
                Vector3 tangent = (nextCenter - center).normalized;
                if ((!closed && (i == 4 || i == rejoin - 3)) || (closed && i == (split + segments - 8) % segments) ||
                    (descent && (i == split + 8 || i == outerMerge - 4)))
                    trim.Arrow(center + lift, acrossA, tangent, 4);
                if (descent && i >= split - 5 && i <= split - 2)
                    trim.Quad(center - acrossA * 4.6f + lift, center + acrossA * 4.6f + lift,
                        center - acrossA * 4.6f + lift + tangent * 0.22f,
                        center + acrossA * 4.6f + lift + tangent * 0.22f, 4);
                distance = end;
            }
            uv[^2] = new(0, distance / 4);
            uv[^1] = new(stations[closed ? 0 : count - 1].Width / 2f, distance / 4);
            var mesh = new Mesh { name = road.name + " surface", vertices = roadVertices, triangles = faces, uv = uv };
            mesh.RecalculateNormals();
            if (closed)
            {
                Vector3[] normals = mesh.normals;
                normals[0] = normals[^2] = (normals[0] + normals[^2]).normalized;
                normals[1] = normals[^1] = (normals[1] + normals[^1]).normalized;
                mesh.normals = normals;
            }
            owned.Add(mesh);
            road.AddComponent<MeshFilter>().sharedMesh = mesh;
            road.AddComponent<MeshRenderer>().sharedMaterial = surface;
            Publish(Child(road.transform, "Shoulders, curbs, rails and route paint"), trim,
                course.Paint(look.Concrete ? 0x9b9b88u : 0x84776au), course.Paint(0xb9b9ae),
                course.Paint(closed ? 0x4f7e85u : 0x9d5d31u), course.Paint(0x5f6765), routeColor, rail);
        }

        /// <summary>Builds a low-poly landscape or a quay surrounded by a single water surface.</summary>
        internal void BuildGround(List<Station> main, List<Station> branch)
        {
            this.main = main;
            this.branch = branch;
            if (circuit == CourseDefinition.Docks)
                BuildHarbor();
            else
                BuildTerrain();
            DressLandscape();
            TrialScenery.BuildSetPieces(circuit, course, this);
        }

        /// <summary>Measures the actual ground triangle or quay elevation at a prop's position.</summary>
        internal float GroundHeight(float x, float z)
        {
            if (circuit == CourseDefinition.Docks)
                return x is >= -97 and <= 101 && z is >= -76 and <= 76 ? -0.35f : -2.7f;
            int ix = terrainAxis.BinarySearch(x), iz = terrainAxis.BinarySearch(z);
            ix = Mathf.Clamp(ix >= 0 ? ix : ~ix - 1, 0, terrainAxis.Count - 2);
            iz = Mathf.Clamp(iz >= 0 ? iz : ~iz - 1, 0, terrainAxis.Count - 2);
            float u = Mathf.InverseLerp(terrainAxis[ix], terrainAxis[ix + 1], x);
            float v = Mathf.InverseLerp(terrainAxis[iz], terrainAxis[iz + 1], z);
            float a = terrainPoints[iz, ix].y, b = terrainPoints[iz, ix + 1].y;
            float c = terrainPoints[iz + 1, ix].y, d = terrainPoints[iz + 1, ix + 1].y;
            return u + v <= 1 ? a + (b - a) * u + (c - a) * v :
                d + (c - d) * (1 - u) + (b - d) * (1 - v);
        }

        /// <summary>Computes terrain height while leaving banked edges and service lanes exposed.</summary>
        private float TerrainHeight(float x, float z)
        {
            float distance = Mathf.Max(Mathf.Abs(x) * 0.8f, Mathf.Abs(z));
            float relief = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(62, 166, distance));
            float noise = Mathf.PerlinNoise(x * 0.018f + 7f, z * 0.017f + 19f);
            float height = -0.48f + relief * (noise * look.Relief - 5f);
            height = Mathf.Min(height, Mathf.Min(RoadClearance(x, z, main, true), RoadClearance(x, z, branch, false)));
            return height;
        }

        /// <summary>Caps terrain near sampled road segments, including their downhill bank edges.</summary>
        private static float RoadClearance(float x, float z, List<Station> stations, bool closed)
        {
            var p = new Vector2(x, z);
            float ceiling = float.PositiveInfinity;
            int count = closed ? stations.Count : stations.Count - 1;
            for (int i = 0; i < count; i++)
            {
                Station a = stations[i], b = stations[(i + 1) % stations.Count];
                Vector2 from = new(a.Center.x, a.Center.z), to = new(b.Center.x, b.Center.z);
                Vector2 segment = to - from;
                float t = Mathf.Clamp01(Vector2.Dot(p - from, segment) / segment.sqrMagnitude);
                float distanceSquared = (p - from - segment * t).sqrMagnitude;
                if (distanceSquared > 22 * 22) continue;
                float d = Mathf.Sqrt(distanceSquared);
                Station station = Station.Lerp(a, b, t);
                float bankDrop = Mathf.Abs(Mathf.Sin(station.Bank * Mathf.Deg2Rad)) * station.Width;
                ceiling = Mathf.Min(ceiling, station.Center.y - bankDrop - 0.7f +
                    Mathf.Max(0, d - station.Width - 5f) * 0.72f);
            }
            return ceiling;
        }

        /// <summary>Uses a denser terrain grid near the road and larger facets at the horizon.</summary>
        private void BuildTerrain()
        {
            var axis = terrainAxis = new List<float> { -650, -490, -350, -250, -180, -140, -120 };
            for (int x = -116; x <= 116; x += 4) axis.Add(x);
            axis.AddRange(new float[] { 120, 140, 180, 250, 350, 490, 650 });
            int count = axis.Count;
            var points = terrainPoints = new Vector3[count, count];
            for (int z = 0; z < count; z++)
            for (int x = 0; x < count; x++)
                points[z, x] = new Vector3(axis[x], TerrainHeight(axis[x], axis[z]), axis[z]);
            var ground = new RoadMesh();
            var materials = new Material[5];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = course.Paint(look.Ground);
                materials[i].color *= 0.8f + i * 0.088f;
            }
            for (int z = 0; z < count - 1; z++)
            for (int x = 0; x < count - 1; x++)
            {
                Vector3 p = points[z, x];
                int shade = Mathf.Clamp((int)(Mathf.PerlinNoise(p.x * 0.085f + 12, p.z * 0.083f + 8) * 6), 0, 4);
                ground.Quad(p, points[z, x + 1], points[z + 1, x], points[z + 1, x + 1], shade);
            }
            Publish(Child(course.Root, "Terrain to horizon"), ground, materials);
        }

        /// <summary>Builds straight harbor walls around the deck and animated water to the horizon.</summary>
        private void BuildHarbor()
        {
            Material quay = course.Paint(0x7b8175);
            course.Cube("Harbor quay foundation", new(2, -1.55f, 0), new(198, 2.4f, 152), Quaternion.identity, quay);
            for (int i = -6; i <= 6; i++)
                Place("quay", new Vector3(-97.2f, -1.35f, i * 11.5f), new(1.3f, 2.5f, 11.55f), Quaternion.identity);

            Shader shader = Resources.Load<Shader>("HarborWater");
            Material water = shader ? new Material(shader) : course.Paint(0x447d88);
            if (shader) owned.Add(water);
            var surface = new RoadMesh();
            surface.Quad(new(-650, -2.15f, -650), new(650, -2.15f, -650),
                new(-650, -2.15f, 650), new(650, -2.15f, 650), 0);
            Publish(Child(course.Root, "Harbor water"), surface, water);
        }

        /// <summary>Places distant clouds and circuit-specific vegetation, masonry, and street furniture.</summary>
        private void DressLandscape()
        {
            if (catalog)
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4 + 0.3f;
                    Vector3 center = new(Mathf.Cos(angle) * 245, 36 + (i % 3) * 11, Mathf.Sin(angle) * 245);
                    if (catalog.TryPlace("cloud", course.Root, center, new(74 + i * 3, 6 + i % 3, 28),
                        Quaternion.Euler(0, i * 29, 0), "Cloud bank", out GameObject cloud))
                        foreach (Renderer renderer in cloud.GetComponentsInChildren<Renderer>())
                        {
                            renderer.shadowCastingMode = ShadowCastingMode.Off;
                            renderer.receiveShadows = false;
                        }
                }

            foreach (TrialScenery.Placement feature in TrialScenery.Landscape(circuit))
            {
                Quaternion rotation = Quaternion.Euler(0, feature.Yaw, 0);
                if (!TrialCourse.ClearsRoute(feature, rotation, main, true) ||
                    !TrialCourse.ClearsRoute(feature, rotation, branch, false))
                    continue;
                Vector3 p = feature.Position;
                p.y = FootingHeight(p, feature.Size, rotation) + feature.Size.y * 0.5f;
                Place(feature.Model, p, feature.Size, rotation);
            }
        }

        /// <summary>Seats large props into the slope so their downhill corners do not float.</summary>
        internal float FootingHeight(Vector3 center, Vector3 size, Quaternion rotation)
        {
            float height = GroundHeight(center.x, center.z);
            if (size.y < 0.5f) return height - 0.015f;
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + rotation * new Vector3(x * size.x * 0.44f, 0, z * size.z * 0.44f);
                height = Mathf.Min(height, GroundHeight(corner.x, corner.z));
            }
            return height - 0.06f;
        }

        /// <summary>Lights the service lane beneath its existing roof without changing physics.</summary>
        internal void LightServiceTunnel()
        {
            Material fixture = course.Paint(0xffd795, true);
            for (int n = 0; n < 3; n++)
            {
                Vector3 p = new(-53.5f + n * 0.8f, 4.21f, -1 - n * 5f);
                GameObject panel = course.Cube("Service tunnel fixture", p, new(4.1f, 0.08f, 0.22f),
                    Quaternion.Euler(0, -10, 0), fixture);
                var bulb = Child(course.Root, "Service tunnel light").AddComponent<Light>();
                bulb.transform.localPosition = panel.transform.localPosition + Vector3.down * 0.16f;
                bulb.type = LightType.Point;
                bulb.color = Rgb(0xffd795);
                bulb.intensity = 2.2f;
                bulb.range = 8f;
            }
        }

        /// <summary>Places the local render-only model or uses simple pieces made from project materials.</summary>
        internal void Place(string key, Vector3 center, Vector3 size, Quaternion rotation)
        {
            if (catalog && catalog.TryPlace(key, course.Root, center, size, rotation, key + " scenery", out GameObject visual))
            {
                if (key is "grass" or "scrub" or "road-patch" || center.sqrMagnitude > 125 * 125)
                    foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                return;
            }

            if (!fallback.TryGetValue(key, out Material material))
            {
                uint color = key switch
                {
                    "street-tree" or "pine" or "upland-tree" or "grass" or "scrub" or "mound" => 0x536b43,
                    "rock" or "retaining-wall" or "keep" or "castle-wall" => 0x807e73,
                    "freight" or "pipe" or "salvage" => 0x926851,
                    "sidewalk" or "gutter" or "quay" => 0x96998e,
                    _ => 0x68767d
                };
                fallback[key] = material = course.Paint(color);
            }
            if (key is "street-tree" or "pine" or "upland-tree")
            {
                if (!fallback.TryGetValue("trunk", out Material trunk))
                    fallback["trunk"] = trunk = course.Paint(0x5f5141);
                course.Cube("Tree trunk", center - Vector3.up * size.y * 0.26f,
                    new(size.x * 0.14f, size.y * 0.48f, size.z * 0.14f), rotation, trunk);
                center.y += size.y * 0.17f;
                size.y *= 0.66f;
            }
            if (key is "street-tree" or "pine" or "upland-tree" or "scrub" or "mound" or "rock" or "grass")
            {
                var facets = new RoadMesh();
                Vector3 top = center + Vector3.up * size.y * 0.5f;
                float ringHeight = center.y - size.y * 0.5f;
                for (int i = 0; i < 7; i++)
                {
                    float angle = i * Mathf.PI * 2 / 7;
                    float next = (i + 1) * Mathf.PI * 2 / 7;
                    Vector3 a = new(center.x + Mathf.Cos(angle) * size.x / 2, ringHeight, center.z + Mathf.Sin(angle) * size.z / 2);
                    Vector3 b = new(center.x + Mathf.Cos(next) * size.x / 2, ringHeight, center.z + Mathf.Sin(next) * size.z / 2);
                    facets.Triangle(a, top, b, 0);
                }
                Publish(Child(course.Root, key + " scenery"), facets, material);
            }
            else course.Cube(key + " scenery", center, size, rotation, material);
        }

        /// <summary>Publishes a mesh owned by this race without adding physics components.</summary>
        private void Publish(GameObject obj, RoadMesh source, params Material[] materials)
        {
            Mesh mesh = source.Finish(obj.name, materials.Length);
            owned.Add(mesh);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        /// <summary>Creates a render object's transform in circuit coordinates.</summary>
        private static GameObject Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        /// <summary>Converts an authored RGB value into a Unity color.</summary>
        private static Color Rgb(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        /// <summary>Batches scenery faces by material and keeps repeatable road UVs.</summary>
        private sealed class RoadMesh
        {
            private readonly List<Vector3> vertices = new();
            private readonly List<Vector2> uv = new();
            private readonly List<int>[] faces = { new(), new(), new(), new(), new(), new() };

            /// <summary>Adds a two-triangle face with optional measured UV extents.</summary>
            public void Ribbon(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int material,
                float widthA, float widthB, float start, float end)
            {
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uv.Add(new(0, start)); uv.Add(new(widthA, start)); uv.Add(new(0, end)); uv.Add(new(widthB, end));
                faces[material].Add(i); faces[material].Add(i + 2); faces[material].Add(i + 1);
                faces[material].Add(i + 1); faces[material].Add(i + 2); faces[material].Add(i + 3);
            }

            /// <summary>Adds an untextured two-triangle face.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int material, bool reverse = false)
            {
                if (reverse) Ribbon(b, a, d, c, material, 1, 1, 0, 1);
                else Ribbon(a, b, c, d, material, 1, 1, 0, 1);
            }

            /// <summary>Adds a single flat facet.</summary>
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, int material)
            {
                faces[material].Add(vertices.Count); faces[material].Add(vertices.Count + 1);
                faces[material].Add(vertices.Count + 2);
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.one);
            }

            /// <summary>Adds the six faces of a fitted rail or reflector in circuit coordinates.</summary>
            public void Box(Vector3 center, Quaternion rotation, Vector3 size, int material)
            {
                Vector3 r = rotation * Vector3.right * (size.x * 0.5f);
                Vector3 u = rotation * Vector3.up * (size.y * 0.5f);
                Vector3 f = rotation * Vector3.forward * (size.z * 0.5f);
                Vector3 a = center - r - u - f, b = center + r - u - f;
                Vector3 c = center - r + u - f, d = center + r + u - f;
                Vector3 e = center - r - u + f, g = center - r + u + f;
                Vector3 h = center + r + u + f, j = center + r - u + f;
                Quad(a, b, c, d, material);
                Quad(j, e, h, g, material);
                Quad(e, a, g, c, material);
                Quad(b, j, d, h, material);
                Quad(c, d, g, h, material);
                Quad(e, j, a, b, material);
            }

            /// <summary>Draws a direction arrow against the sampled road tangent and bank.</summary>
            public void Arrow(Vector3 p, Vector3 right, Vector3 forward, int material)
            {
                Quad(p - right * 0.15f - forward * 1.6f, p + right * 0.15f - forward * 1.6f,
                    p - right * 0.15f, p + right * 0.15f, material);
                Triangle(p - right * 0.7f, p + forward * 1.25f, p + right * 0.7f, material);
            }

            /// <summary>Builds a Unity mesh with one submesh per material.</summary>
            public Mesh Finish(string name, int materialCount)
            {
                var mesh = new Mesh { name = name, subMeshCount = materialCount };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv);
                for (int i = 0; i < materialCount; i++) mesh.SetTriangles(faces[i], i);
                mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}
