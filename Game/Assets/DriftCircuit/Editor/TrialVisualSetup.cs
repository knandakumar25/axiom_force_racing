using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Builds the optional visual catalog from locally installed Synty prefabs.</summary>
    public static class TrialVisualSetup
    {
        private const string Vendor = "Assets/SyntyStudios";
        private const string Output = "Assets/DriftCircuitLocal";
        private const string CatalogPath = Output + "/Resources/" + TrialVisualCatalog.ResourceName + ".asset";
        private const string City = "PolygonCity";
        private const string Prototype = "PolygonPrototype";
        private const string Knights = "PolygonKnights";

        private static readonly (string Key, string Pack, string Name)[] Selections =
        {
            ("KatanaFin", Prototype, "SM_Veh_Car_Sports_01"),
            ("SolarRam", City, "SM_Veh_Car_Muscle_01"),
            ("HydroFoil", City, "SM_Veh_Car_Sedan_01"),
            ("PhantomDart", City, "SM_Veh_Car_Small_01"),
            ("TitanCoil", City, "SM_Veh_Car_Van_01"),
            ("PrismWing", City, "SM_Veh_Car_Medium_01"),
            ("office", City, "SM_Bld_OfficeOld_Small_01"),
            ("office-roof", City, "SM_Bld_OfficeOld_Small_Roof_01"),
            ("shop", City, "SM_Bld_Shop_03"),
            ("awning", City, "SM_Bld_Shop_Cover_03"),
            ("bridge-deck", City, "SM_Env_Bridge_Underside_01"),
            ("bridge-beam", City, "SM_Env_Bridge_Support_01"),
            ("bridge-pillar", City, "SM_Env_Bridge_Pillar_01"),
            ("freight", Prototype, "SM_Prop_Crate_03"),
            ("bollard", City, "SM_Env_Street_Divider_01"),
            ("quay", City, "SM_Env_WaterEdge_Straight_01"),
            ("pallet", City, "SM_Prop_Pallet_01"),
            ("pipe", City, "SM_Prop_Pipe_Preset_03"),
            ("vent", City, "SM_Prop_Vents_Exhaust_01"),
            ("foundry-wall", Prototype, "SM_Buildings_WallWindowDouble_5x3_01"),
            ("salvage", City, "SM_Prop_Roof_Aircon_03"),
            ("scrap-bin", City, "SM_Prop_Trashbin_02"),
            ("spire-base", City, "SM_Bld_OfficeOld_Large_Base_01"),
            ("spire-floor", City, "SM_Bld_OfficeOld_Large_Floor_01"),
            ("spire-roof", City, "SM_Bld_OfficeOld_Large_Roof_01"),
            ("keep", Knights, "SM_Bld_Castle_Tower_03"),
            ("castle-wall", Knights, "SM_Bld_Castle_Wall_01")
        };

        /// <summary>Runs the catalog builder from the Editor menu.</summary>
        [MenuItem("Drift Circuit/Build Local Synty Visuals")]
        private static void BuildFromMenu() => Debug.Log(Build());

        /// <summary>Resolves the entire manifest, prepares an inactive generation, then publishes it.</summary>
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the local visual catalog.");

            var missing = new List<string>();
            var inputs = new List<(string Key, GameObject Prefab)>();
            foreach (var selection in Selections)
            {
                string folder = $"{Vendor}/{selection.Pack}/Prefabs";
                string[] matches = AssetDatabase.IsValidFolder(folder)
                    ? AssetDatabase.FindAssets($"{selection.Name} t:Prefab", new[] { folder })
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Where(path => Path.GetFileNameWithoutExtension(path) == selection.Name).ToArray()
                    : Array.Empty<string>();
                GameObject prefab = matches.Length == 1 ? AssetDatabase.LoadAssetAtPath<GameObject>(matches[0]) : null;
                if (!prefab || !prefab.GetComponentsInChildren<MeshFilter>().Any(filter => filter.sharedMesh))
                    missing.Add($"{selection.Key}: {folder}/…/{selection.Name}.prefab ({matches.Length} matches)");
                else
                    inputs.Add((selection.Key, prefab));
            }
            if (missing.Count > 0)
                throw new InvalidOperationException("The local Synty selections are missing, ambiguous, or have no mesh.\n" +
                    string.Join("\n", missing));

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
                throw new InvalidOperationException("The URP Lit shader is unavailable.");

            EnsureFolder(Output);
            EnsureFolder(Output + "/Resources");
            var catalog = AssetDatabase.LoadAssetAtPath<TrialVisualCatalog>(CatalogPath);
            // Build the inactive slot first. The published asset always points at a complete generation.
            string slotA = Output + "/ModelsA";
            string slotB = Output + "/ModelsB";
            string active = catalog && catalog.Models.Length > 0 &&
                AssetDatabase.GetAssetPath(catalog.Models[0].Prefab).StartsWith(slotB + "/", StringComparison.Ordinal)
                ? slotB : slotA;
            string staging = active == slotA ? slotB : slotA;
            if (AssetDatabase.IsValidFolder(staging) && !AssetDatabase.DeleteAsset(staging))
                throw new IOException($"Could not clear the inactive visual folder {staging}.");
            EnsureFolder(staging);

            var converted = new Dictionary<(Material, bool, DriverProfile), Material>();
            var previous = catalog ? catalog.Models : null;
            try
            {
                var models = inputs.Select(input => SaveModel(input.Key, input.Prefab, staging, shader, converted)).ToArray();
                if (catalog)
                {
                    catalog.Models = models;
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                }
                else
                {
                    catalog = ScriptableObject.CreateInstance<TrialVisualCatalog>();
                    catalog.Models = models;
                    AssetDatabase.CreateAsset(catalog, CatalogPath);
                }
            }
            catch
            {
                if (catalog && previous != null)
                {
                    catalog.Models = previous;
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                }
                AssetDatabase.DeleteAsset(staging);
                throw;
            }

            if (AssetDatabase.IsValidFolder(active))
                AssetDatabase.DeleteAsset(active);
            return $"Built {inputs.Count} render-only models in {CatalogPath}.";
        }

        /// <summary>Saves the highest-detail meshes with their nested transforms and fitted bounds.</summary>
        private static TrialVisualCatalog.Model SaveModel(string key, GameObject source, string folder, Shader shader,
            Dictionary<(Material, bool, DriverProfile), Material> converted)
        {
            bool vehicle = source.name.StartsWith("SM_Veh_Car_", StringComparison.Ordinal);
            var root = new GameObject(key);
            try
            {
                var lodGroups = source.GetComponentsInChildren<LODGroup>();
                var lowerLods = new HashSet<Renderer>(lodGroups.SelectMany(group => group.GetLODs().Skip(1))
                    .SelectMany(lod => lod.renderers));
                lowerLods.ExceptWith(lodGroups.Where(group => group.lodCount > 0)
                    .SelectMany(group => group.GetLODs()[0].renderers));

                // Use the labeled front axle to correct an imported vehicle facing -Z.
                float facing = ForwardYaw(source, vehicle);
                var orientation = new GameObject("Orientation").transform;
                orientation.SetParent(root.transform, false);
                orientation.localRotation = Quaternion.Euler(0, facing, 0);
                DriverProfile driver = vehicle ? DriverProfile.All.Single(profile => profile.Chassis.ToString() == key) : null;
                CopyMeshes(source.transform, orientation, lowerLods, source.transform, driver, shader, folder, converted);
                Bounds bounds = ModelBounds(root);
                if (driver != null)
                    AddLampMounts(root.transform, bounds, driver.Chassis);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/{key}.prefab", out bool saved);
                if (!saved || !prefab)
                    throw new IOException($"Could not save the render-only prefab for {source.name}.");
                return new TrialVisualCatalog.Model { Key = key, Prefab = prefab, Bounds = bounds };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Derives the forward axis from the imported front and rear wheel labels.</summary>
        private static float ForwardYaw(GameObject source, bool vehicle)
        {
            if (!vehicle) return 0;
            Transform[] parts = source.GetComponentsInChildren<Transform>();
            float Axle(string side) => parts.Where(part =>
                part.name.Contains("wheel", StringComparison.OrdinalIgnoreCase) &&
                part.name.EndsWith(side, StringComparison.OrdinalIgnoreCase))
                .Select(part => source.transform.InverseTransformPoint(part.position).z).Single();
            float front = Axle("_fl") + Axle("_fr");
            float rear = Axle("_rl") + Axle("_rr");
            if (Mathf.Approximately(front, rear))
                throw new InvalidOperationException($"Cannot determine the front axle of {source.name}.");
            return front > rear ? 0f : 180f;
        }

        /// <summary>Copies render data without instantiating vendor scripts or physics.</summary>
        private static void CopyMeshes(Transform source, Transform parent, HashSet<Renderer> lowerLods,
            Transform sourceRoot, DriverProfile driver, Shader shader, string folder,
            Dictionary<(Material, bool, DriverProfile), Material> converted)
        {
            if (!source.gameObject.activeSelf) return;
            var copy = new GameObject(source.name);
            copy.transform.SetParent(parent, false);
            if (source != sourceRoot)
            {
                copy.transform.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
                copy.transform.localScale = source.localScale;
            }

            if (source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh &&
                source.TryGetComponent(out MeshRenderer renderer) && renderer.enabled && !lowerLods.Contains(renderer))
            {
                DriverProfile bodyPaint = source == sourceRoot ? driver : null;
                if (bodyPaint != null) copy.name = "Driver paint";
                bool glass = source.name.Contains("glass", StringComparison.OrdinalIgnoreCase);
                copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials
                    .Select(material => ConvertMaterial(material, glass, bodyPaint, shader, folder, converted)).ToArray();
            }
            foreach (Transform child in source)
                CopyMeshes(child, copy.transform, lowerLods, sourceRoot, driver, shader, folder, converted);
            if (copy.transform.childCount == 0 && !copy.TryGetComponent<MeshRenderer>(out _))
                UnityEngine.Object.DestroyImmediate(copy);
        }

        /// <summary>Creates a reusable URP material for a vendor surface and optional driver paint.</summary>
        private static Material ConvertMaterial(Material source, bool glass, DriverProfile bodyPaint, Shader shader,
            string folder, Dictionary<(Material, bool, DriverProfile), Material> converted)
        {
            if (!source)
                throw new InvalidOperationException("A selected mesh has an unassigned source material.");
            if (converted.TryGetValue((source, glass, bodyPaint), out Material material)) return material;

            material = new Material(shader) { name = source.name, color = source.HasColor("_BaseColor")
                ? source.GetColor("_BaseColor") : source.HasColor("_Color") ? source.color : Color.white };
            string textureProperty = source.HasTexture("_BaseMap") ? "_BaseMap" :
                source.HasTexture("_MainTex") ? "_MainTex" : null;
            if (textureProperty != null)
            {
                Texture texture = source.GetTexture(textureProperty);
                material.mainTexture = bodyPaint != null ? RepaintAtlas(texture, bodyPaint, folder) : texture;
                material.mainTextureOffset = source.GetTextureOffset(textureProperty);
                material.mainTextureScale = source.GetTextureScale(textureProperty);
            }
            material.SetFloat("_Smoothness", glass ? 0.7f : 0.2f);
            if (glass) material.color = new Color(0.31f, 0.39f, 0.49f);
            AssetDatabase.CreateAsset(material, $"{folder}/Material-{converted.Count:D2}.mat");
            converted.Add((source, glass, bodyPaint), material);
            return material;
        }

        /// <summary>Recolors the selected vehicles' body swatches while retaining their other atlas details.</summary>
        private static Texture2D RepaintAtlas(Texture source, DriverProfile driver, string folder)
        {
            if (!source)
                throw new InvalidOperationException($"The body of {driver.DisplayName} has no paint atlas.");

            bool sports = driver.Chassis == ChassisStyle.KatanaFin;
            Color32 baseSwatch = Rgb(sports ? 0xad3e2cu : 0x4665a9u);
            Color32 darkSwatch = Rgb(sports ? 0x973426u : 0x354d84u);
            Color32 paint = Rgb(driver.PrimaryColor);
            Color32 shade = (Color32)((Color)paint * 0.77f);
            shade.a = 255;

            // The swatches are flat colors. A smaller copy preserves them without loading six full-size atlases.
            float ratio = Mathf.Min(1f, 512f / Mathf.Max(source.width, source.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * ratio));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * ratio));
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = driver.Id + " paint",
                filterMode = source.filterMode,
                wrapMode = source.wrapMode,
                anisoLevel = source.anisoLevel
            };
            RenderTexture previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }

            Color32[] pixels = copy.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (NearColor(pixel, baseSwatch)) pixels[i] = paint;
                else if (NearColor(pixel, darkSwatch)) pixels[i] = shade;
            }
            copy.SetPixels32(pixels);
            copy.Apply(true, true);
            AssetDatabase.CreateAsset(copy, $"{folder}/{driver.Id}-paint.asset");
            return copy;
        }

        /// <summary>Accounts for small atlas compression differences at paint swatches.</summary>
        private static bool NearColor(Color32 pixel, Color32 swatch) =>
            Mathf.Abs(pixel.r - swatch.r) + Mathf.Abs(pixel.g - swatch.g) + Mathf.Abs(pixel.b - swatch.b) <= 20;

        /// <summary>Unpacks a driver's RGB paint choice to an opaque atlas pixel.</summary>
        private static Color32 Rgb(uint hex) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);

        /// <summary>Fits both tail-lamp anchors to the rear face of the selected body mesh.</summary>
        private static void AddLampMounts(Transform root, Bounds bounds, ChassisStyle chassis)
        {
            // Multi-part vehicles have separate rear doors and trim in front of the main body mesh.
            var surfaces = root.GetComponentsInChildren<MeshFilter>().Select(filter =>
            {
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                return (Vertices: filter.sharedMesh.vertices.Select(toRoot.MultiplyPoint3x4).ToArray(),
                    Triangles: filter.sharedMesh.triangles);
            }).ToArray();
            float height = chassis == ChassisStyle.TitanCoil ? 0.39f : 0.56f;
            float y = Mathf.Lerp(bounds.min.y, bounds.max.y, height);

            for (int side = -1; side <= 1; side += 2)
            {
                float x = bounds.center.x + bounds.size.x * 0.32f * side;
                float rear = float.PositiveInfinity;
                foreach (var (vertices, triangles) in surfaces)
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    // Intersect a forward ray in the XY projection and keep the first surface.
                    Vector3 ab = b - a, ac = c - a;
                    float det = ab.x * ac.y - ab.y * ac.x;
                    if (Mathf.Abs(det) < 0.000001f) continue;
                    float u = ((x - a.x) * ac.y - (y - a.y) * ac.x) / det;
                    float v = (ab.x * (y - a.y) - ab.y * (x - a.x)) / det;
                    if (u >= 0 && v >= 0 && u + v <= 1)
                        rear = Mathf.Min(rear, a.z + u * ab.z + v * ac.z);
                }

                if (float.IsInfinity(rear))
                    throw new InvalidOperationException($"Cannot place the {chassis} tail lamp at x={x:F2}, y={y:F2}.");
                Transform mount = new GameObject(side < 0 ? "Left tail lamp mount" : "Right tail lamp mount").transform;
                mount.SetParent(root, false);
                mount.localPosition = new Vector3(x, y, rear);
            }
        }

        /// <summary>Measures every retained mesh in the fitted prefab's local coordinate space.</summary>
        private static Bounds ModelBounds(GameObject root)
        {
            var bounds = new Bounds();
            bool first = true;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Bounds mesh = filter.sharedMesh.bounds;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 corner = new(x == 0 ? mesh.min.x : mesh.max.x, y == 0 ? mesh.min.y : mesh.max.y,
                        z == 0 ? mesh.min.z : mesh.max.z);
                    Vector3 point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (first) bounds = new Bounds(point, Vector3.zero);
                    else bounds.Encapsulate(point);
                    first = false;
                }
            }
            if (first || Mathf.Min(bounds.size.x, bounds.size.y, bounds.size.z) < 0.001f)
                throw new InvalidOperationException($"{root.name} has no usable render bounds.");
            return bounds;
        }

        /// <summary>Creates a generated asset folder if Unity has yet to import it.</summary>
        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }
    }
}
