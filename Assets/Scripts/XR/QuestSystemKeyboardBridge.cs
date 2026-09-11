using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Keeps a Unity InputField connected to Meta's Quest system keyboard overlay.
/// The keyboard is requested from select, pointer click, and submit so a failed
/// early request can always be retried with a controller.
/// OculusProjectConfig.requiresSystemKeyboard must remain enabled for Android builds.
/// </summary>
[RequireComponent(typeof(InputField))]
[DisallowMultipleComponent]
public sealed class QuestSystemKeyboardBridge : MonoBehaviour,
    ISelectHandler,
    IPointerClickHandler,
    ISubmitHandler
{
    private InputField inputField;
    private TouchScreenKeyboard keyboard;
    private Coroutine openRoutine;

    private void Awake()
    {
        inputField = GetComponent<InputField>();
    }

    private void Update()
    {
        if (keyboard == null || inputField == null)
            return;

        TouchScreenKeyboard.Status status = keyboard.status;
        if (status == TouchScreenKeyboard.Status.Visible)
        {
            if (inputField.text != keyboard.text)
                inputField.text = keyboard.text;
            return;
        }

        if (status == TouchScreenKeyboard.Status.Done)
        {
            if (inputField.text != keyboard.text)
                inputField.text = keyboard.text;
        }

        if (status == TouchScreenKeyboard.Status.Done ||
            status == TouchScreenKeyboard.Status.Canceled ||
            status == TouchScreenKeyboard.Status.LostFocus)
        {
            keyboard = null;
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        RequestKeyboard();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        RequestKeyboard();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        RequestKeyboard();
    }

    private void RequestKeyboard()
    {
        if (!isActiveAndEnabled || inputField == null || !inputField.interactable ||
            !TouchScreenKeyboard.isSupported)
        {
            return;
        }

        if (keyboard != null && keyboard.status == TouchScreenKeyboard.Status.Visible)
            return;

        if (openRoutine != null)
            StopCoroutine(openRoutine);
        openRoutine = StartCoroutine(OpenKeyboardAfterInputFieldActivation());
    }

    private IEnumerator OpenKeyboardAfterInputFieldActivation()
    {
        // Let InputField process the XR pointer selection first. On current Unity
        // versions it opens the Quest keyboard itself, so reuse that instance.
        inputField.ActivateInputField();
        yield return null;

        keyboard = inputField.touchScreenKeyboard;
        if (keyboard == null ||
            keyboard.status == TouchScreenKeyboard.Status.Canceled ||
            keyboard.status == TouchScreenKeyboard.Status.LostFocus)
        {
            TouchScreenKeyboard.hideInput = false;
            keyboard = TouchScreenKeyboard.Open(
                inputField.text,
                TouchScreenKeyboardType.Default,
                true,
                false,
                false,
                false,
                string.Empty,
                0);
        }

        openRoutine = null;
    }

    private void OnDisable()
    {
        if (openRoutine != null)
        {
            StopCoroutine(openRoutine);
            openRoutine = null;
        }

        if (keyboard != null && keyboard.status == TouchScreenKeyboard.Status.Visible)
            keyboard.active = false;
        keyboard = null;
    }
}