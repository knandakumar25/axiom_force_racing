using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriftCircuit
{
    /// <summary>Builds the flat test circuit, its split, and the visible checkpoint gates.</summary>
    internal sealed class TrialCourse
    {
        private readonly CourseDefinition definition;
        private readonly Transform root;
        private readonly List<Object> owned;
        private readonly Material road, shortcut, rail, cyan, amber, white, dark;
        private readonly Material[] gateMaterials;
        private RailCrossing crossing;

        /// <summary>Parents the crossing labels alongside the generated road and signs.</summary>
        internal Transform Root => root;

        /// <summary>Creates shared materials and records them for scene teardown.</summary>
        public TrialCourse(CourseDefinition definition, Transform root, List<Object> owned)
        {
            this.definition = definition;
            this.root = root;
            this.owned = owned;
            road = Paint(0x263445);
            shortcut = Paint(0x433e38);
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
            BuildRoad(definition.MainName, main, branch, true, road, cyan);
            BuildRoad(definition.ShortcutName, branch, main, false, shortcut, amber);
            for (int i = 2; i < branch.Count - 2; i += 4)
            {
                Vector3 heading = branch[i + 1].Center - branch[i - 1].Center;
                Cube("Shortcut guide", branch[i].Center + Vector3.up * 0.033f,
                    new(0.19f, 0.018f, 1.6f), Quaternion.LookRotation(heading), amber);
            }
            for (int i = 0; i < definition.Gates.Length; i++)
                BuildGate(i);

            foreach (var landmark in definition.Landmarks)
            {
                Material material = landmark.Material switch
                {
                    CourseDefinition.Surface.Cyan => cyan,
                    CourseDefinition.Surface.Amber => amber,
                    CourseDefinition.Surface.Dark => dark,
                    _ => rail
                };
                Cube(landmark.Name, landmark.Position, landmark.Scale, Quaternion.Euler(0, landmark.Yaw, 0), material, landmark.Solid);
            }
            if (definition == CourseDefinition.Rustrail)
                crossing = new RailCrossing(this, dark, rail);
        }

        /// <summary>Applies the crossing phase when this circuit has a timed shortcut.</summary>
        internal void UpdateCrossing(double elapsed, TrialKart kart)
        {
            if (crossing != null)
                crossing.Update(elapsed, kart.PhysicsBounds);
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

        /// <summary>Returns the course centerline for the physical input-driven play test.</summary>
        internal static Vector3[] DrivingLine(CourseDefinition definition, bool takeShortcut)
        {
            var stations = Sample(definition.Main, true);
            if (takeShortcut)
            {
                int split = stations.FindIndex(s => s.Center == definition.Shortcut[0].Center);
                int rejoin = stations.FindIndex(s => s.Center == definition.Shortcut[definition.ShortcutRejoin].Center);
                var branch = Sample(definition.Shortcut, false);
                Vector3 mergePoint = stations[rejoin].Center;
                Vector3 forward = (stations[rejoin + 1].Center - mergePoint).normalized;
                int merge = branch.FindIndex(s => Vector3.Dot(s.Center - mergePoint, forward) >= 0f);
                branch.RemoveRange(merge, branch.Count - merge);
                stations.RemoveRange(split, rejoin - split);
                stations.InsertRange(split, branch);
            }
            return stations.ConvertAll(s => s.Center).ToArray();
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
                    result.Add(Station.Lerp(a, b, (float)piece / pieces));
            }
            if (!closed)
                result.Add(corners[^1]);
            return result;
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
            for (int i = 0; i < count; i++)
            {
                Station station = stations[i];
                Vector3 before = stations[closed ? (i + count - 1) % count : Mathf.Max(0, i - 1)].Center;
                Vector3 after = stations[closed ? (i + 1) % count : Mathf.Min(count - 1, i + 1)].Center;
                Vector3 right = Vector3.Cross(Vector3.up, (after - before).normalized);
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
                AddEdge(vertices[a], vertices[b], other, !closed, marking);
                AddEdge(vertices[a + 1], vertices[b + 1], other, !closed, marking);
            }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            owned.Add(mesh);
            var obj = new GameObject(name);
            obj.transform.SetParent(root, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = surface;
            obj.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        /// <summary>Leaves open edges at the split and merge so both branches can be driven.</summary>
        private void AddEdge(Vector3 a, Vector3 b, List<Station> other, bool otherClosed, Material marking)
        {
            Vector3 middle = (a + b) * 0.5f;
            if (NearRoad(middle, other, otherClosed))
                return;
            Quaternion rotation = Quaternion.LookRotation(b - a);
            float length = Vector3.Distance(a, b);
            Cube("Barrier", middle + Vector3.up * 0.38f, new(0.45f, 0.76f, length + 0.16f), rotation, rail, true);
            Cube("Edge marker", middle + Vector3.up * 0.78f, new(0.12f, 0.06f, length * 0.78f), rotation, marking);
        }

        /// <summary>Checks a sampled edge against the width of the adjacent road.</summary>
        private static bool NearRoad(Vector3 point, List<Station> stations, bool closed)
        {
            point.y = 0;
            int segments = closed ? stations.Count : stations.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Station a = stations[i], b = stations[(i + 1) % stations.Count];
                Vector3 segment = b.Center - a.Center;
                float t = Mathf.Clamp01(Vector3.Dot(point - a.Center, segment) / segment.sqrMagnitude);
                Station nearest = Station.Lerp(a, b, t);
                float clearance = nearest.Width + 1.1f;
                if ((point - nearest.Center).sqrMagnitude < clearance * clearance)
                    return true;
            }
            return false;
        }

        /// <summary>Places a gantry and a road marking at the rules engine's gate plane.</summary>
        private void BuildGate(int index)
        {
            Checkpoint gate = definition.Gates[index];
            Material marker = gateMaterials[index] = Paint(0x5debdc, true);
            Vector3 right = Vector3.Cross(Vector3.up, gate.Forward);
            Quaternion rotation = Quaternion.LookRotation(gate.Forward);
            float width = gate.HalfWidth;
            for (int side = -1; side <= 1; side += 2)
                Cube(gate.Name + " pylon", gate.Center + right * width * side + Vector3.up * 1.9f,
                    new(0.34f, 3.8f, 0.42f), rotation, marker);
            Cube(gate.Name + " header", gate.Center + Vector3.up * 3.75f, new(width * 2, 0.23f, 0.4f), rotation, marker);
            if (index == definition.Gates.Length - 1)
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
