using System.Collections;
using UnityEngine;

/// <summary>
/// Quest 2 smooth locomotion. The left stick moves relative to the headset,
/// the right stick turns around the headset, and a CharacterController keeps
/// the player inside the authored room and on collidable floors.
/// </summary>
[DisallowMultipleComponent]
public sealed class Q2VirtualRoomLocomotion : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrOrigin;
    [SerializeField] private Transform head;
    [SerializeField] private BoxCollider playerMoveArea;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float movementSpeed = 1.5f;
    [SerializeField, Range(0f, 1f)] private float movementDeadzone = 0.15f;

    [Header("Turning")]
    [SerializeField, Min(0f)] private float turnSpeed = 90f;
    [SerializeField, Range(0f, 1f)] private float turnDeadzone = 0.2f;
    [SerializeField] private bool faceRoomCenterOnStart = true;
    [SerializeField, Range(1, 10)] private int initialViewAlignmentDelayFrames = 3;

    [Header("Collision")]
    [SerializeField, Min(0.05f)] private float playerRadius = 0.22f;
    [SerializeField, Min(0.1f)] private float minimumPlayerHeight = 0.8f;
    [SerializeField, Min(0.1f)] private float maximumPlayerHeight = 2.1f;
    [SerializeField, Min(0f)] private float boundaryPadding = 0.02f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float groundedVelocity = -1f;

    private CharacterController characterController;
    private float verticalVelocity;
    private bool initialViewAligned;
    private Coroutine initialViewAlignmentRoutine;

    public void Configure(Transform origin, Transform headTransform, BoxCollider moveArea)
    {
        xrOrigin = origin;
        head = headTransform;
        playerMoveArea = moveArea;
        ResolveCharacterController();

        if (faceRoomCenterOnStart && !initialViewAligned)
        {
            if (initialViewAlignmentRoutine != null)
                StopCoroutine(initialViewAlignmentRoutine);
            initialViewAlignmentRoutine = StartCoroutine(AlignInitialViewToRoomCenter());
        }
    }

    private IEnumerator AlignInitialViewToRoomCenter()
    {
        int delayFrames = Mathf.Max(1, initialViewAlignmentDelayFrames);
        for (int i = 0; i < delayFrames; i++)
            yield return null;

        ResolveReferences();
        if (xrOrigin == null || head == null || playerMoveArea == null)
        {
            initialViewAlignmentRoutine = null;
            yield break;
        }

        Vector3 currentForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (currentForward.sqrMagnitude < 0.0001f)
            currentForward = Vector3.ProjectOnPlane(xrOrigin.forward, Vector3.up);

        Vector3 directionToCenter = playerMoveArea.bounds.center - head.position;
        directionToCenter.y = 0f;
        if (directionToCenter.sqrMagnitude < 0.04f)
            directionToCenter = Vector3.ProjectOnPlane(playerMoveArea.transform.forward, Vector3.up);

        if (currentForward.sqrMagnitude > 0.0001f &&
            directionToCenter.sqrMagnitude > 0.0001f)
        {
            float yaw = Vector3.SignedAngle(
                currentForward.normalized,
                directionToCenter.normalized,
                Vector3.up);

            // Rotate around the tracked head so the player's world position does
            // not jump while the initial view is redirected toward the room.
            xrOrigin.RotateAround(head.position, Vector3.up, yaw);
        }

        verticalVelocity = 0f;
        initialViewAligned = true;
        initialViewAlignmentRoutine = null;
    }

    private void Update()
    {
        XRPlayerInputStatePublisher input = XRPlayerInputStatePublisher.Instance;
        if (input == null || input.DeviceMode != PlayerDeviceMode.Quest2VR)
            return;

        ResolveReferences();
        if (xrOrigin == null || head == null || playerMoveArea == null)
            return;

        ResolveCharacterController();
        if (characterController == null || !characterController.enabled)
            return;

        UpdateCharacterControllerShape();
        ApplySmoothTurn(input.RightState.PrimaryAxis.x);

        Vector3 horizontalMovement = CalculateHorizontalMovement(input.LeftState.PrimaryAxis);
        horizontalMovement = ConstrainMovementToPlayArea(horizontalMovement);
        MoveWithCollision(horizontalMovement);
        KeepHeadInsidePlayArea();
    }

    private void ApplySmoothTurn(float rawAxis)
    {
        float turnInput = ApplyDeadzone(rawAxis, turnDeadzone);
        if (Mathf.Approximately(turnInput, 0f))
            return;

        float yaw = turnInput * turnSpeed * Time.deltaTime;
        xrOrigin.RotateAround(head.position, Vector3.up, yaw);
    }

    private Vector3 CalculateHorizontalMovement(Vector2 rawAxis)
    {
        float magnitude = rawAxis.magnitude;
        if (magnitude <= movementDeadzone)
            return Vector3.zero;

        Vector2 axis = rawAxis.normalized *
                       Mathf.InverseLerp(movementDeadzone, 1f, Mathf.Clamp01(magnitude));

        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        return (right * axis.x + forward * axis.y) * movementSpeed * Time.deltaTime;
    }

    private void MoveWithCollision(Vector3 horizontalMovement)
    {
        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = groundedVelocity;
        else
            verticalVelocity += gravity * Time.deltaTime;

        Vector3 movement = horizontalMovement +
                           Vector3.up * (verticalVelocity * Time.deltaTime);
        CollisionFlags flags = characterController.Move(movement);
        if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            verticalVelocity = groundedVelocity;
    }

    private Vector3 ConstrainMovementToPlayArea(Vector3 movement)
    {
        Bounds bounds = playerMoveArea.bounds;
        float margin = playerRadius + boundaryPadding;
        Vector3 requestedHead = head.position + movement;
        Vector3 clampedHead = ClampToBounds(requestedHead, bounds, margin);
        Vector3 constrained = clampedHead - head.position;
        constrained.y = 0f;
        return constrained;
    }

    private void KeepHeadInsidePlayArea()
    {
        Bounds bounds = playerMoveArea.bounds;
        float margin = playerRadius + boundaryPadding;
        Vector3 clampedHead = ClampToBounds(head.position, bounds, margin);
        Vector3 correction = clampedHead - head.position;
        correction.y = 0f;

        if (correction.sqrMagnitude > 0.000001f)
            characterController.Move(correction);
    }

    private static Vector3 ClampToBounds(Vector3 position, Bounds bounds, float margin)
    {
        float minX = bounds.min.x + margin;
        float maxX = bounds.max.x - margin;
        float minZ = bounds.min.z + margin;
        float maxZ = bounds.max.z - margin;

        if (minX > maxX)
            minX = maxX = bounds.center.x;
        if (minZ > maxZ)
            minZ = maxZ = bounds.center.z;

        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.z = Mathf.Clamp(position.z, minZ, maxZ);
        return position;
    }

    private void UpdateCharacterControllerShape()
    {
        Vector3 localHead = xrOrigin.InverseTransformPoint(head.position);
        float minimumHeight = Mathf.Max(minimumPlayerHeight, playerRadius * 2f);
        float height = Mathf.Clamp(localHead.y, minimumHeight, maximumPlayerHeight);

        characterController.radius = playerRadius;
        characterController.height = height;
        characterController.center = new Vector3(
            localHead.x,
            height * 0.5f + characterController.skinWidth,
            localHead.z);
    }

    private void ResolveReferences()
    {
        if (head == null && Camera.main != null)
            head = Camera.main.transform;
        if (xrOrigin == null && head != null)
            xrOrigin = head.root;
    }

    private void ResolveCharacterController()
    {
        if (xrOrigin == null)
            return;
        if (characterController != null && characterController.transform == xrOrigin)
            return;

        characterController = xrOrigin.GetComponent<CharacterController>();
        if (characterController == null)
            characterController = xrOrigin.gameObject.AddComponent<CharacterController>();

        characterController.detectCollisions = true;
        characterController.radius = playerRadius;
        characterController.skinWidth = Mathf.Min(0.05f, playerRadius * 0.25f);
        characterController.stepOffset = 0.2f;
        characterController.slopeLimit = 45f;
        characterController.minMoveDistance = 0f;
    }

    private static float ApplyDeadzone(float value, float deadzone)
    {
        float magnitude = Mathf.Abs(value);
        if (magnitude <= deadzone)
            return 0f;

        float scaled = Mathf.InverseLerp(deadzone, 1f, Mathf.Clamp01(magnitude));
        return Mathf.Sign(value) * scaled;
    }
}
