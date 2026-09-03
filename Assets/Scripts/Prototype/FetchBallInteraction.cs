using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PC용 공 줍기/던지기와 강아지 공놀이 사이를 연결합니다.
/// G로 바라보는 공을 들고, 다시 G를 누르면 카메라 정면으로 던집니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(1000)]
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

    [Header("Recovery")]
    [Tooltip("공이 이 높이보다 아래로 떨어지면 맵 중앙의 안전한 바닥으로 되돌립니다.")]
    [SerializeField] private float minimumValidWorldY = -0.75f;
    [Tooltip("맵 중앙에서 이 거리보다 멀어진 공을 이탈한 것으로 판단합니다.")]
    [SerializeField, Min(1f)] private float maximumDistanceFromMapCenter = 7f;
    [SerializeField] private Vector3 mapCenter = Vector3.zero;
    [SerializeField, Min(0.5f)] private float recoveryFloorProbeHeight = 3f;

    private static FetchBallInteraction playerHeldBall;
    private static int lastConsumedInteractionFrame = -1;

    private Rigidbody ballBody;
    private Collider[] ballColliders;
    private Transform originalParent;
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
        originalParent = transform.parent;
        ballBody = GetComponent<Rigidbody>();
        ballColliders = GetComponentsInChildren<Collider>(true);
        ConfigureFreePhysics();
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();

        if (!heldByPlayer && !carriedByDog && IsOutsidePlayableArea())
        {
            RecoverToFloor(mapCenter);
            return;
        }

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

        // Animator와 강아지 AI의 LateUpdate가 모두 끝난 뒤 최종 입 자세를 따라갑니다.
        // Rigidbody 보간에 맡기지 않고 렌더 프레임마다 정확한 위치를 적용해야
        // 회전 중 공이 한 프레임씩 뒤처져 보이지 않습니다.
        if (carriedByDog && companionAI != null)
            SyncDogCarryPose(companionAI);
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
        RestoreOriginalParent();
        playerHeldBall = this;
        heldVelocity = Vector3.zero;
        SetCollidersEnabled(true);
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;
        ballBody.useGravity = false;
        ballBody.interpolation = RigidbodyInterpolation.None;
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        ballBody.isKinematic = true;
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
        Vector3 desiredPosition = snap
            ? targetPosition
            : Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref heldVelocity,
                1f / Mathf.Max(1f, heldFollowSpeed));
        transform.position = GetCollisionSafePosition(transform.position, desiredPosition);
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
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;
        ballBody.useGravity = false;
        ballBody.interpolation = RigidbodyInterpolation.None;
        ballBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        ballBody.isKinematic = true;
        SetCollidersEnabled(false);
        RestoreOriginalParent();
        SyncDogCarryPose(dog);
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
        RestoreOriginalParent();
        transform.position = worldPosition;
        ConfigureFreePhysics();
        ballBody.linearVelocity = releaseVelocity;
        ballBody.angularVelocity = releaseAngularVelocity;
        lastThrowTime = float.NegativeInfinity;
    }

    public void SyncDogCarryPose(ShepherdCompanionAI dog)
    {
        if (!carriedByDog || dog == null || dog != companionAI)
            return;

        transform.SetPositionAndRotation(
            dog.GetBallCarryPoint(),
            dog.GetBallCarryRotation());
    }

    public void RecoverToFloor(Vector3 preferredPosition)
    {
        RecoverToFloor(preferredPosition, true);
    }

    public void RecoverToFloor(Vector3 preferredPosition, bool notifyDog)
    {
        if (heldByPlayer)
            return;

        carriedByDog = false;
        RestoreOriginalParent();
        ConfigureFreePhysics();
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;

        Vector3 recoveredPosition = FindRecoveryFloorPosition(preferredPosition);
        transform.SetPositionAndRotation(recoveredPosition, Quaternion.identity);
        lastThrowTime = Time.time - dogPickupDelay;

        if (notifyDog && companionAI != null)
            companionAI.BeginChasingThrownBall(this);
    }

    private void RestoreOriginalParent()
    {
        if (transform.parent != originalParent)
            transform.SetParent(originalParent, true);
    }

    private Vector3 GetCollisionSafePosition(Vector3 from, Vector3 target)
    {
        Vector3 displacement = target - from;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
            return target;

        float radius = GetCollisionRadius();
        RaycastHit[] hits = Physics.SphereCastAll(
            from,
            radius,
            displacement / distance,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float nearestDistance = distance;
        Transform playerRoot = playerCamera != null ? playerCamera.transform.root : null;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (OwnsCollider(candidate))
                continue;

            Transform candidateTransform = candidate.transform;
            if (playerRoot != null &&
                (candidateTransform == playerRoot || candidateTransform.IsChildOf(playerRoot)))
                continue;

            nearestDistance = Mathf.Min(nearestDistance, Mathf.Max(0f, hits[i].distance - 0.01f));
        }

        return from + displacement.normalized * nearestDistance;
    }

    private float GetCollisionRadius()
    {
        float radius = 0.05f;
        for (int i = 0; i < ballColliders.Length; i++)
        {
            Collider candidate = ballColliders[i];
            if (candidate == null)
                continue;

            Vector3 extents = candidate.bounds.extents;
            radius = Mathf.Max(radius, Mathf.Min(extents.x, Mathf.Min(extents.y, extents.z)));
        }

        return radius;
    }

    private bool IsOutsidePlayableArea()
    {
        if (transform.position.y < minimumValidWorldY)
            return true;

        Vector3 planarOffset = transform.position - mapCenter;
        planarOffset.y = 0f;
        return planarOffset.sqrMagnitude >
               maximumDistanceFromMapCenter * maximumDistanceFromMapCenter;
    }

    private Vector3 FindRecoveryFloorPosition(Vector3 preferredPosition)
    {
        float radius = GetCollisionRadius();
        Vector3[] offsets =
        {
            Vector3.zero,
            Vector3.forward * 0.55f,
            Vector3.right * 0.55f,
            Vector3.back * 0.55f,
            Vector3.left * 0.55f,
            (Vector3.forward + Vector3.right).normalized * 0.8f,
            (Vector3.back + Vector3.left).normalized * 0.8f
        };

        preferredPosition.y = mapCenter.y;
        for (int i = 0; i < offsets.Length; i++)
        {
            Vector3 candidate = preferredPosition + offsets[i];
            Vector3 rayOrigin = candidate + Vector3.up * recoveryFloorProbeHeight;
            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                recoveryFloorProbeHeight * 2f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
            {
                RaycastHit hit = hits[hitIndex];
                if (OwnsCollider(hit.collider) || Vector3.Dot(hit.normal, Vector3.up) < 0.65f)
                    continue;

                Vector3 floorPosition = hit.point + Vector3.up * (radius + 0.025f);
                if (!Physics.CheckSphere(
                        floorPosition,
                        radius * 0.92f,
                        Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore))
                    return floorPosition;
            }
        }

        return new Vector3(
            preferredPosition.x,
            mapCenter.y + Mathf.Max(0.15f, radius + 0.025f),
            preferredPosition.z);
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
