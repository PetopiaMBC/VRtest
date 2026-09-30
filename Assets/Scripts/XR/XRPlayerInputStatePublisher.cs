using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

/// <summary>
/// Reads Oculus Touch / Touch Plus through Unity XR and publishes one shared input contract.
/// No gameplay behavior or keyboard fallback lives here.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public sealed class XRPlayerInputStatePublisher : MonoBehaviour
{
    [SerializeField] private PlayerDeviceMode deviceMode = PlayerDeviceMode.Quest2VR;
    [SerializeField] private Transform trackingOrigin;

    private bool previousLeftGrip;
    private bool previousRightGrip;
    private bool previousLeftPrimaryButton;
    private bool previousRightPrimaryButton;
    private bool previousLeftSecondaryButton;
    private bool previousRightSecondaryButton;
    private Vector3 previousLeftSimulatedPosition;
    private Vector3 previousRightSimulatedPosition;
    private Quaternion previousLeftSimulatedRotation = Quaternion.identity;
    private Quaternion previousRightSimulatedRotation = Quaternion.identity;
    private double previousLeftSimulatedTimestamp;
    private double previousRightSimulatedTimestamp;
    private bool hasPreviousLeftSimulatedPose;
    private bool hasPreviousRightSimulatedPose;

    public static XRPlayerInputStatePublisher Instance { get; private set; }
    public PlayerDeviceMode DeviceMode => deviceMode;
    public PlayerInputState LeftState { get; private set; }
    public PlayerInputState RightState { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        UpdateDeviceMode(SceneManager.GetActiveScene());
        ResolveTrackingOrigin();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        Instance = null;
    }

    private void Update()
    {
        ResolveTrackingOrigin();
        LeftState = ReadController(
            XRNode.LeftHand,
            previousLeftGrip,
            previousLeftPrimaryButton,
            previousLeftSecondaryButton);
        RightState = ReadController(
            XRNode.RightHand,
            previousRightGrip,
            previousRightPrimaryButton,
            previousRightSecondaryButton);
        previousLeftGrip = LeftState.GripHeld;
        previousRightGrip = RightState.GripHeld;
        previousLeftPrimaryButton = LeftState.PrimaryButtonHeld;
        previousRightPrimaryButton = RightState.PrimaryButtonHeld;
        previousLeftSecondaryButton = LeftState.SecondaryButtonHeld;
        previousRightSecondaryButton = RightState.SecondaryButtonHeld;
    }

    public PlayerInputState GetState(PlayerHand hand)
    {
        return hand == PlayerHand.Left ? LeftState : RightState;
    }

    private PlayerInputState ReadController(
        XRNode node,
        bool previousGrip,
        bool previousPrimaryButton,
        bool previousSecondaryButton)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        bool hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPosition);
        bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion localRotation);
        bool hasTrackingFlag = device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked);
        device.TryGetFeatureValue(CommonUsages.deviceVelocity, out Vector3 localVelocity);
        device.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out Vector3 localAngularVelocity);
        device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 primaryAxis);
        device.TryGetFeatureValue(CommonUsages.trigger, out float triggerValue);
        device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripHeld);
        device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryButtonHeld);
        device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondaryButtonHeld);

        bool trackingValid = device.isValid && hasPosition && hasRotation &&
                             (!hasTrackingFlag || isTracked);
        if (!trackingValid && TryReadSimulatedController(
                node,
                previousGrip,
                previousPrimaryButton,
                previousSecondaryButton,
                out PlayerInputState simulatedState))
            return simulatedState;

        Vector3 worldPosition = trackingOrigin != null
            ? trackingOrigin.TransformPoint(localPosition)
            : localPosition;
        Quaternion worldRotation = trackingOrigin != null
            ? trackingOrigin.rotation * localRotation
            : localRotation;
        Vector3 worldVelocity = trackingOrigin != null
            ? trackingOrigin.TransformDirection(localVelocity)
            : localVelocity;
        Vector3 worldAngularVelocity = trackingOrigin != null
            ? trackingOrigin.TransformDirection(localAngularVelocity)
            : localAngularVelocity;

        return new PlayerInputState
        {
            ControllerPose = new Pose(worldPosition, worldRotation),
            ControllerVelocity = worldVelocity,
            ControllerAngularVelocity = worldAngularVelocity,
            PrimaryAxis = primaryAxis,
            TriggerValue = triggerValue,
            GripHeld = gripHeld,
            GripPressed = gripHeld && !previousGrip,
            GripReleased = !gripHeld && previousGrip,
            PrimaryButtonHeld = primaryButtonHeld,
            PrimaryButtonPressed = primaryButtonHeld && !previousPrimaryButton,
            SecondaryButtonHeld = secondaryButtonHeld,
            SecondaryButtonPressed = secondaryButtonHeld && !previousSecondaryButton,
            IsTrackingValid = trackingValid,
            DeviceMode = deviceMode,
            Timestamp = Time.unscaledTimeAsDouble
        };
    }

    /// <summary>
    /// XR Interaction Simulator devices only exist in the Input System and are not
    /// returned by UnityEngine.XR.InputDevices. Read them as a fallback so Editor
    /// simulation uses the same contact, grab, and throw gameplay path as Quest.
    /// </summary>
    private bool TryReadSimulatedController(
        XRNode node,
        bool previousGrip,
        bool previousPrimaryButton,
        bool previousSecondaryButton,
        out PlayerInputState state)
    {
        state = default;
        XRSimulatedController controller = FindSimulatedController(node);
        if (controller == null || !controller.added || !controller.enabled)
            return false;

        Vector3 localPosition = controller.devicePosition.ReadValue();
        Quaternion localRotation = controller.deviceRotation.ReadValue();
        bool trackingValid = controller.isTracked == null || controller.isTracked.isPressed;
        CalculateSimulatedVelocity(
            node,
            localPosition,
            localRotation,
            out Vector3 localVelocity,
            out Vector3 localAngularVelocity);

        Vector3 worldPosition = trackingOrigin != null
            ? trackingOrigin.TransformPoint(localPosition)
            : localPosition;
        Quaternion worldRotation = trackingOrigin != null
            ? trackingOrigin.rotation * localRotation
            : localRotation;
        Vector3 worldVelocity = trackingOrigin != null
            ? trackingOrigin.TransformDirection(localVelocity)
            : localVelocity;
        Vector3 worldAngularVelocity = trackingOrigin != null
            ? trackingOrigin.TransformDirection(localAngularVelocity)
            : localAngularVelocity;

        bool gripHeld = controller.gripButton.isPressed || controller.grip.ReadValue() >= 0.5f;
        bool primaryButtonHeld = controller.primaryButton.isPressed;
        bool secondaryButtonHeld = controller.secondaryButton.isPressed;

        state = new PlayerInputState
        {
            ControllerPose = new Pose(worldPosition, worldRotation),
            ControllerVelocity = worldVelocity,
            ControllerAngularVelocity = worldAngularVelocity,
            PrimaryAxis = controller.primary2DAxis.ReadValue(),
            TriggerValue = controller.trigger.ReadValue(),
            GripHeld = gripHeld,
            GripPressed = gripHeld && !previousGrip,
            GripReleased = !gripHeld && previousGrip,
            PrimaryButtonHeld = primaryButtonHeld,
            PrimaryButtonPressed = primaryButtonHeld && !previousPrimaryButton,
            SecondaryButtonHeld = secondaryButtonHeld,
            SecondaryButtonPressed = secondaryButtonHeld && !previousSecondaryButton,
            IsTrackingValid = trackingValid,
            DeviceMode = deviceMode,
            Timestamp = Time.unscaledTimeAsDouble
        };
        return true;
    }

    private static XRSimulatedController FindSimulatedController(XRNode node)
    {
        var requiredUsage = node == XRNode.LeftHand
            ? UnityEngine.InputSystem.CommonUsages.LeftHand
            : UnityEngine.InputSystem.CommonUsages.RightHand;
        var devices = UnityEngine.InputSystem.InputSystem.devices;
        for (int i = 0; i < devices.Count; i++)
        {
            if (!(devices[i] is XRSimulatedController controller))
                continue;

            for (int usageIndex = 0; usageIndex < controller.usages.Count; usageIndex++)
            {
                if (controller.usages[usageIndex] == requiredUsage)
                    return controller;
            }
        }

        return null;
    }

    private void CalculateSimulatedVelocity(
        XRNode node,
        Vector3 position,
        Quaternion rotation,
        out Vector3 velocity,
        out Vector3 angularVelocity)
    {
        double now = Time.unscaledTimeAsDouble;
        bool isLeft = node == XRNode.LeftHand;
        bool hasPrevious = isLeft
            ? hasPreviousLeftSimulatedPose
            : hasPreviousRightSimulatedPose;
        Vector3 previousPosition = isLeft
            ? previousLeftSimulatedPosition
            : previousRightSimulatedPosition;
        Quaternion previousRotation = isLeft
            ? previousLeftSimulatedRotation
            : previousRightSimulatedRotation;
        double previousTimestamp = isLeft
            ? previousLeftSimulatedTimestamp
            : previousRightSimulatedTimestamp;

        float deltaTime = hasPrevious ? (float)(now - previousTimestamp) : 0f;
        velocity = deltaTime > 0.0001f
            ? (position - previousPosition) / deltaTime
            : Vector3.zero;
        angularVelocity = deltaTime > 0.0001f
            ? CalculateAngularVelocity(previousRotation, rotation, deltaTime)
            : Vector3.zero;

        if (isLeft)
        {
            previousLeftSimulatedPosition = position;
            previousLeftSimulatedRotation = rotation;
            previousLeftSimulatedTimestamp = now;
            hasPreviousLeftSimulatedPose = true;
        }
        else
        {
            previousRightSimulatedPosition = position;
            previousRightSimulatedRotation = rotation;
            previousRightSimulatedTimestamp = now;
            hasPreviousRightSimulatedPose = true;
        }
    }

    private static Vector3 CalculateAngularVelocity(
        Quaternion previousRotation,
        Quaternion currentRotation,
        float deltaTime)
    {
        Quaternion delta = currentRotation * Quaternion.Inverse(previousRotation);
        delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);
        if (angleDegrees > 180f)
            angleDegrees -= 360f;
        if (Mathf.Abs(angleDegrees) < 0.001f || axis.sqrMagnitude < 0.000001f)
            return Vector3.zero;

        return axis.normalized * (angleDegrees * Mathf.Deg2Rad / deltaTime);
    }

    private void ResolveTrackingOrigin()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.transform.parent != null)
            trackingOrigin = mainCamera.transform.parent;
    }

    private void HandleActiveSceneChanged(Scene previous, Scene next)
    {
        UpdateDeviceMode(next);
        trackingOrigin = null;
    }

    private void UpdateDeviceMode(Scene scene)
    {
        deviceMode = scene.name.IndexOf("Q3", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     scene.name.IndexOf("MR", System.StringComparison.OrdinalIgnoreCase) >= 0
            ? PlayerDeviceMode.Quest3MR
            : PlayerDeviceMode.Quest2VR;
    }
}
