using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controller-friendly fallback name pad shown only in Android Quest builds.
/// It writes through the existing InputField so adoption validation stays unchanged.
/// </summary>
[RequireComponent(typeof(InputField))]
[DisallowMultipleComponent]
public sealed class QuestNameInputPad : MonoBehaviour
{
    private const float PanelWidth = 900f;
    private const float PanelHeight = 310f;
    private static readonly Color PanelColor = new Color(0.13f, 0.09f, 0.07f, 0.94f);
    private static readonly Color KeyColor = new Color(1f, 0.96f, 0.88f, 1f);
    private static readonly Color AccentColor = new Color(0.98f, 0.43f, 0.40f, 1f);
    private static readonly Color TextColor = new Color(0.30f, 0.16f, 0.11f, 1f);

    private InputField inputField;
    private RectTransform padRoot;
    private Font font;
    private Sprite keySprite;

    private void Awake()
    {
        inputField = GetComponent<InputField>();
    }

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        BuildPad();
#endif
    }

    private void BuildPad()
    {
        if (padRoot != null || inputField == null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        font = inputField.textComponent != null ? inputField.textComponent.font : null;
        Image inputImage = inputField.targetGraphic as Image;
        keySprite = inputImage != null ? inputImage.sprite : null;

        GameObject root = new GameObject("Quest Name Input Pad", typeof(RectTransform), typeof(Image), typeof(Shadow));
        root.transform.SetParent(canvas.transform, false);
        padRoot = root.GetComponent<RectTransform>();
        padRoot.anchorMin = Vector2.zero;
        padRoot.anchorMax = Vector2.zero;
        padRoot.pivot = Vector2.zero;
        padRoot.anchoredPosition = new Vector2(55f, 45f);
        padRoot.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        Image panelImage = root.GetComponent<Image>();
        panelImage.sprite = keySprite;
        panelImage.type = keySprite != null ? Image.Type.Sliced : Image.Type.Simple;
        panelImage.color = PanelColor;
        panelImage.raycastTarget = true;

        Shadow shadow = root.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -8f);

        string[] suggestedNames =
        {
            "\uBCF4\uB9AC", // Bori
            "\uCD08\uCF54", // Choco
            "\uBABD\uC774", // Mongi
            "\uB450\uBD80", // Dubu
            "\uCF69\uC774", // Kongi
            "\uBCC4\uC774"  // Byeori
        };
        CreateRow(suggestedNames, 242f, 126f, 50f, 10f, true);
        CreateRow(new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" }, 166f, 70f, 58f, 8f, false);
        CreateRow(new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L" }, 96f, 70f, 58f, 8f, false);
        CreateRow(new[] { "Z", "X", "C", "V", "B", "N", "M", "<", "CLR" }, 26f, 70f, 58f, 8f, false);
    }

    private void CreateRow(string[] values, float y, float width, float height, float spacing, bool suggestions)
    {
        float totalWidth = values.Length * width + (values.Length - 1) * spacing;
        float x = (PanelWidth - totalWidth) * 0.5f;

        for (int i = 0; i < values.Length; i++)
        {
            string value = values[i];
            Button button = CreateKey(value, new Vector2(x, y), new Vector2(width, height), suggestions);
            button.onClick.AddListener(() => ApplyKey(value, suggestions));
            x += width + spacing;
        }
    }

    private Button CreateKey(string label, Vector2 position, Vector2 size, bool accent)
    {
        GameObject keyObject = new GameObject("Key " + label, typeof(RectTransform), typeof(Image), typeof(Button));
        keyObject.transform.SetParent(padRoot, false);

        RectTransform rect = keyObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = keyObject.GetComponent<Image>();
        image.sprite = keySprite;
        image.type = keySprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = accent ? AccentColor : KeyColor;
        image.raycastTarget = true;

        Button button = keyObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = accent ? new Color(1f, 0.56f, 0.51f, 1f) : Color.white;
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = accent ? new Color(0.84f, 0.30f, 0.28f, 1f) : new Color(0.86f, 0.82f, 0.76f, 1f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(rect, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Text text = labelObject.GetComponent<Text>();
        text.font = font;
        text.text = label;
        text.fontSize = accent ? 23 : 25;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = accent ? Color.white : TextColor;
        text.raycastTarget = false;
        return button;
    }

    private void ApplyKey(string value, bool suggestion)
    {
        if (inputField == null || !inputField.interactable)
            return;

        if (suggestion)
        {
            inputField.text = value;
        }
        else if (value == "<")
        {
            RemoveLastTextElement();
        }
        else if (value == "CLR")
        {
            inputField.text = string.Empty;
        }
        else if (GetTextElementCount(inputField.text) < AdoptionIntroController.MaximumNameLength)
        {
            inputField.text += value;
        }

        inputField.caretPosition = inputField.text.Length;
    }

    private void RemoveLastTextElement()
    {
        string value = inputField.text;
        if (string.IsNullOrEmpty(value))
            return;

        int[] starts = StringInfo.ParseCombiningCharacters(value);
        if (starts.Length == 0)
        {
            inputField.text = string.Empty;
            return;
        }

        inputField.text = value.Substring(0, starts[starts.Length - 1]);
    }

    private static int GetTextElementCount(string value)
    {
        return string.IsNullOrEmpty(value) ? 0 : StringInfo.ParseCombiningCharacters(value).Length;
    }
}