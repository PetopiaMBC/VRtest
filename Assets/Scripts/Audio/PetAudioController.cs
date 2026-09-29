using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable 3D audio layer for the companion dog.
///
/// Automatic mode follows paw contact, locomotion exertion, petting, held state,
/// and bark animation timing. The public Audio* methods are intentionally named
/// for direct use as Animation Events when a clip needs frame-perfect authoring.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public sealed class PetAudioController : MonoBehaviour
{
    private const string ResourcePath = "PetAudio";
    private const int PawCount = 4;

    private static readonly string[] PawBoneNames =
    {
        "foot_f.L",
        "foot_f.R",
        "foot_b.L",
        "foot_b.R"
    };

    private static readonly string[] SoftSurfaceKeywords =
    {
        "carpet",
        "rug",
        "grass",
        "bed",
        "fabric",
        "cloth",
        "soft",
        "cushion",
        "sofa",
        "카펫",
        "러그",
        "잔디",
        "침대",
        "천",
        "쿠션",
        "소파",
        "부드러운"
    };

    [Serializable]
    private sealed class PawRuntime
    {
        public Transform Bone;
        public AudioSource Source;
        public float PreviousY;
        public float PreviousVerticalVelocity;
        public bool WasGrounded;
        public bool Initialized;
    }

    [Header("자동 연결")]
    [Tooltip("켜면 강아지의 발이 바닥에 닿는 순간을 찾아 발소리를 자동으로 재생합니다.")]
    [SerializeField] private bool automaticFootsteps = true;
    [Tooltip("달리거나 쓰다듬을 때, 또는 안겨 있을 때 헥헥거리는 숨소리를 자동으로 재생합니다.")]
    [SerializeField] private bool automaticPanting = true;
    [Tooltip("짖기 애니메이션에서 입이 열리는 순간에 짖는 소리를 자동으로 재생합니다.")]
    [SerializeField] private bool automaticBark = true;
    [Tooltip("쓰다듬기를 시작할 때 기분 좋은 낑낑 소리가 날 확률입니다.")]
    [SerializeField, Range(0f, 1f)] private float pettingWhineChance = 0.38f;

    [Header("발이 바닥에 닿을 때")]
    [SerializeField] private LayerMask groundMask = Physics.DefaultRaycastLayers;
    [SerializeField] private LayerMask softSurfaceLayers;
    [SerializeField, Range(0.02f, 0.2f)] private float groundProbeHeight = 0.08f;
    [SerializeField, Range(0.03f, 0.25f)] private float groundProbeDistance = 0.16f;
    [SerializeField, Range(0.03f, 0.2f)] private float footstepRetriggerInterval = 0.075f;
    [SerializeField, Range(0f, 1f)] private float walkFootstepVolume = 0.24f;
    [SerializeField, Range(0f, 1f)] private float trotFootstepVolume = 0.32f;
    [SerializeField, Range(0f, 1f)] private float runFootstepVolume = 0.42f;

    [Header("헥헥거림")]
    [SerializeField, Range(0f, 1f)] private float pantVolume = 0.22f;
    [SerializeField, Range(0f, 1f)] private float pantExertionThreshold = 0.38f;
    [SerializeField, Min(0.01f)] private float exertionRisePerSecond = 0.45f;
    [SerializeField, Min(0.01f)] private float exertionRecoveryPerSecond = 0.12f;
    [SerializeField, Range(0f, 0.4f)] private float breathGap = 0.045f;

    [Header("짖음과 목소리 타이밍")]
    [SerializeField, Range(0f, 0.8f)] private float barkNormalizedTime = 0.16f;
    [SerializeField, Range(0f, 1f)] private float barkVolume = 0.72f;
    [SerializeField, Range(0f, 1f)] private float vocalVolume = 0.38f;
    [SerializeField, Min(0f)] private float bodySettleDelayAfterRelease = 0.5f;

    [Header("3D 공간 음향")]
    [SerializeField, Min(0.05f)] private float minimumDistance = 0.55f;
    [SerializeField, Min(1f)] private float maximumDistance = 9f;

    [Header("원하는 음원으로 바꾸기 (선택)")]
    [Tooltip("비워 두면 Resources/PetAudio 폴더의 기본 음원을 자동으로 사용합니다.")]
    [SerializeField] private AudioClip[] softFootsteps = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] hardFootsteps = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] pantCycleClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] pantInClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] pantOutClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] barkClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] happyWhineClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] sniffClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] bodySettleClips = Array.Empty<AudioClip>();

    private readonly PawRuntime[] paws =
    {
        new PawRuntime(),
        new PawRuntime(),
        new PawRuntime(),
        new PawRuntime()
    };
    private readonly RaycastHit[] groundHits = new RaycastHit[12];

    private Animator animator;
    private ShepherdCompanionAI companion;
    private ShepherdVertexGrabInteraction grabInteraction;
    private Transform mouthAnchor;
    private AudioSource voiceSource;
    private AudioSource breathSource;
    private AudioSource bodySource;
    private float nextFootstepTime;
    private float nextBreathTime;
    private float exertion;
    private bool playExhaleNext = true;
    private bool wasPetting;
    private bool wasHeld;
    private bool wasBarkAnimation;
    private bool pendingBark;
    private float pendingBodySettleTime = float.PositiveInfinity;
    private int lastSoftFootstep = -1;
    private int lastHardFootstep = -1;
    private int lastPantCycle = -1;
    private int lastPantIn = -1;
    private int lastPantOut = -1;
    private int lastBark = -1;
    private int lastWhine = -1;
    private int lastSniff = -1;
    private int lastBodySettle = -1;

    private void Awake()
    {
        ResolveReferences();
        LoadResourceClips();
        BuildSpatialSources();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        string clipName = GetDominantAnimationClipName();

        if (automaticFootsteps)
            UpdateAutomaticFootsteps(clipName);
        if (automaticPanting)
            UpdateAutomaticPanting(clipName);
        if (automaticBark)
            UpdateAutomaticBark();

        UpdateInteractionAccents();
    }

    private void ResolveReferences()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
        if (companion == null)
            companion = GetComponent<ShepherdCompanionAI>();
        if (grabInteraction == null)
            grabInteraction = GetComponent<ShepherdVertexGrabInteraction>();

        if (mouthAnchor == null)
        {
            mouthAnchor = FindBone("mouth");
            if (mouthAnchor == null)
                mouthAnchor = FindBone("head");
            if (mouthAnchor == null)
                mouthAnchor = transform;
        }

        for (int i = 0; i < PawCount; i++)
        {
            if (paws[i].Bone == null)
                paws[i].Bone = FindBone(PawBoneNames[i]);
        }
    }

    private Transform FindBone(string boneName)
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name.Equals(boneName, StringComparison.OrdinalIgnoreCase))
                return transforms[i];
        }

        return null;
    }

    private void LoadResourceClips()
    {
        AudioClip[] allClips = Resources.LoadAll<AudioClip>(ResourcePath);
        Array.Sort(allClips, (left, right) => string.CompareOrdinal(left.name, right.name));
        AssignIfEmpty(ref softFootsteps, FilterByPrefix(allClips, "Paw_Soft_"));
        AssignIfEmpty(ref hardFootsteps, FilterByPrefix(allClips, "Paw_Hard_"));
        AssignIfEmpty(ref pantCycleClips, FilterByPrefix(allClips, "PantCycle_"));
        AssignIfEmpty(ref pantInClips, FilterByPrefix(allClips, "Pant_In_"));
        AssignIfEmpty(ref pantOutClips, FilterByPrefix(allClips, "Pant_Out_"));
        AssignIfEmpty(ref barkClips, FilterByPrefix(allClips, "Bark_"));
        AssignIfEmpty(ref happyWhineClips, FilterByPrefix(allClips, "HappyWhine_"));
        AssignIfEmpty(ref sniffClips, FilterByPrefix(allClips, "Sniff_"));
        AssignIfEmpty(ref bodySettleClips, FilterByPrefix(allClips, "BodySettle_"));
    }

    private static void AssignIfEmpty(ref AudioClip[] target, AudioClip[] fallback)
    {
        if (target == null || target.Length == 0)
            target = fallback;
    }

    private static AudioClip[] FilterByPrefix(AudioClip[] source, string prefix)
    {
        List<AudioClip> result = new List<AudioClip>();
        for (int i = 0; i < source.Length; i++)
        {
            AudioClip clip = source[i];
            if (clip != null && clip.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                result.Add(clip);
        }

        return result.ToArray();
    }

    private void BuildSpatialSources()
    {
        voiceSource = GetOrCreateSource(mouthAnchor, "Pet Audio Voice", 1f);
        breathSource = GetOrCreateSource(mouthAnchor, "Pet Audio Breath", 0.85f);
        bodySource = GetOrCreateSource(transform, "Pet Audio Body", 1f);
        for (int i = 0; i < PawCount; i++)
        {
            Transform anchor = paws[i].Bone != null ? paws[i].Bone : transform;
            paws[i].Source = GetOrCreateSource(anchor, "Pet Audio Paw " + i, 1f);
        }
    }

    private AudioSource GetOrCreateSource(Transform anchor, string sourceName, float sourceVolume)
    {
        Transform existing = anchor.Find(sourceName);
        GameObject sourceObject;
        if (existing != null)
        {
            sourceObject = existing.gameObject;
        }
        else
        {
            sourceObject = new GameObject(sourceName);
            sourceObject.transform.SetParent(anchor, false);
        }

        AudioSource source = sourceObject.GetComponent<AudioSource>();
        if (source == null)
            source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.volume = sourceVolume;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = minimumDistance;
        source.maxDistance = maximumDistance;
        return source;
    }

    private void UpdateAutomaticFootsteps(string clipName)
    {
        if (!IsLocomotionClipName(clipName) || grabInteraction != null && grabInteraction.IsHolding)
        {
            ResetPawContactState();
            return;
        }

        float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        for (int i = 0; i < PawCount; i++)
        {
            PawRuntime paw = paws[i];
            if (paw.Bone == null)
                continue;

            float currentY = paw.Bone.position.y;
            float verticalVelocity = paw.Initialized
                ? (currentY - paw.PreviousY) / deltaTime
                : 0f;
            bool grounded = TryFindGround(paw.Bone, out RaycastHit hit);

            if (paw.Initialized)
            {
                bool contacted = grounded && !paw.WasGrounded;
                bool reachedBottom = grounded && paw.PreviousVerticalVelocity < -0.025f &&
                                     verticalVelocity >= -0.01f;
                if ((contacted || reachedBottom) && Time.time >= nextFootstepTime)
                {
                    PlayFootstep(i, IsSoftSurface(hit.collider), GetFootstepVolume(clipName));
                    nextFootstepTime = Time.time + footstepRetriggerInterval;
                }
            }

            paw.PreviousY = currentY;
            paw.PreviousVerticalVelocity = verticalVelocity;
            paw.WasGrounded = grounded;
            paw.Initialized = true;
        }
    }

    private void ResetPawContactState()
    {
        for (int i = 0; i < PawCount; i++)
        {
            PawRuntime paw = paws[i];
            paw.Initialized = false;
            paw.WasGrounded = false;
            paw.PreviousVerticalVelocity = 0f;
        }
    }

    private bool TryFindGround(Transform paw, out RaycastHit bestHit)
    {
        Vector3 origin = paw.position + Vector3.up * groundProbeHeight;
        float distance = groundProbeHeight + groundProbeDistance;
        int count = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            groundHits,
            distance,
            groundMask,
            QueryTriggerInteraction.Ignore);

        bestHit = default;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = groundHits[i];
            if (candidate.collider == null || candidate.collider.transform.IsChildOf(transform))
                continue;
            if (candidate.distance >= nearest)
                continue;
            nearest = candidate.distance;
            bestHit = candidate;
        }

        return nearest < float.PositiveInfinity;
    }

    private bool IsSoftSurface(Collider surface)
    {
        if (surface == null)
            return false;
        if ((softSurfaceLayers.value & (1 << surface.gameObject.layer)) != 0)
            return true;

        string surfaceName = surface.name;
        if (surface.sharedMaterial != null)
            surfaceName += " " + surface.sharedMaterial.name;
        return IsSoftSurfaceName(surfaceName);
    }

    private float GetFootstepVolume(string clipName)
    {
        string lower = clipName.ToLowerInvariant();
        if (lower.Contains("run"))
            return runFootstepVolume;
        if (lower.Contains("trot"))
            return trotFootstepVolume;
        return walkFootstepVolume;
    }

    private void UpdateAutomaticPanting(string clipName)
    {
        string lower = clipName.ToLowerInvariant();
        bool exerting = lower.Contains("run") || lower.Contains("trot");
        exertion = Mathf.Clamp01(exertion +
            (exerting ? exertionRisePerSecond : -exertionRecoveryPerSecond) * Time.deltaTime);

        bool held = grabInteraction != null && grabInteraction.IsHolding;
        bool petting = companion != null && companion.IsPettingReactionActive;
        bool shouldPant = held || petting || exertion >= pantExertionThreshold;
        if (!shouldPant)
        {
            nextBreathTime = Mathf.Max(nextBreathTime, Time.time + 0.08f);
            playExhaleNext = true;
            return;
        }

        if (Time.time < nextBreathTime || breathSource == null || breathSource.isPlaying)
            return;

        if (pantCycleClips != null && pantCycleClips.Length > 0)
        {
            PlayPantCycle();
        }
        else
        {
            if (playExhaleNext)
                PlayPantOut();
            else
                PlayPantIn();
            playExhaleNext = !playExhaleNext;
        }
    }

    private void UpdateAutomaticBark()
    {
        bool barkAnimation = TryGetAnimationNormalizedTimeContaining(
            "bark",
            out float normalized);
        if (barkAnimation && !wasBarkAnimation)
            pendingBark = true;

        if (pendingBark && barkAnimation)
        {
            if (normalized >= barkNormalizedTime)
            {
                PlayBark();
                pendingBark = false;
            }
        }

        if (!barkAnimation)
            pendingBark = false;
        wasBarkAnimation = barkAnimation;
    }

    private bool TryGetAnimationNormalizedTimeContaining(
        string namePart,
        out float normalizedTime)
    {
        normalizedTime = 0f;
        if (animator == null || !animator.isActiveAndEnabled)
            return false;

        AnimatorClipInfo[] next = animator.IsInTransition(0)
            ? animator.GetNextAnimatorClipInfo(0)
            : Array.Empty<AnimatorClipInfo>();
        if (ContainsClip(next, namePart))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
            normalizedTime = nextState.normalizedTime - Mathf.Floor(nextState.normalizedTime);
            return true;
        }

        AnimatorClipInfo[] current = animator.GetCurrentAnimatorClipInfo(0);
        if (!ContainsClip(current, namePart))
            return false;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        normalizedTime = currentState.normalizedTime - Mathf.Floor(currentState.normalizedTime);
        return true;
    }

    private static bool ContainsClip(AnimatorClipInfo[] clips, string namePart)
    {
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].clip != null && clips[i].weight > 0.01f &&
                clips[i].clip.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private void UpdateInteractionAccents()
    {
        bool petting = companion != null && companion.IsPettingReactionActive;
        if (petting && !wasPetting && UnityEngine.Random.value <= pettingWhineChance)
            PlayHappyWhine();
        wasPetting = petting;

        bool held = grabInteraction != null && grabInteraction.IsHolding;
        if (!held && wasHeld)
            pendingBodySettleTime = Time.time + bodySettleDelayAfterRelease;
        wasHeld = held;

        if (Time.time >= pendingBodySettleTime)
        {
            PlayBodySettle();
            pendingBodySettleTime = float.PositiveInfinity;
        }
    }

    private string GetDominantAnimationClipName()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return string.Empty;

        AnimatorClipInfo[] current = animator.GetCurrentAnimatorClipInfo(0);
        AnimatorClipInfo[] next = animator.IsInTransition(0)
            ? animator.GetNextAnimatorClipInfo(0)
            : Array.Empty<AnimatorClipInfo>();
        AnimatorClipInfo best = default;
        bool found = false;

        for (int i = 0; i < current.Length; i++)
        {
            if (!found || current[i].weight > best.weight)
            {
                best = current[i];
                found = true;
            }
        }
        for (int i = 0; i < next.Length; i++)
        {
            if (!found || next[i].weight > best.weight)
            {
                best = next[i];
                found = true;
            }
        }

        return found && best.clip != null ? best.clip.name : string.Empty;
    }

    private void PlayFootstep(int pawIndex, bool soft, float volume)
    {
        if (pawIndex < 0 || pawIndex >= paws.Length)
            return;
        AudioClip clip = soft
            ? ChooseClip(softFootsteps, ref lastSoftFootstep)
            : ChooseClip(hardFootsteps, ref lastHardFootstep);
        PlayOneShot(paws[pawIndex].Source, clip, volume, 0.92f, 1.08f);
    }

    private static AudioClip ChooseClip(AudioClip[] clips, ref int lastIndex)
    {
        if (clips == null || clips.Length == 0)
            return null;
        if (clips.Length == 1)
        {
            lastIndex = 0;
            return clips[0];
        }

        int index = UnityEngine.Random.Range(0, clips.Length - 1);
        if (index >= lastIndex)
            index++;
        lastIndex = index;
        return clips[index];
    }

    private static void PlayOneShot(
        AudioSource source,
        AudioClip clip,
        float volume,
        float minimumPitch,
        float maximumPitch)
    {
        if (source == null || clip == null)
            return;
        source.pitch = UnityEngine.Random.Range(minimumPitch, maximumPitch);
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    public void AudioFootstepFrontLeft() => PlayAnimationEventFootstep(0);
    public void AudioFootstepFrontRight() => PlayAnimationEventFootstep(1);
    public void AudioFootstepBackLeft() => PlayAnimationEventFootstep(2);
    public void AudioFootstepBackRight() => PlayAnimationEventFootstep(3);

    private void PlayAnimationEventFootstep(int pawIndex)
    {
        if (pawIndex < 0 || pawIndex >= paws.Length)
            return;
        bool soft = paws[pawIndex].Bone != null &&
                    TryFindGround(paws[pawIndex].Bone, out RaycastHit hit) &&
                    IsSoftSurface(hit.collider);
        PlayFootstep(pawIndex, soft, GetFootstepVolume(GetDominantAnimationClipName()));
    }

    public void PlayPantIn()
    {
        AudioClip clip = ChooseClip(pantInClips, ref lastPantIn);
        PlayOneShot(breathSource, clip, pantVolume * 0.82f, 0.96f, 1.06f);
        nextBreathTime = Time.time + GetClipDuration(clip, breathSource) + breathGap;
    }

    private void PlayPantCycle()
    {
        AudioClip clip = ChooseClip(pantCycleClips, ref lastPantCycle);
        PlayOneShot(breathSource, clip, pantVolume, 0.98f, 1.025f);
        nextBreathTime = Time.time + GetClipDuration(clip, breathSource) + breathGap;
    }

    public void PlayPantOut()
    {
        AudioClip clip = ChooseClip(pantOutClips, ref lastPantOut);
        PlayOneShot(breathSource, clip, pantVolume, 0.94f, 1.05f);
        nextBreathTime = Time.time + GetClipDuration(clip, breathSource) + breathGap;
    }

    public void PlayBark()
    {
        AudioClip clip = ChooseClip(barkClips, ref lastBark);
        PlayOneShot(voiceSource, clip, barkVolume, 0.94f, 1.06f);
    }

    public void PlayHappyWhine()
    {
        AudioClip clip = ChooseClip(happyWhineClips, ref lastWhine);
        PlayOneShot(voiceSource, clip, vocalVolume, 0.96f, 1.05f);
    }

    public void PlaySniff()
    {
        AudioClip clip = ChooseClip(sniffClips, ref lastSniff);
        PlayOneShot(breathSource, clip, pantVolume * 0.75f, 0.95f, 1.08f);
    }

    public void PlayBodySettle()
    {
        AudioClip clip = ChooseClip(bodySettleClips, ref lastBodySettle);
        PlayOneShot(bodySource, clip, 0.3f, 0.92f, 1.05f);
    }

    // Animation Event aliases use an Audio* prefix so artists can find them quickly.
    public void AudioPantIn() => PlayPantIn();
    public void AudioPantOut() => PlayPantOut();
    public void AudioBark() => PlayBark();
    public void AudioHappyWhine() => PlayHappyWhine();
    public void AudioSniff() => PlaySniff();
    public void AudioBodySettle() => PlayBodySettle();

    private static float GetClipDuration(AudioClip clip, AudioSource source)
    {
        if (clip == null)
            return 0.2f;
        float pitch = source != null ? Mathf.Max(0.1f, Mathf.Abs(source.pitch)) : 1f;
        return clip.length / pitch;
    }

    public static bool IsLocomotionClipName(string clipName)
    {
        if (string.IsNullOrEmpty(clipName))
            return false;
        string lower = clipName.ToLowerInvariant();
        return lower.Contains("walk") || lower.Contains("trot") ||
               lower.Contains("run") || lower.Contains("turn");
    }

    public static bool IsSoftSurfaceName(string surfaceName)
    {
        if (string.IsNullOrEmpty(surfaceName))
            return false;
        string lower = surfaceName.ToLowerInvariant();
        for (int i = 0; i < SoftSurfaceKeywords.Length; i++)
        {
            if (lower.Contains(SoftSurfaceKeywords[i]))
                return true;
        }

        return false;
    }
}
