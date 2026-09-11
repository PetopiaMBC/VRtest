using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>Converts the runtime Adoption canvas into controller-ray UI on an XR display.</summary>
[RequireComponent(typeof(Canvas))]
[DisallowMultipleComponent]
public sealed class QuestWorldSpaceCanvasAdapter : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float distanceFromHead = 1.15f;
    [SerializeField] private Vector2 worldResolution = new Vector2(1920f, 1080f);
    [SerializeField, Min(0.0001f)] private float worldScale = 0.0007f;

    private bool configured;

    private void Update()
    {
        if (!configured && IsXRDisplayRunning())
            ConfigureForXR();
    }

    private void ConfigureForXR()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;

        Canvas canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.sortingOrder = 20;

        RectTransform rect = (RectTransform)transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = worldResolution;
        rect.localScale = Vector3.one * worldScale;
        rect.position = camera.transform.position + camera.transform.forward * distanceFromHead;

        // Keep the same readable orientation as the original Quest canvas. The XR
        // raycaster below explicitly accepts either face for interaction.
        rect.rotation = Quaternion.LookRotation(rect.position - camera.transform.position, Vector3.up);

        GraphicRaycaster desktopRaycaster = GetComponent<GraphicRaycaster>();
        if (desktopRaycaster != null)
            desktopRaycaster.enabled = false;

        TrackedDeviceGraphicRaycaster xrRaycaster = GetComponent<TrackedDeviceGraphicRaycaster>();
        if (xrRaycaster == null)
            xrRaycaster = gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        xrRaycaster.ignoreReversedGraphics = false;
        xrRaycaster.checkFor2DOcclusion = false;
        xrRaycaster.checkFor3DOcclusion = false;

        if (GetComponent<QuestControllerUiFallback>() == null)
            gameObject.AddComponent<QuestControllerUiFallback>();

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        InputSystemUIInputModule desktopInput = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (desktopInput != null)
            desktopInput.enabled = false;

        XRUIInputModule xrInput = eventSystem.GetComponent<XRUIInputModule>();
        if (xrInput == null)
            xrInput = eventSystem.gameObject.AddComponent<XRUIInputModule>();
        xrInput.enableXRInput = true;
        configured = true;
    }

    private static bool IsXRDisplayRunning()
    {
        List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        for (int i = 0; i < displays.Count; i++)
        {
            if (displays[i] != null && displays[i].running)
                return true;
        }
        return false;
    }
}