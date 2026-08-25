using UnityEngine;

/// <summary>
/// Small finite-state controller for the placeholder dog.
/// The dog alternates between idle, wandering and looking at the player.
/// </summary>
public sealed class PrototypePetAgent : MonoBehaviour
{
    private enum PetState
    {
        Idle,
        Wander,
        LookAtPlayer
    }

    [Header("Movement")]
    [Min(0.1f)] public float walkSpeed = 0.7f;
    [Min(0.1f)] public float turnSpeed = 240f;
    [Min(0.1f)] public float roomHalfExtent = 3.3f;
    [Min(0.05f)] public float targetReachDistance = 0.2f;

    [Header("Directional Head Look")]
    public bool useDirectionalHeadLook = true;
    [Min(1f)] public float headTurnSpeed = 360f;
    [Tooltip("Maximum combined yaw of the neck and head.")]
    [Range(5f, 45f)] public float headYawLimit = 28f;
    [Range(0.05f, 0.95f)] public float neckTurnShare = 0.35f;
    [Range(0.05f, 1f)] public float bodyTurnSpeedWhileHeadLeads = 0.3f;
    [Tooltip("The body starts following once the remaining horizontal error is larger than this angle.")]
    [Range(0f, 45f)] public float bodyTurnDeadZone = 7f;

    [Header("State Durations")]
    public Vector2 idleDuration = new Vector2(1.5f, 4f);
    public Vector2 wanderDuration = new Vector2(3f, 7f);
    public Vector2 lookAtDuration = new Vector2(1.5f, 3.5f);
    [Range(0f, 1f)] public float lookAtPlayerProbability = 0.12f;

    private CharacterController characterController;
    private Animator animator;
    private Transform movementRoot;
    private Camera playerCamera;
    private PetState currentState;
    private Vector3 wanderTarget;
    private float stateTimeRemaining;
    private bool interactionFocus;
    private bool interactionHeadLookEnabled;
    private bool carried;
    private Transform headTransform;
    private Transform neckTransform;
    private Quaternion headRestRotation;
    private Quaternion neckRestRotation;
    private bool lookBonesResolved;

    private const string IdleAnimationState = "A_CartoonAnimal-Dog_Idle";
    private const string RunAnimationState = "A_CartoonAnimal-Dog_Run";
    private const string MovingParameter = "IsMoving";

    public Transform MovementRoot => movementRoot != null ? movementRoot : transform;

    public bool IsCarried => carried;

    /// <summary>
    /// Temporarily takes control away from the random AI so the dog can face
    /// the player during a prompt or a pet interaction.
    /// </summary>
    public void SetInteractionFocus(bool focused, bool enableHeadLook = true)
    {
        if (interactionFocus == focused && interactionHeadLookEnabled == (focused && enableHeadLook))
            return;

        interactionFocus = focused;
        interactionHeadLookEnabled = focused && enableHeadLook;
        if (focused)
        {
            currentState = PetState.LookAtPlayer;
            PlayAnimation(IdleAnimationState);
        }
        else
        {
            EnterRandomState();
        }
    }

    /// <summary>
    /// Suspends autonomous movement while the dog is attached to the player's hold anchor.
    /// </summary>
    public void SetCarried(bool value)
    {
        if (carried == value)
            return;

        carried = value;
        interactionFocus = false;
        interactionHeadLookEnabled = false;

        if (carried)
        {
            if (characterController != null)
                characterController.enabled = false;

            PlayAnimation(IdleAnimationState);
            return;
        }

        if (characterController != null)
            characterController.enabled = true;

        EnterRandomState();
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        movementRoot = transform;

        // The imported controller animates the prefab root. Give locomotion its own
        // parent so the animation can keep its local pose without resetting movement.
        if (animator != null && animator.gameObject == gameObject)
        {
            float height = characterController != null ? characterController.height : 1.2f;
            float radius = characterController != null ? characterController.radius : 0.35f;
            Vector3 center = characterController != null ? characterController.center : new Vector3(0f, 0.6f, 0f);
            float stepOffset = characterController != null ? characterController.stepOffset : 0.2f;
            float skinWidth = characterController != null ? characterController.skinWidth : 0.03f;

            GameObject movementObject = new GameObject("Dog Movement Root (Runtime)");
            movementObject.transform.SetParent(transform.parent, true);
            movementObject.transform.SetPositionAndRotation(transform.position, transform.rotation);
            movementObject.transform.localScale = Vector3.one;

            transform.SetParent(movementObject.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            movementRoot = movementObject.transform;

            if (characterController != null)
            {
                characterController.enabled = false;
            }

            characterController = movementObject.AddComponent<CharacterController>();
            characterController.height = height;
            characterController.radius = radius;
            characterController.center = center;
            characterController.stepOffset = stepOffset;
            characterController.skinWidth = skinWidth;
        }

        ResolveLookBones();
    }

    private void Start()
    {
        playerCamera = Camera.main;
        EnterRandomState();
    }

    // The imported dog controller writes the model root during the normal animation update.
    // Running locomotion in LateUpdate keeps the AI movement after that visual update.
    private void LateUpdate()
    {
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
        {
            playerCamera = Camera.main;
        }

        if (carried)
            return;

        if (interactionFocus)
        {
            // During the prompt the head leads within its realistic yaw cone.
            // The body catches up once the player is outside that cone. The
            // actual pet animation can temporarily suppress this to own the head.
            UpdateLookAtPlayer(interactionHeadLookEnabled);
            return;
        }

        stateTimeRemaining -= Time.deltaTime;

        switch (currentState)
        {
            case PetState.Idle:
                KeepGrounded();
                UpdateDirectionalHeadLook(movementRoot.forward);
                break;
            case PetState.Wander:
                UpdateWander();
                break;
            case PetState.LookAtPlayer:
                UpdateLookAtPlayer(true);
                break;
        }

        if (stateTimeRemaining <= 0f)
        {
            EnterRandomState();
        }
    }

    private void EnterRandomState()
    {
        float roll = Random.value;
        float lookAtChance = playerCamera != null ? lookAtPlayerProbability : 0f;

        if (roll < lookAtChance)
        {
            currentState = PetState.LookAtPlayer;
            stateTimeRemaining = Random.Range(lookAtDuration.x, lookAtDuration.y);
            PlayAnimation(IdleAnimationState);
            return;
        }

        if (roll < lookAtChance + 0.38f)
        {
            currentState = PetState.Idle;
            stateTimeRemaining = Random.Range(idleDuration.x, idleDuration.y);
            PlayAnimation(IdleAnimationState);
            return;
        }

        currentState = PetState.Wander;
        stateTimeRemaining = Random.Range(wanderDuration.x, wanderDuration.y);
        wanderTarget = new Vector3(
            Random.Range(-roomHalfExtent, roomHalfExtent),
            movementRoot.position.y,
            Random.Range(-roomHalfExtent, roomHalfExtent));
        PlayAnimation(RunAnimationState);
    }

    private void UpdateWander()
    {
        Vector3 direction = wanderTarget - movementRoot.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= targetReachDistance * targetReachDistance)
        {
            EnterRandomState();
            return;
        }

        PlayAnimation(RunAnimationState);
        float bodyTurnSpeed = UpdateDirectionalHeadLook(direction);
        RotateTowards(direction, bodyTurnSpeed);
        CollisionFlags collisionFlags = characterController != null
            ? characterController.Move(movementRoot.forward * (walkSpeed * Time.deltaTime) + Vector3.down * 0.02f)
            : CollisionFlags.None;

        if ((collisionFlags & CollisionFlags.Sides) != 0)
        {
            EnterRandomState();
        }
    }

    private void UpdateLookAtPlayer(bool allowHeadLook)
    {
        PlayAnimation(IdleAnimationState);
        KeepGrounded();

        if (playerCamera == null)
        {
            return;
        }

        Vector3 direction = playerCamera.transform.position - movementRoot.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.01f)
        {
            float bodyTurnSpeed = allowHeadLook
                ? UpdateDirectionalHeadLook(direction)
                : turnSpeed;

            // Let the head take small corrections first. Once the remaining
            // error is larger than the small dead zone, the body follows at
            // the deliberately slower head-leading speed.
            bool shouldTurnBody = !allowHeadLook ||
                Mathf.Abs(GetRelativeYaw(direction)) > bodyTurnDeadZone;
            if (shouldTurnBody)
                RotateTowards(direction, bodyTurnSpeed);
        }
    }

    private float GetRelativeYaw(Vector3 direction)
    {
        direction.y = 0f;
        Vector3 localDirection = movementRoot.InverseTransformDirection(direction.normalized);
        return Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
    }

    private void KeepGrounded()
    {
        if (characterController != null && !characterController.isGrounded)
        {
            characterController.Move(Vector3.down * (9.81f * Time.deltaTime));
        }
    }

    private float UpdateDirectionalHeadLook(Vector3 direction)
    {
        if (!useDirectionalHeadLook || movementRoot == null)
            return turnSpeed;

        ResolveLookBones();
        if (headTransform == null && neckTransform == null)
            return turnSpeed;

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            direction = movementRoot.forward;

        Vector3 localDirection = movementRoot.InverseTransformDirection(direction.normalized);
        float desiredYaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        float limitedYaw = Mathf.Clamp(desiredYaw, -headYawLimit, headYawLimit);
        float bodyAlignment = Mathf.InverseLerp(
            bodyTurnDeadZone,
            headYawLimit,
            Mathf.Abs(limitedYaw));
        float bodyTurnSpeed = Mathf.Lerp(
            turnSpeed * bodyTurnSpeedWhileHeadLeads,
            turnSpeed,
            bodyAlignment);

        float maxStep = headTurnSpeed * Time.deltaTime;
        float neckYaw = limitedYaw * neckTurnShare;
        float headYaw = limitedYaw - neckYaw;

        if (neckTransform != null)
        {
            Quaternion neckTarget = neckRestRotation * Quaternion.Euler(0f, neckYaw, 0f);
            neckTransform.localRotation = Quaternion.RotateTowards(
                neckTransform.localRotation,
                neckTarget,
                maxStep);
        }

        if (headTransform != null)
        {
            Quaternion headTarget = headRestRotation * Quaternion.Euler(0f, headYaw, 0f);
            headTransform.localRotation = Quaternion.RotateTowards(
                headTransform.localRotation,
                headTarget,
                maxStep);
        }

        return bodyTurnSpeed;
    }

    private void ResolveLookBones()
    {
        if (lookBonesResolved)
            return;

        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform candidate = children[i];
            if (candidate == transform)
                continue;

            if (headTransform == null && IsBoneName(candidate.name, "Head", "Face"))
                headTransform = candidate;

            if (neckTransform == null && IsBoneName(candidate.name, "Neck"))
                neckTransform = candidate;
        }

        if (headTransform != null)
            headRestRotation = headTransform.localRotation;
        if (neckTransform != null)
            neckRestRotation = neckTransform.localRotation;

        lookBonesResolved = true;
    }

    private static bool IsBoneName(string candidateName, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (candidateName.Equals(names[i], System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void RotateTowards(Vector3 direction, float currentTurnSpeed)
    {
        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        movementRoot.rotation = Quaternion.RotateTowards(
            movementRoot.rotation,
            targetRotation,
            currentTurnSpeed * Time.deltaTime);
    }

    private void PlayAnimation(string stateName)
    {
        if (animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }

        int stateHash = Animator.StringToHash(stateName);

        for (int i = 0; i < animator.parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = animator.parameters[i];
            if (parameter.name == MovingParameter && parameter.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(MovingParameter, stateName == RunAnimationState);
                break;
            }
        }

        if (!animator.HasState(0, stateHash) || animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
        {
            return;
        }

        // The imported controller can evaluate its transition graph in the same
        // frame as this AI update. Play the requested locomotion state directly
        // so the visual state cannot be held on Idle while the dog is moving.
        animator.Play(stateHash, 0, 0f);
    }
}
