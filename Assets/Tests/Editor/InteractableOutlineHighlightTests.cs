#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// XR ???? ??? ???? ??? ?? ?? ??? ?????.
/// ??? ???? ??? ???? ?? ??? ??? ?????? ?????.
/// </summary>
public sealed class InteractableOutlineHighlightTests
{
    [Test]
    public void OutlineShader_IsSupportedAndAvailable()
    {
        Shader shader = Shader.Find("Hidden/VRTest/InteractionOutline");
        Assert.That(shader, Is.Not.Null);
        Assert.That(shader.isSupported, Is.True);
    }

    [Test]
    public void TailWagInterval_DecreasesAsAffinityIncreases()
    {
        Type tailWagType = Type.GetType(
            "PetTailWagController, Assembly-CSharp");
        Assert.That(tailWagType, Is.Not.Null);

        MethodInfo calculateInterval = tailWagType.GetMethod(
            "CalculateAverageInterval",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(calculateInterval, Is.Not.Null);

        float lowAffinity = (float)calculateInterval.Invoke(
            null,
            new object[] { 0f, 9f, 2f });
        float highAffinity = (float)calculateInterval.Invoke(
            null,
            new object[] { 1f, 9f, 2f });

        Assert.That(lowAffinity, Is.EqualTo(9f).Within(0.001f));
        Assert.That(highAffinity, Is.EqualTo(2f).Within(0.001f));
        Assert.That(highAffinity, Is.LessThan(lowAffinity));
    }

    [Test]
    public void HeldLimbDirection_TiltsForwardInsteadOfHangingStraightDown()
    {
        Type grabType = Type.GetType(
            "ShepherdVertexGrabInteraction, Assembly-CSharp");
        Assert.That(grabType, Is.Not.Null);

        MethodInfo composeDirection = grabType.GetMethod(
            "ComposeHeldLimbDirection",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(composeDirection, Is.Not.Null);

        Vector3 direction = (Vector3)composeDirection.Invoke(
            null,
            new object[]
            {
                Vector3.down,
                Vector3.forward,
                Vector3.zero,
                0.84f,
                0.28f,
                0f
            });

        Assert.That(direction.magnitude, Is.EqualTo(1f).Within(0.001f));
        Assert.That(direction.y, Is.LessThan(-0.9f));
        Assert.That(direction.z, Is.GreaterThan(0.25f));
    }

    [Test]
    public void OutlineMaterial_IsPackagedAsAResourcesReference()
    {
        Material material = Resources.Load<Material>("XRInteractionOutline");
        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader, Is.Not.Null);
        Assert.That(material.shader.name, Is.EqualTo("Hidden/VRTest/InteractionOutline"));
    }

    [Test]
    public void SetHighlighted_CreatesAndTogglesOutlineShell()
    {
        Type highlightType = Type.GetType(
            "InteractableOutlineHighlight, Assembly-CSharp");
        Assert.That(highlightType, Is.Not.Null);

        MethodInfo setHighlighted = highlightType.GetMethod(
            "SetHighlighted",
            BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo isHighlighted = highlightType.GetProperty(
            "IsHighlighted",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.That(setHighlighted, Is.Not.Null);
        Assert.That(isHighlighted, Is.Not.Null);

        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            Renderer source = target.GetComponent<Renderer>();
            Component highlight = target.AddComponent(highlightType);

            setHighlighted.Invoke(
                highlight,
                new object[] { true, Color.cyan, 0.008f });

            Renderer shell = FindAdditionalRenderer(target, source);
            Assert.That(shell, Is.Not.Null);
            Assert.That(shell.enabled, Is.True);
            Assert.That((bool)isHighlighted.GetValue(highlight), Is.True);

            setHighlighted.Invoke(
                highlight,
                new object[] { false, Color.cyan, 0.008f });
            Assert.That(shell.enabled, Is.False);
            Assert.That((bool)isHighlighted.GetValue(highlight), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void TemporarySphereProbe_CalculatesSurfacePenetration()
    {
        GameObject probeObject = new GameObject("Controller Probe");
        GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            SphereCollider probe = probeObject.AddComponent<SphereCollider>();
            probe.radius = 0.045f;
            probe.isTrigger = true;
            BoxCollider target = targetObject.GetComponent<BoxCollider>();
            targetObject.transform.position = Vector3.zero;

            bool overlaps = Physics.ComputePenetration(
                probe,
                new Vector3(0.51f, 0f, 0f),
                Quaternion.identity,
                target,
                target.transform.position,
                target.transform.rotation,
                out Vector3 direction,
                out float distance);

            Assert.That(overlaps, Is.True);
            Assert.That(direction.x, Is.GreaterThan(0.9f));
            Assert.That(distance, Is.GreaterThan(0f));
            probe.enabled = false;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probeObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    private static Renderer FindAdditionalRenderer(GameObject target, Renderer source)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != source)
                return renderers[i];
        }

        return null;
    }
}
#endif
