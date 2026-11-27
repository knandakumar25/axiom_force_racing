using UnityEngine;
using UnityEngine.Rendering;

namespace DriftCircuit
{
    internal sealed class TrialGhost
    {
        private readonly GameObject model;

        public TrialGhost(Transform parent, Transform kart, Material material)
        {
            model = new GameObject("Personal best ghost");
            model.transform.SetParent(parent, false);
            CopyMeshes(kart, model.transform, material);
            model.SetActive(false);
        }

        /// <summary>Copies local transforms and all mesh slots without inheriting gameplay components.</summary>
        private static void CopyMeshes(Transform source, Transform target, Material material)
        {
            if (source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh &&
                source.TryGetComponent(out MeshRenderer original) && original.enabled)
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = target.gameObject.AddComponent<MeshRenderer>();
                var slots = new Material[filter.sharedMesh.subMeshCount];
                System.Array.Fill(slots, material);
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            foreach (Transform child in source)
            {
                if (!child.gameObject.activeSelf || !child.GetComponentInChildren<MeshFilter>()) continue;
                var copy = new GameObject(child.name).transform;
                copy.SetParent(target, false);
                copy.SetLocalPositionAndRotation(child.localPosition, child.localRotation);
                copy.localScale = child.localScale;
                CopyMeshes(child, copy, material);
            }
        }

        public void Refresh(TrialRecord record, double elapsed, double renderTime, bool racing)
        {
            bool visible = racing && record != null && elapsed < record.BestTotal;
            model.SetActive(visible);
            if (visible)
            {
                GhostPose pose = Sample(record.Poses, renderTime);
                model.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            }
        }

        internal static GhostPose Sample(GhostPose[] poses, double elapsed)
        {
            // Find the last sample at or before this time. If two poses share a time,
            // the post-reset pose is last and takes effect at that exact timestamp.
            int low = 0, high = poses.Length;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (poses[mid].Time <= elapsed)
                    low = mid + 1;
                else
                    high = mid;
            }

            GhostPose from = poses[Mathf.Max(0, low - 1)];
            if (low == 0 || low == poses.Length || poses[low].Reset)
                return from;
            GhostPose to = poses[low];
            float blend = (float)((elapsed - from.Time) / (to.Time - from.Time));
            return new GhostPose(elapsed, Vector3.Lerp(from.Position, to.Position, blend),
                Quaternion.Slerp(from.Rotation, to.Rotation, blend));
        }
    }
}
