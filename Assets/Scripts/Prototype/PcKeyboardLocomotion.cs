using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Desktop fallback for testing the XR scene without a Quest headset.
/// WASD / arrow keys move the XR Origin relative to the headset view.
/// </summary>
public sealed class PcKeyboardLocomotion : MonoBehaviour
{
    [Header("Keyboard")]
    [Min(0.1f)] public float moveSpeed = 2.5f;
    public bool useHeadRelativeMovement = true;
    public bool allowSprint = true;
    [Min(1f)] public float sprintMultiplier = 1.75f;

    [Header("PC Mouse Look")]
    public bool enableMouseLook = true;
    [Min(0.01f)] public float mouseSensitivity = 0.12f;
    [Range(-89f, -1f)] public float minPitch = -80f;
    [Range(1f, 89f)] public float maxPitch = 80f;
    public bool lockCursorOnPlay = true;

    [Header("Prototype Room Bounds")]
    public Vector3 roomCenter = Vector3.zero;
    [Min(0.5f)] public float roomHalfExtent = 3.55f;

    [Header("Crouch")]
    [Tooltip("C 또는 왼쪽/오른쪽 Ctrl을 누를 때 앉기 상태를 전환합니다.")]
    public bool enableCrouch = true;
    [Min(0.1f)] public float crouchViewDrop = 0.65f;
    [Min(0.1f)] public float crouchTransitionSpeed = 5f;
    [Min(0.5f)] public float crouchedControllerHeight = 1.05f;

    private CharacterController characterController;
    private Transform headTransform;
    private Transform cameraOffsetTransform;
    private float cameraPitch;
    private bool isCrouching;
    private float standingCameraOffsetY;
    private float standingControllerHeight;
    private Vector3 standingControllerCenter;

    public bool IsCrouching => isCrouching;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        headTransform = FindHeadTransform();
        cameraOffsetTransform = FindCameraOffsetTransform();
        cameraPitch = cameraOffsetTransform != null
            ? NormalizeAngle(cameraOffsetTransform.localEulerAngles.x)
            : 0f;
        standingCameraOffsetY = cameraOffsetTransform != null
            ? cameraOffsetTransform.localPosition.y
            : 0f;
        if (characterController != null)
        {
            standingControllerHeight = characterController.height;
            standingControllerCenter = characterController.center;
        }
    }

    private void Start()
    {
        if (enableMouseLook && lockCursorOnPlay)
        {
            LockCursor();
        }
    }

    private void Update()
    {
        UpdateMouseLook();
        UpdateCrouchInput();
        UpdateCrouchPose();

        if (Keyboard.current == null)
        {
            return;
        }

        Vector2 input = ReadKeyboardInput();
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        if (input.sqrMagnitude < 0.001f)
        {
            return;
        }

        if (headTransform == null || !headTransform.gameObject.activeInHierarchy)
        {
            headTransform = FindHeadTransform();
        }

        Vector3 forward = useHeadRelativeMovement && headTransform != null
            ? FlattenDirection(headTransform.forward)
            : FlattenDirection(transform.forward);
        Vector3 right = useHeadRelativeMovement && headTransform != null
            ? FlattenDirection(headTransform.right)
            : FlattenDirection(transform.right);

        Vector3 movement = (forward * input.y + right * input.x).normalized;
        float speed = moveSpeed;
        if (allowSprint && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
        {
            speed *= sprintMultiplier;
        }

        Vector3 delta = movement * (speed * Time.deltaTime);
        if (characterController != null && characterController.enabled)
        {
            characterController.Move(delta);
        }
        else
        {
            transform.position += delta;
        }

        ClampToPrototypeRoom();
    }

    private void UpdateCrouchInput()
    {
        if (!enableCrouch || Keyboard.current == null)
            return;

        bool togglePressed = Keyboard.current.cKey.wasPressedThisFrame ||
                             Keyboard.current.leftCtrlKey.wasPressedThisFrame ||
                             Keyboard.current.rightCtrlKey.wasPressedThisFrame;
        if (togglePressed)
            isCrouching = !isCrouching;
    }

    private void UpdateCrouchPose()
    {
        if (cameraOffsetTransform == null)
        {
            cameraOffsetTransform = FindCameraOffsetTransform();
            if (cameraOffsetTransform == null)
                return;
        }

        float targetOffsetY = standingCameraOffsetY - (isCrouching ? crouchViewDrop : 0f);
        Vector3 offsetPosition = cameraOffsetTransform.localPosition;
        offsetPosition.y = Mathf.MoveTowards(
            offsetPosition.y,
            targetOffsetY,
            crouchTransitionSpeed * Time.deltaTime);
        cameraOffsetTransform.localPosition = offsetPosition;

        if (characterController == null)
            return;

        float targetHeight = isCrouching
            ? Mathf.Min(standingControllerHeight, crouchedControllerHeight)
            : standingControllerHeight;
        float previousHeight = characterController.height;
        characterController.height = Mathf.MoveTowards(
            previousHeight,
            targetHeight,
            crouchTransitionSpeed * Time.deltaTime);

        Vector3 targetCenter = standingControllerCenter;
        targetCenter.y -= (standingControllerHeight - characterController.height) * 0.5f;
        characterController.center = Vector3.MoveTowards(
            characterController.center,
            targetCenter,
            crouchTransitionSpeed * Time.deltaTime);
    }

    private void UpdateMouseLook()
    {
        if (!enableMouseLook || Mouse.current == null)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            UnlockCursor();
        }
        else if (Cursor.lockState != CursorLockMode.Locked && Mouse.current.leftButton.wasPressedThisFrame)
        {
            LockCursor();
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        if (mouseDelta.sqrMagnitude < 0.001f)
        {
            return;
        }

        transform.Rotate(Vector3.up, mouseDelta.x * mouseSensitivity, Space.World);

        if (cameraOffsetTransform == null)
        {
            cameraOffsetTransform = FindCameraOffsetTransform();
        }

        if (cameraOffsetTransform != null)
        {
            cameraPitch = Mathf.Clamp(
                cameraPitch - mouseDelta.y * mouseSensitivity,
                minPitch,
                maxPitch);
            cameraOffsetTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }
    }

    private Transform FindCameraOffsetTransform()
    {
        if (headTransform == null)
        {
            headTransform = FindHeadTransform();
        }

        return headTransform != null ? headTransform.parent : null;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDisable()
    {
        UnlockCursor();
    }

    private static Vector2 ReadKeyboardInput()
    {
        Keyboard keyboard = Keyboard.current;
        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
        {
            horizontal -= 1f;
        }
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
        {
            horizontal += 1f;
        }
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            vertical += 1f;
        }
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            vertical -= 1f;
        }

        return new Vector2(horizontal, vertical);
    }

    private void ClampToPrototypeRoom()
    {
        Vector3 position = transform.position;
        position.x = Mathf.Clamp(position.x, roomCenter.x - roomHalfExtent, roomCenter.x + roomHalfExtent);
        position.z = Mathf.Clamp(position.z, roomCenter.z - roomHalfExtent, roomCenter.z + roomHalfExtent);
        transform.position = position;
    }

    private Transform FindHeadTransform()
    {
        if (Camera.main != null)
        {
            return Camera.main.transform;
        }

        Transform[] children = GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child.name == "Main Camera")
            {
                return child;
            }
        }

        return null;
    }

    private static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
    }
}
