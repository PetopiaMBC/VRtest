using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Quest controller fallback for runtime world-space UI.
/// Controller trigger uses the controller ray; A/X can click the UI under head gaze.
/// This stays independent from XRUIInputModule so onboarding can never be blocked.
/// </summary>
[RequireComponent(typeof(Canvas))]
[DisallowMultipleComponent]
public sealed class QuestControllerUiFallback : MonoBehaviour
{
    private const float MaximumDistance = 10f;
    private const float DuplicateClickGuard = 0.2f;

    private Canvas canvas;
    private Camera xrCamera;
    private Transform rightAim;
    private Transform leftAim;
    private bool previousRightTrigger;
    private bool previousLeftTrigger;
    private bool previousRightPrimary;
    private bool previousLeftPrimary;
    private int lastTargetId;
    private float lastClickTime = -10f;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
    }

    private void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (canvas == null)
            return;

        if (xrCamera == null)
            xrCamera = Camera.main;
        if (xrCamera == null)
            return;

        CacheAimTransforms();

        InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        bool rightTriggerDown = ReadButtonDown(rightDevice, CommonUsages.triggerButton, ref previousRightTrigger);
        bool leftTriggerDown = ReadButtonDown(leftDevice, CommonUsages.triggerButton, ref previousLeftTrigger);
        bool rightPrimaryDown = ReadButtonDown(rightDevice, CommonUsages.primaryButton, ref previousRightPrimary);
        bool leftPrimaryDown = ReadButtonDown(leftDevice, CommonUsages.primaryButton, ref previousLeftPrimary);

        if (rightTriggerDown)
            ClickFromAim(rightAim);
        if (leftTriggerDown)
            ClickFromAim(leftAim);
        if (rightPrimaryDown || leftPrimaryDown)
            ClickFromRay(new Ray(xrCamera.transform.position, xrCamera.transform.forward));
#endif
    }

    private void ClickFromAim(Transform aim)
    {
        if (aim != null && ClickFromRay(new Ray(aim.position, aim.forward)))
            return;

        ClickFromRay(new Ray(xrCamera.transform.position, xrCamera.transform.forward));
    }

    private bool ClickFromRay(Ray ray)
    {
        Selectable target = FindTarget(ray);
        if (target == null)
            return false;

        int targetId = target.gameObject.GetInstanceID();
        if (targetId == lastTargetId && Time.unscaledTime - lastClickTime < DuplicateClickGuard)
            return true;

        lastTargetId = targetId;
        lastClickTime = Time.unscaledTime;

        Button button = target as Button;
        if (button != null)
        {
            button.onClick.Invoke();
            return true;
        }

        EventSystem eventSystem = EventSystem.current;
        InputField input = target as InputField;
        if (eventSystem != null)
        {
            eventSystem.SetSelectedGameObject(target.gameObject);
            PointerEventData pointer = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                eligibleForClick = true,
                position = xrCamera.WorldToScreenPoint(ray.GetPoint(1f))
            };
            ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        else if (input != null)
        {
            input.ActivateInputField();
        }

        return true;
    }

    private Selectable FindTarget(Ray ray)
    {
        Selectable[] selectables = canvas.GetComponentsInChildren<Selectable>(false);
        Selectable best = null;
        float bestDistance = float.PositiveInfinity;
        int bestDepth = -1;

        for (int i = 0; i < selectables.Length; i++)
        {
            Selectable candidate = selectables[i];
            if (candidate == null || !candidate.IsActive() || !candidate.IsInteractable())
                continue;

            RectTransform rect = candidate.transform as RectTransform;
            if (rect == null)
                continue;

            Plane plane = new Plane(rect.forward, rect.position);
            if (!plane.Raycast(ray, out float distance) || distance < 0.02f || distance > MaximumDistance)
                continue;

            Vector3 localPoint = rect.InverseTransformPoint(ray.GetPoint(distance));
            if (!rect.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
                continue;

            int depth = GetDepth(rect);
            if (distance < bestDistance - 0.01f ||
                (Mathf.Abs(distance - bestDistance) <= 0.01f && depth > bestDepth))
            {
                best = candidate;
                bestDistance = distance;
                bestDepth = depth;
            }
        }

        return best;
    }

    private void CacheAimTransforms()
    {
        if (rightAim != null && leftAim != null)
            return;

        Transform searchRoot = xrCamera.transform.root;
        Transform[] transforms = searchRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current.name != "Near-Far Interactor")
                continue;

            string parentName = current.parent != null ? current.parent.name : string.Empty;
            if (rightAim == null && parentName.Contains("Right"))
                rightAim = current;
            else if (leftAim == null && parentName.Contains("Left"))
                leftAim = current;
        }
    }

    private static bool ReadButtonDown(InputDevice device, InputFeatureUsage<bool> usage, ref bool previous)
    {
        bool current = device.isValid && device.TryGetFeatureValue(usage, out bool value) && value;
        bool pressedThisFrame = current && !previous;
        previous = current;
        return pressedThisFrame;
    }

    private static int GetDepth(Transform target)
    {
        int depth = 0;
        while (target.parent != null)
        {
            depth++;
            target = target.parent;
        }
        return depth;
    }
}