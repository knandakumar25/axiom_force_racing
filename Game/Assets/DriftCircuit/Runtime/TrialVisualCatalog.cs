using System;
using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Optional generated models. The race can build its primitive visuals without this asset.</summary>
    public sealed class TrialVisualCatalog : ScriptableObject
    {
        public const string ResourceName = "DriftCircuitVisuals";

        /// <summary>Pairs a render-only prefab with its measured local bounds.</summary>
        [Serializable]
        public struct Model
        {
            public string Key;
            public GameObject Prefab;
            public Bounds Bounds;
        }

        public Model[] Models = Array.Empty<Model>();

        /// <summary>Fits the chosen model to the authored volume and preserves its child hierarchy.</summary>
        internal bool TryPlace(string key, Transform parent, Vector3 center, Vector3 size,
            Quaternion rotation, string name, out GameObject visual)
        {
            visual = null;
            foreach (Model model in Models)
            {
                if (model.Key != key || !model.Prefab) continue;
                Vector3 extent = model.Bounds.size;
                Vector3 scale = new(size.x / extent.x, size.y / extent.y, size.z / extent.z);
                visual = Instantiate(model.Prefab, parent, false);
                visual.name = name;
                visual.transform.SetLocalPositionAndRotation(
                    center - rotation * Vector3.Scale(model.Bounds.center, scale), rotation);
                visual.transform.localScale = scale;
                return true;
            }
            return false;
        }
    }
}
