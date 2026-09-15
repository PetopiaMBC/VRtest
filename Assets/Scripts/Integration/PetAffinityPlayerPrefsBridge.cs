using System;
using UnityEngine;

/// <summary>
/// Temporary D-facing persistence adapter. It is intentionally separate from
/// PetAffinitySystem so a project save provider can replace it without changing
/// C's behavior and decay rules.
/// </summary>
[DisallowMultipleComponent]
public sealed class PetAffinityPlayerPrefsBridge : MonoBehaviour
{
    private const string AffinityKey = "VRTest.PetAffinity.Value.v1";
    private const string SavedAtUtcTicksKey = "VRTest.PetAffinity.SavedAtUtcTicks.v1";
    private const float AutosaveInterval = 60f;

    private PetAffinitySystem affinitySystem;
    private float autosaveElapsed;

    private void Awake()
    {
        affinitySystem = GetComponent<PetAffinitySystem>();
        Restore();
    }

    private void OnEnable()
    {
        if (affinitySystem == null)
            affinitySystem = GetComponent<PetAffinitySystem>();
        if (affinitySystem != null)
            affinitySystem.AffinityChanged += HandleAffinityChanged;
    }

    private void OnDisable()
    {
        if (affinitySystem != null)
            affinitySystem.AffinityChanged -= HandleAffinityChanged;
        SaveNow();
    }

    private void Update()
    {
        autosaveElapsed += Time.unscaledDeltaTime;
        if (autosaveElapsed < AutosaveInterval)
            return;

        autosaveElapsed = 0f;
        SaveNow();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            SaveNow();
    }

    private void OnApplicationQuit()
    {
        SaveNow();
    }

    private void HandleAffinityChanged(float value)
    {
        SaveNow();
    }

    private void Restore()
    {
        if (affinitySystem == null || !PlayerPrefs.HasKey(AffinityKey))
            return;

        float savedAffinity = PlayerPrefs.GetFloat(AffinityKey, affinitySystem.Affinity);
        string savedTicksText = PlayerPrefs.GetString(SavedAtUtcTicksKey, string.Empty);
        if (!long.TryParse(savedTicksText, out long savedTicks) || savedTicks <= 0L)
        {
            affinitySystem.SetAffinity(savedAffinity);
            return;
        }

        affinitySystem.RestoreSnapshot(
            new PetAffinitySnapshot(savedAffinity, savedTicks),
            DateTime.UtcNow);
        // Commit the already-applied offline loss immediately so it cannot be
        // applied twice if the scene reloads during the same session.
        SaveNow();
    }

    private void SaveNow()
    {
        if (affinitySystem == null)
            return;

        PetAffinitySnapshot snapshot = affinitySystem.CaptureSnapshot(DateTime.UtcNow);
        PlayerPrefs.SetFloat(AffinityKey, snapshot.Affinity);
        PlayerPrefs.SetString(SavedAtUtcTicksKey, snapshot.SavedAtUtcTicks.ToString());
        PlayerPrefs.Save();
    }
}
