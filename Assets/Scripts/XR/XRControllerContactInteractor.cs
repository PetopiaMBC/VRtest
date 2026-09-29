using UnityEngine;

/// <summary>
/// Owns close-range XR interaction with the dog and fetch ball.
/// It detects contact, shows touch feedback, limits the rendered controller at the
/// contacted surface, recognizes deliberate back-and-forth petting, and handles grip grabs.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(0)]
public sealed class XRControllerContactInteractor : MonoBehaviour
{
    [Header("Contact")]
    [SerializeField, Range(0.02f, 0.2f)] private float contactRadius = 0.11f;
    [SerializeField] private LayerMask contactMask = ~0;

    [Header("Interaction Feedback")]
    [Tooltip("컨트롤러가 닿은 강아지 또는 공을 외곽선과 표면 빛으로 표시합니다.")]
    [SerializeField] private bool showInteractionOutline = true;
    [Tooltip("컨트롤러가 닿은 대상의 강조 색상입니다.")]
    [SerializeField] private Color interactionOutlineColor = new Color(0.55f, 0.9f, 1f, 0.72f);
    [Tooltip("모델 표면에서 바깥쪽으로 확장되는 외곽선 두께(미터)입니다.")]
    [SerializeField, Range(0.001f, 0.03f)] private float interactionOutlineWidth = 0.006f;

    [Header("Controller Surface Limit")]
    [Tooltip("보이는 컨트롤러와 잡기 기준점이 대상 안쪽으로 과도하게 관통하지 않게 합니다.")]
    [SerializeField] private bool limitControllerPenetration = true;
    [Tooltip("컨트롤러 접촉부를 구로 보았을 때의 반지름(미터)입니다.")]
    [SerializeField, Range(0.015f, 0.09f)] private float controllerSolidRadius = 0.045f;
    [Tooltip("잡기와 쓰다듬기를 위해 허용하는 표면 겹침 깊이(미터)입니다.")]
    [SerializeField, Range(0f, 0.04f)] private float allowedSurfaceOverlap = 0.015f;
    [Tooltip("접촉이 끝난 뒤 컨트롤러 모델이 실제 손 위치로 돌아오는 속도입니다.")]
    [SerializeField, Range(0.1f, 1f)] private float visualReturnSharpness = 0.35f;

    [Header("Petting Gesture")]
    [SerializeField, Range(0.04f, 0.25f)] private float strokeDistance = 0.09f;
    [SerializeField, Range(0.001f, 0.03f)] private float movementDeadzone = 0.004f;
    [SerializeField, Range(0.08f, 0.5f)] private float minimumStrokeDuration = 0.18f;
    [SerializeField, Range(0.4f, 4f)] private float gestureTimeout = 2f;
    [Tooltip("쓰다듬기로 인정할 왕복 방향 횟수입니다. 2는 한 번 갔다가 돌아오는 동작입니다.")]
    [SerializeField, Range(2, 4)] private int requiredAlternatingStrokes = 2;
    [SerializeField, Range(3f, 8f)] private float minimumPettingRetriggerInterval = 5f;

    [Header("Call")]
    [Tooltip("Quest B/Y (secondary button) calls the dog. This avoids the A/X UI click binding.")]
    [SerializeField, Min(0.1f)] private float minimumCallRetriggerInterval = 1.5f;

    [Header("Temporary Affinity Test")]
    [Tooltip("Hold both grips, then press X/Y/A/B to set affinity to 0/30/70/100.")]
    [SerializeField] private bool enableAffinityTestShortcuts = true;

    private sealed class HandRuntime
    {
        public PlayerHand Hand;
        public Transform Anchor;
        public ShepherdCompanionAI ContactDog;
        public FetchBallInteraction ContactBall;
        public Collider ContactCollider;
        public SphereCollider PenetrationProbe;
        public Transform ControllerVisual;
        public Vector3 ControllerVisualDefaultLocalPosition;
        public bool HasControllerVisualDefault;
        public Vector3 VisualWorldOffset;
        public Vector3 PreviousPosition;
        public Vector3 StrokeOrigin;
        public bool HasPreviousPosition;
        public float StrokeStartedAt;
        public float StrokeTravel;
        public int ActiveDirection;
        public int LastCompletedDirection;
        public int CompletedStrokes;
        public float LastStrokeTime;
        public Vector3 StrokeAxis;
        public ShepherdCompanionAI GestureDog;
    }

    private readonly Collider[] overlapResults = new Collider[64];
    private readonly HandRuntime left = new HandRuntime { Hand = PlayerHand.Left };
    private readonly HandRuntime right = new HandRuntime { Hand = PlayerHand.Right };

    private XRPlayerInputStatePublisher input;
    private ShepherdCompanionAI pettingContactLatch;
    private ShepherdVertexGrabInteraction heldDog;
    private FetchBallInteraction heldBall;
    private InteractableOutlineHighlight leftHighlight;
    private InteractableOutlineHighlight rightHighlight;
    private PlayerHand holdingHand;
    private float nextPettingAllowedTime;
    private float nextCallAllowedTime;
    private float nextControllerVisualSearchTime;

    private void Awake()
    {
        input = GetComponent<XRPlayerInputStatePublisher>();
        left.Anchor = CreateAnchor("Left Controller Contact");
        right.Anchor = CreateAnchor("Right Controller Contact");
        left.PenetrationProbe = CreatePenetrationProbe(left.Anchor);
        right.PenetrationProbe = CreatePenetrationProbe(right.Anchor);
    }

    private void Update()
    {
        if (input == null)
            input = XRPlayerInputStatePublisher.Instance;
        if (input == null)
        {
            ClearInteractionHighlights();
            return;
        }

        UpdateHandContact(left, input.LeftState);
        UpdateHandContact(right, input.RightState);

        if (pettingContactLatch != null &&
            left.ContactDog != pettingContactLatch &&
            right.ContactDog != pettingContactLatch)
            pettingContactLatch = null;

        bool affinityTestModifierHeld = enableAffinityTestShortcuts &&
                                        heldDog == null && heldBall == null &&
                                        input.LeftState.GripHeld &&
                                        input.RightState.GripHeld;
        if (affinityTestModifierHeld)
        {
            ClearInteractionHighlights();
            ProcessAffinityTestInput(input.LeftState, input.RightState);
            ResetGesture(left);
            ResetGesture(right);
            return;
        }

        UpdateInteractionHighlights();
        ProcessGripInteraction(left, input.LeftState);
        ProcessGripInteraction(right, input.RightState);
        UpdatePetting(left, input.LeftState);
        UpdatePetting(right, input.RightState);
        ProcessCallInput(input.LeftState, input.RightState);
    }

    private void LateUpdate()
    {
        ResolveControllerVisualsIfNeeded();
        ApplyControllerVisualOffset(left);
        ApplyControllerVisualOffset(right);
    }

    private void OnDisable()
    {
        ClearInteractionHighlights();
    }

    private static void ProcessAffinityTestInput(
        PlayerInputState leftState,
        PlayerInputState rightState)
    {
        float targetAffinity;
        if (leftState.PrimaryButtonPressed)
            targetAffinity = 0f;
        else if (leftState.SecondaryButtonPressed)
            targetAffinity = 30f;
        else if (rightState.PrimaryButtonPressed)
            targetAffinity = 70f;
        else if (rightState.SecondaryButtonPressed)
            targetAffinity = 100f;
        else
            return;

        ShepherdCompanionAI dog = FindFirstObjectByType<ShepherdCompanionAI>();
        if (dog == null)
            return;

        dog.SetAffinityForTesting(targetAffinity);
        dog.RequestCall();
    }

    private void ProcessCallInput(PlayerInputState leftState, PlayerInputState rightState)
    {
        if ((!leftState.SecondaryButtonPressed && !rightState.SecondaryButtonPressed) ||
            Time.unscaledTime < nextCallAllowedTime ||
            heldDog != null || heldBall != null)
            return;

        ShepherdCompanionAI dog = FindFirstObjectByType<ShepherdCompanionAI>();
        if (dog != null && dog.RequestCall())
            nextCallAllowedTime = Time.unscaledTime + minimumCallRetriggerInterval;
    }

    private Transform CreateAnchor(string anchorName)
    {
        GameObject anchor = new GameObject(anchorName);
        anchor.layer = LayerMask.NameToLayer("Ignore Raycast");
        anchor.transform.SetParent(transform, false);
        return anchor.transform;
    }

    private static SphereCollider CreatePenetrationProbe(Transform anchor)
    {
        SphereCollider probe = anchor.gameObject.AddComponent<SphereCollider>();
        probe.isTrigger = true;
        probe.enabled = false;
        return probe;
    }

    private void UpdateHandContact(HandRuntime hand, PlayerInputState state)
    {
        hand.ContactDog = null;
        hand.ContactBall = null;
        hand.ContactCollider = null;

        if (!state.IsTrackingValid)
        {
            ResetGesture(hand);
            hand.VisualWorldOffset = Vector3.zero;
            hand.Anchor.gameObject.SetActive(false);
            return;
        }

        hand.Anchor.gameObject.SetActive(true);
        int count = Physics.OverlapSphereNonAlloc(
            state.ControllerPose.position,
            contactRadius,
            overlapResults,
            contactMask,
            QueryTriggerInteraction.Collide);
        float nearestDogDistance = float.PositiveInfinity;
        float nearestBallDistance = float.PositiveInfinity;
        Collider nearestDogCollider = null;
        Collider nearestBallCollider = null;

        for (int i = 0; i < count; i++)
        {
            Collider candidate = overlapResults[i];
            if (candidate == null || candidate.transform.IsChildOf(transform))
                continue;

            Vector3 closest = candidate.ClosestPoint(state.ControllerPose.position);
            float distance = (closest - state.ControllerPose.position).sqrMagnitude;

            ShepherdCompanionAI dog = candidate.GetComponentInParent<ShepherdCompanionAI>();
            if (dog != null && distance < nearestDogDistance)
            {
                nearestDogDistance = distance;
                hand.ContactDog = dog;
                nearestDogCollider = candidate;
            }

            FetchBallInteraction ball = candidate.GetComponentInParent<FetchBallInteraction>();
            if (ball != null && distance < nearestBallDistance)
            {
                nearestBallDistance = distance;
                hand.ContactBall = ball;
                nearestBallCollider = candidate;
            }
        }

        // Dog interaction has the same priority as grip handling. Otherwise use the ball.
        hand.ContactCollider = hand.ContactDog != null
            ? nearestDogCollider
            : nearestBallCollider;

        Vector3 constrainedPosition = ResolveControllerSurfacePosition(
            hand,
            state.ControllerPose.position,
            state.ControllerPose.rotation);
        hand.VisualWorldOffset = constrainedPosition - state.ControllerPose.position;
        hand.Anchor.SetPositionAndRotation(constrainedPosition, state.ControllerPose.rotation);
    }

    private void UpdatePetting(HandRuntime hand, PlayerInputState state)
    {
        ShepherdCompanionAI dog = hand.ContactDog;
        // Grip is a grab gesture, never a petting gesture. Clearing progress here
        // prevents the approach motion used to grab the dog from adding stroke distance.
        if (state.GripHeld || state.GripPressed)
        {
            ResetGesture(hand);
            return;
        }

        if (dog == null || dog == pettingContactLatch || heldDog != null || heldBall != null)
        {
            if (dog == null)
                ResetGesture(hand);
            return;
        }

        if (hand.GestureDog != dog)
        {
            ResetGesture(hand);
            hand.GestureDog = dog;
        }

        if (Time.unscaledTime < nextPettingAllowedTime)
        {
            ResetGesture(hand);
            return;
        }

        Vector3 position = state.ControllerPose.position;
        if (!hand.HasPreviousPosition)
        {
            hand.PreviousPosition = position;
            hand.StrokeOrigin = position;
            hand.StrokeStartedAt = Time.unscaledTime;
            hand.HasPreviousPosition = true;
            return;
        }

        if (Time.unscaledTime - hand.StrokeStartedAt > gestureTimeout)
            ResetGestureProgress(hand, position);

        Vector3 delta = position - hand.PreviousPosition;
        hand.PreviousPosition = position;
        float movement = delta.magnitude;

        // Use displacement from the contact origin instead of accumulated frame
        // movement. Small controller jitter can no longer add up into a petting
        // gesture, while one intentional stroke still works in any direction.
        if (requiredAlternatingStrokes <= 1)
        {
            float deliberateDistance = Vector3.Distance(position, hand.StrokeOrigin);
            float strokeDuration = Time.unscaledTime - hand.StrokeStartedAt;
            if (deliberateDistance < strokeDistance ||
                strokeDuration < minimumStrokeDuration)
                return;

            if (dog.RequestPetting())
            {
                pettingContactLatch = dog;
                nextPettingAllowedTime = Time.unscaledTime + minimumPettingRetriggerInterval;
            }
            ResetGesture(hand);
            return;
        }

        if (movement < movementDeadzone)
            return;

        if (hand.StrokeAxis == Vector3.zero)
            hand.StrokeAxis = delta.normalized;

        float directedDelta = Vector3.Dot(delta, hand.StrokeAxis);
        if (Mathf.Abs(directedDelta) < movementDeadzone)
            return;

        int direction = directedDelta > 0f ? 1 : -1;
        if (hand.ActiveDirection == 0)
            hand.ActiveDirection = direction;

        if (direction != hand.ActiveDirection)
        {
            hand.ActiveDirection = direction;
            hand.StrokeTravel = directedDelta;
        }
        else
        {
            hand.StrokeTravel += directedDelta;
        }

        if (Mathf.Abs(hand.StrokeTravel) < strokeDistance ||
            direction == hand.LastCompletedDirection)
            return;

        hand.CompletedStrokes++;
        hand.LastCompletedDirection = direction;
        hand.LastStrokeTime = Time.unscaledTime;
        hand.StrokeTravel = 0f;
        hand.ActiveDirection = 0;

        if (hand.CompletedStrokes < requiredAlternatingStrokes ||
            Time.unscaledTime - hand.StrokeStartedAt < minimumStrokeDuration)
            return;

        if (dog.RequestPetting())
        {
            pettingContactLatch = dog;
            nextPettingAllowedTime = Time.unscaledTime + minimumPettingRetriggerInterval;
        }
        ResetGesture(hand);
    }

    private void ProcessGripInteraction(HandRuntime hand, PlayerInputState state)
    {
        if (heldDog != null || heldBall != null)
        {
            if (hand.Hand != holdingHand || !state.GripReleased)
                return;

            if (heldDog != null)
                heldDog.ReleaseXRGrab();
            if (heldBall != null)
                heldBall.ReleaseXRGrab(state.ControllerVelocity, state.ControllerAngularVelocity);
            heldDog = null;
            heldBall = null;
            return;
        }

        if (!state.GripPressed)
            return;

        if (hand.ContactDog != null)
        {
            ShepherdVertexGrabInteraction dogGrab =
                hand.ContactDog.GetComponent<ShepherdVertexGrabInteraction>();
            if (dogGrab != null && dogGrab.TryBeginXRGrab(hand.Anchor))
            {
                heldDog = dogGrab;
                holdingHand = hand.Hand;
                return;
            }
        }

        if (hand.ContactBall != null && hand.ContactBall.TryBeginXRGrab(hand.Anchor))
        {
            heldBall = hand.ContactBall;
            holdingHand = hand.Hand;
        }
    }

    /// <summary>
    /// 컨트롤러가 실제로 닿은 대상을 강아지, 공 순서로 강조합니다.
    /// 잡기 가능 여부와 무관하게 접촉 자체를 알려 주므로 쓰다듬기 때도 표시됩니다.
    /// </summary>
    private void UpdateInteractionHighlights()
    {
        InteractableOutlineHighlight nextLeft = ResolveHighlight(left);
        InteractableOutlineHighlight nextRight = ResolveHighlight(right);

        DisableIfNoLongerSelected(leftHighlight, nextLeft, nextRight);
        DisableIfNoLongerSelected(rightHighlight, nextLeft, nextRight);

        if (nextLeft != null)
            nextLeft.SetHighlighted(true, interactionOutlineColor, interactionOutlineWidth);
        if (nextRight != null && nextRight != nextLeft)
            nextRight.SetHighlighted(true, interactionOutlineColor, interactionOutlineWidth);

        leftHighlight = nextLeft;
        rightHighlight = nextRight;
    }

    private InteractableOutlineHighlight ResolveHighlight(HandRuntime hand)
    {
        if (!showInteractionOutline || heldDog != null || heldBall != null)
            return null;

        if (hand.ContactDog != null)
            return GetOrCreateHighlight(hand.ContactDog.gameObject);

        if (hand.ContactBall != null)
            return GetOrCreateHighlight(hand.ContactBall.gameObject);

        return null;
    }

    private static InteractableOutlineHighlight GetOrCreateHighlight(GameObject target)
    {
        InteractableOutlineHighlight highlight =
            target.GetComponent<InteractableOutlineHighlight>();
        return highlight != null
            ? highlight
            : target.AddComponent<InteractableOutlineHighlight>();
    }

    private void DisableIfNoLongerSelected(
        InteractableOutlineHighlight previous,
        InteractableOutlineHighlight nextLeft,
        InteractableOutlineHighlight nextRight)
    {
        if (previous != null && previous != nextLeft && previous != nextRight)
            previous.SetHighlighted(false, interactionOutlineColor, interactionOutlineWidth);
    }

    private void ClearInteractionHighlights()
    {
        if (leftHighlight != null)
            leftHighlight.SetHighlighted(false, interactionOutlineColor, interactionOutlineWidth);
        if (rightHighlight != null && rightHighlight != leftHighlight)
            rightHighlight.SetHighlighted(false, interactionOutlineColor, interactionOutlineWidth);
        leftHighlight = null;
        rightHighlight = null;
    }

    private static void ResetGesture(HandRuntime hand)
    {
        hand.HasPreviousPosition = false;
        hand.StrokeOrigin = Vector3.zero;
        hand.StrokeStartedAt = 0f;
        hand.StrokeTravel = 0f;
        hand.ActiveDirection = 0;
        hand.LastCompletedDirection = 0;
        hand.CompletedStrokes = 0;
        hand.LastStrokeTime = 0f;
        hand.StrokeAxis = Vector3.zero;
        hand.GestureDog = null;
    }

    private static void ResetGestureProgress(HandRuntime hand, Vector3 position)
    {
        ShepherdCompanionAI currentDog = hand.ContactDog;
        ResetGesture(hand);
        hand.PreviousPosition = position;
        hand.StrokeOrigin = position;
        hand.StrokeStartedAt = Time.unscaledTime;
        hand.HasPreviousPosition = true;
        hand.GestureDog = currentDog;
    }

    /// <summary>
    /// Uses a disabled sphere probe, so this calculation does not add physical trigger events.
    /// Only the excess penetration beyond the small interaction allowance is corrected.
    /// </summary>
    private Vector3 ResolveControllerSurfacePosition(
        HandRuntime hand,
        Vector3 rawPosition,
        Quaternion rawRotation)
    {
        if (!limitControllerPenetration || hand.ContactCollider == null ||
            hand.PenetrationProbe == null)
            return rawPosition;

        hand.PenetrationProbe.radius = controllerSolidRadius;
        // ComputePenetration ignores disabled colliders. Enable the trigger only for
        // this immediate query, then disable it before the next physics simulation.
        hand.PenetrationProbe.enabled = true;
        bool overlaps;
        Vector3 separationDirection;
        float separationDistance;
        try
        {
            overlaps = Physics.ComputePenetration(
                hand.PenetrationProbe,
                rawPosition,
                rawRotation,
                hand.ContactCollider,
                hand.ContactCollider.transform.position,
                hand.ContactCollider.transform.rotation,
                out separationDirection,
                out separationDistance);
        }
        finally
        {
            hand.PenetrationProbe.enabled = false;
        }
        if (!overlaps || separationDistance <= allowedSurfaceOverlap)
            return rawPosition;

        return rawPosition +
               separationDirection * (separationDistance - allowedSurfaceOverlap);
    }

    private void ResolveControllerVisualsIfNeeded()
    {
        if ((left.ControllerVisual != null && right.ControllerVisual != null) ||
            Time.unscaledTime < nextControllerVisualSearchTime)
            return;

        nextControllerVisualSearchTime = Time.unscaledTime + 1f;
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (left.ControllerVisual == null && candidate.name == "Left Controller Visual")
                SetControllerVisual(left, candidate);
            else if (right.ControllerVisual == null && candidate.name == "Right Controller Visual")
                SetControllerVisual(right, candidate);
        }
    }

    private static void SetControllerVisual(HandRuntime hand, Transform visual)
    {
        hand.ControllerVisual = visual;
        hand.ControllerVisualDefaultLocalPosition = visual.localPosition;
        hand.HasControllerVisualDefault = true;
    }

    private void ApplyControllerVisualOffset(HandRuntime hand)
    {
        if (!hand.HasControllerVisualDefault || hand.ControllerVisual == null ||
            hand.ControllerVisual.parent == null)
            return;

        Vector3 localOffset = hand.ControllerVisual.parent.InverseTransformVector(
            hand.VisualWorldOffset);
        Vector3 target = hand.ControllerVisualDefaultLocalPosition + localOffset;

        // Collision correction is immediate. Returning to the tracked pose is softened
        // slightly to avoid a visible snap when contact ends.
        if (hand.VisualWorldOffset.sqrMagnitude > 0.000001f)
            hand.ControllerVisual.localPosition = target;
        else
            hand.ControllerVisual.localPosition = Vector3.Lerp(
                hand.ControllerVisual.localPosition,
                hand.ControllerVisualDefaultLocalPosition,
                visualReturnSharpness);
    }
}
