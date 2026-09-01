using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PC용 공 줍기/던지기와 강아지 공놀이 사이를 연결합니다.
/// G로 바라보는 공을 들고, 다시 G를 누르면 카메라 정면으로 던집니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class FetchBallInteraction : MonoBehaviour
{
    [Header("Player Interaction")]
    [SerializeField] private Key interactionKey = Key.G;
    [SerializeField, Min(0.5f)] private float interactionDistance = 4f;
    [SerializeField, Min(0.2f)] private float holdDistance = 0.72f;
    [SerializeField] private float holdVerticalOffset = -0.18f;
    [SerializeField, Min(0.1f)] private float heldFollowSpeed = 24f;

    [Header("Throw")]
    [SerializeField, Min(0.1f)] private float throwSpeed = 7.5f;
    [SerializeField, Min(0f)] private float throwUpwardBoost = 1.35f;
    [SerializeField, Min(0f)] private float throwSpin = 9f;

    [Header("Dog Fetch")]
    [SerializeField, Min(0f)] private float dogPickupDelay = 0.45f;
    [SerializeField, Min(0.1f)] private float dogPickupMaximumSpeed = 4.5f;
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.72f;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private ShepherdCompanionAI companionAI;

    private static FetchBallInteraction playerHeldBall;
    private static int lastConsumedInteractionFrame = -1;

    private Rigidbody ballBody;
    private Collider[] ballColliders;
    private bool heldByPlayer;
    private bool carriedByDog;
    private float lastThrowTime = float.NegativeInfinity;
    private Vector3 heldVelocity;
    private PhysicsMaterial runtimeBallMaterial;

    public static bool IsAnyBallHeldByPlayer => playerHeldBall != null;
    public static bool ConsumedGrabInputThisFrame => lastConsumedInteractionFrame == Time.frameCount;
    public bool IsHeldByPlayer => heldByPlayer;
    public bool IsCarriedByDog => carriedByDog;
    public bool WasThrown => !heldByPlayer && !carriedByDog && lastThrowTime > float.NegativeInfinity;
    public Vector3 WorldPosition => transform.position;

    public bool CanBePickedUpByDog
    {
        get
        {
            if (heldByPlayer || carriedByDog || ballBody == null)
                return false;

            return Time.time - lastThrowTime >= dogPickupDelay &&
                   ballBody.linearVelocity.magnitude <= dogPickupMaximumSpeed;
        }
    }

    private void Awake()
    {
        gameObject.isStatic = false;
        ballBody = GetComponent<Rigidbody>();
        ballColliders = GetComponentsInChildren<Collider>(true);
        ConfigureFreePhysics();
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();

        if (heldByPlayer)
            UpdatePlayerHeldPose();

        if (Keyboard.current == null ||
            !Keyboard.current[interactionKey].wasPressedThisFrame)
            return;

        if (heldByPlayer)
        {
            lastConsumedInteractionFrame = Time.frameCount;
            ThrowFromPlayer();
            return;
        }

        if (playerHeldBall == null && !carriedByDog && IsPlayerLookingAtBall())
        {
            lastConsumedInteractionFrame = Time.frameCount;
            PickUpByPlayer();
        }
    }

    private void LateUpdate()
    {
        if (heldByPlayer)
        {
            UpdatePlayerHeldPose();
            return;
        }

        if (carriedByDog && companionAI != null)
            transform.SetPositionAndRotation(
                companionAI.GetBallCarryPoint(),
                companionAI.GetBallCarryRotation());
    }

    private void OnDisable()
    {
        if (playerHeldBall == this)
            playerHeldBall = null;
    }

    private void OnDestroy()
    {
        if (runtimeBallMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeBallMaterial);
        else
            DestroyImmediate(runtimeBallMaterial);
    }

    private void ResolveReferences()
    {
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            playerCamera = Camera.main;

        if (companionAI == null)
            companionAI = FindFirstObjectByType<ShepherdCompanionAI>();
    }

    private bool IsPlayerLookingAtBall()
    {
        if (playerCamera == null)
            return false;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                interactionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return false;

        Transform hitTransform = hit.collider.transform;
        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    private void PickUpByPlayer()
    {
        if (playerCamera == null || playerHeldBall != null)
            return;

        heldByPlayer = true;
        carriedByDog = false;
        playerHeldBall = this;
        heldVelocity = Vector3.zero;
        SetCollidersEnabled(true);
        ballBody.isKinematic = true;
        ballBody.useGravity = false;
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;
        UpdatePlayerHeldPose(true);

        if (companionAI != null)
            companionAI.BeginWatchingHeldBall(this);
    }

    private void UpdatePlayerHeldPose(bool snap = false)
    {
        if (playerCamera == null)
            return;

        Vector3 targetPosition = playerCamera.transform.position +
                                 playerCamera.transform.forward * holdDistance +
                                 playerCamera.transform.up * holdVerticalOffset;
        transform.position = snap
            ? targetPosition
            : Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref heldVelocity,
                1f / Mathf.Max(1f, heldFollowSpeed));
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            playerCamera.transform.rotation,
            720f * Time.deltaTime);
    }

    private void ThrowFromPlayer()
    {
        if (!heldByPlayer || playerCamera == null)
            return;

        heldByPlayer = false;
        if (playerHeldBall == this)
            playerHeldBall = null;

        transform.position = playerCamera.transform.position +
                             playerCamera.transform.forward * Mathf.Max(0.45f, holdDistance);
        ConfigureFreePhysics();
        Vector3 throwDirection = playerCamera.transform.forward.normalized;
        ballBody.linearVelocity = throwDirection * throwSpeed + Vector3.up * throwUpwardBoost;
        ballBody.angularVelocity = playerCamera.transform.right * throwSpin;
        lastThrowTime = Time.time;

        if (companionAI != null)
            companionAI.BeginChasingThrownBall(this);
    }

    public bool TryAttachToDog(ShepherdCompanionAI dog)
    {
        if (dog == null || heldByPlayer || carriedByDog)
            return false;

        companionAI = dog;
        carriedByDog = true;
        ballBody.isKinematic = true;
        ballBody.useGravity = false;
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;
        SetCollidersEnabled(false);
        transform.SetPositionAndRotation(
            companionAI.GetBallCarryPoint(),
            companionAI.GetBallCarryRotation());
        return true;
    }

    public void DropFromDog(
        Vector3 worldPosition,
        Vector3 releaseVelocity,
        Vector3 releaseAngularVelocity)
    {
        if (!carriedByDog)
            return;

        carriedByDog = false;
        transform.position = worldPosition;
        ConfigureFreePhysics();
        ballBody.linearVelocity = releaseVelocity;
        ballBody.angularVelocity = releaseAngularVelocity;
        lastThrowTime = float.NegativeInfinity;
    }

    public bool OwnsCollider(Collider candidate)
    {
        if (candidate == null)
            return false;

        Transform candidateTransform = candidate.transform;
        return candidateTransform == transform || candidateTransform.IsChildOf(transform);
    }

    private void ConfigureFreePhysics()
    {
        if (ballBody == null)
            return;

        SetCollidersEnabled(true);
        ballBody.isKinematic = false;
        ballBody.useGravity = true;
        ballBody.mass = 0.3f;
        ballBody.linearDamping = 0.06f;
        ballBody.angularDamping = 0.08f;
        ballBody.maxAngularVelocity = 30f;
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballBody.interpolation = RigidbodyInterpolation.Interpolate;

        if (runtimeBallMaterial == null)
        {
            runtimeBallMaterial = new PhysicsMaterial("Fetch Ball Bounce (Runtime)")
            {
                bounciness = bounciness,
                dynamicFriction = 0.22f,
                staticFriction = 0.18f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
        }

        for (int i = 0; i < ballColliders.Length; i++)
        {
            if (ballColliders[i] != null)
                ballColliders[i].material = runtimeBallMaterial;
        }
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (ballColliders == null)
            return;

        for (int i = 0; i < ballColliders.Length; i++)
        {
            if (ballColliders[i] != null)
                ballColliders[i].enabled = enabled;
        }
    }
}
