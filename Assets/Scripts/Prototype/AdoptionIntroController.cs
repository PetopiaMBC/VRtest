using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 첫 실행에서만 이름을 받고 입양 정보를 저장하는 임시 도입 화면입니다.
/// 이후 도입 연출을 교체하더라도 저장 키와 PlayScene 진입 계약은 유지할 수 있습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class AdoptionIntroController : MonoBehaviour
{
    public const string AdoptionCompletedKey = "MyLittlePet.AdoptionCompleted";
    public const string DogNameKey = "MyLittlePet.DogName";
    public const int MaximumNameLength = 6;

    [SerializeField] private string playSceneName = "PlayScene";
    [SerializeField] private string previewDogName = "Adoption Shiba Preview";
    [SerializeField] private string previewIdleState = "Arm_Shiba|Idle_2";
    [SerializeField] private string previewHappyStartState = "Arm_Shiba|Idle_5_start";
    [SerializeField] private string previewHappyLoopState = "Arm_Shiba|Idle_5_loop";
    [SerializeField, Min(0f)] private float playSceneTransitionDelay = 1.1f;

    private InputField nameInput;
    private Text guidanceText;
    private Button confirmButton;
    private Animator previewAnimator;
    private Font interfaceFont;
    private bool applyingSanitizedText;
    private bool transitionStarted;

    public static bool HasCompletedAdoption =>
        PlayerPrefs.GetInt(AdoptionCompletedKey, 0) == 1;

    public static string SavedDogName =>
        PlayerPrefs.GetString(DogNameKey, string.Empty);

    private void Awake()
    {
        if (HasCompletedAdoption)
        {
            SceneManager.LoadScene(playSceneName);
            return;
        }

        PreparePreviewDog();
        CreateInterface();
    }

    private IEnumerator Start()
    {
        if (HasCompletedAdoption || nameInput == null)
            yield break;

        yield return null;
        nameInput.ActivateInputField();
        nameInput.Select();
    }

    private void Update()
    {
        if (transitionStarted || nameInput == null || Keyboard.current == null)
            return;

        if (Keyboard.current.enterKey.wasPressedThisFrame ||
            Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            ConfirmAdoption();
    }

    public void ConfirmAdoption()
    {
        if (transitionStarted || nameInput == null)
            return;

        string sanitizedName = SanitizeName(nameInput.text).Trim();
        nameInput.text = sanitizedName;
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            guidanceText.text = "이름을 한 글자 이상 입력해 주세요.";
            guidanceText.color = new Color(0.9f, 0.28f, 0.22f);
            nameInput.ActivateInputField();
            return;
        }

        PlayerPrefs.SetString(DogNameKey, sanitizedName);
        PlayerPrefs.SetInt(AdoptionCompletedKey, 1);
        PlayerPrefs.Save();

        transitionStarted = true;
        nameInput.interactable = false;
        confirmButton.interactable = false;
        guidanceText.color = new Color(0.25f, 0.5f, 0.3f);
        guidanceText.text = sanitizedName + "(이)라는 이름을 기억했어요!";
        StartCoroutine(PlayWelcomeAndEnterPlayScene());
    }

    private void OnNameChanged(string value)
    {
        if (applyingSanitizedText)
            return;

        string sanitized = SanitizeName(value);
        if (sanitized == value)
            return;

        applyingSanitizedText = true;
        nameInput.text = sanitized;
        nameInput.caretPosition = sanitized.Length;
        applyingSanitizedText = false;
    }

    private IEnumerator PlayWelcomeAndEnterPlayScene()
    {
        if (previewAnimator != null)
            previewAnimator.CrossFadeInFixedTime(previewHappyStartState, 0.12f);

        yield return new WaitForSeconds(0.65f);

        if (previewAnimator != null)
            previewAnimator.CrossFadeInFixedTime(previewHappyLoopState, 0.12f);

        yield return new WaitForSeconds(playSceneTransitionDelay);
        SceneManager.LoadScene(playSceneName);
    }

    private void PreparePreviewDog()
    {
        GameObject previewDog = GameObject.Find(previewDogName);
        if (previewDog == null)
            return;

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

    private void CreateInterface()
    {
        interfaceFont = CreateKoreanCapableFont();
        EnsureEventSystem();

        GameObject canvasObject = new GameObject(
            "Adoption UI",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = CreatePanel(canvasObject.transform);
        CreateText(
            "Title",
            panel,
            "새로운 가족을 만났어요",
            52,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Vector2(80f, -55f),
            new Vector2(-80f, -125f));

        guidanceText = CreateText(
            "Guidance",
            panel,
            "강아지의 이름을 정해 주세요. (한글 가능, 최대 6자)",
            28,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            new Vector2(110f, -145f),
            new Vector2(-110f, -195f));
        guidanceText.color = new Color(0.24f, 0.26f, 0.3f);

        nameInput = CreateNameInput(panel);
        confirmButton = CreateConfirmButton(panel);
        confirmButton.onClick.AddListener(ConfirmAdoption);
    }

    private RectTransform CreatePanel(Transform parent)
    {
        GameObject panelObject = new GameObject("Name Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(parent, false);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.5f, 0f);
        panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 45f);
        panel.sizeDelta = new Vector2(920f, 360f);
        panelObject.GetComponent<Image>().color = new Color(1f, 0.98f, 0.94f, 0.96f);
        return panel;
    }

    private InputField CreateNameInput(RectTransform parent)
    {
        GameObject inputObject = new GameObject("Dog Name Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        inputObject.transform.SetParent(parent, false);
        RectTransform rect = inputObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(-105f, -215f);
        rect.sizeDelta = new Vector2(500f, 76f);
        inputObject.GetComponent<Image>().color = Color.white;

        Text inputText = CreateText(
            "Text",
            rect,
            string.Empty,
            34,
            FontStyle.Normal,
            TextAnchor.MiddleLeft,
            new Vector2(24f, 0f),
            new Vector2(-24f, 0f));
        inputText.color = new Color(0.12f, 0.13f, 0.16f);

        Text placeholder = CreateText(
            "Placeholder",
            rect,
            "예: 몽이",
            34,
            FontStyle.Italic,
            TextAnchor.MiddleLeft,
            new Vector2(24f, 0f),
            new Vector2(-24f, 0f));
        placeholder.color = new Color(0.55f, 0.55f, 0.58f, 0.8f);

        InputField input = inputObject.GetComponent<InputField>();
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = MaximumNameLength;
        input.onValueChanged.AddListener(OnNameChanged);
        return input;
    }

    private Button CreateConfirmButton(RectTransform parent)
    {
        GameObject buttonObject = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(280f, -215f);
        rect.sizeDelta = new Vector2(220f, 76f);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.95f, 0.58f, 0.2f);

        Text label = CreateText(
            "Label",
            rect,
            "함께하기",
            30,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Vector2.zero,
            Vector2.zero);
        label.color = Color.white;

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.68f, 0.3f);
        colors.pressedColor = new Color(0.82f, 0.43f, 0.12f);
        button.colors = colors;
        return button;
    }

    private Text CreateText(
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
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Text text = textObject.GetComponent<Text>();
        text.font = interfaceFont;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = new Color(0.12f, 0.13f, 0.16f);
        return text;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
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
        Font font = Font.CreateDynamicFontFromOSFont(candidates, 36);
        return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        int[] textElementStarts = StringInfo.ParseCombiningCharacters(value);
        int elementCount = Mathf.Min(MaximumNameLength, textElementStarts.Length);
        if (elementCount >= textElementStarts.Length)
            return value;

        return value.Substring(0, textElementStarts[elementCount]);
    }
}
