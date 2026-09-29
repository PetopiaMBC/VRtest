using UnityEngine;

/// <summary>
/// Adds a lightweight procedural tail wag after the Animator has evaluated.
/// It works during idle, locomotion, petting, and XR holding without replacing
/// any animation clips. Affinity controls how often normal wag bursts occur.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1200)]
public sealed class PetTailWagController : MonoBehaviour
{
    #region 인스펙터 설정

    [Header("꼬리 흔들기")]
    [Tooltip("호감도와 쓰다듬기 상태에 따른 절차적 꼬리 흔들기를 사용합니다.")]
    [SerializeField] private bool enableTailWag = true;
    [Tooltip("호감도가 낮을 때 꼬리 흔들기 사이의 평균 대기 시간(초)입니다.")]
    [SerializeField, Min(1f)] private float lowAffinityAverageInterval = 9f;
    [Tooltip("호감도가 높을 때 꼬리 흔들기 사이의 평균 대기 시간(초)입니다.")]
    [SerializeField, Min(0.5f)] private float highAffinityAverageInterval = 2.2f;
    [Tooltip("한 번 자연스럽게 꼬리를 흔드는 최소·최대 시간(초)입니다.")]
    [SerializeField] private Vector2 wagBurstDurationRange = new Vector2(1.2f, 2.4f);
    [Tooltip("일반 상태에서 꼬리가 좌우로 움직이는 최대 각도입니다.")]
    [SerializeField, Range(2f, 28f)] private float normalMaximumAngle = 15f;
    [Tooltip("쓰다듬는 동안 꼬리가 좌우로 움직이는 최대 각도입니다.")]
    [SerializeField, Range(4f, 35f)] private float pettingMaximumAngle = 22f;
    [Tooltip("호감도가 낮을 때 꼬리 흔들기 속도입니다.")]
    [SerializeField, Range(0.5f, 4f)] private float lowAffinityFrequency = 1.65f;
    [Tooltip("호감도가 높을 때 꼬리 흔들기 속도입니다.")]
    [SerializeField, Range(1f, 5f)] private float highAffinityFrequency = 2.75f;
    [Tooltip("꼬리 흔들기가 시작되고 멈출 때의 부드러움입니다.")]
    [SerializeField, Range(1f, 12f)] private float wagBlendSpeed = 5f;

    #endregion

    #region 내부 상태

    private static readonly string[][] TailBoneNames =
    {
        new[] { "DEF-spine.003", "Tail_01" },
        new[] { "DEF-spine.002", "Tail_02" },
        new[] { "DEF-spine.001", "Tail_03" },
        new[] { "DEF-spine", "Tail_04" },
        new[] { "Tail_05" }
    };

    private static readonly float[] TailBoneWeights = { 0.16f, 0.34f, 0.56f, 0.78f, 1f };

    private readonly Transform[] tailBones = new Transform[5];
    private ShepherdCompanionAI companionAI;
    private float nextBurstTime;
    private float burstEndTime;
    private float wagPhase;
    private float wagBlend;
    private bool referencesResolved;

    #endregion

    private void Awake()
    {
        companionAI = GetComponent<ShepherdCompanionAI>();
        ResolveTailBones();
        ScheduleNextBurst(true);
    }

    private void OnEnable()
    {
        ScheduleNextBurst(true);
    }

    private void LateUpdate()
    {
        if (!enableTailWag)
            return;

        if (!referencesResolved)
            ResolveTailBones();
        if (tailBones[0] == null)
            return;

        float affinity = companionAI != null
            ? companionAI.NormalizedAffinity
            : 0f;
        bool petting = companionAI != null && companionAI.IsPettingReactionActive;

        if (!petting && Time.time >= nextBurstTime && Time.time >= burstEndTime)
            StartWagBurst(affinity);

        bool normalBurstActive = Time.time < burstEndTime;
        float targetBlend = petting
            ? 1f
            : normalBurstActive
                ? Mathf.Lerp(0.45f, 0.9f, affinity)
                : 0f;
        wagBlend = Mathf.MoveTowards(
            wagBlend,
            targetBlend,
            wagBlendSpeed * Time.deltaTime);
        if (wagBlend <= 0.001f)
            return;

        float frequency = Mathf.Lerp(
            lowAffinityFrequency,
            highAffinityFrequency,
            affinity);
        if (petting)
            frequency *= 1.15f;
        wagPhase += Time.deltaTime * frequency * Mathf.PI * 2f;

        float maximumAngle = petting
            ? pettingMaximumAngle
            : normalMaximumAngle * Mathf.Lerp(0.72f, 1f, affinity);
        ApplyTailWave(maximumAngle * wagBlend);
    }

    private void StartWagBurst(float affinity)
    {
        float minimum = Mathf.Min(wagBurstDurationRange.x, wagBurstDurationRange.y);
        float maximum = Mathf.Max(wagBurstDurationRange.x, wagBurstDurationRange.y);
        burstEndTime = Time.time + Random.Range(minimum, maximum);
        ScheduleNextBurst(false, affinity);
    }

    private void ScheduleNextBurst(bool initial, float affinity = -1f)
    {
        if (affinity < 0f)
            affinity = companionAI != null ? companionAI.NormalizedAffinity : 0f;

        float average = CalculateAverageInterval(
            affinity,
            lowAffinityAverageInterval,
            highAffinityAverageInterval);
        float delay = initial
            ? Random.Range(0.6f, Mathf.Max(0.7f, average * 0.7f))
            : Random.Range(average * 0.7f, average * 1.3f);
        nextBurstTime = Time.time + delay;
    }

    private void ApplyTailWave(float maximumAngle)
    {
        // Phase delay across the five joints makes a travelling wave instead
        // of rotating the curled tail as one rigid piece.
        for (int i = 0; i < tailBones.Length; i++)
        {
            Transform bone = tailBones[i];
            if (bone == null)
                continue;

            float wave = Mathf.Sin(wagPhase - i * 0.22f);
            float angle = wave * maximumAngle * TailBoneWeights[i];
            bone.rotation = Quaternion.AngleAxis(angle, transform.up) * bone.rotation;
        }
    }

    private void ResolveTailBones()
    {
        Transform[] allBones = GetComponentsInChildren<Transform>(true);
        for (int slot = 0; slot < TailBoneNames.Length; slot++)
            tailBones[slot] = FindByAnyName(allBones, TailBoneNames[slot]);

        referencesResolved = tailBones[0] != null;
    }

    private static Transform FindByAnyName(Transform[] bones, string[] names)
    {
        for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
        {
            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                if (bones[boneIndex].name.Equals(
                        names[nameIndex],
                        System.StringComparison.OrdinalIgnoreCase))
                    return bones[boneIndex];
            }
        }

        return null;
    }

    public static float CalculateAverageInterval(
        float normalizedAffinity,
        float lowAffinityInterval,
        float highAffinityInterval)
    {
        return Mathf.Lerp(
            Mathf.Max(0.1f, lowAffinityInterval),
            Mathf.Max(0.1f, highAffinityInterval),
            Mathf.Clamp01(normalizedAffinity));
    }
}
