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
            foreach (MeshFilter source in kart.GetComponentsInChildren<MeshFilter>())
            {
                var part = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
                Transform pose = source.transform;
                part.transform.SetParent(model.transform, false);
                part.transform.SetLocalPositionAndRotation(pose.localPosition, pose.localRotation);
                part.transform.localScale = pose.localScale;
                part.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = part.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            model.SetActive(false);
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
