using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

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
