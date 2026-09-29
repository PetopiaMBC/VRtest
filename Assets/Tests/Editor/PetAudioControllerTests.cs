#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PetAudioControllerTests
{
    [Test]
    public void PetAudioResources_ContainEveryRequiredReusableGroup()
    {
        AudioClip[] clips = Resources.LoadAll<AudioClip>("PetAudio");
        Assert.That(clips.Length, Is.GreaterThanOrEqualTo(32));
        AssertPrefixCount(clips, "Paw_Soft_", 6);
        AssertPrefixCount(clips, "Paw_Hard_", 6);
        AssertPrefixCount(clips, "PantCycle_", 4);
        AssertPrefixCount(clips, "Pant_In_", 3);
        AssertPrefixCount(clips, "Pant_Out_", 4);
        AssertPrefixCount(clips, "Bark_", 4);
        AssertPrefixCount(clips, "HappyWhine_", 3);
        AssertPrefixCount(clips, "Sniff_", 3);
        AssertPrefixCount(clips, "BodySettle_", 3);

        for (int i = 0; i < clips.Length; i++)
        {
            Assert.That(clips[i].channels, Is.EqualTo(1), clips[i].name + " must stay mono for 3D audio.");
            Assert.That(clips[i].length, Is.GreaterThan(0.05f), clips[i].name);
        }
    }

    [Test]
    public void LocomotionClassification_CoversCurrentDogAnimationNames()
    {
        Type type = Type.GetType("PetAudioController, Assembly-CSharp");
        Assert.That(type, Is.Not.Null);
        MethodInfo method = type.GetMethod("IsLocomotionClipName", BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        Assert.That((bool)method.Invoke(null, new object[] { "Arm_Shiba|Walk_F_IP" }), Is.True);
        Assert.That((bool)method.Invoke(null, new object[] { "Arm_Shiba|Trot_F_IP" }), Is.True);
        Assert.That((bool)method.Invoke(null, new object[] { "Arm_Shiba|Run_F_IP" }), Is.True);
        Assert.That((bool)method.Invoke(null, new object[] { "Arm_Shiba|Turn_L180_IP" }), Is.True);
        Assert.That((bool)method.Invoke(null, new object[] { "Arm_Shiba|Idle_2" }), Is.False);
    }

    [Test]
    public void SurfaceClassification_RecognizesSoftMaterialNames()
    {
        Type type = Type.GetType("PetAudioController, Assembly-CSharp");
        Assert.That(type, Is.Not.Null);
        MethodInfo method = type.GetMethod("IsSoftSurfaceName", BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        Assert.That((bool)method.Invoke(null, new object[] { "Living Room Carpet" }), Is.True);
        Assert.That((bool)method.Invoke(null, new object[] { "Dog Bed Fabric" }), Is.True);
        Assert.That((bool)method.Invoke(null, new object[] { "거실 카펫" }), Is.True);
        Assert.That((bool)method.Invoke(null, new object[] { "강아지 침대" }), Is.True);
        Assert.That((bool)method.Invoke(null, new object[] { "Concrete Floor" }), Is.False);
    }

    private static void AssertPrefixCount(AudioClip[] clips, string prefix, int expected)
    {
        int count = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                count++;
        }
        Assert.That(count, Is.GreaterThanOrEqualTo(expected), prefix);
    }
}
#endif
