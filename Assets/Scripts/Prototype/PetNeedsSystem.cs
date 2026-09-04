using UnityEngine;

/// <summary>
/// Stores the dog's private hunger and fatigue values. This component has no UI;
/// gameplay systems report completed interactions through the public methods below.
/// </summary>
[DisallowMultipleComponent]
public sealed class PetNeedsSystem : MonoBehaviour
{
    public const float MinimumValue = 0f;
    public const float MaximumValue = 100f;

    [Header("Current private needs (not shown in UI)")]
    [SerializeField, Range(MinimumValue, MaximumValue)] private float hunger = MaximumValue;
    [SerializeField, Range(MinimumValue, MaximumValue)] private float fatigue = MaximumValue;

    [Header("Passive decrease")]
    [SerializeField] private bool decreaseNeedsOverTime = true;
    [SerializeField, Min(0.1f)] private float hungerDecreaseInterval = 15f;
    [SerializeField, Min(0.1f)] private float fatigueDecreaseInterval = 30f;
    [SerializeField, Min(0.01f)] private float passiveDecreaseAmount = 1f;

    [Header("Interaction costs")]
    [SerializeField, Min(0f)] private float fetchCatchHungerCost = 3f;
    [SerializeField, Min(0f)] private float fetchCatchFatigueCost = 5f;
    [SerializeField, Min(0f)] private float grabbedFatigueCost = 3f;
    [SerializeField, Min(0f)] private float pettingFatigueCost = 1f;

    [Header("Recovery")]
    [SerializeField, Min(0f)] private float mealRecovery = 70f;
    [SerializeField, Min(0f)] private float sleepRecovery = 70f;

    [Header("Automatic behavior thresholds")]
    [SerializeField, Range(MinimumValue, MaximumValue)] private float optionalMealThreshold = 30f;
    [SerializeField, Range(MinimumValue, MaximumValue)] private float mandatorySleepThreshold = 20f;

    private float hungerIntervalElapsed;
    private float fatigueIntervalElapsed;

    public float Hunger => hunger;
    public float Fatigue => fatigue;
    public bool RequiresImmediateMeal => hunger <= MinimumValue;
    public bool RequiresSleep => fatigue <= mandatorySleepThreshold;
    public bool CanChooseOptionalMeal => hunger > MinimumValue && hunger <= optionalMealThreshold;

    private void Update()
    {
        if (!decreaseNeedsOverTime)
            return;

        hungerIntervalElapsed += Time.deltaTime;
        fatigueIntervalElapsed += Time.deltaTime;

        float safeHungerInterval = Mathf.Max(0.1f, hungerDecreaseInterval);
        while (hungerIntervalElapsed >= safeHungerInterval)
        {
            hungerIntervalElapsed -= safeHungerInterval;
            ChangeHunger(-passiveDecreaseAmount);
        }

        float safeFatigueInterval = Mathf.Max(0.1f, fatigueDecreaseInterval);
        while (fatigueIntervalElapsed >= safeFatigueInterval)
        {
            fatigueIntervalElapsed -= safeFatigueInterval;
            ChangeFatigue(-passiveDecreaseAmount);
        }
    }

    public bool ShouldChooseOptionalMeal(float randomValue)
    {
        if (!CanChooseOptionalMeal)
            return false;

        return Mathf.Clamp01(randomValue) < GetOptionalMealChance();
    }

    public float GetOptionalMealChance()
    {
        if (!CanChooseOptionalMeal)
            return 0f;
        if (hunger <= 10f)
            return 0.7f;
        if (hunger <= 20f)
            return 0.4f;
        return 0.2f;
    }

    public void RegisterBallCaught()
    {
        ChangeHunger(-fetchCatchHungerCost);
        ChangeFatigue(-fetchCatchFatigueCost);
    }

    public void RegisterGrabbed()
    {
        ChangeFatigue(-grabbedFatigueCost);
    }

    public void RegisterPetting()
    {
        ChangeFatigue(-pettingFatigueCost);
    }

    public void RegisterMealFinished(bool completedNaturally)
    {
        if (completedNaturally)
            ChangeHunger(mealRecovery);
    }

    public void RegisterSleepFinished(bool completedNaturally)
    {
        if (completedNaturally)
            ChangeFatigue(sleepRecovery);
    }

    public void SetNeeds(float newHunger, float newFatigue)
    {
        hunger = Mathf.Clamp(newHunger, MinimumValue, MaximumValue);
        fatigue = Mathf.Clamp(newFatigue, MinimumValue, MaximumValue);
    }

    private void ChangeHunger(float amount)
    {
        hunger = Mathf.Clamp(hunger + amount, MinimumValue, MaximumValue);
    }

    private void ChangeFatigue(float amount)
    {
        fatigue = Mathf.Clamp(fatigue + amount, MinimumValue, MaximumValue);
    }

    private void OnValidate()
    {
        hunger = Mathf.Clamp(hunger, MinimumValue, MaximumValue);
        fatigue = Mathf.Clamp(fatigue, MinimumValue, MaximumValue);
        optionalMealThreshold = Mathf.Clamp(optionalMealThreshold, MinimumValue, MaximumValue);
        mandatorySleepThreshold = Mathf.Clamp(mandatorySleepThreshold, MinimumValue, MaximumValue);
        hungerDecreaseInterval = Mathf.Max(0.1f, hungerDecreaseInterval);
        fatigueDecreaseInterval = Mathf.Max(0.1f, fatigueDecreaseInterval);
    }
}
