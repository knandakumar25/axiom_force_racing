using DriftCircuit;
using NUnit.Framework;
using UnityEngine;

/// <summary>Exercises visual contracts without requiring the licensed bundle.</summary>
public sealed class TrialVisualTests
{
    /// <summary>Checks the ghost render hierarchy under nonuniform scale with a multi-slot mesh.</summary>
    [Test]
    public void GhostPreservesNestedPoseAndUsesEverySubmesh()
    {
        var kart = new GameObject("Kart");
        var ghostParent = new GameObject("Ghost parent");
        var mesh = new Mesh { subMeshCount = 3 };
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        try
        {
            var fit = new GameObject("Fitted vehicle").transform;
            fit.SetParent(kart.transform, false);
            fit.localPosition = new Vector3(1, -2, 3);
            fit.localScale = new Vector3(2, 3, 4);
            var part = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider)).transform;
            part.SetParent(fit, false);
            part.SetLocalPositionAndRotation(new Vector3(2, 1, 4), Quaternion.Euler(15, 37, 72));
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            new TrialGhost(ghostParent.transform, kart.transform, material);

            Transform ghostPart = ghostParent.transform.GetChild(0).Find("Fitted vehicle/Part");
            Assert.That(ghostPart.localToWorldMatrix, Is.EqualTo(part.localToWorldMatrix));
            Assert.That(ghostPart.GetComponent<MeshRenderer>().sharedMaterials,
                Is.EqualTo(new[] { material, material, material }));
            Assert.That(ghostPart.GetComponent<Collider>(), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(ghostParent);
            Object.DestroyImmediate(kart);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(mesh);
        }
    }
}
