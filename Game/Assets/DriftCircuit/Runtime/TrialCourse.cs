using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriftCircuit
{
    /// <summary>Builds the circuit, render-only surroundings, and visible race cues.</summary>
    internal sealed class TrialCourse
    {
        private readonly CourseDefinition definition;
        private readonly TrialVisualCatalog visuals;
        private readonly Transform root;
        private readonly List<Object> owned;
        private readonly Material road, shortcut, rail, cyan, amber, white, dark;
        private readonly Material[] gateMaterials;
        private readonly TrialEnvironment environment;
        private readonly List<GameObject> beaconViews = new();
        private RailCrossing crossing;

        /// <summary>Parents the crossing labels alongside the generated road and signs.</summary>
        internal Transform Root => root;
        internal RailCrossing Crossing => crossing;
        internal TrialEnvironment Environment => environment;

        /// <summary>Creates shared materials and records them for scene teardown.</summary>
        public TrialCourse(CourseDefinition definition, Transform root, List<Object> owned,
            TrialVisualCatalog visuals = null)
        {
            this.definition = definition;
            this.root = root;
            this.owned = owned;
            this.visuals = visuals;
            environment = new TrialEnvironment(definition, this, visuals, owned);
            road = environment.RoadSurface(false);
            shortcut = environment.RoadSurface(true);
            rail = Paint(0x203446);
            dark = Paint(0x111b2a);
            cyan = Paint(0x5debdc, true);
            amber = Paint(0xffbc68, true);
            white = Paint(0xf0fcff, true);
            gateMaterials = new Material[definition.Gates.Length];
        }

        /// <summary>Builds driveable surfaces, barriers, gate markers, and primitive landmarks.</summary>
        public void Build()
        {
            List<Station> main = Sample(definition.Main, true);
            List<Station> branch = Sample(definition.Shortcut, false);
            environment.BuildGround(main, branch);
            BuildRoad(definition.MainName, main, branch, true, road, cyan);
            BuildRoad(definition.ShortcutName, branch, main, false, shortcut, amber);
            for (int i = 2; i < branch.Count - 2; i += 4)
            {
                Vector3 heading = branch[i + 1].Center - branch[i - 1].Center;
                Cube("Shortcut guide", branch[i].Center + Vector3.up * 0.033f,
                    new(0.19f, 0.018f, 1.6f), Quaternion.LookRotation(heading), amber);
                GameObject beacon = Cube("Route beacon", branch[i].Center + Vector3.up * 1.45f,
                    new(0.55f, 0.55f, 0.55f), Quaternion.LookRotation(heading), cyan, false);
                beacon.SetActive(false);
                beaconViews.Add(beacon);
            }
            for (int i = 0; i < definition.Gates.Length; i++)
                BuildGate(i);

            // The first landmark was the render-only foundation. The terrain now covers it.
            for (int i = 1; i < definition.Landmarks.Length; i++)
            {
                var landmark = definition.Landmarks[i];
                if (landmark.Name == "Dock deck") continue; // The new quay already provides this deck.
                Vector3 position = landmark.Position, size = landmark.Scale;
                float ground = environment.GroundHeight(position.x, position.z);
                if (landmark.Name is "Market stall" or "Market awning" or "Loading container" or "Quay bollard")
                    position.y += ground;
                // Keep the beam attachment at its authored height and extend the footing into the terrain.
                if (landmark.Name.EndsWith("support", System.StringComparison.Ordinal) ||
                    landmark.Name is "Gantry west leg" or "Gantry east leg" or "Skyport terminal")
                {
                    float extension = position.y - size.y * 0.5f - ground + 0.1f;
                    size.y += extension;
                    position.y -= extension * 0.5f;
                }
                Material material = landmark.Material switch
                {
                    CourseDefinition.Surface.Cyan => cyan,
                    CourseDefinition.Surface.Amber => amber,
                    CourseDefinition.Surface.Dark => dark,
                    _ => rail
                };
                Quaternion rotation = Quaternion.Euler(0, landmark.Yaw, 0);
                string model = TrialScenery.ModelFor(landmark.Name);
                // Shop fronts face the road west of the market. The quarter turn keeps the authored footprint.
                bool market = model == "shop" || model == "awning";
                Quaternion modelRotation = market ? rotation * Quaternion.Euler(0, 90, 0) : rotation;
                Vector3 modelSize = market ? new Vector3(size.z, size.y, size.x) : size;
                bool replaced = model != null && visuals && visuals.TryPlace(model, root, position,
                    modelSize, modelRotation, landmark.Name + " model", out _);
                if (!replaced || landmark.Solid)
                {
                    GameObject block = Cube(landmark.Name, position, size, rotation, material, landmark.Solid);
                    block.GetComponent<Renderer>().enabled = !replaced;
                }
            }
            if (definition == CourseDefinition.Viaduct)
                environment.LightServiceTunnel();
            foreach (TrialScenery.Placement placement in TrialScenery.Dressing(definition))
            {
                Quaternion rotation = Quaternion.Euler(0, placement.Yaw, 0);
                if (ClearsRoute(placement, rotation, main, true) && ClearsRoute(placement, rotation, branch, false))
                    PlaceDressing(placement, rotation);
            }
            if (definition == CourseDefinition.Rustrail || definition == CourseDefinition.Foundry)
                crossing = new RailCrossing(this, dark, rail);
        }

        /// <summary>Places a scenery prop or stacks separate facade modules within one cleared footprint.</summary>
        private void PlaceDressing(TrialScenery.Placement placement, Quaternion rotation)
        {
            Vector3 position = placement.Position;
            position.y = environment.FootingHeight(position, placement.Size, rotation) + placement.Size.y * 0.5f;
            if (placement.Model != "spire")
            {
                environment.Place(placement.Model, position, placement.Size, rotation);
                return;
            }

            int floors = Mathf.RoundToInt(placement.Size.y / 4f) - 2;
            float step = placement.Size.y / (floors + 2);
            Vector3 size = new(placement.Size.x, step, placement.Size.z);
            Vector3 center = position - Vector3.up * (placement.Size.y - step) * 0.5f;
            for (int level = 0; level < floors + 2; level++)
            {
                string key = level == 0 ? "spire-base" : level == floors + 1 ? "spire-roof" : "spire-floor";
                environment.Place(key, center + Vector3.up * (step * level), size, rotation);
            }
        }

        /// <summary>Checks the rotated footprint against the road and its edge markers, including elevated routes.</summary>
        internal static bool ClearsRoute(TrialScenery.Placement placement, Quaternion rotation,
            List<Station> route, bool closed)
        {
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            int segments = closed ? route.Count : route.Count - 1;
            Vector2 center = new(placement.Position.x, placement.Position.z);
            for (int i = 0; i < segments; i++)
            {
                Station first = route[i], next = route[(i + 1) % route.Count];
                Vector2 start = new(first.Center.x, first.Center.z);
                Vector2 segment = new(next.Center.x - start.x, next.Center.z - start.y);
                float t = segment.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(center - start, segment) / segment.sqrMagnitude) : 0;
                Vector2 offset = center - (start + t * segment);
                float distance = offset.magnitude;
                if (distance < 0.001f) return false;
                Vector3 direction = new(offset.x / distance, 0, offset.y / distance);
                float radius = (placement.Size.x * Mathf.Abs(Vector3.Dot(direction, right)) +
                    placement.Size.z * Mathf.Abs(Vector3.Dot(direction, forward))) * 0.5f;
                if (distance < radius + Mathf.Lerp(first.Width, next.Width, t) + 1f) return false;
            }
            return true;
        }

        /// <summary>Applies the crossing phase when this circuit has a timed shortcut.</summary>
        internal void UpdateCrossing(double elapsed, TrialKart kart, TrialKart rival = null)
        {
            if (crossing != null)
                crossing.Update(elapsed, kart.PhysicsBounds, rival?.PhysicsBounds);
        }

        /// <summary>Toggles Route Beacon shortcut guidance markers along the branch.</summary>
        internal void ShowRouteBeacon(bool active)
        {
            foreach (GameObject beacon in beaconViews)
                if (beacon) beacon.SetActive(active);
        }

        /// <summary>Lights the next valid gate while leaving the other markers visible.</summary>
        public void ShowExpected(int index)
        {
            for (int i = 0; i < gateMaterials.Length; i++)
            {
                var color = i == index ? new Color(0.35f, 1, 0.81f) : new Color(0.25f, 0.39f, 0.52f);
                if (i == index && i == definition.Gates.Length - 1)
                    color = new Color(1, 0.8f, 0.38f);
                gateMaterials[i].color = color;
                gateMaterials[i].SetColor("_EmissionColor", color * (i == index ? 0.65f : 0.15f));
            }
        }

        /// <summary>Returns the sampled course centerline for the rival and input-driven play tests.</summary>
        internal static Vector3[] DrivingLine(CourseDefinition definition, bool takeShortcut)
        {
            var stations = Sample(definition.Main, true);
            if (takeShortcut)
            {
                int split = ClosestStation(stations, definition.Shortcut[0].Center);
                int rejoin = ClosestStation(stations, definition.Shortcut[definition.ShortcutRejoin].Center);
                var branch = Sample(definition.Shortcut, false);
                Vector3 mergePoint = stations[rejoin].Center;
                Vector3 forward = (stations[rejoin + 1].Center - mergePoint).normalized;
                int merge = branch.FindIndex(s => Vector3.Dot(s.Center - mergePoint, forward) >= 0f);
                if (merge >= 0)
                    branch.RemoveRange(merge, branch.Count - merge);
                stations.RemoveRange(split, rejoin - split);
                stations.InsertRange(split, branch);
            }
            return stations.ConvertAll(s => s.Center).ToArray();
        }

        /// <summary>Finds a route connection even when corner smoothing moves its sampled center.</summary>
        internal static int ClosestStation(List<Station> stations, Vector3 point)
        {
            int index = 0;
            for (int i = 1; i < stations.Count; i++)
                if ((stations[i].Center - point).sqrMagnitude < (stations[index].Center - point).sqrMagnitude)
                    index = i;
            return index;
        }

        /// <summary>Rounds the authored bends and samples the road for mesh and barrier construction.</summary>
        private static List<Station> Sample(Station[] route, bool closed)
        {
            var corners = new List<Station>();
            for (int i = 0; i < route.Length; i++)
            {
                Station current = route[i];
                if (!closed && (i == 0 || i == route.Length - 1))
                {
                    corners.Add(current);
                    continue;
                }
                Station previous = route[(i + route.Length - 1) % route.Length];
                Station next = route[(i + 1) % route.Length];
                Vector3 incoming = current.Center - previous.Center;
                Vector3 outgoing = next.Center - current.Center;
                if (Vector3.Dot(incoming.normalized, outgoing.normalized) > 0.999f)
                {
                    corners.Add(current);
                    continue;
                }
                float inset = Mathf.Min(9f, Mathf.Min(incoming.magnitude, outgoing.magnitude) * 0.44f);
                Station start = Station.Lerp(current, previous, inset / incoming.magnitude);
                Station end = Station.Lerp(current, next, inset / outgoing.magnitude);
                for (int step = 0; step <= 5; step++)
                {
                    float t = step / 5f;
                    corners.Add(Station.Lerp(Station.Lerp(start, current, t), Station.Lerp(current, end, t), t));
                }
            }
            if (closed)
                corners.Add(corners[0]);

            var result = new List<Station>();
            for (int i = 1; i < corners.Count; i++)
            {
                Station a = corners[i - 1], b = corners[i];
                int pieces = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a.Center, b.Center) / 2.8f));
                for (int piece = 0; piece < pieces; piece++)
                {
                    float t = (float)piece / pieces;
                    result.Add(i == 1 && route == CourseDefinition.CyberGrave.Shortcut
                        ? RidgeDeparture(a, b, t) : Station.Lerp(a, b, t));
                }
            }
            if (!closed)
                result.Add(corners[^1]);
            return result;
        }

        /// <summary>Starts the ridge from the shared road tangent and rolls in the bank after the split.</summary>
        private static Station RidgeDeparture(Station split, Station next, float t)
        {
            Vector3 offset = next.Center - split.Center;
            Vector3 first = split.Center + Vector3.back * (offset.magnitude / 3f);
            Vector3 second = next.Center - offset / 3f;
            Vector3 a = Vector3.Lerp(split.Center, first, t);
            Vector3 b = Vector3.Lerp(first, second, t);
            Vector3 c = Vector3.Lerp(second, next.Center, t);
            Vector3 center = Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);
            return new Station(center, Mathf.Lerp(split.Width, next.Width, t), Mathf.Lerp(split.Bank, next.Bank, t * t));
        }

        /// <summary>Builds a continuous road ribbon and spaces barriers along its sampled edges.</summary>
        private void BuildRoad(string name, List<Station> stations, List<Station> other, bool closed,
            Material surface, Material marking)
        {
            int count = stations.Count;
            int segments = closed ? count : count - 1;
            float height = closed ? 0 : 0.018f; // Clears coincident faces where the ribbons overlap.
            var vertices = new Vector3[count * 2];
            var triangles = new int[segments * 6];
            var barrierEdges = new bool[segments * 2];
            for (int i = 0; i < count; i++)
            {
                Station station = stations[i];
                Vector3 before = stations[closed ? (i + count - 1) % count : Mathf.Max(0, i - 1)].Center;
                Vector3 after = stations[closed ? (i + 1) % count : Mathf.Min(count - 1, i + 1)].Center;
                Vector3 tangent = (after - before).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, tangent);
                if (Mathf.Abs(station.Bank) > 0.01f)
                    right = Quaternion.AngleAxis(station.Bank, tangent) * right;
                Vector3 center = station.Center + Vector3.up * height;
                vertices[i * 2] = center - right * station.Width;
                vertices[i * 2 + 1] = center + right * station.Width;
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = (i + 1) % count * 2, start = i * 6;
                triangles[start] = a;
                triangles[start + 1] = b;
                triangles[start + 2] = a + 1;
                triangles[start + 3] = a + 1;
                triangles[start + 4] = b;
                triangles[start + 5] = b + 1;
                barrierEdges[a] = AddEdge(vertices[a], vertices[b], other, !closed);
                barrierEdges[a + 1] = AddEdge(vertices[a + 1], vertices[b + 1], other, !closed);
            }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            owned.Add(mesh);
            var obj = new GameObject(name);
            obj.transform.SetParent(root, false);
            obj.AddComponent<MeshCollider>().sharedMesh = mesh;
            environment.BuildRoadView(obj, vertices, stations, barrierEdges, closed, surface, marking, rail);
        }

        /// <summary>Leaves open edges at the split and merge so both branches can be driven.</summary>
        private bool AddEdge(Vector3 a, Vector3 b, List<Station> other, bool otherClosed)
        {
            Vector3 middle = (a + b) * 0.5f;
            // Open the outside guard before the ridge peels away. Its narrow deck doesn't
            // reach the main-road edge until after both routes have started passing the split.
            Vector3 split = other[0].Center;
            bool ridgeEntry = !otherClosed && definition == CourseDefinition.CyberGrave &&
                middle.x < split.x - 3f && Mathf.Abs(middle.z - split.z) < 5f;
            if (ridgeEntry || NearRoad(middle, other, otherClosed))
                return false;
            Quaternion rotation = Quaternion.LookRotation(b - a);
            float length = Vector3.Distance(a, b);
            GameObject barrier = Cube("Barrier", middle + Vector3.up * 0.38f,
                new(0.45f, 0.76f, length + 0.16f), rotation, rail, true);
            barrier.GetComponent<Renderer>().enabled = false; // The view batches all rail and reflector faces.
            return true;
        }

        /// <summary>Checks a sampled edge against the width and elevation of the adjacent road.</summary>
        private static bool NearRoad(Vector3 point, List<Station> stations, bool closed)
        {
            int segments = closed ? stations.Count : stations.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Station a = stations[i], b = stations[(i + 1) % stations.Count];
                Vector3 segment = b.Center - a.Center;
                float t = Mathf.Clamp01(Vector3.Dot(point - a.Center, segment) / segment.sqrMagnitude);
                Station nearest = Station.Lerp(a, b, t);
                float clearance = nearest.Width + 1.1f;
                Vector2 flatDelta = new(point.x - nearest.Center.x, point.z - nearest.Center.z);
                if (Mathf.Abs(point.y - nearest.Center.y) <= 2.5f && flatDelta.sqrMagnitude < clearance * clearance)
                    return true;
            }
            return false;
        }

        /// <summary>Places a gantry and a road marking at the rules engine's gate plane.</summary>
        private void BuildGate(int index)
        {
            Checkpoint gate = definition.Gates[index];
            Material marker = gateMaterials[index] = Paint(0x5debdc, true);
            BuildGate(gate, marker, index == definition.Gates.Length - 1);
        }

        /// <summary>Reuses the non-solid checkpoint gantry and existing text presentation for pickups.</summary>
        internal void BuildRelayGates()
        {
            foreach (Checkpoint gate in RelayBoostPickups.GatesFor(definition))
            {
                Material color = gate.Name == "PULSE SHIELD" ? Paint(0x34f4ff, true) :
                    gate.Name == "FLUX RIBBON" ? Paint(0xf04be8, true) :
                    gate.Name == "SIGNAL TAP" ? Paint(0xff9a3c, true) :
                    gate.Name == "SLIPSTREAM LINK" ? Paint(0x4cff9a, true) :
                    gate.Name == "ITEM GATE" ? cyan : amber;
                BuildGate(gate, color, false);
                var label = new GameObject(gate.Name + " label");
                label.transform.SetParent(root, false);
                label.transform.SetPositionAndRotation(gate.Center + Vector3.up * 3.35f,
                    Quaternion.LookRotation(gate.Forward));
                var text = label.AddComponent<TextMesh>();
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.font = font;
                text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                text.text = gate.Name;
                text.anchor = TextAnchor.MiddleCenter;
                text.characterSize = 0.22f;
                text.fontSize = 48;
                text.color = color.color;
            }
        }

        /// <summary>Builds a non-solid bounded gate, with checkers only at the lap finish.</summary>
        private void BuildGate(Checkpoint gate, Material marker, bool finish)
        {
            Vector3 right = Vector3.Cross(Vector3.up, gate.Forward);
            Quaternion rotation = Quaternion.LookRotation(gate.Forward);
            float width = gate.HalfWidth;
            for (int side = -1; side <= 1; side += 2)
                Cube(gate.Name + " pylon", gate.Center + right * width * side + Vector3.up * 1.9f,
                    new(0.34f, 3.8f, 0.42f), rotation, marker);
            Cube(gate.Name + " header", gate.Center + Vector3.up * 3.75f, new(width * 2, 0.23f, 0.4f), rotation, marker);
            if (finish)
            {
                for (int stripe = 0; stripe < 12; stripe++)
                {
                    float tile = width / 6f;
                    for (int row = 0; row < 2; row++)
                        Cube("Finish checker", gate.Center + right * (-width + (stripe + 0.5f) * tile) +
                            gate.Forward * ((row - 0.5f) * tile) + Vector3.up * 0.028f,
                            new(tile, 0.026f, tile), rotation, (stripe + row) % 2 == 0 ? white : dark);
                }
            }
            else
                Cube(gate.Name + " crossing", gate.Center + Vector3.up * 0.028f,
                    new(width * 2, 0.025f, 0.24f), rotation, marker);
        }

        /// <summary>Creates a tracked material using the active render pipeline's default shader.</summary>
        public Material Paint(uint rgb, bool luminous = false)
        {
            Shader shader = GraphicsSettings.currentRenderPipeline?.defaultMaterial?.shader ?? Shader.Find("Standard");
            Color color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
            var material = new Material(shader) { color = color };
            material.SetFloat("_Smoothness", 0.18f);
            if (luminous)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.42f);
            }
            owned.Add(material);
            return material;
        }

        /// <summary>Places a primitive block using a shared material and optional solid collision.</summary>
        public GameObject Cube(string name, Vector3 position, Vector3 scale, Quaternion rotation,
            Material material, bool solid = false, Transform parent = null)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent ? parent : root, false);
            box.transform.SetLocalPositionAndRotation(position, rotation);
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            box.GetComponent<BoxCollider>().enabled = solid;
            return box;
        }
    }
}
