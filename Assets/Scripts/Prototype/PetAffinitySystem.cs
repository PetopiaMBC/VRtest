using System;
using UnityEngine;

public enum PetAffinityStage
{
    New,
    Familiar,
    Bonded
}

public enum PetAffinityExperience
{
    Petting,
    Fetch,
    Grab,
    CallResponse
}

[Serializable]
public struct PetAffinitySnapshot
{
    public float Affinity;
    public long SavedAtUtcTicks;

    public PetAffinitySnapshot(float affinity, long savedAtUtcTicks)
    {
        Affinity = affinity;
        SavedAtUtcTicks = savedAtUtcTicks;
    }
}

/// <summary>
/// Keeps the dog's hidden runtime affinity. There is deliberately no UI here.
/// D can restore or replace the value later through SetAffinity without changing
/// the behavior code owned by C.
/// </summary>
[DisallowMultipleComponent]
public sealed class PetAffinitySystem : MonoBehaviour
{
    public const float MinimumValue = 0f;
    public const float MaximumValue = 100f;

    [Header("Hidden affinity (not shown in UI)")]
    [SerializeField, Range(MinimumValue, MaximumValue)] private float affinity;
    [SerializeField, Range(1f, 99f)] private float familiarThreshold = 30f;
    [SerializeField, Range(1f, 99f)] private float bondedThreshold = 70f;

    [Header("Positive experiences")]
    [SerializeField, Min(0f)] private float pettingReward = 2f;
    [SerializeField, Min(0f)] private float fetchReward = 5f;
    [SerializeField, Min(0f)] private float grabReward = 3f;
    [SerializeField, Min(0f)] private float callResponseReward = 2f;

    [Header("Anti-grind cooldowns")]
    [Tooltip("Each experience owns an independent cooldown with this duration.")]
    [SerializeField, Min(0f)] private float rewardCooldown = 20f;

    [Header("Affinity decay")]
    [Tooltip("Active play time without any successful dog interaction before boredom begins.")]
    [SerializeField, Min(0f)] private float activeInactivityGracePeriod = 600f;
    [Tooltip("After the grace period, affinity decreases by one at this interval.")]
    [SerializeField, Min(1f)] private float activeDecayInterval = 120f;
    [Tooltip("Closed-game time before offline affinity decay begins.")]
    [SerializeField, Min(0f)] private float offlineGracePeriod = 86400f;
    [Tooltip("After the offline grace period, affinity decreases by one at this interval.")]
    [SerializeField, Min(1f)] private float offlineDecayInterval = 3600f;

    private readonly float[] nextRewardTimes = new float[4];
    private float activeInactivityElapsed;
    private float activeDecayElapsed;

    public float Affinity => affinity;
    public float NormalizedAffinity => Mathf.InverseLerp(MinimumValue, MaximumValue, affinity);
    public PetAffinityStage Stage => GetStageForValue(affinity, familiarThreshold, bondedThreshold);

    public event Action<float> AffinityChanged;
    public event Action<PetAffinityStage> StageChanged;

    private void Update()
    {
        float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
        activeInactivityElapsed += deltaTime;
        if (activeInactivityElapsed <= activeInactivityGracePeriod)
            return;

        activeDecayElapsed += deltaTime;
        int decaySteps = CalculateDecaySteps(activeDecayElapsed, 0f, activeDecayInterval);
        if (decaySteps <= 0)
            return;

        activeDecayElapsed -= decaySteps * Mathf.Max(1f, activeDecayInterval);
        SetAffinity(affinity - decaySteps);
    }

    public bool RegisterExperience(PetAffinityExperience experience)
    {
        int index = (int)experience;
        if (index < 0 || index >= nextRewardTimes.Length)
            return false;

        // Even a repeated interaction inside the reward cooldown means the dog
        // was not ignored, so it resets boredom without granting more affinity.
        activeInactivityElapsed = 0f;
        activeDecayElapsed = 0f;
        if (Time.unscaledTime < nextRewardTimes[index])
            return false;

        nextRewardTimes[index] = Time.unscaledTime + rewardCooldown;
        SetAffinity(affinity + GetReward(experience));
        return true;
    }

    public PetAffinitySnapshot CaptureSnapshot(DateTime utcNow)
    {
        return new PetAffinitySnapshot(affinity, utcNow.ToUniversalTime().Ticks);
    }

    /// <summary>
    /// Restores D's persisted snapshot and applies whole hourly decay steps only
    /// after the first 24 offline hours have elapsed.
    /// </summary>
    public int RestoreSnapshot(PetAffinitySnapshot snapshot, DateTime utcNow)
    {
        long elapsedTicks = Math.Max(0L, utcNow.ToUniversalTime().Ticks - snapshot.SavedAtUtcTicks);
        double elapsedSeconds = TimeSpan.FromTicks(elapsedTicks).TotalSeconds;
        int decaySteps = CalculateDecaySteps(
            elapsedSeconds,
            offlineGracePeriod,
            offlineDecayInterval);

        SetAffinity(snapshot.Affinity - decaySteps);
        activeInactivityElapsed = 0f;
        activeDecayElapsed = 0f;
        return decaySteps;
    }

    /// <summary>
    /// Entry point for D's persisted state provider. C only consumes the value.
    /// </summary>
    public void SetAffinity(float value)
    {
        PetAffinityStage previousStage = Stage;
        float clamped = Mathf.Clamp(value, MinimumValue, MaximumValue);
        if (Mathf.Approximately(affinity, clamped))
            return;

        affinity = clamped;
        AffinityChanged?.Invoke(affinity);

        PetAffinityStage currentStage = Stage;
        if (currentStage != previousStage)
            StageChanged?.Invoke(currentStage);
    }

    public void SetAffinityForTesting(float value)
    {
        activeInactivityElapsed = 0f;
        activeDecayElapsed = 0f;
        SetAffinity(value);
    }

    public static PetAffinityStage GetStageForValue(
        float value,
        float familiarAt = 30f,
        float bondedAt = 70f)
    {
        float safeFamiliar = Mathf.Clamp(familiarAt, MinimumValue, MaximumValue);
        float safeBonded = Mathf.Clamp(bondedAt, safeFamiliar, MaximumValue);
        if (value >= safeBonded)
            return PetAffinityStage.Bonded;
        if (value >= safeFamiliar)
            return PetAffinityStage.Familiar;
        return PetAffinityStage.New;
    }

    public static int CalculateDecaySteps(
        double elapsedSeconds,
        double gracePeriodSeconds,
        double decayIntervalSeconds)
    {
        double safeElapsed = Math.Max(0d, elapsedSeconds);
        double safeGrace = Math.Max(0d, gracePeriodSeconds);
        double safeInterval = Math.Max(1d, decayIntervalSeconds);
        if (safeElapsed <= safeGrace)
            return 0;

        double steps = Math.Floor((safeElapsed - safeGrace) / safeInterval);
        return steps >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)steps);
    }

    private float GetReward(PetAffinityExperience experience)
    {
        switch (experience)
        {
            case PetAffinityExperience.Petting:
                return pettingReward;
            case PetAffinityExperience.Fetch:
                return fetchReward;
            case PetAffinityExperience.Grab:
                return grabReward;
            case PetAffinityExperience.CallResponse:
                return callResponseReward;
            default:
                return 0f;
        }
    }

    private void OnValidate()
    {
        affinity = Mathf.Clamp(affinity, MinimumValue, MaximumValue);
        familiarThreshold = Mathf.Clamp(familiarThreshold, 1f, MaximumValue - 1f);
        bondedThreshold = Mathf.Clamp(bondedThreshold, familiarThreshold + 1f, MaximumValue);
        rewardCooldown = Mathf.Max(0f, rewardCooldown);
        activeInactivityGracePeriod = Mathf.Max(0f, activeInactivityGracePeriod);
        activeDecayInterval = Mathf.Max(1f, activeDecayInterval);
        offlineGracePeriod = Mathf.Max(0f, offlineGracePeriod);
        offlineDecayInterval = Mathf.Max(1f, offlineDecayInterval);
    }
}
