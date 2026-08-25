using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shows a world-space interaction prompt above the dog and accepts E/Q only
/// when the player is close enough and the camera is actually aimed at it.
/// </summary>
public sealed class DogPetInteraction : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField, Min(0.1f)] private float interactionDistance = 1.3f;
    [SerializeField, Min(0.1f)] private float grabDistance = 1.45f;
    [SerializeField, Range(1f, 90f)] private float lookAngle = 38f;
    [SerializeField, Min(0.05f)] private float promptHeight = 0.72f;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Camera playerCamera;

    [Header("Dog pose")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform[] eyeObjects;
    [SerializeField] private Animator animator;
    [SerializeField] private string petTrigger = "Pet";
    [SerializeField, Range(2, 3)] private int headTilts = 3;
    [SerializeField, Range(2f, 20f)] private float tiltAngle = 9f;
    [SerializeField, Min(0.05f)] private float tiltDuration = 0.22f;
    [SerializeField, Min(0.5f)] private float petFocusDuration = 2f;

    [Header("Prompt")]
    [SerializeField] private string promptText = "E  Pet";
    [SerializeField] private string grabPromptText = "Q  잡기";
    [SerializeField] private string releasePromptText = "Q  내려놓기";
    [SerializeField] private Vector3 holdLocalPosition = new Vector3(0f, -0.66f, 4.0f);
    [SerializeField] private Vector3 holdLocalEulerAngles = new Vector3(0f, 180f, 0f);
    [SerializeField] private Vector3 holdSupportOffset = new Vector3(0f, -0.05f, 0f);
    [SerializeField] private Vector3 heldCradleEulerAngles = new Vector3(0f, 0f, 0f);
    [SerializeField, Range(0f, 30f)] private float heldBodySagAngle = 18f;
    [SerializeField, Range(0f, 1f)] private float heldLimbGravityInfluence = 0.45f;
    [SerializeField, Range(0f, 1f)] private float heldTailGravityInfluence = 0.5f;
    [SerializeField] private Color promptColor = new Color(1f, 0.86f, 0.35f, 1f);

    private Canvas promptCanvas;
    private Canvas heartCanvas;
    private GameObject promptBackground;
    private Text promptLabel;
    private readonly List<Heart> hearts = new List<Heart>();
    private Quaternion headRestRotation;
    private bool headRestRotationCaptured;
    private bool canPet;
    private bool canGrab;
    private bool petting;
    private bool holding;
    private PrototypePetAgent petAgent;
    private Transform holdAnchor;
    private Transform heldMovementRoot;
    private Transform heldOriginalParent;
    private readonly List<HeldBonePose> heldBonePoses = new List<HeldBonePose>();
    private Transform leftShoulderBone;
    private Transform rightShoulderBone;
    private Transform leftArmBone;
    private Transform rightArmBone;
    private Transform leftForeArmBone;
    private Transform rightForeArmBone;
    private Transform neckBone;
    private Transform spineBone;
    private Transform spine1Bone;
    private Transform spine2Bone;
    private Transform leftUpperLegBone;
    private Transform rightUpperLegBone;
    private Transform leftLowerLegBone;
    private Transform rightLowerLegBone;
    private readonly List<Transform> tailBones = new List<Transform>();
    private Transform holdSupportBone;
    private bool heldPoseCaptured;
    private bool animatorWasEnabled;
    private bool petAnimatorPoseCaptured;
    private bool petAnimatorWasEnabled;
    private Quaternion neckRestRotation;
    private bool neckRestRotationCaptured;
    private Transform promptFollowRoot;
    private Vector3 fixedPromptLocalPosition;
    private bool fixedPromptPositionCaptured;
    private Font runtimeFont;

    private sealed class Heart
    {
        public RectTransform Rect;
        public CanvasGroup Group;
        public Vector2 StartPosition;
        public float StartTime;
        public float Duration;
        public float Drift;
    }

    private sealed class HeldBonePose
    {
        public Transform Bone;
        public Quaternion RestLocalRotation;
    }

    private static readonly string[] EyeNames =
    {
        "Eye", "eye", "Eyes", "eyes", "Eye_L", "Eye_R",
        "LeftEye", "RightEye", "Left Eye", "Right Eye"
    };

    private static readonly string[] PlayerNames =
    {
        "Player", "player", "Character", "character", "MainCharacter"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallOnNamedDogs()
    {
        InstallOnNamedDogsInLoadedScenes();

        GameObject installerObject = new GameObject("PetInteractionInstaller");
        DontDestroyOnLoad(installerObject);
        installerObject.AddComponent<DogPetInteractionInstaller>();
    }

    private static void InstallOnNamedDogsInLoadedScenes()
    {
        GameObject[] objects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            if (!IsDogLikeName(objects[i].name) || HasDogNamedAncestor(objects[i].transform))
                continue;

            if (objects[i].GetComponent<DogPetInteraction>() == null)
                objects[i].AddComponent<DogPetInteraction>();
        }
    }

    private sealed class DogPetInteractionInstaller : MonoBehaviour
    {
        private float nextScanTime;

        private void Update()
        {
            if (Time.unscaledTime < nextScanTime)
                return;

            nextScanTime = Time.unscaledTime + 0.5f;
            InstallOnNamedDogsInLoadedScenes();
        }
    }

    private static bool HasDogNamedAncestor(Transform candidate)
    {
        Transform ancestor = candidate.parent;
        while (ancestor != null)
        {
            if (IsDogLikeName(ancestor.name))
                return true;
            ancestor = ancestor.parent;
        }

        return false;
    }

    private static bool IsDogLikeName(string objectName)
    {
        string loweredName = objectName.ToLowerInvariant();
        if (loweredName.Contains("movement root"))
            return false;

        return loweredName.Contains("dog") || loweredName.Contains("puppy");
    }

    private void Awake()
    {
        ResolveReferences();
        CreatePrompt();
    }

    private void OnDestroy()
    {
        RestoreHeldPose();

        if (petAnimatorPoseCaptured && animator != null)
            animator.enabled = petAnimatorWasEnabled;

        if (promptCanvas != null)
            Destroy(promptCanvas.gameObject);
        if (heartCanvas != null)
            Destroy(heartCanvas.gameObject);
    }

    private void Update()
    {
        ResolveReferences();

        if (holding)
        {
            // While the dog is in the player's arms, neither pet nor grab
            // guidance should compete with the held character on screen.
            SetPromptVisible(false);
            if (IsGrabPressed())
                ReleaseDog();

            UpdateHearts();
            return;
        }

        canPet = IsPlayerCloseAndLooking(interactionDistance);
        canGrab = IsPlayerCloseAndLooking(grabDistance);
        bool promptVisible = (canPet || canGrab) && !petting;
        if (promptLabel != null)
            promptLabel.text = canGrab ? promptText + "   " + grabPromptText : promptText;

        SetPromptVisible(promptVisible);
        SetDogInteractionFocus(petting || promptVisible);

        if (promptVisible && canGrab && IsGrabPressed())
            GrabDog();
        else if (promptVisible && canPet && IsPetPressed())
            StartCoroutine(PetRoutine());

        UpdateHearts();
    }

    private void LateUpdate()
    {
        // Keep the held pose independent from the prompt UI. In XR or when
        // the camera is briefly unavailable, the UI can be skipped while the
        // dog's limbs must still be updated every frame.
        if (holding)
        {
            UpdateHeldAnchorPosition();
            ApplyHeldPose();
        }

        if (promptCanvas == null || playerCamera == null)
            return;

        promptCanvas.transform.position = GetPromptWorldPosition();
        promptCanvas.transform.LookAt(playerCamera.transform);
        promptCanvas.transform.Rotate(0f, 180f, 0f);

        if (heartCanvas != null)
        {
            heartCanvas.transform.position = GetHeartWorldPosition();
            heartCanvas.transform.LookAt(playerCamera.transform);
            heartCanvas.transform.Rotate(0f, 180f, 0f);
        }
    }

    private void ResolveReferences()
    {
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            playerCamera = Camera.main;

        if (playerTransform == null)
            playerTransform = ResolvePlayer();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (petAgent == null)
            petAgent = GetComponent<PrototypePetAgent>();

        if (headTransform == null)
        {
            // FindChildByNames checks names while traversing the hierarchy,
            // so ask for the actual head before falling back to the neck.
            headTransform = FindChildByNames(new[] { "Head", "head" }) ??
                FindChildByNames(new[] { "Face", "face" }) ??
                FindChildByNames(new[] { "Neck", "neck" });
        }

        if (headTransform != null && !headRestRotationCaptured)
        {
            headRestRotation = headTransform.localRotation;
            headRestRotationCaptured = true;
        }

        if (neckBone == null)
            neckBone = FindChildByNames(new[] { "Neck", "neck" });

        if (neckBone != null && !neckRestRotationCaptured)
        {
            neckRestRotation = neckBone.localRotation;
            neckRestRotationCaptured = true;
        }

        if (eyeObjects == null || eyeObjects.Length == 0)
            eyeObjects = FindEyes();

        ResolvePromptFollowRoot();

        if (promptCanvas != null)
        {
            promptCanvas.worldCamera = playerCamera;
            promptCanvas.transform.position = GetPromptWorldPosition();
        }

        if (heartCanvas != null)
        {
            heartCanvas.worldCamera = playerCamera;
            heartCanvas.transform.position = GetHeartWorldPosition();
        }
    }

    private void SetDogInteractionFocus(bool focused)
    {
        if (petAgent == null)
            petAgent = GetComponent<PrototypePetAgent>();

        if (petAgent != null)
            petAgent.SetInteractionFocus(focused, focused && !petting);
    }

    private void ResolvePromptFollowRoot()
    {
        Transform desiredRoot = petAgent != null && petAgent.MovementRoot != null
            ? petAgent.MovementRoot
            : (transform.parent != null ? transform.parent : transform);

        if (promptFollowRoot != desiredRoot)
        {
            promptFollowRoot = desiredRoot;
            fixedPromptPositionCaptured = false;
        }

        if (fixedPromptPositionCaptured || promptFollowRoot == null)
            return;

        Vector3 anchorWorldPosition = headTransform != null
            ? headTransform.position
            : GetDogBounds().max;
        fixedPromptLocalPosition = promptFollowRoot.InverseTransformPoint(anchorWorldPosition);
        fixedPromptLocalPosition += Vector3.up * Mathf.Clamp(promptHeight, 0.05f, 1f);
        fixedPromptPositionCaptured = true;
    }

    private Transform ResolvePlayer()
    {
        try
        {
            GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
                return taggedPlayer.transform;
        }
        catch (UnityException)
        {
            // The project may not define a Player tag.
        }

        for (int i = 0; i < PlayerNames.Length; i++)
        {
            GameObject namedPlayer = GameObject.Find(PlayerNames[i]);
            if (namedPlayer != null)
                return namedPlayer.transform;
        }

        return playerCamera != null ? playerCamera.transform : null;
    }

    private Transform FindChildByNames(string[] names)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == transform)
                continue;

            for (int j = 0; j < names.Length; j++)
            {
                if (children[i].name.Equals(names[j], System.StringComparison.OrdinalIgnoreCase))
                    return children[i];
            }
        }

        return null;
    }

    private Transform[] FindEyes()
    {
        List<Transform> foundEyes = new List<Transform>();
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == transform)
                continue;

            string childName = children[i].name.ToLowerInvariant();
            for (int j = 0; j < EyeNames.Length; j++)
            {
                if (childName == EyeNames[j].ToLowerInvariant() ||
                    childName.Contains(EyeNames[j].ToLowerInvariant()))
                {
                    if (!foundEyes.Contains(children[i]))
                        foundEyes.Add(children[i]);
                    break;
                }
            }
        }

        return foundEyes.ToArray();
    }

    private Vector3 GetHeartWorldPosition()
    {
        Transform faceTransform = FindChildByNames(new[] { "Head", "head", "Face", "face" });
        if (faceTransform != null)
            return faceTransform.position + Vector3.up * 0.08f;

        if (headTransform != null)
            return headTransform.position + Vector3.up * 0.08f;

        return GetDogBounds().center + Vector3.up * 0.1f;
    }

    private bool IsPlayerCloseAndLooking(float maxDistance)
    {
        if (playerCamera == null)
            return false;

        Bounds dogBounds = GetDogBounds();
        Vector3 playerPosition = playerTransform != null
            ? playerTransform.position
            : playerCamera.transform.position;
        Vector3 closestPoint = dogBounds.ClosestPoint(playerPosition);
        if ((closestPoint - playerPosition).sqrMagnitude > maxDistance * maxDistance)
            return false;

        Vector3 targetPosition = GetGazeTargetPosition(dogBounds);
        Vector3 cameraToDog = targetPosition - playerCamera.transform.position;
        if (cameraToDog.sqrMagnitude < 0.01f ||
            Vector3.Angle(playerCamera.transform.forward, cameraToDog) > lookAngle)
            return false;

        // The view ray must pass through the dog's rendered bounds. This works
        // even when the imported model has no collider of its own.
        Ray gazeRay = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!dogBounds.IntersectRay(gazeRay, out float boundsDistance))
            return false;

        // Reject a wall or another object that blocks the dog before its bounds.
        if (Physics.Raycast(gazeRay, out RaycastHit hit, boundsDistance + 0.05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            hit.distance < boundsDistance - 0.03f &&
            !IsDogPart(hit.transform))
        {
            return false;
        }

        return true;
    }

    private Bounds GetDogBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(transform.position, Vector3.one * 0.5f);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        return bounds;
    }

    private Vector3 GetGazeTargetPosition(Bounds dogBounds)
    {
        if (headTransform != null)
            return headTransform.position;

        return dogBounds.center + Vector3.up * Mathf.Min(0.15f, dogBounds.extents.y * 0.2f);
    }

    private Vector3 GetPromptWorldPosition()
    {
        if (fixedPromptPositionCaptured && promptFollowRoot != null)
            return promptFollowRoot.TransformPoint(fixedPromptLocalPosition);

        Bounds dogBounds = GetDogBounds();
        return dogBounds.max + Vector3.up * Mathf.Clamp(promptHeight, 0.05f, 1f);
    }

    private bool IsDogPart(Transform candidate)
    {
        return candidate == transform || candidate.IsChildOf(transform);
    }

    private bool IsPetPressed()
    {
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    private bool IsGrabPressed()
    {
        return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
    }

    private void GrabDog()
    {
        if (holding || playerCamera == null || petAgent == null)
            return;

        heldMovementRoot = petAgent.MovementRoot;
        if (heldMovementRoot == null)
            return;

        heldOriginalParent = heldMovementRoot.parent;
        CaptureHeldPose();

        // Place the dog from the camera's current facing direction. While
        // held, the anchor is refreshed every frame so turning the player
        // turns and repositions the dog with the player's view.
        // Resolve the hold point from the camera's current world-space axes.
        // This keeps the dog in front of the direction the player is looking,
        // even when the XR camera has rotated independently of its parent.
        float holdForwardDistance = Mathf.Max(0.1f, Mathf.Abs(holdLocalPosition.z));
        Vector3 flatForward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        // Keep the 4m separation horizontal. A pitched-down camera must not
        // push the dog below the floor when the player looks toward the ground.
        Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward).normalized;
        Vector3 holdWorldPosition = playerCamera.transform.position
            + flatRight * holdLocalPosition.x
            + Vector3.up * holdLocalPosition.y
            + flatForward * holdForwardDistance;
        Quaternion holdWorldRotation = playerCamera.transform.rotation * Quaternion.Euler(holdLocalEulerAngles);

        holdAnchor = new GameObject("Dog Hold Anchor").transform;
        holdAnchor.SetPositionAndRotation(holdWorldPosition, holdWorldRotation);

        heldMovementRoot.SetParent(holdAnchor, false);
        heldMovementRoot.localRotation = Quaternion.Euler(heldCradleEulerAngles);

        // Attach at the middle of the torso instead of the neck or model
        // origin. This reads like two hands wrapping around the body between
        // the front paws and hind legs, while the head remains visible above.
        if (holdSupportBone != null)
        {
            Vector3 supportLocalPosition = heldMovementRoot.InverseTransformPoint(holdSupportBone.position);
            heldMovementRoot.localPosition = -supportLocalPosition + holdSupportOffset;
        }
        else
        {
            heldMovementRoot.localPosition = Vector3.zero;
        }

        petAgent.SetCarried(true);
        ApplyHeldPose();

        holding = true;
        canPet = false;
        canGrab = false;
        SetPromptVisible(false);
    }

    private void UpdateHeldAnchorPosition()
    {
        if (holdAnchor == null || playerCamera == null)
            return;

        float holdForwardDistance = Mathf.Max(0.1f, Mathf.Abs(holdLocalPosition.z));
        Vector3 flatForward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward).normalized;
        Vector3 holdWorldPosition = playerCamera.transform.position
            + flatRight * holdLocalPosition.x
            + Vector3.up * holdLocalPosition.y
            + flatForward * holdForwardDistance;
        Quaternion holdWorldRotation = playerCamera.transform.rotation * Quaternion.Euler(holdLocalEulerAngles);
        holdAnchor.SetPositionAndRotation(holdWorldPosition, holdWorldRotation);
    }

    private void ReleaseDog()
    {
        if (!holding || heldMovementRoot == null)
            return;

        Vector3 dropPosition = playerCamera != null
            ? playerCamera.transform.position + playerCamera.transform.forward * 1.15f
            : heldMovementRoot.position;
        Vector3 rayOrigin = dropPosition + Vector3.up * 1.5f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            dropPosition.y = hit.point.y;
        }

        Quaternion dropRotation = playerCamera != null
            ? Quaternion.Euler(0f, playerCamera.transform.eulerAngles.y, 0f)
            : heldMovementRoot.rotation;

        heldMovementRoot.SetParent(heldOriginalParent, true);
        heldMovementRoot.SetPositionAndRotation(dropPosition, dropRotation);
        RestoreHeldPose();
        petAgent.SetCarried(false);

        if (holdAnchor != null)
            Destroy(holdAnchor.gameObject);

        holdAnchor = null;
        heldMovementRoot = null;
        heldOriginalParent = null;
        holding = false;
    }

    private void CaptureHeldPose()
    {
        heldBonePoses.Clear();
        tailBones.Clear();

        Transform skeletonRoot = FindChildByNames(new[] { "Root" });
        if (skeletonRoot != null)
        {
            Transform[] bones = skeletonRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                heldBonePoses.Add(new HeldBonePose
                {
                    Bone = bones[i],
                    RestLocalRotation = bones[i].localRotation
                });
            }
        }

        holdSupportBone = FindChildByNames(new[] { "Spine1" }) ??
            FindChildByNames(new[] { "Spine2" }) ??
            FindChildByNames(new[] { "Spine" });
        leftShoulderBone = FindChildByNames(new[] { "LeftShoulder" });
        rightShoulderBone = FindChildByNames(new[] { "RightShoulder" });
        leftArmBone = FindChildByNames(new[] { "LeftArm" });
        rightArmBone = FindChildByNames(new[] { "RightArm" });
        leftForeArmBone = FindChildByNames(new[] { "LeftForeArm" });
        rightForeArmBone = FindChildByNames(new[] { "RightForeArm" });
        neckBone = FindChildByNames(new[] { "Neck" });
        spineBone = FindChildByNames(new[] { "Spine" });
        spine1Bone = FindChildByNames(new[] { "Spine1" });
        spine2Bone = FindChildByNames(new[] { "Spine2" });
        leftUpperLegBone = FindChildByNames(new[] { "LeftUpLeg" });
        rightUpperLegBone = FindChildByNames(new[] { "RightUpLeg" });
        leftLowerLegBone = FindChildByNames(new[] { "LeftLeg" });
        rightLowerLegBone = FindChildByNames(new[] { "RightLeg" });

        Transform tailRoot = FindChildByNames(new[] { "Tail" });
        if (tailRoot != null)
        {
            Transform[] tailChain = tailRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < tailChain.Length; i++)
                tailBones.Add(tailChain[i]);
        }

        animatorWasEnabled = animator != null && animator.enabled;
        if (animator != null)
            animator.enabled = false;

        heldPoseCaptured = heldBonePoses.Count > 0;
    }

    private void RestoreHeldPose()
    {
        if (!heldPoseCaptured)
            return;

        for (int i = 0; i < heldBonePoses.Count; i++)
        {
            HeldBonePose pose = heldBonePoses[i];
            if (pose.Bone != null)
                pose.Bone.localRotation = pose.RestLocalRotation;
        }

        if (animator != null)
            animator.enabled = animatorWasEnabled;

        heldBonePoses.Clear();
        leftShoulderBone = null;
        rightShoulderBone = null;
        leftArmBone = null;
        rightArmBone = null;
        leftForeArmBone = null;
        rightForeArmBone = null;
        neckBone = null;
        spineBone = null;
        spine1Bone = null;
        spine2Bone = null;
        leftUpperLegBone = null;
        rightUpperLegBone = null;
        leftLowerLegBone = null;
        rightLowerLegBone = null;
        tailBones.Clear();
        holdSupportBone = null;
        heldPoseCaptured = false;
    }

    private void ApplyHeldPose()
    {
        if (!heldPoseCaptured || holdAnchor == null)
            return;

        for (int i = 0; i < heldBonePoses.Count; i++)
        {
            HeldBonePose pose = heldBonePoses[i];
            if (pose.Bone != null)
                pose.Bone.localRotation = pose.RestLocalRotation;
        }

        // The body is supported around its middle, so use a controlled bend
        // through the spine. The lower torso still needs to sag toward the
        // floor instead of staying rigid and horizontal.
        float sag = heldBodySagAngle;
        ApplyLocalOffset(spineBone, Quaternion.Euler(sag * 0.35f, 0f, 0f));
        ApplyLocalOffset(spine1Bone, Quaternion.Euler(sag * 0.65f, 0f, 0f));
        ApplyLocalOffset(spine2Bone, Quaternion.Euler(sag * 0.45f, 0f, 0f));

        // Use the project's gravity direction so the pose remains correct if
        // the game later changes gravity or runs in a rotated XR scene.
        Vector3 downDirection = Physics.gravity.sqrMagnitude > 0.001f
            ? Physics.gravity.normalized
            : Vector3.down;
        Vector3 dogFrontDirection = holdAnchor.TransformDirection(Vector3.back);
        Vector3 dogBackDirection = holdAnchor.TransformDirection(Vector3.forward);
        Vector3 leftDirection = -holdAnchor.right;
        Vector3 rightDirection = holdAnchor.right;

        // Let the lower torso fall away from the supported middle of the body.
        // Re-anchoring the support bone after this keeps the hands under the
        // torso while the rest of the body responds to gravity.
        float torsoGravityInfluence = Mathf.Clamp01(0.28f + heldLimbGravityInfluence * 0.65f);
        Vector3 torsoGravityDirection = Vector3.ProjectOnPlane(downDirection, holdAnchor.right);
        if (torsoGravityDirection.sqrMagnitude < 0.0001f)
            torsoGravityDirection = downDirection;
        torsoGravityDirection.Normalize();
        AimBoneTowardChildInPlane(spineBone, torsoGravityDirection, holdAnchor.right, torsoGravityInfluence);
        Vector3 upperTorsoDropDirection = (torsoGravityDirection * 0.55f + dogFrontDirection * 0.45f).normalized;
        // Keep the head-bearing upper torso readable; most of the drop should
        // happen below the hand support rather than folding the head down.
        AimBoneTowardChildInPlane(spine1Bone, upperTorsoDropDirection, holdAnchor.right, torsoGravityInfluence * 0.18f);

        // Cradled pose: front paws fold toward the chest instead of hanging
        // straight down, while the hind legs open gently toward the player.
        Vector3 leftArmDirection = (dogFrontDirection * 0.7f + leftDirection * 0.35f + Vector3.up * 0.12f).normalized;
        Vector3 rightArmDirection = (dogFrontDirection * 0.7f + rightDirection * 0.35f + Vector3.up * 0.12f).normalized;
        float gravityBias = heldLimbGravityInfluence;
        Vector3 leftForeArmDirection = (dogFrontDirection * 0.48f + leftDirection * 0.18f + Vector3.up * 0.22f + downDirection * (0.12f + gravityBias * 0.22f)).normalized;
        Vector3 rightForeArmDirection = (dogFrontDirection * 0.48f + rightDirection * 0.18f + Vector3.up * 0.22f + downDirection * (0.12f + gravityBias * 0.22f)).normalized;
        Vector3 leftLegDirection = (dogFrontDirection * 0.38f + leftDirection * 0.42f + downDirection * (0.42f + gravityBias * 0.5f)).normalized;
        Vector3 rightLegDirection = (dogFrontDirection * 0.38f + rightDirection * 0.42f + downDirection * (0.42f + gravityBias * 0.5f)).normalized;
        Vector3 leftLowerLegDirection = (dogFrontDirection * 0.5f + leftDirection * 0.3f + downDirection * (0.32f + gravityBias * 0.42f)).normalized;
        Vector3 rightLowerLegDirection = (dogFrontDirection * 0.5f + rightDirection * 0.3f + downDirection * (0.32f + gravityBias * 0.42f)).normalized;
        Vector3 tailDirection = (downDirection * (0.65f + gravityBias * 0.35f) + dogBackDirection * 0.35f).normalized;

        AimBoneTowardChild(leftShoulderBone, leftArmDirection, 0.65f);
        AimBoneTowardChild(rightShoulderBone, rightArmDirection, 0.65f);
        AimBoneTowardChild(leftArmBone, leftArmDirection, 0.65f);
        AimBoneTowardChild(rightArmBone, rightArmDirection, 0.65f);
        AimBoneTowardChild(leftForeArmBone, leftForeArmDirection, 0.7f);
        AimBoneTowardChild(rightForeArmBone, rightForeArmDirection, 0.7f);
        AimBoneTowardChild(leftUpperLegBone, leftLegDirection, 0.58f);
        AimBoneTowardChild(rightUpperLegBone, rightLegDirection, 0.58f);
        AimBoneTowardChild(leftLowerLegBone, leftLowerLegDirection, 0.62f);
        AimBoneTowardChild(rightLowerLegBone, rightLowerLegDirection, 0.62f);

        for (int i = 0; i < tailBones.Count; i++)
        {
            Vector3 segmentDirection = (tailDirection + dogBackDirection * (i * 0.12f)).normalized;
            AimBoneTowardChild(tailBones[i], segmentDirection, heldTailGravityInfluence * 0.8f);
        }

        KeepHeldFaceReadable();
        KeepHeldSupportAtAnchor();
    }

    private void KeepHeldSupportAtAnchor()
    {
        if (heldMovementRoot == null || holdSupportBone == null)
            return;

        Vector3 supportLocalPosition = heldMovementRoot.InverseTransformPoint(holdSupportBone.position);
        heldMovementRoot.localPosition = -supportLocalPosition + holdSupportOffset;
    }

    private void KeepHeldFaceReadable()
    {
        if (headTransform == null)
            return;

        Vector3 targetPosition = playerCamera != null
            ? playerCamera.transform.position
            : headTransform.position + holdAnchor.TransformDirection(Vector3.back);
        Vector3 lookDirection = targetPosition - headTransform.position;
        if (lookDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        if (neckBone != null)
            neckBone.rotation = Quaternion.Slerp(neckBone.rotation, targetRotation, 0.35f);
        headTransform.rotation = Quaternion.Slerp(headTransform.rotation, targetRotation, 0.7f);
    }

    private static void ApplyLocalOffset(Transform bone, Quaternion offset)
    {
        if (bone != null)
            bone.localRotation *= offset;
    }

    private static void AimBoneTowardChild(Transform bone, Vector3 desiredDirection, float influence = 1f)
    {
        if (bone == null || bone.childCount == 0 || desiredDirection.sqrMagnitude < 0.001f)
            return;

        Transform child = bone.GetChild(0);
        Vector3 currentDirection = child.position - bone.position;
        if (currentDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 blendedDirection = Vector3.Slerp(
            currentDirection.normalized,
            desiredDirection.normalized,
            Mathf.Clamp01(influence));
        Quaternion correction = Quaternion.FromToRotation(
            currentDirection.normalized,
            blendedDirection);
        bone.rotation = correction * bone.rotation;
    }

    private static void AimBoneTowardChildInPlane(
        Transform bone,
        Vector3 desiredDirection,
        Vector3 planeNormal,
        float influence)
    {
        if (bone == null || bone.childCount == 0 || desiredDirection.sqrMagnitude < 0.001f)
            return;

        Transform child = bone.GetChild(0);
        Vector3 currentDirection = child.position - bone.position;
        Vector3 currentPlanarDirection = Vector3.ProjectOnPlane(currentDirection, planeNormal);
        Vector3 desiredPlanarDirection = Vector3.ProjectOnPlane(desiredDirection, planeNormal);
        if (currentPlanarDirection.sqrMagnitude < 0.0001f || desiredPlanarDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion correction = Quaternion.FromToRotation(
            currentPlanarDirection.normalized,
            desiredPlanarDirection.normalized);
        Quaternion planarCorrection = Quaternion.Slerp(
            Quaternion.identity,
            correction,
            Mathf.Clamp01(influence));
        bone.rotation = planarCorrection * bone.rotation;
    }

    private IEnumerator PetRoutine()
    {
        petting = true;
        SetDogInteractionFocus(true);
        SetPromptVisible(false);
        SpawnHearts();
        float focusEndTime = Time.unscaledTime + petFocusDuration;

        // The imported pet animation can drive the ear/head bones without
        // moving the face vertices weighted to the neck. Pause it while the
        // procedural face tilt runs so the whole face shares one motion.
        petAnimatorPoseCaptured = animator != null;
        petAnimatorWasEnabled = animator != null && animator.enabled;
        if (animator != null)
            animator.enabled = false;

        if (headTransform != null || neckBone != null)
        {
            for (int i = 0; i < headTilts; i++)
            {
                float direction = i % 2 == 0 ? 1f : -1f;
                yield return RotateFaceTo(direction * tiltAngle);
                yield return RotateFaceTo(0f);
            }
        }
        else
        {
            yield return new WaitForSeconds(tiltDuration * headTilts * 2f);
        }

        float remainingFocusTime = focusEndTime - Time.unscaledTime;
        if (remainingFocusTime > 0f)
            yield return new WaitForSecondsRealtime(remainingFocusTime);

        if (petAnimatorPoseCaptured && animator != null)
            animator.enabled = petAnimatorWasEnabled;
        petAnimatorPoseCaptured = false;

        petting = false;
        SetDogInteractionFocus(false);
    }

    private IEnumerator RotateFaceTo(float rollAngle)
    {
        Quaternion headTarget = headRestRotation * Quaternion.Euler(0f, 0f, rollAngle * 0.6f);
        Quaternion neckTarget = neckRestRotation * Quaternion.Euler(0f, 0f, rollAngle * 0.4f);
        Quaternion headStart = headTransform != null ? headTransform.localRotation : Quaternion.identity;
        Quaternion neckStart = neckBone != null ? neckBone.localRotation : Quaternion.identity;
        float elapsed = 0f;
        while (elapsed < tiltDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / tiltDuration);
            if (neckBone != null)
                neckBone.localRotation = Quaternion.Slerp(neckStart, neckTarget, t);
            if (headTransform != null)
                headTransform.localRotation = Quaternion.Slerp(headStart, headTarget, t);
            yield return null;
        }

        if (neckBone != null)
            neckBone.localRotation = neckTarget;
        if (headTransform != null)
            headTransform.localRotation = headTarget;
    }

    private void CreatePrompt()
    {
        runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasObject = new GameObject("DogPetPrompt");
        canvasObject.transform.SetParent(transform, false);
        promptCanvas = canvasObject.AddComponent<Canvas>();
        promptCanvas.renderMode = RenderMode.WorldSpace;
        promptCanvas.worldCamera = playerCamera;
        promptCanvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasObject.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(360f, 58f);
        canvasRect.localScale = Vector3.one * 0.004f;

        promptBackground = new GameObject("PromptBackground");
        promptBackground.transform.SetParent(canvasObject.transform, false);
        Image panel = promptBackground.AddComponent<Image>();
        panel.color = new Color(0.06f, 0.04f, 0.08f, 0.88f);
        RectTransform panelRect = promptBackground.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        GameObject labelObject = new GameObject("PromptLabel");
        labelObject.transform.SetParent(canvasObject.transform, false);
        promptLabel = labelObject.AddComponent<Text>();
        promptLabel.font = runtimeFont;
        promptLabel.fontSize = 23;
        promptLabel.fontStyle = FontStyle.Bold;
        promptLabel.alignment = TextAnchor.MiddleCenter;
        promptLabel.text = promptText;
        promptLabel.color = promptColor;
        promptLabel.raycastTarget = false;

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        SetPromptVisible(false);

        GameObject heartCanvasObject = new GameObject("DogPetHearts");
        heartCanvasObject.transform.SetParent(transform, false);
        heartCanvas = heartCanvasObject.AddComponent<Canvas>();
        heartCanvas.renderMode = RenderMode.WorldSpace;
        heartCanvas.worldCamera = playerCamera;
        heartCanvas.sortingOrder = 101;

        CanvasScaler heartScaler = heartCanvasObject.AddComponent<CanvasScaler>();
        heartScaler.dynamicPixelsPerUnit = 10f;

        RectTransform heartCanvasRect = heartCanvasObject.GetComponent<RectTransform>();
        heartCanvasRect.sizeDelta = new Vector2(260f, 160f);
        heartCanvasRect.localScale = Vector3.one * 0.006f;
        heartCanvasObject.transform.position = GetHeartWorldPosition();
        heartCanvasObject.transform.LookAt(playerCamera != null ? playerCamera.transform : transform);
        heartCanvasObject.transform.Rotate(0f, 180f, 0f);
    }

    private void SetPromptVisible(bool visible)
    {
        if (promptBackground != null)
            promptBackground.SetActive(visible);
        if (promptLabel != null)
            promptLabel.gameObject.SetActive(visible);
    }

    private void SpawnHearts()
    {
        if (heartCanvas == null)
            return;

        for (int i = 0; i < 4; i++)
        {
            GameObject heartObject = new GameObject("PetHeart");
            heartObject.transform.SetParent(heartCanvas.transform, false);

            Text heartLabel = heartObject.AddComponent<Text>();
            heartLabel.font = runtimeFont;
            heartLabel.fontSize = Random.Range(24, 34);
            heartLabel.alignment = TextAnchor.MiddleCenter;
            heartLabel.text = "♥";
            heartLabel.color = new Color(1f, Random.Range(0.35f, 0.7f), 0.72f, 1f);
            heartLabel.raycastTarget = false;

            RectTransform heartRect = heartObject.GetComponent<RectTransform>();
            heartRect.sizeDelta = new Vector2(60f, 60f);
            heartRect.anchoredPosition = new Vector2(Random.Range(-75f, 75f), Random.Range(-35f, 35f));

            CanvasGroup group = heartObject.AddComponent<CanvasGroup>();
            hearts.Add(new Heart
            {
                Rect = heartRect,
                Group = group,
                StartPosition = heartRect.anchoredPosition,
                StartTime = Time.time,
                Duration = Random.Range(0.65f, 1.05f),
                Drift = Random.Range(-35f, 35f)
            });
        }
    }

    private void UpdateHearts()
    {
        for (int i = hearts.Count - 1; i >= 0; i--)
        {
            Heart heart = hearts[i];
            float t = (Time.time - heart.StartTime) / heart.Duration;
            if (t >= 1f || heart.Rect == null)
            {
                if (heart.Rect != null)
                    Destroy(heart.Rect.gameObject);
                hearts.RemoveAt(i);
                continue;
            }

            heart.Rect.anchoredPosition = heart.StartPosition + new Vector2(heart.Drift * t, 55f * t);
            heart.Rect.localScale = Vector3.one * Mathf.Lerp(0.75f, 1.15f, Mathf.Sin(t * Mathf.PI));
            heart.Group.alpha = Mathf.Sin((1f - t) * Mathf.PI * 0.5f);
        }
    }
}
