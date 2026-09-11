using UnityEngine;

/// <summary>
/// Converts continuous controller contact into petting and hold-to-grab interactions.
/// Petting recognizes a deliberate controller stroke and is latched until contact ends.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(0)]
public sealed class XRControllerContactInteractor : MonoBehaviour
{
    [Header("Contact")]
    [SerializeField, Range(0.02f, 0.2f)] private float contactRadius = 0.11f;
    [SerializeField] private LayerMask contactMask = ~0;

    [Header("Petting Gesture")]
    [SerializeField, Range(0.04f, 0.25f)] private float strokeDistance = 0.09f;
    [SerializeField, Range(0.001f, 0.03f)] private float movementDeadzone = 0.004f;
    [SerializeField, Range(0.08f, 0.5f)] private float minimumStrokeDuration = 0.18f;
    [SerializeField, Range(0.4f, 4f)] private float gestureTimeout = 2f;
    [SerializeField, Range(1, 4)] private int requiredAlternatingStrokes = 1;
    [SerializeField, Range(3f, 8f)] private float minimumPettingRetriggerInterval = 5f;

    private sealed class HandRuntime
    {
        public PlayerHand Hand;
        public Transform Anchor;
        public ShepherdCompanionAI ContactDog;
        public FetchBallInteraction ContactBall;
        public Vector3 PreviousPosition;
        public Vector3 StrokeOrigin;
        public bool HasPreviousPosition;
        public float StrokeStartedAt;
        public float StrokeTravel;
        public int ActiveDirection;
        public int LastCompletedDirection;
        public int CompletedStrokes;
        public float LastStrokeTime;
    }

    private readonly Collider[] overlapResults = new Collider[64];
    private readonly HandRuntime left = new HandRuntime { Hand = PlayerHand.Left };
    private readonly HandRuntime right = new HandRuntime { Hand = PlayerHand.Right };

    private XRPlayerInputStatePublisher input;
    private ShepherdCompanionAI pettingContactLatch;
    private ShepherdVertexGrabInteraction heldDog;
    private FetchBallInteraction heldBall;
    private PlayerHand holdingHand;
    private float nextPettingAllowedTime;

    private void Awake()
    {
        input = GetComponent<XRPlayerInputStatePublisher>();
        left.Anchor = CreateAnchor("Left Controller Contact");
        right.Anchor = CreateAnchor("Right Controller Contact");
    }

    private void Update()
    {
        if (input == null)
            input = XRPlayerInputStatePublisher.Instance;
        if (input == null)
            return;

        UpdateHandContact(left, input.LeftState);
        UpdateHandContact(right, input.RightState);

        if (pettingContactLatch != null &&
            left.ContactDog != pettingContactLatch &&
            right.ContactDog != pettingContactLatch)
            pettingContactLatch = null;

        UpdatePetting(left, input.LeftState);
        UpdatePetting(right, input.RightState);
        ProcessGripInteraction(left, input.LeftState);
        ProcessGripInteraction(right, input.RightState);
    }

    private Transform CreateAnchor(string anchorName)
    {
        GameObject anchor = new GameObject(anchorName);
        anchor.transform.SetParent(transform, false);
        return anchor.transform;
    }

    private void UpdateHandContact(HandRuntime hand, PlayerInputState state)
    {
        hand.ContactDog = null;
        hand.ContactBall = null;

        if (!state.IsTrackingValid)
        {
            ResetGesture(hand);
            hand.Anchor.gameObject.SetActive(false);
            return;
        }

        hand.Anchor.gameObject.SetActive(true);
        hand.Anchor.SetPositionAndRotation(
            state.ControllerPose.position,
            state.ControllerPose.rotation);

        int count = Physics.OverlapSphereNonAlloc(
            state.ControllerPose.position,
            contactRadius,
            overlapResults,
            contactMask,
            QueryTriggerInteraction.Collide);
        float nearestDogDistance = float.PositiveInfinity;
        float nearestBallDistance = float.PositiveInfinity;

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
            }

            FetchBallInteraction ball = candidate.GetComponentInParent<FetchBallInteraction>();
            if (ball != null && distance < nearestBallDistance)
            {
                nearestBallDistance = distance;
                hand.ContactBall = ball;
            }
        }
    }

    private void UpdatePetting(HandRuntime hand, PlayerInputState state)
    {
        ShepherdCompanionAI dog = hand.ContactDog;
        if (dog == null || dog == pettingContactLatch || heldDog != null || heldBall != null)
        {
            if (dog == null)
                ResetGesture(hand);
            return;
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

        float horizontalDelta = Vector3.Dot(delta, dog.transform.right);
        int direction = horizontalDelta > 0f ? 1 : -1;
        if (hand.ActiveDirection == 0)
            hand.ActiveDirection = direction;

        if (direction != hand.ActiveDirection)
        {
            hand.ActiveDirection = direction;
            hand.StrokeTravel = horizontalDelta;
        }
        else
        {
            hand.StrokeTravel += horizontalDelta;
        }

        if (Mathf.Abs(hand.StrokeTravel) < strokeDistance ||
            direction == hand.LastCompletedDirection)
            return;

        hand.CompletedStrokes++;
        hand.LastCompletedDirection = direction;
        hand.LastStrokeTime = Time.unscaledTime;
        hand.StrokeTravel = 0f;
        hand.ActiveDirection = 0;

        if (hand.CompletedStrokes < requiredAlternatingStrokes)
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
    }

    private static void ResetGestureProgress(HandRuntime hand, Vector3 position)
    {
        ResetGesture(hand);
        hand.PreviousPosition = position;
        hand.StrokeOrigin = position;
        hand.StrokeStartedAt = Time.unscaledTime;
        hand.HasPreviousPosition = true;
    }
}
