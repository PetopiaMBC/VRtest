using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 첫 실행에서만 이름을 받고 입양 정보를 저장하는 도입 화면입니다.
/// 이름 저장 키와 PlayScene 진입 계약은 이후 도입 연출을 교체해도 유지합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class AdoptionIntroController : MonoBehaviour
{
    private enum PreviewPose
    {
        Standing,
        Sitting,
        Lying
    }

    public const string AdoptionCompletedKey = "MyLittlePet.AdoptionCompleted";
    public const string DogNameKey = "MyLittlePet.DogName";
    public const int MinimumNameLength = 2;
    public const int MaximumNameLength = 6;

    [Header("Scene Flow")]
    [SerializeField] private string playSceneName = "PlayScene";
    [SerializeField] private string quest2SceneName = "Q2VirtualRoomScene";
    [SerializeField] private string quest3SceneName = "Q3MRScene";
    [SerializeField] private string previewDogName = "Adoption Shiba Preview";
    [SerializeField, Tooltip("Unity Editor에서 AdoptionScene을 직접 실행할 때 저장 여부와 관계없이 입양 UI를 보여줍니다. 빌드에는 영향을 주지 않습니다.")]
    private bool showAdoptionWhenTestingInEditor = true;
    [SerializeField, Min(0f)] private float playSceneTransitionDelay = 1.1f;
    [SerializeField, Min(0.05f)] private float fadeOutDuration = 0.45f;
    [SerializeField, Min(0.05f)] private float fadeInDuration = 0.55f;

    [Header("Preview Animation")]
    [SerializeField] private string previewIdleState = "Arm_Shiba|Idle_2";
    [SerializeField] private string previewHappyStartState = "Arm_Shiba|Idle_5_start";
    [SerializeField] private string previewHappyLoopState = "Arm_Shiba|Idle_5_loop";

    [Header("Preview Idle Variety")]
    [SerializeField] private string[] previewStandingIdleStates =
    {
        "Arm_Shiba|Idle_1",
        "Arm_Shiba|Idle_2",
        "Arm_Shiba|Idle_3",
        "Arm_Shiba|Idle_4",
        "Arm_Shiba|Idle_6",
        "Arm_Shiba|Idle_7"
    };
    [SerializeField] private string previewSittingStartState = "Arm_Shiba|Sitting_start";
    [SerializeField] private string[] previewSittingLoopStates =
    {
        "Arm_Shiba|Sitting_loop_1",
        "Arm_Shiba|Sitting_loop_2"
    };
    [SerializeField] private string previewSittingEndState = "Arm_Shiba|Sitting_end";
    [SerializeField] private string previewLyingStartState = "Arm_Shiba|Lie_belly_start";
    [SerializeField] private string[] previewLyingLoopStates =
    {
        "Arm_Shiba|Lie_belly_loop_1",
        "Arm_Shiba|Lie_belly_loop_2"
    };
    [SerializeField] private string previewLyingEndState = "Arm_Shiba|Lie_belly_end";
    [SerializeField, Range(0f, 1f)] private float previewSittingChance = 0.22f;
    [SerializeField, Range(0f, 1f)] private float previewLyingChance = 0.16f;
    [SerializeField] private Vector2 previewStandingDurationRange = new Vector2(2.4f, 4.5f);
    [SerializeField] private Vector2 previewRestDurationRange = new Vector2(3f, 5.5f);

    [Header("UI Font")]
    [SerializeField] private Font regularInterfaceFont;
    [SerializeField] private Font boldInterfaceFont;

    [Header("Background")]
    [SerializeField] private Texture2D adoptionBackgroundTexture;
    [SerializeField, Min(1f)] private float backgroundZoom = 1.18f;
    [SerializeField] private Vector2 backgroundUvOffset = new Vector2(0.025f, 0.08f);
    [SerializeField, Min(1f)] private float backgroundDistance = 20f;

    private static readonly Color Cocoa = new Color(0.30f, 0.16f, 0.11f);
    private static readonly Color MutedCocoa = new Color(0.43f, 0.32f, 0.26f);
    private static readonly Color Cream = new Color(1f, 0.975f, 0.91f, 0.985f);
    private static readonly Color CreamBorder = new Color(0.76f, 0.63f, 0.46f);
    private static readonly Color Salmon = new Color(0.98f, 0.43f, 0.40f);
    private static readonly Color SalmonHighlight = new Color(1f, 0.55f, 0.49f);
    private static readonly Color SalmonPressed = new Color(0.86f, 0.30f, 0.29f);
    private static readonly Color DisabledButton = new Color(0.73f, 0.67f, 0.63f);

    private InputField nameInput;
    private Text guidanceText;
    private Text countText;
    private Text confirmLabel;
    private Button confirmButton;
    private Animator previewAnimator;
    private Transform previewDogTransform;
    private Font interfaceFont;
    private Sprite roundedSprite;
    private Texture2D roundedTexture;
    private CanvasGroup fadeGroup;
    private Coroutine previewIdleRoutine;
    private PreviewPose previewPose;
    private string currentPreviewIdleState;
    private bool applyingSanitizedText;
    private bool transitionStarted;
    private string lastValidatedVisibleName;
    private Keyboard imeKeyboard;
    private string imeCompositionText = string.Empty;
    private GameObject backgroundObject;
    private Material backgroundMaterial;
    private Camera backgroundCamera;
    private float lastBackgroundAspect = -1f;
    private bool xrStageAligned;

    public static bool HasCompletedAdoption =>
        PlayerPrefs.GetInt(AdoptionCompletedKey, 0) == 1;

    public static string SavedDogName =>
        PlayerPrefs.GetString(DogNameKey, string.Empty);

    private void Awake()
    {
        if (ShouldSkipAdoption())
        {
            SceneManager.LoadScene(ResolveGameplaySceneName());
            return;
        }

        PrepareBackground();
        PreparePreviewDog();
        CreateInterface();
        BindImeKeyboard();
    }

    private void OnDisable()
    {
        UnbindImeKeyboard();
    }

    private void OnDestroy()
    {
        ReleaseBackground();
    }

    private IEnumerator Start()
    {
        if (ShouldSkipAdoption() || nameInput == null)
            yield break;

        yield return null;
#if UNITY_ANDROID && !UNITY_EDITOR
        // Opening Meta's overlay keyboard during the first scene frame is unreliable.
        // Leave the field unselected so an XR controller click performs a fresh request.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
#else
        nameInput.ActivateInputField();
        nameInput.Select();
#endif

        if (previewAnimator != null)
            previewIdleRoutine = StartCoroutine(PlayPreviewIdleVariety());
    }

    private void Update()
    {
        if (transitionStarted || nameInput == null)
            return;

        UpdateBackgroundLayout();
        TryAlignQuestStage();
        BindImeKeyboard();
        RefreshNameValidation();

        if (Keyboard.current == null)
            return;

        if ((Keyboard.current.enterKey.wasPressedThisFrame ||
             Keyboard.current.numpadEnterKey.wasPressedThisFrame) &&
            string.IsNullOrEmpty(imeCompositionText) &&
            confirmButton != null && confirmButton.interactable)
        {
            ConfirmAdoption();
        }
    }

    public void ConfirmAdoption()
    {
        if (transitionStarted || nameInput == null || confirmButton == null)
            return;

        string sanitizedName = SanitizeName(nameInput.text).Trim();
        if (!IsValidName(sanitizedName))
        {
            UpdateConfirmAvailability(sanitizedName);
            nameInput.ActivateInputField();
            return;
        }

        nameInput.SetTextWithoutNotify(sanitizedName);
        PlayerPrefs.SetString(DogNameKey, sanitizedName);
        PlayerPrefs.SetInt(AdoptionCompletedKey, 1);
        PlayerPrefs.Save();

        transitionStarted = true;
        nameInput.interactable = false;
        confirmButton.interactable = false;
        confirmLabel.text = "만나러 가는 중...";
        guidanceText.color = new Color(0.30f, 0.50f, 0.31f);
        guidanceText.text = sanitizedName + "(이)와 이제 함께해요!";
        StartCoroutine(PlayWelcomeAndEnterPlayScene());
    }

    private void OnNameChanged(string value)
    {
        if (applyingSanitizedText)
            return;

        string sanitized = SanitizeName(value);
        if (sanitized != value)
        {
            applyingSanitizedText = true;
            nameInput.SetTextWithoutNotify(sanitized);
            nameInput.caretPosition = sanitized.Length;
            applyingSanitizedText = false;
        }

        RefreshNameValidation(true);
    }

    private void RefreshNameValidation(bool force = false)
    {
        string visibleName = GetVisibleNameForValidation();
        if (!force && visibleName == lastValidatedVisibleName)
            return;

        lastValidatedVisibleName = visibleName;
        UpdateConfirmAvailability(visibleName);
    }

    private string GetVisibleNameForValidation()
    {
        string committedText = SanitizeName(nameInput != null ? nameInput.text : string.Empty);
        string compositionText = SanitizeName(imeCompositionText);
        if (string.IsNullOrEmpty(compositionText) || nameInput == null || !nameInput.isFocused)
            return committedText;

        // Unity UI keeps the active IME syllable outside InputField.text until the
        // composition is committed. Insert it at the caret so Korean input updates
        // the counter and button on the same frame as the visible text.
        int insertionIndex = Mathf.Clamp(nameInput.caretPosition, 0, committedText.Length);
        return committedText.Insert(insertionIndex, compositionText);
    }

    private void BindImeKeyboard()
    {
        Keyboard currentKeyboard = Keyboard.current;
        if (imeKeyboard == currentKeyboard)
            return;

        UnbindImeKeyboard();
        imeKeyboard = currentKeyboard;
        if (imeKeyboard != null)
            imeKeyboard.onIMECompositionChange += OnImeCompositionChanged;
    }

    private void UnbindImeKeyboard()
    {
        if (imeKeyboard != null)
            imeKeyboard.onIMECompositionChange -= OnImeCompositionChanged;

        imeKeyboard = null;
        imeCompositionText = string.Empty;
    }

    private void OnImeCompositionChanged(
        UnityEngine.InputSystem.LowLevel.IMECompositionString composition)
    {
        imeCompositionText = composition.ToString();
        RefreshNameValidation(true);
    }

    private void UpdateConfirmAvailability(string value)
    {
        string trimmed = string.IsNullOrEmpty(value) ? string.Empty : value.Trim();
        int length = GetTextElementCount(trimmed);
        bool valid = length >= MinimumNameLength && length <= MaximumNameLength;

        if (confirmButton != null)
            confirmButton.interactable = valid && !transitionStarted;

        if (countText != null)
        {
            countText.text = length + " / " + MaximumNameLength;
            countText.color = valid ? MutedCocoa : new Color(0.78f, 0.42f, 0.37f);
        }

        if (guidanceText == null || transitionStarted)
            return;

        guidanceText.color = valid ? MutedCocoa : new Color(0.74f, 0.39f, 0.34f);
        if (length == 0)
            guidanceText.text = "2~6글자 이름을 입력해주세요";
        else if (length < MinimumNameLength)
            guidanceText.text = "한 글자만 더 입력하면 함께할 수 있어요";
        else if (length > MaximumNameLength)
            guidanceText.text = "이름은 6글자 이내로 입력해주세요";
        else
            guidanceText.text = "좋아요! 이 이름으로 함께할 수 있어요";
    }

    private IEnumerator PlayWelcomeAndEnterPlayScene()
    {
        // 현재 재생 중인 동작이 한 사이클을 마칠 때까지 기다린 뒤 환영 동작을 시작합니다.
        if (previewIdleRoutine != null)
        {
            yield return previewIdleRoutine;
            previewIdleRoutine = null;
        }

        if (previewAnimator != null && previewPose != PreviewPose.Standing)
        {
            string standUpState = previewPose == PreviewPose.Sitting
                ? previewSittingEndState
                : previewLyingEndState;
            previewPose = PreviewPose.Standing;
            PlayPreviewState(standUpState, 0.12f);
            yield return new WaitForSecondsRealtime(GetPreviewStateDuration(standUpState, 0.75f));
        }

        PlayPreviewState(previewHappyStartState, 0.12f);
        yield return new WaitForSecondsRealtime(
            GetPreviewStateDuration(previewHappyStartState, 0.65f));

        PlayPreviewState(previewHappyLoopState, 0.12f);

        yield return new WaitForSecondsRealtime(
            GetWholeCycleDuration(
                previewHappyLoopState,
                new Vector2(playSceneTransitionDelay, playSceneTransitionDelay),
                playSceneTransitionDelay));

        // 새 씬에서도 페이드 인을 마무리할 수 있도록 전환 담당 오브젝트만 잠시 유지합니다.
        DontDestroyOnLoad(gameObject);
        if (fadeGroup != null)
        {
            fadeGroup.blocksRaycasts = true;
            yield return FadeTo(1f, fadeOutDuration);
        }

        // The XR camera persists between scenes, so its child background would also
        // persist. Remove both the quad and its runtime material while the fade is black.
        ReleaseBackground();

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(ResolveGameplaySceneName());
        if (loadOperation != null)
        {
            while (!loadOperation.isDone)
                yield return null;
        }

        // PlayScene의 카메라가 첫 프레임을 그린 뒤 검은 화면을 걷어 냅니다.
        yield return null;
        yield return null;

        if (fadeGroup != null)
        {
            yield return FadeTo(0f, fadeInDuration);
            fadeGroup.blocksRaycasts = false;
        }

        Destroy(gameObject);
    }

    private IEnumerator PlayPreviewIdleVariety()
    {
        currentPreviewIdleState = previewIdleState;
        previewPose = PreviewPose.Standing;
        yield return new WaitForSecondsRealtime(
            GetWholeCycleDuration(previewIdleState, previewStandingDurationRange, 2f));

        while (!transitionStarted && previewAnimator != null)
        {
            float sittingChance = Mathf.Clamp01(previewSittingChance);
            float lyingChance = Mathf.Clamp01(previewLyingChance);
            float choice = Random.value;

            if (choice < sittingChance)
                yield return PlayPreviewRestSequence(true);
            else if (choice < sittingChance + lyingChance)
                yield return PlayPreviewRestSequence(false);
            else
                yield return PlayPreviewStandingIdle();

        }
    }

    private IEnumerator PlayPreviewStandingIdle()
    {
        previewPose = PreviewPose.Standing;
        string nextState = ChoosePreviewState(previewStandingIdleStates, currentPreviewIdleState);
        if (!string.IsNullOrEmpty(nextState))
        {
            currentPreviewIdleState = nextState;
            PlayPreviewState(nextState, 0.18f);
        }

        yield return new WaitForSecondsRealtime(
            GetWholeCycleDuration(nextState, previewStandingDurationRange, 3f));
    }

    private IEnumerator PlayPreviewRestSequence(bool sitting)
    {
        string startState = sitting ? previewSittingStartState : previewLyingStartState;
        string[] loopStates = sitting ? previewSittingLoopStates : previewLyingLoopStates;
        string endState = sitting ? previewSittingEndState : previewLyingEndState;

        previewPose = sitting ? PreviewPose.Sitting : PreviewPose.Lying;
        PlayPreviewState(startState, 0.18f);
        yield return new WaitForSecondsRealtime(GetPreviewStateDuration(startState, sitting ? 0.75f : 0.9f));
        if (transitionStarted)
            yield break;

        string loopState = ChoosePreviewState(loopStates, string.Empty);
        PlayPreviewState(loopState, 0.15f);
        yield return new WaitForSecondsRealtime(
            GetWholeCycleDuration(loopState, previewRestDurationRange, 4f));
        if (transitionStarted)
            yield break;

        previewPose = PreviewPose.Standing;
        PlayPreviewState(endState, 0.15f);
        yield return new WaitForSecondsRealtime(GetPreviewStateDuration(endState, sitting ? 0.75f : 0.9f));
    }

    private void PlayPreviewState(string stateName, float blendDuration)
    {
        if (previewAnimator == null || string.IsNullOrEmpty(stateName))
            return;

        previewAnimator.CrossFadeInFixedTime(stateName, blendDuration, 0);
    }

    private float GetPreviewStateDuration(string stateName, float fallback)
    {
        if (previewAnimator == null ||
            previewAnimator.runtimeAnimatorController == null ||
            string.IsNullOrEmpty(stateName))
        {
            return fallback;
        }

        AnimationClip[] clips = previewAnimator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && clips[i].name == stateName)
                return Mathf.Max(0.2f, clips[i].length);
        }

        return fallback;
    }

    private float GetWholeCycleDuration(string stateName, Vector2 targetRange, float fallback)
    {
        float clipDuration = GetPreviewStateDuration(stateName, fallback);
        float targetDuration = RandomDuration(targetRange, fallback);
        int completedCycles = Mathf.Max(1, Mathf.RoundToInt(targetDuration / clipDuration));
        return clipDuration * completedCycles;
    }

    private static string ChoosePreviewState(string[] states, string excludedState)
    {
        if (states == null || states.Length == 0)
            return string.Empty;

        int availableCount = 0;
        for (int i = 0; i < states.Length; i++)
        {
            if (!string.IsNullOrEmpty(states[i]) && states[i] != excludedState)
                availableCount++;
        }

        if (availableCount == 0)
            return states[0];

        int selected = Random.Range(0, availableCount);
        for (int i = 0; i < states.Length; i++)
        {
            if (string.IsNullOrEmpty(states[i]) || states[i] == excludedState)
                continue;

            if (selected-- == 0)
                return states[i];
        }

        return states[0];
    }

    private static float RandomDuration(Vector2 range, float fallback)
    {
        float minimum = Mathf.Max(0.1f, Mathf.Min(range.x, range.y));
        float maximum = Mathf.Max(minimum, Mathf.Max(range.x, range.y));
        return maximum > minimum ? Random.Range(minimum, maximum) : Mathf.Max(minimum, fallback);
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        float startAlpha = fadeGroup.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            progress = progress * progress * (3f - 2f * progress);
            fadeGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, progress);
            yield return null;
        }

        fadeGroup.alpha = targetAlpha;
    }

    private void PreparePreviewDog()
    {
        GameObject previewDog = GameObject.Find(previewDogName);
        if (previewDog == null)
            return;

        previewDogTransform = previewDog.transform;
        previewAnimator = previewDog.GetComponent<Animator>();
        Collider[] colliders = previewDog.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Rigidbody[] bodies = previewDog.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].isKinematic = true;
            bodies[i].useGravity = false;
        }

        if (previewAnimator != null)
        {
            previewAnimator.applyRootMotion = false;
            previewAnimator.CrossFadeInFixedTime(previewIdleState, 0.05f);
        }

    }

    private void TryAlignQuestStage()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (xrStageAligned || previewDogTransform == null)
            return;

        Camera xrCamera = Camera.main;
        if (xrCamera == null || !xrCamera.stereoEnabled)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(xrCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            return;

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 targetPosition = xrCamera.transform.position + forward * 2.2f - right * 0.72f;
        targetPosition.y = xrCamera.transform.position.y - 1.35f;

        previewDogTransform.position = targetPosition;
        previewDogTransform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        xrStageAligned = true;
        UpdateBackgroundLayout(true);
#endif
    }

    private void PrepareBackground()
    {
        if (adoptionBackgroundTexture == null)
            return;

        backgroundCamera = Camera.main;
        if (backgroundCamera == null)
            backgroundCamera = FindFirstObjectByType<Camera>();
        if (backgroundCamera == null)
            return;

        backgroundObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backgroundObject.name = "Adoption Background";
        backgroundObject.transform.SetParent(backgroundCamera.transform, false);

        Collider backgroundCollider = backgroundObject.GetComponent<Collider>();
        if (backgroundCollider != null)
            Destroy(backgroundCollider);

        Shader backgroundShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (backgroundShader == null)
            backgroundShader = Shader.Find("Unlit/Texture");
        if (backgroundShader == null)
        {
            Destroy(backgroundObject);
            backgroundObject = null;
            return;
        }

        backgroundMaterial = new Material(backgroundShader)
        {
            name = "Adoption Background Material (Runtime)",
            mainTexture = adoptionBackgroundTexture
        };
        if (backgroundMaterial.HasProperty("_BaseMap"))
            backgroundMaterial.SetTexture("_BaseMap", adoptionBackgroundTexture);
        if (backgroundMaterial.HasProperty("_BaseColor"))
            backgroundMaterial.SetColor("_BaseColor", Color.white);
        if (backgroundMaterial.HasProperty("_Cull"))
            backgroundMaterial.SetFloat("_Cull", 0f);

        MeshRenderer renderer = backgroundObject.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = backgroundMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        adoptionBackgroundTexture.wrapMode = TextureWrapMode.Clamp;
        UpdateBackgroundLayout(true);
    }

    private void UpdateBackgroundLayout(bool force = false)
    {
        if (backgroundObject == null || backgroundCamera == null ||
            backgroundMaterial == null || adoptionBackgroundTexture == null)
            return;

        float viewportAspect = Mathf.Max(0.01f, backgroundCamera.aspect);
        if (!force && Mathf.Abs(viewportAspect - lastBackgroundAspect) < 0.0001f)
            return;

        lastBackgroundAspect = viewportAspect;
        float distance = Mathf.Clamp(
            backgroundDistance,
            backgroundCamera.nearClipPlane + 0.5f,
            backgroundCamera.farClipPlane - 1f);
#if UNITY_ANDROID && !UNITY_EDITOR
        if (backgroundCamera.stereoEnabled)
            distance = Mathf.Min(distance, 5f);
#endif
        float height = 2f * distance * Mathf.Tan(
            backgroundCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float width = height * viewportAspect;

        Transform backgroundTransform = backgroundObject.transform;
        backgroundTransform.localPosition = new Vector3(0f, 0f, distance);
        backgroundTransform.localRotation = Quaternion.identity;
        backgroundTransform.localScale = new Vector3(width * 1.01f, height * 1.01f, 1f);

        float textureAspect = (float)adoptionBackgroundTexture.width /
                              Mathf.Max(1, adoptionBackgroundTexture.height);
        Vector2 uvScale = Vector2.one;
        if (textureAspect > viewportAspect)
            uvScale.x = viewportAspect / textureAspect;
        else
            uvScale.y = textureAspect / viewportAspect;

        uvScale /= Mathf.Max(1f, backgroundZoom);
        Vector2 uvOffset = (Vector2.one - uvScale) * 0.5f + backgroundUvOffset;
        backgroundMaterial.mainTextureScale = uvScale;
        backgroundMaterial.mainTextureOffset = uvOffset;
        if (backgroundMaterial.HasProperty("_BaseMap"))
        {
            backgroundMaterial.SetTextureScale("_BaseMap", uvScale);
            backgroundMaterial.SetTextureOffset("_BaseMap", uvOffset);
        }
    }

    private void ReleaseBackground()
    {
        if (backgroundObject != null)
        {
            backgroundObject.SetActive(false);
            Destroy(backgroundObject);
            backgroundObject = null;
        }

        if (backgroundMaterial != null)
        {
            Destroy(backgroundMaterial);
            backgroundMaterial = null;
        }

        backgroundCamera = null;
    }

    private void CreateInterface()
    {
        interfaceFont = regularInterfaceFont != null
            ? regularInterfaceFont
            : CreateKoreanCapableFont();
        roundedSprite = CreateRoundedRectSprite();
        EnsureEventSystem();

        GameObject canvasObject = new GameObject(
            "Adoption UI",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        canvasObject.AddComponent<QuestWorldSpaceCanvasAdapter>();

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = CreatePanel(canvasObject.transform);
        CreateHeader(panel);

        Text description = CreateCenteredText(
            "Description",
            panel,
            "우리 가족이 될 강아지의 이름을 지어주세요",
            25,
            FontStyle.Normal,
            new Vector2(0f, -76f),
            new Vector2(670f, 42f));
        description.color = MutedCocoa;

        nameInput = CreateNameInput(panel);

        Text changeHint = CreateCenteredText(
            "Change Hint",
            panel,
            "이름은 나중에도 변경할 수 있어요",
            21,
            FontStyle.Normal,
            new Vector2(0f, -246f),
            new Vector2(650f, 34f));
        changeHint.color = new Color(0.47f, 0.37f, 0.30f);

        guidanceText = CreateCenteredText(
            "Guidance",
            panel,
            string.Empty,
            21,
            FontStyle.Bold,
            new Vector2(0f, -288f),
            new Vector2(650f, 36f));

        confirmButton = CreateConfirmButton(panel);
        confirmButton.onClick.AddListener(ConfirmAdoption);
        UpdateConfirmAvailability(string.Empty);

        CreateFadeOverlay();
    }

    private RectTransform CreatePanel(Transform parent)
    {
        Image panelImage = CreateRoundedImage("Name Card", parent, CreamBorder);
        RectTransform panel = panelImage.rectTransform;
        // 왼쪽의 강아지 영역을 침범하지 않도록 화면 오른쪽 가장자리를 기준으로 배치합니다.
        panel.anchorMin = new Vector2(1f, 0.5f);
        panel.anchorMax = new Vector2(1f, 0.5f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.anchoredPosition = new Vector2(-120f, 0f);
        panel.sizeDelta = new Vector2(780f, 500f);

        Shadow shadow = panelImage.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.30f, 0.20f, 0.14f, 0.28f);
        shadow.effectDistance = new Vector2(0f, -12f);
        shadow.useGraphicAlpha = true;

        Image fill = CreateRoundedImage("Card Fill", panel, Cream);
        Stretch(fill.rectTransform, 5f);
        return panel;
    }

    private void CreateHeader(RectTransform panel)
    {
        Image header = CreateRoundedImage("Title Badge", panel, Salmon);
        RectTransform rect = header.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 20f);
        rect.sizeDelta = new Vector2(560f, 100f);

        Shadow shadow = header.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.42f, 0.16f, 0.12f, 0.35f);
        shadow.effectDistance = new Vector2(0f, -7f);

        Outline outline = header.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.80f, 0.31f, 0.29f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);

        Text title = CreateCenteredText(
            "Title",
            rect,
            "이름을 정해주세요",
            40,
            FontStyle.Bold,
            new Vector2(0f, -50f),
            new Vector2(410f, 76f));
        title.color = Cocoa;

        CreatePaw(rect, new Vector2(-222f, 0f));
        CreatePaw(rect, new Vector2(222f, 0f));
    }

    private InputField CreateNameInput(RectTransform parent)
    {
        Image border = CreateRoundedImage("Dog Name Input Border", parent, CreamBorder);
        RectTransform borderRect = border.rectTransform;
        borderRect.anchorMin = new Vector2(0.5f, 1f);
        borderRect.anchorMax = new Vector2(0.5f, 1f);
        borderRect.pivot = new Vector2(0.5f, 0.5f);
        borderRect.anchoredPosition = new Vector2(0f, -166f);
        borderRect.sizeDelta = new Vector2(620f, 84f);

        Image inputImage = CreateRoundedImage("Dog Name Input", borderRect, new Color(1f, 1f, 1f, 0.96f));
        Stretch(inputImage.rectTransform, 5f);
        inputImage.raycastTarget = true;
        InputField input = inputImage.gameObject.AddComponent<InputField>();
        inputImage.gameObject.AddComponent<QuestSystemKeyboardBridge>();
        inputImage.gameObject.AddComponent<QuestNameInputPad>();
        input.targetGraphic = inputImage;

        Text inputText = CreateStretchText(
            "Text",
            inputImage.rectTransform,
            string.Empty,
            32,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Vector2(36f, 8f),
            new Vector2(-82f, -8f));
        inputText.color = Cocoa;

        Text placeholder = CreateStretchText(
            "Placeholder",
            inputImage.rectTransform,
            "이름을 입력하세요",
            29,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            new Vector2(36f, 8f),
            new Vector2(-82f, -8f));
        placeholder.color = new Color(0.69f, 0.64f, 0.58f, 0.75f);

        countText = CreateCenteredText(
            "Character Count",
            inputImage.rectTransform,
            "0 / 6",
            18,
            FontStyle.Bold,
            new Vector2(258f, -42f),
            new Vector2(72f, 40f));
        countText.color = new Color(0.78f, 0.42f, 0.37f);

        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = InputField.LineType.SingleLine;
        // 글자소 단위로 직접 제한하여 한글과 조합 문자를 중간에서 자르지 않습니다.
        input.characterLimit = 0;
        input.onValueChanged.AddListener(OnNameChanged);
        return input;
    }

    private Button CreateConfirmButton(RectTransform parent)
    {
        Image image = CreateRoundedImage("Confirm", parent, Color.white);
        image.raycastTarget = true;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -410f);
        rect.sizeDelta = new Vector2(360f, 80f);

        Shadow shadow = image.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.42f, 0.16f, 0.12f, 0.34f);
        shadow.effectDistance = new Vector2(0f, -7f);

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Salmon;
        colors.highlightedColor = SalmonHighlight;
        colors.selectedColor = SalmonHighlight;
        colors.pressedColor = SalmonPressed;
        colors.disabledColor = DisabledButton;
        colors.fadeDuration = 0.12f;
        colors.colorMultiplier = 1f;
        button.colors = colors;

        confirmLabel = CreateCenteredText(
            "Label",
            rect,
            "함께하기",
            30,
            FontStyle.Bold,
            new Vector2(0f, -40f),
            new Vector2(330f, 70f));
        confirmLabel.color = Color.white;
        Shadow labelShadow = confirmLabel.gameObject.AddComponent<Shadow>();
        labelShadow.effectColor = new Color(0.35f, 0.12f, 0.10f, 0.38f);
        labelShadow.effectDistance = new Vector2(1f, -2f);
        return button;
    }

    private void CreatePaw(Transform parent, Vector2 position)
    {
        GameObject paw = new GameObject("Paw", typeof(RectTransform));
        paw.transform.SetParent(parent, false);
        RectTransform pawRect = paw.GetComponent<RectTransform>();
        pawRect.anchorMin = pawRect.anchorMax = new Vector2(0.5f, 0.5f);
        pawRect.anchoredPosition = position;
        pawRect.sizeDelta = new Vector2(48f, 48f);

        CreatePawDot(pawRect, new Vector2(0f, -8f), new Vector2(24f, 21f));
        CreatePawDot(pawRect, new Vector2(-13f, 9f), new Vector2(11f, 14f));
        CreatePawDot(pawRect, new Vector2(-4f, 16f), new Vector2(11f, 14f));
        CreatePawDot(pawRect, new Vector2(7f, 16f), new Vector2(11f, 14f));
        CreatePawDot(pawRect, new Vector2(16f, 8f), new Vector2(11f, 14f));
    }

    private void CreatePawDot(Transform parent, Vector2 position, Vector2 size)
    {
        Image dot = CreateRoundedImage("Paw Dot", parent, new Color(0.76f, 0.22f, 0.21f, 0.75f));
        RectTransform rect = dot.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private void CreateFadeOverlay()
    {
        GameObject fadeObject = new GameObject(
            "Scene Transition Fade",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup),
            typeof(Image));
        fadeObject.transform.SetParent(transform, false);

        Canvas canvas = fadeObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;

        CanvasScaler scaler = fadeObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Image image = fadeObject.GetComponent<Image>();
        image.color = new Color(0.075f, 0.055f, 0.045f, 1f);
        image.raycastTarget = true;

        RectTransform rect = fadeObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        fadeGroup = fadeObject.GetComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;
        fadeGroup.interactable = false;
    }

    private Image CreateRoundedImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.sprite = roundedSprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private Text CreateCenteredText(
        string objectName,
        Transform parent,
        string value,
        int fontSize,
        FontStyle fontStyle,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Text text = ConfigureText(textObject.GetComponent<Text>(), value, fontSize, fontStyle, TextAnchor.MiddleCenter);
        return text;
    }

    private Text CreateStretchText(
        string objectName,
        Transform parent,
        string value,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return ConfigureText(textObject.GetComponent<Text>(), value, fontSize, fontStyle, alignment);
    }

    private Text ConfigureText(Text text, string value, int fontSize, FontStyle fontStyle, TextAnchor alignment)
    {
        bool requestsBold = fontStyle == FontStyle.Bold || fontStyle == FontStyle.BoldAndItalic;
        bool usesDedicatedBoldFont = requestsBold && boldInterfaceFont != null;
        text.font = usesDedicatedBoldFont ? boldInterfaceFont : interfaceFont;
        text.text = value;
        text.fontSize = fontSize;
        // Maplestory Bold 자체가 굵은 서체이므로 Unity의 합성 Bold를 다시 겹치지 않습니다.
        text.fontStyle = usesDedicatedBoldFont ? FontStyle.Normal : fontStyle;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = Cocoa;
        return text;
    }

    private Sprite CreateRoundedRectSprite()
    {
        const int size = 64;
        const float radius = 15f;
        roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Adoption UI Rounded Rectangle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

        Color[] pixels = new Color[size * size];
        float half = size * 0.5f;
        float innerExtent = half - radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - innerExtent, 0f);
                float qy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - innerExtent, 0f);
                float distance = Mathf.Sqrt(qx * qx + qy * qy) - radius;
                float alpha = Mathf.Clamp01(0.5f - distance);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        roundedTexture.SetPixels(pixels);
        roundedTexture.Apply(false, false);
        Sprite sprite = Sprite.Create(
            roundedTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(16f, 16f, 16f, 16f));
        sprite.name = "Adoption UI Rounded Sprite";
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
        InputSystemUIInputModule inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
        inputModule.AssignDefaultActions();
    }

    private string ResolveGameplaySceneName()
    {
        bool isQuest3 = SystemInfo.deviceModel.IndexOf(
                            "Quest 3",
                            System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool canLoadQuest3 = Application.CanStreamedLevelBeLoaded(quest3SceneName);
        bool canLoadQuest2 = Application.CanStreamedLevelBeLoaded(quest2SceneName);

        // A Quest build profile contains only its own platform wrapper. This also
        // lets the desktop MR Simulator profile select Q3 without spoofing a device model.
        if (canLoadQuest3 && (isQuest3 || !canLoadQuest2))
            return quest3SceneName;
        if (canLoadQuest2)
            return quest2SceneName;
        if (canLoadQuest3)
            return quest3SceneName;
        return playSceneName;
    }

    private static Font CreateKoreanCapableFont()
    {
        string[] candidates =
        {
            "Noto Sans CJK KR",
            "Noto Sans KR",
            "Malgun Gothic",
            "Arial Unicode MS",
            "Arial"
        };
        Font font = Font.CreateDynamicFontFromOSFont(candidates, 44);
        return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static bool IsValidName(string value)
    {
        int length = GetTextElementCount(value);
        return length >= MinimumNameLength && length <= MaximumNameLength;
    }

    private bool ShouldSkipAdoption()
    {
#if UNITY_EDITOR
        if (showAdoptionWhenTestingInEditor &&
            SceneManager.GetActiveScene().name == gameObject.scene.name)
        {
            return false;
        }
#endif
        return HasCompletedAdoption;
    }

    private static int GetTextElementCount(string value)
    {
        return string.IsNullOrEmpty(value) ? 0 : StringInfo.ParseCombiningCharacters(value).Length;
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // 길이를 자동으로 잘라내지 않습니다. 7자 이상도 화면에 유지하여
        // 사용자가 직접 수정할 수 있게 하고, 버튼 활성 조건에서 거절합니다.
        return value.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }
}
