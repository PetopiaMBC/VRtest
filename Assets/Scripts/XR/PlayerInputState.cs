using UnityEngine;

/// <summary>
/// Platform-neutral controller state consumed by gameplay adapters.
/// </summary>
public struct PlayerInputState
{
    public Pose ControllerPose;
    public Vector3 ControllerVelocity;
    public Vector3 ControllerAngularVelocity;
    public Vector2 PrimaryAxis;
    public float TriggerValue;
    public bool GripHeld;
    public bool GripPressed;
    public bool GripReleased;
    public bool IsTrackingValid;
    public PlayerDeviceMode DeviceMode;
    public double Timestamp;
}

public enum PlayerDeviceMode
{
    Quest2VR,
    Quest3MR
}

public enum PlayerHand
{
    Left,
    Right
}
