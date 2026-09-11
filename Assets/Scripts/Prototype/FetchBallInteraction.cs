using UnityEngine;

/// <summary>
/// 컨트롤러 공 잡기/던지기와 강아지 공놀이 사이를 연결합니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(1000)]
public sealed class FetchBallInteraction : MonoBehaviour
{
    [Header("Player Interaction")]
    [SerializeField, Min(0.5f)] private float interactionDistance = 4f;
    [Tooltip("작아진 공을 화면 정중앙에 정확히 맞추지 않아도 잡을 수 있는 화면 중앙 보조 반경입니다.")]
    [SerializeField, Range(0.02f, 0.3f)] private float grabAimAssistViewportRadius = 0.14f;
    [SerializeField, Min(0.2f)] private float holdDistance = 0.72f;
    [SerializeField] private float holdVerticalOffset = -0.18f;
    [SerializeField, Min(0.1f)] private float heldFollowSpeed = 24f;
    [Tooltip("XR 컨트롤러로 잡은 공이 손과 플레이어 충돌체에 겹치지 않도록 바깥쪽으로 띄우는 거리입니다.")]
    [SerializeField, Range(0.05f, 0.4f)] private float xrHoldSeparation = 0.18f;

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
    [Tooltip("공이 이 높이보다 아래로 떨어지면 마지막 XZ 위치에 가까운 안전한 바닥으로 되돌립니다.")]
    [SerializeField] private float minimumValidWorldY = -0.75f;
    [Tooltip("직사각형 경계가 비활성화된 경우 사용하는 원형 안전 반경입니다.")]
    [SerializeField, Min(1f)] private float maximumDistanceFromMapCenter = 7.35f;
    [Tooltip("맵 중앙에서 X/Z축 방향으로 공이 움직일 수 있는 범위입니다. PlayScene의 플레이어 이동 경계와 같은 값입니다.")]
    [SerializeField] private Vector2 playableAreaHalfExtents = new Vector2(4.55f, 4.55f);
    [SerializeField] private Vector3 mapCenter = Vector3.zero;
    [SerializeField, Min(0.5f)] private float recoveryFloorProbeHeight = 3f;

    private static FetchBallInteraction playerHeldBall;
    private static int lastConsumedInteractionFrame = -1;

    private Rigidbody ballBody;
    private Collider[] ballColliders;
    private Transform originalParent;
    private bool heldByPlayer;
    private Transform xrHoldAnchor;
    private bool carriedByDog;
    private bool dogMouthCaptureInProgress;
    private Vector3 dogCaptureStartPosition;
    private Quaternion dogCaptureStartRotation;
    private float dogMouthCaptureProgress;
    private float lastThrowTime = float.NegativeInfinity;
    private Vector3 heldVelocity;
    private PhysicsMaterial runtimeBallMaterial;

    public static bool IsAnyBallHeldByPlayer => playerHeldBall != null;
    public static bool ConsumedGrabInputThisFrame => lastConsumedInteractionFrame == Time.frameCount;
    public bool IsHeldByPlayer => heldByPlayer;
    public bool IsCarriedByDog => carriedByDog;
    public bool IsDogMouthCaptureInProgress => dogMouthCaptureInProgress;
    public bool WasThrown => !heldByPlayer && !carriedByDog && lastThrowTime > float.NegativeInfinity;
    public Vector3 WorldPosition => transform.position;
    public Vector3 CurrentVelocity => ballBody != null ? ballBody.linearVelocity : Vector3.zero;
    public float CollisionRadius => GetCollisionRadius();

    public void ApplyPetSpaceContract(PetSpaceContractSnapshot snapshot)
    {
        if (snapshot.State != PetSpaceContractState.Valid)
            return;

        if (snapshot.HasWalkArea)
        {
            mapCenter = snapshot.WalkArea.center;
            playableAreaHalfExtents = new Vector2(
                Mathf.Max(0.5f, snapshot.WalkArea.extents.x),
                Mathf.Max(0.5f, snapshot.WalkArea.extents.z));
        }
        else if (snapshot.HasRecoveryPosition)
        {
            mapCenter = snapshot.RecoveryPosition.position;
        }
    }

    public bool TryBeginXRGrab(Transform controllerAnchor)
    {
        if (controllerAnchor == null || heldByPlayer || carriedByDog ||
            dogMouthCaptureInProgress || playerHeldBall != null ||
            ShepherdVertexGrabInteraction.IsAnyDogHeld)
            return false;

        xrHoldAnchor = controllerAnchor;
        PickUpByPlayer();
        if (heldByPlayer)
            return true;

        xrHoldAnchor = null;
        return false;
    }

    public void ReleaseXRGrab(Vector3 controllerVelocity, Vector3 controllerAngularVelocity)
    {
        if (!heldByPlayer || xrHoldAnchor == null)
            return;

        heldByPlayer = false;
        if (playerHeldBall == this)
            playerHeldBall = null;

        xrHoldAnchor = null;
        ConfigureFreePhysics();
        ballBody.linearVelocity = controllerVelocity;
        ballBody.angularVelocity = controllerAngularVelocity;
        lastThrowTime = Time.time;

        if (companionAI != null)
            companionAI.BeginChasingThrownBall(this);
    }

    public static bool TryConsumeSharedGrabInput()
    {
        if (lastConsumedInteractionFrame == Time.frameCount)
            return false;

        lastConsumedInteractionFrame = Time.frameCount;
        return true;
    }

    public bool CanBePickedUpByDog
    {
        get
        {
            if (heldByPlayer || carriedByDog || dogMouthCaptureInProgress || ballBody == null)
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

        if (!heldByPlayer && !carriedByDog && !dogMouthCaptureInProgress &&
            transform.position.y < minimumValidWorldY)
        {
            // 완전히 바닥 아래로 빠졌을 때만 위치 복구를 사용합니다. 정상적으로
            // 날아가는 공을 중앙으로 순간 이동시키지 않도록 마지막 XZ 위치를
            // 최대한 유지합니다.
            RecoverToFloor(GetNearestPlayablePlanarPosition(transform.position));
            return;
        }

        if (!heldByPlayer && !carriedByDog && !dogMouthCaptureInProgress &&
            BounceOffPlayableBoundary())
        {
            return;
        }

        if (heldByPlayer)
            UpdatePlayerHeldPose();

    }

    private void LateUpdate()
    {
        if (heldByPlayer)
        {
            UpdatePlayerHeldPose();
            return;
        }

        if (dogMouthCaptureInProgress && companionAI != null)
        {
            ApplyDogMouthCapturePose(companionAI);
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

        xrHoldAnchor = null;
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

        Vector3 cameraPosition = playerCamera.transform.position;
        Vector3 toBall = transform.position - cameraPosition;
        float distance = toBall.magnitude;
        if (distance < 0.001f || distance > interactionDistance + GetCollisionRadius())
            return false;

        Vector3 viewportPoint = playerCamera.WorldToViewportPoint(transform.position);
        if (viewportPoint.z <= 0f)
            return false;

        Vector2 centerOffset = new Vector2(
            viewportPoint.x - 0.5f,
            viewportPoint.y - 0.5f);
        if (centerOffset.sqrMagnitude >
            grabAimAssistViewportRadius * grabAimAssistViewportRadius)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            cameraPosition,
            toBall / distance,
            distance + GetCollisionRadius(),
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        Transform playerRoot = playerCamera.transform.root;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (OwnsCollider(candidate))
                return true;

            Transform candidateTransform = candidate.transform;
            if (playerRoot != null &&
                (candidateTransform == playerRoot || candidateTransform.IsChildOf(playerRoot)))
                continue;

            // Aim assist never selects a ball through walls, furniture, or the dog.
            return false;
        }

        return false;
    }

    private void PickUpByPlayer()
    {
        if (GetHoldReference() == null || playerHeldBall != null ||
            ShepherdVertexGrabInteraction.IsAnyDogHeld)
            return;

        heldByPlayer = true;
        carriedByDog = false;
        RestoreOriginalParent();
        playerHeldBall = this;
        heldVelocity = Vector3.zero;
        // A held ball is driven kinematically. Keeping its colliders enabled while
        // it overlaps the hand/player rig can depenetrate the CharacterController
        // and push the player backwards, so restore them only on release.
        SetCollidersEnabled(false);
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
        Transform holdReference = GetHoldReference();
        if (holdReference == null)
            return;

        Vector3 targetPosition;
        if (xrHoldAnchor != null)
        {
            Vector3 outward = playerCamera != null
                ? xrHoldAnchor.position - playerCamera.transform.position
                : xrHoldAnchor.forward;
            if (outward.sqrMagnitude < 0.0001f)
                outward = xrHoldAnchor.forward;
            targetPosition = xrHoldAnchor.position +
                             outward.normalized * xrHoldSeparation;
        }
        else
        {
            targetPosition = holdReference.position +
                             holdReference.forward * holdDistance +
                             holdReference.up * holdVerticalOffset;
        }
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
            holdReference.rotation,
            720f * Time.deltaTime);
    }

    private Transform GetHoldReference()
    {
        if (xrHoldAnchor != null)
            return xrHoldAnchor;

        return playerCamera != null ? playerCamera.transform : null;
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
        if (!TryBeginDogMouthCapture(dog))
            return false;

        UpdateDogMouthCapture(dog, 1f);
        return CompleteDogMouthCapture(dog);
    }

    public bool TryBeginDogMouthCapture(ShepherdCompanionAI dog)
    {
        if (dog == null || heldByPlayer || carriedByDog || dogMouthCaptureInProgress)
            return false;

        companionAI = dog;
        dogMouthCaptureInProgress = true;
        dogMouthCaptureProgress = 0f;
        dogCaptureStartPosition = transform.position;
        dogCaptureStartRotation = transform.rotation;
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;
        ballBody.useGravity = false;
        ballBody.interpolation = RigidbodyInterpolation.None;
        ballBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        ballBody.isKinematic = true;
        SetCollidersEnabled(false);
        RestoreOriginalParent();
        return true;
    }

    public void UpdateDogMouthCapture(ShepherdCompanionAI dog, float progress)
    {
        if (!dogMouthCaptureInProgress || dog == null || dog != companionAI)
            return;

        dogMouthCaptureProgress = Mathf.Clamp01(progress);
        ApplyDogMouthCapturePose(dog);
    }

    public bool CompleteDogMouthCapture(ShepherdCompanionAI dog)
    {
        if (!dogMouthCaptureInProgress || dog == null || dog != companionAI)
            return false;

        dogMouthCaptureProgress = 1f;
        ApplyDogMouthCapturePose(dog);
        dogMouthCaptureInProgress = false;
        carriedByDog = true;
        SyncDogCarryPose(dog);
        return true;
    }

    public void CancelDogMouthCapture()
    {
        if (!dogMouthCaptureInProgress)
            return;

        dogMouthCaptureInProgress = false;
        dogMouthCaptureProgress = 0f;
        RestoreOriginalParent();
        ConfigureFreePhysics();
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;
    }

    private void ApplyDogMouthCapturePose(ShepherdCompanionAI dog)
    {
        float smoothProgress = dogMouthCaptureProgress * dogMouthCaptureProgress *
                               (3f - 2f * dogMouthCaptureProgress);
        transform.SetPositionAndRotation(
            Vector3.Lerp(dogCaptureStartPosition, dog.GetBallCarryPoint(), smoothProgress),
            Quaternion.Slerp(dogCaptureStartRotation, dog.GetBallCarryRotation(), smoothProgress));
    }

    public void DropFromDog(
        Vector3 worldPosition,
        Vector3 releaseVelocity,
        Vector3 releaseAngularVelocity)
    {
        if (!carriedByDog && !dogMouthCaptureInProgress)
            return;

        dogMouthCaptureInProgress = false;
        dogMouthCaptureProgress = 0f;
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

        dogMouthCaptureInProgress = false;
        dogMouthCaptureProgress = 0f;
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

    /// <summary>
    /// 정체된 공을 순간 이동시키지 않고 목표 쪽으로 짧게 튕겨 다시 도달할 수 있게 합니다.
    /// </summary>
    public void NudgeToward(Vector3 targetPosition, float speed)
    {
        if (heldByPlayer || carriedByDog || dogMouthCaptureInProgress || ballBody == null)
            return;

        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = -transform.forward;

        direction.Normalize();
        RestoreOriginalParent();
        ConfigureFreePhysics();
        ballBody.linearVelocity = direction * Mathf.Max(0.5f, speed) + Vector3.up * 0.45f;
        ballBody.angularVelocity = Vector3.Cross(Vector3.up, direction) * (throwSpin * 0.35f);
        lastThrowTime = Time.time;
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

    private bool BounceOffPlayableBoundary()
    {
        Vector3 planarOffset = transform.position - mapCenter;
        planarOffset.y = 0f;
        float radius = GetCollisionRadius();
        Vector2 halfExtents = GetPlayableHalfExtents();
        float xLimit = Mathf.Max(0.25f, halfExtents.x - radius);
        float zLimit = Mathf.Max(0.25f, halfExtents.y - radius);
        bool hitX = Mathf.Abs(planarOffset.x) > xLimit;
        bool hitZ = Mathf.Abs(planarOffset.z) > zLimit;
        if (!hitX && !hitZ)
            return false;

        Vector3 clampedPosition = transform.position;
        clampedPosition.x = mapCenter.x + Mathf.Clamp(planarOffset.x, -xLimit, xLimit);
        clampedPosition.z = mapCenter.z + Mathf.Clamp(planarOffset.z, -zLimit, zLimit);
        ballBody.position = clampedPosition;

        Vector3 velocity = ballBody.linearVelocity;
        float restitution = Mathf.Clamp01(bounciness);
        if (hitX && Mathf.Sign(velocity.x) == Mathf.Sign(planarOffset.x))
            velocity.x = -velocity.x * restitution;
        if (hitZ && Mathf.Sign(velocity.z) == Mathf.Sign(planarOffset.z))
            velocity.z = -velocity.z * restitution;

        ballBody.linearVelocity = velocity;
        return true;
    }

    private Vector3 GetNearestPlayablePlanarPosition(Vector3 position)
    {
        Vector3 planarOffset = position - mapCenter;
        planarOffset.y = 0f;
        float radius = GetCollisionRadius();
        Vector2 halfExtents = GetPlayableHalfExtents();
        float xLimit = Mathf.Max(0.25f, halfExtents.x - radius);
        float zLimit = Mathf.Max(0.25f, halfExtents.y - radius);

        return new Vector3(
            mapCenter.x + Mathf.Clamp(planarOffset.x, -xLimit, xLimit),
            mapCenter.y,
            mapCenter.z + Mathf.Clamp(planarOffset.z, -zLimit, zLimit));
    }

    private Vector2 GetPlayableHalfExtents()
    {
        if (playableAreaHalfExtents.x > 0.25f && playableAreaHalfExtents.y > 0.25f)
            return playableAreaHalfExtents;

        float fallbackExtent = Mathf.Max(
            0.5f,
            maximumDistanceFromMapCenter / Mathf.Sqrt(2f));
        return new Vector2(fallbackExtent, fallbackExtent);
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
