#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;

public sealed class PetAffinitySystemTests
{
    [Test]
    public void GetStageForValue_UsesThreeBehaviorBands()
    {
        Type affinityType = Type.GetType("PetAffinitySystem, Assembly-CSharp");
        Assert.That(affinityType, Is.Not.Null);

        MethodInfo method = affinityType.GetMethod(
            "GetStageForValue",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        float[] values = { 0f, 29.99f, 30f, 69.99f, 70f, 100f };
        string[] expected = { "New", "New", "Familiar", "Familiar", "Bonded", "Bonded" };
        for (int i = 0; i < values.Length; i++)
        {
            object stage = method.Invoke(null, new object[] { values[i], 30f, 70f });
            Assert.That(stage.ToString(), Is.EqualTo(expected[i]), $"Affinity {values[i]}");
        }
    }

    [Test]
    public void CalculateDecaySteps_UsesRequestedActiveAndOfflineIntervals()
    {
        Type affinityType = Type.GetType("PetAffinitySystem, Assembly-CSharp");
        Assert.That(affinityType, Is.Not.Null);

        MethodInfo method = affinityType.GetMethod(
            "CalculateDecaySteps",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        AssertDecay(method, 600d, 600d, 120d, 0);
        AssertDecay(method, 719.99d, 600d, 120d, 0);
        AssertDecay(method, 720d, 600d, 120d, 1);
        AssertDecay(method, 840d, 600d, 120d, 2);

        AssertDecay(method, 86400d, 86400d, 3600d, 0);
        AssertDecay(method, 89999.99d, 86400d, 3600d, 0);
        AssertDecay(method, 90000d, 86400d, 3600d, 1);
        AssertDecay(method, 93600d, 86400d, 3600d, 2);
    }

    private static void AssertDecay(
        MethodInfo method,
        double elapsed,
        double grace,
        double interval,
        int expected)
    {
        object result = method.Invoke(null, new object[] { elapsed, grace, interval });
        Assert.That(result, Is.EqualTo(expected), $"Elapsed seconds: {elapsed}");
    }
}
#endif
