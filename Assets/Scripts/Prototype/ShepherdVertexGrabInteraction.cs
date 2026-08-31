using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 셰퍼드를 목 뒤쪽 뼈로 잡아 들어 올리는 기능입니다.
/// 얼굴 폴리곤을 직접 늘리지 않고 리깅된 척추, 다리, 꼬리 뼈를 중력 방향으로
/// 회전시켜 목을 잡고 몸 전체가 아래로 매달리는 모습을 만듭니다.
/// </summary>
public sealed class ShepherdVertexGrabInteraction : MonoBehaviour
{
    #region 인스펙터 설정

    [Header("잡기 입력")]
    [Tooltip("G 키를 눌러 잡기와 내려놓기를 사용할지 여부입니다.")]
    [SerializeField] private bool useKeyboardGrab = true;
    [Tooltip("잡기 판정에 사용할 플레이어 카메라입니다. 비워 두면 Main Camera를 자동으로 찾습니다.")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("카메라에서 강아지까지 잡을 수 있는 최대 거리입니다.")]
    [SerializeField, Min(0.5f)] private float grabDistance = 3f;

    [Header("목 뒤 잡기 기준")]
    [Tooltip("실제로 고정할 목 뒤쪽 뼈입니다. 비워 두면 아래 이름으로 자동으로 찾습니다.")]
    [SerializeField] private Transform neckGrabBone;
    [Tooltip("현재 셰퍼드 리깅에서 목 뒤 기준으로 사용할 뼈 이름입니다.")]
    [SerializeField] private string neckGrabBoneName = "DEF-spine.010";
    [Tooltip("목뼈 중심에서 플레이어 반대쪽으로 이동하는 표면 보정 거리입니다.")]
    [SerializeField, Range(0f, 0.2f)] private float neckGripSurfaceOffset = 0.08f;

    [Header("들고 있을 때 위치")]
    [Tooltip("상호작용 거리 사용을 끄거나 현재 거리를 계산할 수 없을 때 사용하는 기본 거리입니다.")]
    [SerializeField, Min(1f)] private float holdDistance = 3f;
    [Tooltip("잡는 순간 플레이어와 목 뒤 상호작용 지점 사이의 실제 거리를 그대로 들기 거리로 사용할지 여부입니다.")]
    [SerializeField] private bool useInteractionDistanceWhenGrabbed = true;
    [Tooltip("상호작용 거리로 들 때 플레이어와 강아지가 지나치게 겹치지 않도록 보장하는 최소 거리입니다.")]
    [SerializeField, Range(0.5f, 2f)] private float minimumInteractionHoldDistance = 0.8f;
    [Tooltip("멀리 있는 지점을 잡았을 때 강아지가 지나치게 멀리 유지되지 않도록 제한하는 최대 거리입니다.")]
    [SerializeField, Range(1.5f, 4f)] private float maximumInteractionHoldDistance = 3.6f;
    [Tooltip("잡은 직후 실제 상호작용 거리보다 강아지를 추가로 뒤쪽에 배치하는 거리입니다. 값이 클수록 화면에서 조금 더 멀리 보입니다.")]
    [SerializeField, Range(0f, 1f)] private float heldDistanceExtra = 0.45f;
    [Tooltip("강아지를 화면 중앙보다 위 또는 아래에 배치하는 높이입니다.")]
    [SerializeField] private float holdVerticalOffset = -0.2f;
    [Tooltip("플레이어가 위나 아래를 바라볼 때 잡은 위치가 화면 방향을 따라가는 정도입니다.")]
    [SerializeField, Range(0f, 1f)] private float holdVerticalFollow = 1f;
    [Tooltip("강아지 전체 위치가 목표점을 따라가는 부드러움입니다.")]
    [SerializeField, Min(1f)] private float holdFollowSpeed = 10f;
    [Tooltip("강아지가 플레이어를 향해 수평으로 돌아가는 부드러움입니다.")]
    [SerializeField, Min(1f)] private float holdRotationSpeed = 10f;
    [Tooltip("마우스를 빠르게 움직였을 때 잡은 위치가 목표점을 따라가는 데 걸리는 시간입니다. 값이 클수록 반동은 부드러워지지만 조금 늦게 따라옵니다.")]
    [SerializeField, Range(0.03f, 0.4f)] private float holdTargetSmoothTime = 0.12f;
    [Tooltip("잡은 위치가 한 초 동안 이동할 수 있는 최대 거리입니다. 몸이 한 번에 튕기거나 한 바퀴 도는 현상을 막습니다.")]
    [SerializeField, Range(1f, 15f)] private float holdTargetMaximumSpeed = 7f;
    [Tooltip("강아지를 들고 있을 때 카메라의 위아래 방향을 따라갈 수 있는 최대 각도입니다. 이 각도를 넘어 마우스를 내려도 강아지가 플레이어 뒤나 아래로 넘어가지 않습니다.")]
    [SerializeField, Range(20f, 80f)] private float heldCameraPitchLimit = 55f;
    [Tooltip("처음 잡은 높이를 기준으로 잡은 위치가 위아래로 이동할 수 있는 최대 거리입니다. 목 뒤 피부가 계속 끌려가거나 몸이 머리 위로 넘어가는 것을 막습니다.")]
    [SerializeField, Range(0.3f, 2f)] private float heldTargetMaximumVerticalTravel = 1f;
    [Tooltip("들고 있는 동안 엉덩이와 발이 바닥 아래로 내려가지 않도록 남겨 두는 최소 여유 높이입니다.")]
    [SerializeField, Range(0f, 0.5f)] private float heldGroundClearance = 0.12f;
    [Tooltip("들고 있는 강아지 몸이 플레이어를 향해 회전할 수 있는 초당 최대 각도입니다. 값이 낮을수록 몸이 갑자기 뒤집히지 않습니다.")]
    [SerializeField, Range(45f, 360f)] private float heldBodyMaximumTurnSpeed = 160f;

    [Header("몸 전체 중력 포즈")]
    [Tooltip("전체 중력 포즈의 세기입니다. 0은 거의 원래 포즈, 1은 강하게 아래로 처집니다.")]
    [SerializeField, Range(0f, 1f)] private float gravityStrength = 0.5f;
    [Tooltip("몸통 척추가 목 아래쪽으로 처지는 비율입니다.")]
    [SerializeField, Range(0f, 1f)] private float bodyGravityInfluence = 0.9f;
    [Tooltip("엉덩이와 하부 척추가 목 아래쪽으로 내려오는 비율입니다. 값이 클수록 엉덩이가 몸통 아래로 매달립니다.")]
    [SerializeField, Range(0f, 1f)] private float pelvisGravityInfluence = 0.95f;
    [Tooltip("목에서 엉덩이까지의 실제 척추 길이 중 아래로 늘어뜨릴 길이 비율입니다. 값이 너무 크면 몸이 팽팽해지고, 너무 작으면 허리가 많이 접힙니다.")]
    [SerializeField, Range(0.55f, 1f)] private float pelvisHangLengthRatio = 0.88f;
    [Tooltip("목과 엉덩이 사이에 반드시 남겨 두는 최소 척추 길이 비율입니다. 몸통이 뒤로 C자처럼 접히거나 얼굴이 가슴에 묻히면 값을 높입니다.")]
    [SerializeField, Range(0.7f, 0.95f)] private float heldSpineMinimumLengthRatio = 0.84f;
    [Tooltip("들었을 때 엉덩이를 플레이어 반대쪽으로 물리는 거리입니다. 몸통이 가슴 앞으로 접히지 않고 목 아래에서 자연스럽게 매달리도록 합니다.")]
    [SerializeField, Range(0f, 0.4f)] private float heldPelvisBackwardOffset = 0.14f;
    [Tooltip("들었을 때 척추 마디들이 목 아래에서 한 방향으로 이어지게 만드는 힘입니다. 몸이 뒤로 활처럼 휘거나 얼굴이 가슴에 묻히면 값을 높입니다.")]
    [SerializeField, Range(0f, 1f)] private float heldSpineStraighteningStrength = 0.94f;
    [Tooltip("목의 잡힌 위치와 아래쪽 엉덩이를 척추 관절로 연결할 때 자세를 다듬는 반복 횟수입니다. 5~8 정도가 자연스럽습니다.")]
    [SerializeField, Range(1, 12)] private int spineIkIterations = 6;
    [Tooltip("척추 관절들이 목과 엉덩이 사이를 따라 휘어지는 정도입니다. 너무 낮으면 목이 목표점에 닿지 않고, 너무 높으면 자세 변화가 딱딱해질 수 있습니다.")]
    [SerializeField, Range(0f, 1f)] private float spineIkStrength = 0.9f;
    [Tooltip("들려 있을 때 강아지 머리가 플레이어를 바라볼 수 있는 최대 각도입니다.")]
    [SerializeField, Range(0f, 90f)] private float heldHeadLookAngle = 65f;
    [Tooltip("들려 있는 강아지가 얼굴 방향을 유지하는 힘입니다. 값이 클수록 척추가 흔들려도 얼굴이 플레이어 쪽을 단단하게 바라봅니다.")]
    [SerializeField, Range(0f, 1f)] private float heldHeadStiffness = 0.92f;
    [Tooltip("강아지가 플레이어를 볼 때 고개를 위아래로 움직일 수 있는 최대 각도입니다. 화면을 크게 올리거나 내려도 머리가 과하게 꺾이지 않도록 제한합니다.")]
    [SerializeField, Range(0f, 45f)] private float heldHeadMaximumPitch = 18f;
    [Tooltip("목의 움직임과 별개로 머리뼈가 똑바른 형태를 유지하도록 돕는 추가 지지력입니다.")]
    [SerializeField, Range(0f, 1f)] private float heldHeadUprightSupport = 0.75f;
    [Tooltip("잡힌 목뼈가 머리를 위로 받치며 플레이어 쪽으로 세워지는 힘입니다.")]
    [SerializeField, Range(0f, 1f)] private float heldNeckUprightStrength = 0.88f;
    [Tooltip("세운 목이 플레이어 쪽으로 기울어지는 정도입니다. 값이 클수록 얼굴이 카메라 쪽으로 더 잘 보입니다.")]
    [SerializeField, Range(0f, 1f)] private float heldNeckForwardLean = 0.42f;
    [Tooltip("목을 세울 때 원래 자세에서 회전할 수 있는 최대 각도입니다.")]
    [SerializeField, Range(0f, 90f)] private float heldNeckMaximumAngle = 58f;
    [Tooltip("앞다리와 뒷다리가 아래로 늘어지는 비율입니다.")]
    [SerializeField, Range(0f, 1f)] private float limbGravityInfluence = 0.85f;
    [Tooltip("꼬리가 아래로 축 처지는 비율입니다.")]
    [SerializeField, Range(0f, 1f)] private float tailGravityInfluence = 0.9f;
    [Tooltip("척추가 원래 방향에서 아래 방향으로 꺾일 수 있는 최대 각도입니다.")]
    [SerializeField, Range(0f, 90f)] private float bodyMaximumSagAngle = 78f;
    [Tooltip("다리 뼈가 아래 방향으로 꺾일 수 있는 최대 각도입니다.")]
    [SerializeField, Range(0f, 90f)] private float limbMaximumSagAngle = 55f;
    [Tooltip("뒷다리 체인이 바닥 방향으로 늘어질 수 있는 최대 각도입니다. 앞다리보다 크게 설정해 골반 아래로 떨어지게 합니다.")]
    [SerializeField, Range(0f, 90f)] private float hindLegMaximumSagAngle = 78f;
    [Tooltip("꼬리 뼈가 아래 방향으로 꺾일 수 있는 최대 각도입니다.")]
    [SerializeField, Range(0f, 90f)] private float tailMaximumSagAngle = 80f;
    [Tooltip("중력 포즈가 완성되는 속도입니다. 낮을수록 몸이 늦게 따라와 더 유연하게 보입니다.")]
    [SerializeField, Min(1f)] private float deformationFollowSpeed = 5f;

    [Header("중력 영향 범위")]
    [Tooltip("몸통과 다리 포즈에 중력이 영향을 주는 기준 거리입니다.")]
    [SerializeField, Min(0.1f)] private float gravityInfluenceRadius = 1.25f;

    [Header("Natural grab transitions")]
    [Tooltip("Blend into and out of the held pose. Disable this to restore the previous immediate grab/release behavior.")]
    [SerializeField] private bool useNaturalGrabTransitions = true;
    [Tooltip("Seconds used to move the dog from the grabbed point into the held position.")]
    [SerializeField, Range(0.1f, 1.5f)] private float naturalLiftDuration = 0.45f;
    [Tooltip("Small upward arc used while lifting the dog.")]
    [SerializeField, Range(0f, 0.35f)] private float naturalLiftArcHeight = 0.1f;
    [Tooltip("Seconds used to lower the dog onto the nearest floor.")]
    [SerializeField, Range(0.2f, 2f)] private float naturalLowerDuration = 0.7f;
    [Tooltip("Clearance kept between the released dog's rendered feet and the floor.")]
    [SerializeField, Range(0.01f, 0.15f)] private float naturalReleaseGroundClearance = 0.04f;
    [Tooltip("Seconds used to blend from the final lowered pose into the resumed idle animation.")]
    [SerializeField, Range(0.1f, 0.75f)] private float naturalReleaseAnimationBlendDuration = 0.32f;

    [Header("Held panting")]
    [Tooltip("Play the breathing clip's jaw and tongue motion while the dog is held.")]
    [SerializeField] private bool playPantingWhileHeld = true;
    [Tooltip("Optional override. When empty, a controller clip containing this name is selected automatically.")]
    [SerializeField] private AnimationClip heldPantingClip;
    [SerializeField] private string heldPantingClipName = "Idle Breathing";
    [Tooltip("Controller clip names used at random while the dog is held. The current clip completes one full cycle before another clip is selected.")]
    [SerializeField] private string[] heldIdleClipNames = Array.Empty<string>();
    [Tooltip("Seconds used to blend the face and head from one held idle into the next.")]
    [SerializeField, Range(0.05f, 0.75f)] private float heldIdleTransitionDuration = 0.28f;
    [Tooltip("Playback speed of the held panting loop.")]
    [SerializeField, Range(0.5f, 2.5f)] private float heldPantingSpeed = 1.15f;
    [Tooltip("Strength of jaw and tongue animation while held.")]
    [SerializeField, Range(0f, 1f)] private float heldPantingStrength = 1f;
    [Tooltip("Strength of the held idle clip's subtle head movement.")]
    [SerializeField, Range(0f, 0.5f)] private float heldHeadIdleStrength = 0.18f;

    [Header("Held collision")]
    [Tooltip("Layers that block the dog while it is being held.")]
    [SerializeField] private LayerMask heldCollisionMask = Physics.DefaultRaycastLayers;
    [Tooltip("Small clearance kept between the held dog and walls or floors.")]
    [SerializeField, Range(0.005f, 0.1f)] private float heldCollisionSkin = 0.025f;
    [Tooltip("Scales the rendered bounds used for held collision checks.")]
    [SerializeField, Range(0.8f, 1f)] private float heldCollisionBoundsScale = 0.98f;
    [Tooltip("Number of wall-slide passes used when the held dog reaches a surface.")]
    [SerializeField, Range(1, 4)] private int heldCollisionSlideIterations = 3;

    #endregion

    #region 내부 상태

    private sealed class BoneRestPose
    {
        public Transform bone;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    private struct HeldIdleBonePose
    {
        public bool valid;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    private readonly List<BoneRestPose> restPoses = new List<BoneRestPose>();
    private readonly List<BoneRestPose> releaseStartPoses = new List<BoneRestPose>();
    private readonly RaycastHit[] heldCollisionHits = new RaycastHit[32];
    private readonly Collider[] heldOverlapResults = new Collider[32];
    private GameObject heldCollisionProbeObject;
    private BoxCollider heldCollisionProbe;
    private GameObject heldPantingSamplerRoot;
    private Transform heldPantingSamplerJaw;
    private Transform heldPantingSamplerTongue;
    private Transform heldPantingSamplerTongueMiddle;
    private Transform heldPantingSamplerTongueTip;
    private Transform heldPantingSamplerHead;
    private Quaternion heldPantingSamplerHeadRestRotation = Quaternion.identity;
    private readonly List<AnimationClip> heldIdleClips = new List<AnimationClip>();
    private float heldPantingTime;
    private int heldIdleClipIndex = -1;
    private int previousHeldIdleClipIndex = -1;
    private float heldIdleTransitionTime;
    private bool heldIdleTransitionActive;
    private bool heldIdleClipsResolved;
    private HeldIdleBonePose heldIdleTransitionHead;
    private HeldIdleBonePose heldIdleTransitionJaw;
    private HeldIdleBonePose heldIdleTransitionTongue;
    private HeldIdleBonePose heldIdleTransitionTongueMiddle;
    private HeldIdleBonePose heldIdleTransitionTongueTip;

    private Animator animator;
    private ShepherdCompanionAI shepherdAI;
    private SkinnedMeshRenderer skinnedRenderer;
    private MeshCollider meshCollider;
    private Mesh grabCollisionMesh;

    private Transform torsoBase;
    private Transform torsoLower;
    private Transform torsoMid;
    private Transform torsoUpper;
    private Transform torsoNeck;
    private Transform neckBase;
    private Transform frontShoulderLeft;
    private Transform frontShoulderRight;
    private Transform frontThighLeft;
    private Transform frontThighRight;
    private Transform frontShinLeft;
    private Transform frontShinRight;
    private Transform frontFootLeft;
    private Transform frontFootRight;
    private Transform hindThighLeft;
    private Transform hindThighRight;
    private Transform hindShinLeft;
    private Transform hindShinRight;
    private Transform hindFootLeft;
    private Transform hindFootRight;
    private Transform tailBase;
    private Transform tailMiddle;
    private Transform tailTip;
    private Transform tailEnd;
    private Transform headBone;
    private Transform jawBone;
    private Transform tongueBone;
    private Transform tongueMiddleBone;
    private Transform tongueTipBone;
    private Vector3 headForwardAxisLocal = Vector3.forward;
    private Vector3 headUpAxisLocal = Vector3.up;

    private bool holding;
    private Vector3 rootVelocity;
    private Vector3 currentGrabTarget;
    private Vector3 grabTargetVelocity;
    private Vector3 previousPlayerCameraPosition;
    private bool playerCameraPositionInitialized;
    private Transform playerCollisionRoot;
    private Vector3 stableHeldPlanarForward;
    private float activeHoldDistance;
    private float initialTargetWorldY;
    private float nextColliderRefreshTime;
    private float heldPoseBlend;
    private bool liftingTransition;
    private bool loweringTransition;
    private float transitionStartTime;
    private float liftTransitionProgress = 1f;
    private float lowerTransitionProgress;
    private Vector3 liftStartGrabTarget;
    private Vector3 lowerStartRootPosition;
    private Vector3 lowerTargetRootPosition;
    private float lowerGroundHeight;
    private bool lowerGroundFound;
    private bool releasePoseBlending;
    private float releasePoseBlendStartTime;
    private float spineChainLength;
    private float heldBodyDropBelowGrip;
    private bool grabTargetInitialized;

    private bool animatorWasEnabled;
    private bool animatorWasApplyRootMotion;
    private float animatorWasSpeed;
    private bool aiWasEnabled;
    private bool skinnedRendererWasEnabled;

    #endregion

    #region Unity 생명주기

    private void Awake()
    {
        ResolveReferences();
        CacheBones();
        CreateGrabCollisionMesh();
    }

    private void Update()
    {
        ResolveReferences();

        if (!holding && Time.time >= nextColliderRefreshTime)
        {
            RefreshGrabCollisionMesh();
            nextColliderRefreshTime = Time.time + 0.12f;
        }

        if (useKeyboardGrab && Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
        {
            if (holding)
                ReleaseShepherd();
            else if (TryFindGrabHit())
                GrabShepherd();
        }
    }

    private void LateUpdate()
    {
        if (releasePoseBlending)
            UpdateReleasePoseBlend();

        if (!holding || playerCamera == null || neckGrabBone == null)
            return;

        if (loweringTransition)
        {
            UpdateNaturalLowering();
            ApplyHeldGravityPose();
            transform.position = MoveHeldRootWithCollision(transform.position);

            if (lowerTransitionProgress >= 1f)
                CompleteReleaseShepherd();
            return;
        }

        UpdateHeldRoot();
        ApplyHeldGravityPose();
    }

    private void OnDisable()
    {
        if (holding)
            CompleteReleaseShepherd();

        releasePoseBlending = false;
        releaseStartPoses.Clear();
    }

    private void OnDestroy()
    {
        DestroyRuntimeHelper(heldCollisionProbeObject);
        DestroyRuntimeHelper(heldPantingSamplerRoot);
    }

    private void DestroyRuntimeHelper(GameObject helper)
    {
        if (helper == null)
            return;

        if (Application.isPlaying)
            Destroy(helper);
        else
            DestroyImmediate(helper);
    }

    #endregion

    #region 참조 및 충돌 판정

    private void ResolveReferences()
    {
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            playerCamera = Camera.main;

        if (playerCamera != null &&
            (playerCollisionRoot == null ||
             !playerCamera.transform.IsChildOf(playerCollisionRoot)))
        {
            CharacterController playerController =
                playerCamera.GetComponentInParent<CharacterController>();
            playerCollisionRoot = playerController != null
                ? playerController.transform
                : playerCamera.transform.root;
        }

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (shepherdAI == null)
            shepherdAI = GetComponent<ShepherdCompanionAI>();

        if (skinnedRenderer == null)
            skinnedRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (neckGrabBone == null && !string.IsNullOrEmpty(neckGrabBoneName))
            neckGrabBone = FindBone(neckGrabBoneName);
    }

    private bool TryFindGrabHit()
    {
        if (playerCamera == null || meshCollider == null || !meshCollider.enabled)
            return false;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                Mathf.Max(grabDistance, 3f),
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore))
            return false;

        return hit.collider == meshCollider;
    }

    private void CreateGrabCollisionMesh()
    {
        if (skinnedRenderer == null || skinnedRenderer.sharedMesh == null)
            return;

        meshCollider = skinnedRenderer.GetComponent<MeshCollider>();
        if (meshCollider == null)
            meshCollider = skinnedRenderer.gameObject.AddComponent<MeshCollider>();

        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycastLayer >= 0)
            meshCollider.gameObject.layer = ignoreRaycastLayer;

        meshCollider.convex = false;
        meshCollider.isTrigger = false;
        RefreshGrabCollisionMesh();
    }

    private void RefreshGrabCollisionMesh()
    {
        if (skinnedRenderer == null || meshCollider == null || !skinnedRenderer.enabled)
            return;

        if (grabCollisionMesh == null)
        {
            grabCollisionMesh = new Mesh
            {
                name = "Shepherd Grab Collision Mesh (Runtime)"
            };
            grabCollisionMesh.MarkDynamic();
        }

        skinnedRenderer.BakeMesh(grabCollisionMesh, true);
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = grabCollisionMesh;
        meshCollider.enabled = true;
    }

    #endregion

    #region 잡기 및 내려놓기

    private void GrabShepherd()
    {
        if (holding || neckGrabBone == null || skinnedRenderer == null)
            return;

        holding = true;
        rootVelocity = Vector3.zero;
        grabTargetVelocity = Vector3.zero;
        heldPoseBlend = 0f;
        BeginHeldIdleSequence();
        releasePoseBlending = false;
        releaseStartPoses.Clear();

        // 애니메이션 뼈를 기본 포즈로 되돌리기 전에 실제로 상호작용한 목 뒤 위치를 저장합니다.
        // 이 위치와 거리를 사용해야 강아지가 고정된 3m 지점으로 순간 이동하지 않습니다.
        Vector3 interactionGripPoint = GetNeckGripWorldPoint();
        float interactionDistance = Vector3.Distance(
            playerCamera.transform.position,
            interactionGripPoint);
        activeHoldDistance = useInteractionDistanceWhenGrabbed
            ? Mathf.Clamp(
                interactionDistance + heldDistanceExtra,
                minimumInteractionHoldDistance,
                Mathf.Max(maximumInteractionHoldDistance, 3.6f))
            : holdDistance;

        // 애니메이션이 만든 순간 자세가 남아 있으면 머리 기준축도 함께 흔들릴 수 있으므로,
        // 들기 포즈와 동일한 기본 뼈 상태에서 얼굴의 앞·위 방향 기준을 저장합니다.
        ResetBonePose();
        spineChainLength = CalculateSpineChainLength();
        heldBodyDropBelowGrip = Mathf.Max(
            spineChainLength * GetHeldSpineHangRatio(),
            neckGrabBone.position.y - skinnedRenderer.bounds.min.y);

        stableHeldPlanarForward = Vector3.ProjectOnPlane(
            playerCamera.transform.forward,
            Vector3.up);
        if (stableHeldPlanarForward.sqrMagnitude < 0.01f)
            stableHeldPlanarForward = -transform.forward;
        stableHeldPlanarForward.Normalize();

        // 첫 프레임은 계산된 카메라 앞 지점이 아니라 실제 상호작용 지점에서 시작합니다.
        // 바닥 안전 높이만 보정한 뒤 다음 프레임부터 저장한 거리를 유지하며 카메라를 따라갑니다.
        initialTargetWorldY = interactionGripPoint.y;
        Vector3 initialCameraTarget = interactionGripPoint;
        initialCameraTarget = ConstrainGrabTargetHeight(initialCameraTarget);
        currentGrabTarget = initialCameraTarget;
        grabTargetInitialized = true;
        previousPlayerCameraPosition = playerCamera.transform.position;
        playerCameraPositionInitialized = true;
        loweringTransition = false;
        lowerTransitionProgress = 0f;
        lowerGroundFound = false;
        liftingTransition = useNaturalGrabTransitions;
        liftTransitionProgress = liftingTransition ? 0f : 1f;
        liftStartGrabTarget = initialCameraTarget;
        transitionStartTime = Time.time;

        if (shepherdAI != null)
        {
            aiWasEnabled = shepherdAI.enabled;
            shepherdAI.enabled = false;
        }

        if (animator != null)
        {
            animatorWasEnabled = animator.enabled;
            animatorWasApplyRootMotion = animator.applyRootMotion;
            animatorWasSpeed = animator.speed;
            animator.applyRootMotion = false;
            animator.enabled = false;
        }

        skinnedRendererWasEnabled = skinnedRenderer.enabled;
        skinnedRenderer.enabled = true;

        if (meshCollider != null)
            meshCollider.enabled = false;
    }

    private void ReleaseShepherd()
    {
        if (!holding)
            return;

        if (useNaturalGrabTransitions && !loweringTransition)
        {
            BeginNaturalLowering();
            return;
        }

        CompleteReleaseShepherd();
    }

    private void BeginNaturalLowering()
    {
        liftingTransition = false;
        liftTransitionProgress = 1f;
        loweringTransition = true;
        lowerTransitionProgress = 0f;
        transitionStartTime = Time.time;
        lowerStartRootPosition = transform.position;
        lowerTargetRootPosition = transform.position;
        rootVelocity = Vector3.zero;
        grabTargetVelocity = Vector3.zero;

        lowerGroundFound = TryFindNaturalReleaseGround(out lowerGroundHeight);
        if (lowerGroundFound)
            lowerTargetRootPosition.y = GetGroundedReleaseRootY(lowerGroundHeight);
    }

    private void CompleteReleaseShepherd()
    {
        if (!holding)
            return;

        CaptureReleasePose();

        if (loweringTransition && lowerGroundFound)
        {
            Vector3 groundedPosition = transform.position;
            groundedPosition.y = GetGroundedReleaseRootY(lowerGroundHeight);
            transform.position = groundedPosition;
        }

        holding = false;
        liftingTransition = false;
        loweringTransition = false;
        liftTransitionProgress = 1f;
        lowerTransitionProgress = 0f;
        lowerGroundFound = false;
        rootVelocity = Vector3.zero;
        grabTargetVelocity = Vector3.zero;
        heldPoseBlend = 0f;
        heldBodyDropBelowGrip = 0f;
        activeHoldDistance = 0f;
        grabTargetInitialized = false;
        playerCameraPositionInitialized = false;

        if (skinnedRenderer != null)
            skinnedRenderer.enabled = skinnedRendererWasEnabled;

        if (animator != null)
        {
            animator.applyRootMotion = animatorWasApplyRootMotion;
            animator.speed = animatorWasSpeed;
            animator.enabled = animatorWasEnabled;
        }

        if (shepherdAI != null)
        {
            shepherdAI.enabled = aiWasEnabled;
            if (aiWasEnabled)
                shepherdAI.ResumeAfterGrab(naturalReleaseAnimationBlendDuration);
        }

        if (animator != null && animator.enabled && releaseStartPoses.Count > 0)
        {
            animator.Update(0f);
            releasePoseBlendStartTime = Time.time;
            releasePoseBlending = true;
            UpdateReleasePoseBlend();
        }
        else
        {
            RestoreBonePose();
            releasePoseBlending = false;
            releaseStartPoses.Clear();
        }

        RefreshGrabCollisionMesh();
    }

    private void UpdateNaturalLowering()
    {
        float duration = Mathf.Max(0.01f, naturalLowerDuration);
        lowerTransitionProgress = Mathf.Clamp01(
            (Time.time - transitionStartTime) / duration);
        float easedProgress = Mathf.SmoothStep(0f, 1f, lowerTransitionProgress);
        Vector3 desiredRootPosition = Vector3.Lerp(
            lowerStartRootPosition,
            lowerTargetRootPosition,
            easedProgress);

        Vector3 horizontalTarget = new Vector3(
            desiredRootPosition.x,
            transform.position.y,
            desiredRootPosition.z);
        Vector3 resolvedRootPosition = MoveHeldRootWithCollision(horizontalTarget);
        resolvedRootPosition.y = desiredRootPosition.y;
        transform.position = resolvedRootPosition;
        rootVelocity = Vector3.zero;
    }

    private float GetGroundedReleaseRootY(float groundHeight)
    {
        if (shepherdAI != null)
            return shepherdAI.GetGroundedRootY(groundHeight);

        if (skinnedRenderer != null)
        {
            return transform.position.y +
                   groundHeight + naturalReleaseGroundClearance -
                   skinnedRenderer.bounds.min.y;
        }

        return transform.position.y;
    }

    private bool TryFindNaturalReleaseGround(out float groundHeight)
    {
        groundHeight = 0f;
        if (skinnedRenderer == null)
            return false;

        Bounds bounds = skinnedRenderer.bounds;
        Vector3 rayOrigin = new Vector3(
            bounds.center.x,
            bounds.max.y + 0.5f,
            bounds.center.z);
        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            heldCollisionHits,
            12f,
            heldCollisionMask,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool foundGround = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = heldCollisionHits[i];
            if (hit.collider == null || IsHeldSelfCollider(hit.collider))
                continue;
            if (hit.distance <= 0.0001f && hit.point == Vector3.zero)
                continue;
            if (Vector3.Dot(hit.normal, Vector3.up) < 0.35f)
                continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                groundHeight = hit.point.y;
                foundGround = true;
            }
        }

        return foundGround;
    }

    private Vector3 GetNeckGripWorldPoint()
    {
        Vector3 awayFromPlayer = neckGrabBone.position - playerCamera.transform.position;
        awayFromPlayer.y = 0f;
        if (awayFromPlayer.sqrMagnitude < 0.0001f)
            awayFromPlayer = -transform.forward;

        return neckGrabBone.position + awayFromPlayer.normalized * neckGripSurfaceOffset;
    }

    #endregion

    #region 뼈 캐시

    private void CacheBones()
    {
        restPoses.Clear();

        Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
        Transform shibaSkeletonRoot = FindBone("Root_bone");
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform candidate = allTransforms[i];
            bool isShepherdBone = candidate.name.StartsWith(
                "DEF-",
                StringComparison.OrdinalIgnoreCase);
            bool isShibaBone = shibaSkeletonRoot != null &&
                               (candidate == shibaSkeletonRoot ||
                                candidate.IsChildOf(shibaSkeletonRoot));
            if (isShepherdBone || isShibaBone)
            {
                restPoses.Add(new BoneRestPose
                {
                    bone = candidate,
                    localPosition = candidate.localPosition,
                    localRotation = candidate.localRotation,
                    localScale = candidate.localScale
                });
            }
        }

        torsoBase = FindBoneAny("DEF-spine.004", "Spine_base");
        torsoLower = FindBoneAny("DEF-spine.005", "Spine_02");
        torsoMid = FindBoneAny("DEF-spine.006", "Spine_03");
        torsoUpper = FindBoneAny("DEF-spine.007", "Spine_04");
        torsoNeck = FindBoneAny("DEF-spine.008", "Spine_05");
        neckBase = FindBoneAny("DEF-spine.009", "neck");
        neckGrabBone = FindBone(neckGrabBoneName) ??
                       FindBoneAny("DEF-spine.010", "neck", "Spine_05");

        frontShoulderLeft = FindBoneAny("DEF-shoulder.L", "hip_f.L");
        frontShoulderRight = FindBoneAny("DEF-shoulder.R", "hip_f.R");
        frontThighLeft = FindBoneAny("DEF-front_thigh.L", "thigh_f.L");
        frontThighRight = FindBoneAny("DEF-front_thigh.R", "thigh_f.R");
        frontShinLeft = FindBoneAny("DEF-front_shin.L", "leg_f.L", "shin_f.L");
        frontShinRight = FindBoneAny("DEF-front_shin.R", "leg_f.R", "shin_f.R");
        frontFootLeft = FindBoneAny("DEF-front_foot.L", "foot_f.L");
        frontFootRight = FindBoneAny("DEF-front_foot.R", "foot_f.R");
        hindThighLeft = FindBoneAny("DEF-thigh.L", "thigh_b.L");
        hindThighRight = FindBoneAny("DEF-thigh.R", "thigh_b.R");
        hindShinLeft = FindBoneAny("DEF-shin.L", "leg_b.L", "shin_b.L");
        hindShinRight = FindBoneAny("DEF-shin.R", "leg_b.R", "shin_b.R");
        hindFootLeft = FindBoneAny("DEF-foot.L", "foot_b.L");
        hindFootRight = FindBoneAny("DEF-foot.R", "foot_b.R");

        tailBase = FindBoneAny("DEF-spine.003", "Tail_01");
        tailMiddle = FindBoneAny("DEF-spine.002", "Tail_02");
        tailTip = FindBoneAny("DEF-spine.001", "Tail_04", "Tail_03");
        tailEnd = FindBoneAny("DEF-spine", "Tail_05");
        headBone = FindBoneAny("DEF-spine.011", "head");
        jawBone = FindBoneAny("DEF-jaw", "mouth");
        tongueBone = FindBoneAny("DEF-tongue", "tongue_1");
        tongueMiddleBone = FindBoneAny("DEF-tongue.001", "tongue_2");
        tongueTipBone = FindBoneAny("DEF-tongue.002", "tongue_3", "tongue_4");
        CacheHeadOrientationAxes();
        spineChainLength = CalculateSpineChainLength();
    }

    private void CacheHeadOrientationAxes()
    {
        if (headBone == null)
            return;

        // 얼굴 정면축은 리그마다 다릅니다. 셰퍼드는 거의 로컬 +Z지만 시바는 로컬 +Y이므로,
        // 루트의 전방/상방을 머리 로컬 공간에 저장해 어느 리그에서도 같은 얼굴 프레임을 복원합니다.
        headForwardAxisLocal = headBone.InverseTransformDirection(transform.forward).normalized;
        Vector3 localUp = headBone.InverseTransformDirection(transform.up);
        headUpAxisLocal = Vector3.ProjectOnPlane(localUp, headForwardAxisLocal).normalized;

        if (headForwardAxisLocal.sqrMagnitude < 0.0001f)
            headForwardAxisLocal = Vector3.forward;
        if (headUpAxisLocal.sqrMagnitude < 0.0001f)
            headUpAxisLocal = Vector3.up;
    }

    private Transform FindBone(string boneName)
    {
        if (string.IsNullOrEmpty(boneName))
            return null;

        Transform[] bones = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].name.Equals(boneName, StringComparison.OrdinalIgnoreCase))
                return bones[i];
        }

        return null;
    }

    private Transform FindBoneAny(params string[] boneNames)
    {
        for (int i = 0; i < boneNames.Length; i++)
        {
            Transform bone = FindBone(boneNames[i]);
            if (bone != null)
                return bone;
        }

        return null;
    }

    private void ResetBonePose()
    {
        for (int i = 0; i < restPoses.Count; i++)
        {
            BoneRestPose rest = restPoses[i];
            if (rest.bone == null)
                continue;

            rest.bone.localPosition = rest.localPosition;
            rest.bone.localRotation = rest.localRotation;
            rest.bone.localScale = rest.localScale;
        }
    }

    private void RestoreBonePose()
    {
        ResetBonePose();
    }

    private void CaptureReleasePose()
    {
        releaseStartPoses.Clear();
        for (int i = 0; i < restPoses.Count; i++)
        {
            Transform bone = restPoses[i].bone;
            if (bone == null)
                continue;

            releaseStartPoses.Add(new BoneRestPose
            {
                bone = bone,
                localPosition = bone.localPosition,
                localRotation = bone.localRotation,
                localScale = bone.localScale
            });
        }
    }

    private void UpdateReleasePoseBlend()
    {
        if (!releasePoseBlending || animator == null || !animator.enabled)
            return;

        float duration = Mathf.Max(0.01f, naturalReleaseAnimationBlendDuration);
        float progress = Mathf.Clamp01(
            (Time.time - releasePoseBlendStartTime) / duration);
        float blend = Mathf.SmoothStep(0f, 1f, progress);

        for (int i = 0; i < releaseStartPoses.Count; i++)
        {
            BoneRestPose start = releaseStartPoses[i];
            if (start.bone == null)
                continue;

            Vector3 animationPosition = start.bone.localPosition;
            Quaternion animationRotation = start.bone.localRotation;
            Vector3 animationScale = start.bone.localScale;

            start.bone.localPosition = Vector3.Lerp(
                start.localPosition,
                animationPosition,
                blend);
            start.bone.localRotation = Quaternion.Slerp(
                start.localRotation,
                animationRotation,
                blend);
            start.bone.localScale = Vector3.Lerp(
                start.localScale,
                animationScale,
                blend);
        }

        if (progress < 1f)
            return;

        releasePoseBlending = false;
        releaseStartPoses.Clear();
    }

    #endregion

    #region 들고 있을 때 이동

    private void UpdateHeldRoot()
    {
        ApplyPlayerTranslationToGrabTarget();

        Vector3 cameraTarget = CalculateDesiredGrabTarget();
        cameraTarget.y = Mathf.Lerp(initialTargetWorldY, cameraTarget.y, holdVerticalFollow);
        cameraTarget = ConstrainGrabTargetHeight(cameraTarget);

        if (!grabTargetInitialized)
        {
            currentGrabTarget = cameraTarget;
            grabTargetInitialized = true;
        }
        else if (liftingTransition)
        {
            float duration = Mathf.Max(0.01f, naturalLiftDuration);
            liftTransitionProgress = Mathf.Clamp01(
                (Time.time - transitionStartTime) / duration);
            float easedProgress = Mathf.SmoothStep(
                0f,
                1f,
                liftTransitionProgress);
            float arc = Mathf.Sin(liftTransitionProgress * Mathf.PI) *
                        naturalLiftArcHeight;

            currentGrabTarget = Vector3.Lerp(
                liftStartGrabTarget,
                cameraTarget,
                easedProgress) + Vector3.up * arc;
            grabTargetVelocity = Vector3.zero;

            if (liftTransitionProgress >= 1f)
            {
                liftingTransition = false;
                currentGrabTarget = cameraTarget;
            }
        }
        else
        {
            currentGrabTarget = Vector3.SmoothDamp(
                currentGrabTarget,
                cameraTarget,
                ref grabTargetVelocity,
                holdTargetSmoothTime,
                holdTargetMaximumSpeed,
                Time.deltaTime);
        }

        // 몸이 바라볼 방향은 움직이는 잡기 목표점이 아니라 카메라의 안정된 수평 방향만 사용합니다.
        // 카메라가 거의 바닥을 향해 수평 성분이 사라져도 마지막 방향을 유지하므로 180도 반전하지 않습니다.
        Vector3 faceDirection = -stableHeldPlanarForward;
        if (faceDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(faceDirection.normalized, Vector3.up);
            Quaternion candidateRotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                heldBodyMaximumTurnSpeed * Time.deltaTime);
            ApplyHeldRotationWithCollision(candidateRotation);
        }

    }

    private Vector3 CalculateDesiredGrabTarget()
    {
        Vector3 cameraForward = playerCamera.transform.forward;
        Vector3 planarForward = Vector3.ProjectOnPlane(cameraForward, Vector3.up);

        // 카메라가 수직에 가까워질 때 아주 작은 수평 벡터의 부호가 바뀌면 몸 전체가
        // 반대쪽을 향해 돌 수 있습니다. 충분한 수평 방향이 있을 때만 기준 방향을 갱신합니다.
        if (planarForward.sqrMagnitude >= 0.04f)
        {
            planarForward.Normalize();
            float directionBlend = 1f - Mathf.Exp(-holdRotationSpeed * Time.deltaTime);
            stableHeldPlanarForward = Vector3.Slerp(
                stableHeldPlanarForward,
                planarForward,
                directionBlend).normalized;
        }
        else if (stableHeldPlanarForward.sqrMagnitude < 0.0001f)
        {
            stableHeldPlanarForward = -transform.forward;
        }

        float rawPitch = Mathf.Asin(Mathf.Clamp(cameraForward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float limitedPitch = Mathf.Clamp(rawPitch, -heldCameraPitchLimit, heldCameraPitchLimit);
        float pitchRadians = limitedPitch * Mathf.Deg2Rad;
        Vector3 limitedForward =
            stableHeldPlanarForward * Mathf.Cos(pitchRadians) +
            Vector3.up * Mathf.Sin(pitchRadians);
        float effectiveHoldDistance = activeHoldDistance > 0.01f
            ? activeHoldDistance
            : Mathf.Min(holdDistance, 3f);

        // 높이 오프셋은 카메라의 기울어진 Up 대신 월드의 위쪽을 사용합니다.
        // 그래서 마우스를 내릴 때 오프셋까지 회전하며 강아지를 뒤로 잡아당기지 않습니다.
        return playerCamera.transform.position +
               limitedForward.normalized * effectiveHoldDistance +
               Vector3.up * holdVerticalOffset;
    }

    private void ApplyPlayerTranslationToGrabTarget()
    {
        if (playerCamera == null)
        {
            playerCameraPositionInitialized = false;
            return;
        }

        Vector3 cameraPosition = playerCamera.transform.position;
        if (playerCameraPositionInitialized && grabTargetInitialized)
        {
            Vector3 cameraDelta = cameraPosition - previousPlayerCameraPosition;
            currentGrabTarget += cameraDelta;
            if (liftingTransition)
                liftStartGrabTarget += cameraDelta;
        }

        previousPlayerCameraPosition = cameraPosition;
        playerCameraPositionInitialized = true;
    }

    private Vector3 ConstrainGrabTargetHeight(Vector3 target)
    {
        target.y = Mathf.Clamp(
            target.y,
            initialTargetWorldY - heldTargetMaximumVerticalTravel,
            initialTargetWorldY + heldTargetMaximumVerticalTravel);

        // 목표 지점의 XZ 위치에서 바닥을 찾습니다. 목 잡기 지점 아래에는 척추와 골반이
        // 매달리므로, 그 길이와 바닥 여유 높이를 합친 만큼 목을 바닥 위에 유지합니다.
        // 천장이 있는 실내에서 천장을 바닥으로 오인하지 않도록 플레이어 눈높이 바로 위에서
        // 아래로만 검사합니다. 목표가 이미 바닥 아래에 있어도 이 시작점은 항상 바닥 위에 있습니다.
        float rayStartY = playerCamera.transform.position.y + 0.25f;
        Vector3 rayOrigin = new Vector3(target.x, rayStartY, target.z);
        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit groundHit,
                12f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            float hangingSpineLength = Mathf.Max(0.1f, spineChainLength) *
                                       GetHeldSpineHangRatio();
            // 척추뿐 아니라 현재 포즈의 발끝까지 포함합니다. 한 번 측정된 가장 긴 길이를
            // 들고 있는 동안 유지해 다리가 흔들려도 다시 바닥 아래로 빠지지 않게 합니다.
            float hangingBodyLength = Mathf.Max(hangingSpineLength, heldBodyDropBelowGrip);
            float minimumNeckHeight = groundHit.point.y + hangingBodyLength + heldGroundClearance;
            target.y = Mathf.Max(target.y, minimumNeckHeight);
        }

        return target;
    }

    #endregion

    #region 중력 기반 뼈 포즈

    private void ApplyHeldRotationWithCollision(Quaternion candidateRotation)
    {
        if (skinnedRenderer == null || heldCollisionMask.value == 0)
        {
            transform.rotation = candidateRotation;
            return;
        }

        Quaternion previousRotation = transform.rotation;
        if (Quaternion.Angle(previousRotation, candidateRotation) < 0.01f)
            return;

        Bounds previousBounds = skinnedRenderer.bounds;
        bool wasOverlapping = HasHeldBlockingOverlap(previousBounds);

        transform.rotation = candidateRotation;
        Bounds candidateBounds = skinnedRenderer.bounds;

        // Do not let turning the hanging body create a new wall/floor overlap.
        // Existing tiny overlaps are allowed to rotate so movement can pull the
        // dog back out instead of permanently locking its orientation.
        if (!wasOverlapping && HasHeldBlockingOverlap(candidateBounds))
            transform.rotation = previousRotation;
    }

    private Vector3 MoveHeldRootWithCollision(Vector3 desiredRootPosition)
    {
        if (skinnedRenderer == null || heldCollisionMask.value == 0)
            return desiredRootPosition;

        Vector3 initialRootPosition = transform.position;
        Vector3 resolvedRootPosition = initialRootPosition;
        Bounds renderedBounds = skinnedRenderer.bounds;
        Vector3 boundsCenterOffset = renderedBounds.center - initialRootPosition;
        Vector3 halfExtents = GetHeldCollisionHalfExtents(renderedBounds);
        Vector3 remainingMovement = desiredRootPosition - initialRootPosition;
        int iterations = Mathf.Clamp(heldCollisionSlideIterations, 1, 4);

        for (int i = 0; i < iterations; i++)
        {
            float distance = remainingMovement.magnitude;
            if (distance < 0.0001f)
                break;

            Vector3 direction = remainingMovement / distance;
            Vector3 castCenter = resolvedRootPosition + boundsCenterOffset;
            if (!TryGetNearestHeldCollision(
                    castCenter,
                    halfExtents,
                    direction,
                    distance + heldCollisionSkin,
                    out RaycastHit hit))
            {
                resolvedRootPosition += remainingMovement;
                break;
            }

            float allowedDistance = Mathf.Clamp(
                hit.distance - heldCollisionSkin,
                0f,
                distance);
            Vector3 allowedMovement = direction * allowedDistance;
            resolvedRootPosition += allowedMovement;

            Vector3 blockedRemainder = remainingMovement - allowedMovement;
            remainingMovement = Vector3.ProjectOnPlane(blockedRemainder, hit.normal);
            rootVelocity = Vector3.ProjectOnPlane(rootVelocity, hit.normal);
        }

        return ResolveHeldOverlaps(
            resolvedRootPosition,
            boundsCenterOffset,
            halfExtents);
    }

    private bool TryGetNearestHeldCollision(
        Vector3 center,
        Vector3 halfExtents,
        Vector3 direction,
        float distance,
        out RaycastHit nearestHit)
    {
        int hitCount = Physics.BoxCastNonAlloc(
            center,
            halfExtents,
            direction,
            heldCollisionHits,
            Quaternion.identity,
            distance,
            heldCollisionMask,
            QueryTriggerInteraction.Ignore);

        nearestHit = default;
        float nearestDistance = float.PositiveInfinity;
        bool foundHit = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = heldCollisionHits[i];
            if (hit.collider == null || IsHeldSelfCollider(hit.collider))
                continue;

            // Unity reports casts that begin inside a collider with a zero
            // point and distance. Compute the real separation direction so a
            // floor overlap does not freeze horizontal travel while movement
            // farther into an already-overlapping wall still remains blocked.
            if (hit.distance <= 0.0001f && hit.point == Vector3.zero)
            {
                if (TryGetHeldOverlapSeparation(
                        hit.collider,
                        center,
                        halfExtents,
                        out Vector3 separationDirection,
                        out _))
                {
                    if (Vector3.Dot(direction, separationDirection) >= -0.0001f)
                        continue;

                    hit.normal = separationDirection;
                }
                else
                {
                    // If Unity cannot calculate separation for an unusual
                    // collider type, fail closed instead of allowing tunneling.
                    hit.normal = -direction;
                }
            }

            // A surface only blocks movement that points into it. This keeps
            // horizontal movement free while the bounds are resting on a floor.
            if (Vector3.Dot(direction, hit.normal) >= -0.0001f)
                continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                nearestHit = hit;
                foundHit = true;
            }
        }

        return foundHit;
    }

    private bool HasHeldBlockingOverlap(Bounds renderedBounds)
    {
        Vector3 halfExtents = GetHeldCollisionHalfExtents(renderedBounds);
        int overlapCount = Physics.OverlapBoxNonAlloc(
            renderedBounds.center,
            halfExtents,
            heldOverlapResults,
            Quaternion.identity,
            heldCollisionMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlapCount; i++)
        {
            Collider overlap = heldOverlapResults[i];
            if (overlap != null &&
                !IsHeldSelfCollider(overlap) &&
                TryGetHeldOverlapSeparation(
                    overlap,
                    renderedBounds.center,
                    halfExtents,
                    out _,
                    out float separationDistance) &&
                separationDistance > 0.0001f)
                return true;
        }

        return false;
    }

    private Vector3 ResolveHeldOverlaps(
        Vector3 rootPosition,
        Vector3 boundsCenterOffset,
        Vector3 halfExtents)
    {
        const int maximumDepenetrationPasses = 4;

        for (int pass = 0; pass < maximumDepenetrationPasses; pass++)
        {
            Vector3 center = rootPosition + boundsCenterOffset;
            int overlapCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                heldOverlapResults,
                Quaternion.identity,
                heldCollisionMask,
                QueryTriggerInteraction.Ignore);
            bool correctedOverlap = false;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider overlap = heldOverlapResults[i];
                if (overlap == null || IsHeldSelfCollider(overlap))
                    continue;

                if (!TryGetHeldOverlapSeparation(
                        overlap,
                        center,
                        halfExtents,
                        out Vector3 separationDirection,
                        out float separationDistance) ||
                    separationDistance <= 0.0001f)
                    continue;

                Vector3 correction = separationDirection *
                                     (separationDistance + heldCollisionSkin);
                rootPosition += correction;
                center += correction;

                float inwardVelocity = Vector3.Dot(rootVelocity, separationDirection);
                if (inwardVelocity < 0f)
                    rootVelocity -= separationDirection * inwardVelocity;

                correctedOverlap = true;
            }

            if (!correctedOverlap)
                break;
        }

        return rootPosition;
    }

    private bool TryGetHeldOverlapSeparation(
        Collider otherCollider,
        Vector3 center,
        Vector3 halfExtents,
        out Vector3 separationDirection,
        out float separationDistance)
    {
        EnsureHeldCollisionProbe();
        heldCollisionProbe.size = halfExtents * 2f;
        heldCollisionProbe.center = Vector3.zero;
        heldCollisionProbe.enabled = true;

        bool overlaps = Physics.ComputePenetration(
            heldCollisionProbe,
            center,
            Quaternion.identity,
            otherCollider,
            otherCollider.transform.position,
            otherCollider.transform.rotation,
            out separationDirection,
            out separationDistance);

        heldCollisionProbe.enabled = false;
        return overlaps;
    }

    private void EnsureHeldCollisionProbe()
    {
        if (heldCollisionProbe != null)
            return;

        heldCollisionProbeObject = new GameObject("Held Dog Collision Probe")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycastLayer >= 0)
            heldCollisionProbeObject.layer = ignoreRaycastLayer;

        heldCollisionProbeObject.transform.localScale = Vector3.one;
        heldCollisionProbe = heldCollisionProbeObject.AddComponent<BoxCollider>();
        heldCollisionProbe.isTrigger = true;
        heldCollisionProbe.enabled = false;
    }

    private Vector3 GetHeldCollisionHalfExtents(Bounds renderedBounds)
    {
        float scale = Mathf.Clamp(heldCollisionBoundsScale, 0.8f, 1f);
        Vector3 extents = renderedBounds.extents * scale;
        return new Vector3(
            Mathf.Max(0.03f, extents.x),
            Mathf.Max(0.03f, extents.y),
            Mathf.Max(0.03f, extents.z));
    }

    private bool IsHeldSelfCollider(Collider collider)
    {
        if (collider == heldCollisionProbe)
            return true;

        Transform colliderTransform = collider.transform;
        if (colliderTransform == transform || colliderTransform.IsChildOf(transform))
            return true;

        // The held dog is intentionally close to the XR rig. Player body and
        // hand colliders must travel with it instead of being treated as walls.
        return playerCollisionRoot != null &&
               (colliderTransform == playerCollisionRoot ||
                colliderTransform.IsChildOf(playerCollisionRoot));
    }

    private void ApplyHeldGravityPose()
    {
        ResetBonePose();
        float poseBlendTarget = 1f;
        if (useNaturalGrabTransitions)
        {
            if (loweringTransition)
                poseBlendTarget = 1f - Mathf.SmoothStep(0f, 1f, lowerTransitionProgress);
            else if (liftingTransition)
                poseBlendTarget = Mathf.SmoothStep(0f, 1f, liftTransitionProgress);
        }

        heldPoseBlend = Mathf.MoveTowards(
            heldPoseBlend,
            poseBlendTarget,
            deformationFollowSpeed * Time.deltaTime);

        Vector3 gravityDirection = Physics.gravity.sqrMagnitude > 0.001f
            ? Physics.gravity.normalized
            : Vector3.down;
        // 기존 씬에 0.55가 남아 있어도 새 포즈에서는 최대 0.5까지만 사용해 몸이 덜 늘어납니다.
        float effectiveGravityStrength = Mathf.Min(gravityStrength, 0.5f);
        float bodyInfluence = effectiveGravityStrength * bodyGravityInfluence * heldPoseBlend;
        float pelvisInfluence = Mathf.Clamp01(
            effectiveGravityStrength * pelvisGravityInfluence * 1.8f) * heldPoseBlend;
        float limbInfluence = effectiveGravityStrength * limbGravityInfluence * heldPoseBlend;
        float tailInfluence = effectiveGravityStrength * tailGravityInfluence * heldPoseBlend;
        float distanceScale = Mathf.Clamp01(gravityInfluenceRadius / 1.25f);

        bodyInfluence *= distanceScale;
        pelvisInfluence *= distanceScale;
        limbInfluence *= distanceScale;
        tailInfluence *= distanceScale;

        // 카메라 앞의 목표점은 손이 닿는 목 뒤 피부 표면을 뜻합니다.
        // 뼈 중심은 표면보다 플레이어 쪽에 조금 더 가깝게 두어 목이 안쪽으로 파여 보이지 않게 합니다.
        Vector3 awayFromPlayer = currentGrabTarget - playerCamera.transform.position;
        if (awayFromPlayer.sqrMagnitude < 0.0001f)
            awayFromPlayer = playerCamera.transform.forward;
        awayFromPlayer.Normalize();
        Vector3 neckBoneTarget = currentGrabTarget - awayFromPlayer * neckGripSurfaceOffset;

        // 네 발로 서는 기본 포즈의 척추는 수평에 가깝습니다. 그 상태에서 목 끝만 IK로 잡아당기면
        // 남은 길이가 가슴 뒤쪽으로 밀려 C자로 접힐 수 있습니다. 먼저 각 척추 마디를 아래에서
        // 위로 같은 방향에 가깝게 세워 준 다음, 잡힌 목 위치를 한 번만 목표점에 맞춥니다.
        float effectiveSpineLength = Mathf.Max(0.1f, spineChainLength);
        float hangRatio = GetHeldSpineHangRatio();
        Vector3 pelvisToNeckDirection = (
            -gravityDirection * effectiveSpineLength * hangRatio +
            transform.forward * heldPelvisBackwardOffset).normalized;
        float spineShapeInfluence = Mathf.Clamp01(
            heldSpineStraighteningStrength * heldPoseBlend *
            Mathf.Lerp(0.82f, 1f, Mathf.Max(bodyInfluence, pelvisInfluence)));
        ShapeHeldSpine(pelvisToNeckDirection, spineShapeInfluence);

        // 뼈 회전을 마친 뒤 목의 실제 위치 오차를 루트 이동 한 번으로만 보정합니다.
        // 매 프레임 기본 뼈 포즈에서 다시 계산하므로 Y값이 계속 누적되어 올라가지 않습니다.
        if (!loweringTransition)
        {
            Vector3 desiredRootPosition = transform.position + neckBoneTarget - neckGrabBone.position;
            Vector3 smoothedRootPosition = Vector3.SmoothDamp(
                transform.position,
                desiredRootPosition,
                ref rootVelocity,
                1f / holdFollowSpeed);
            transform.position = MoveHeldRootWithCollision(smoothedRootPosition);
        }

        Vector3 dogForward = transform.forward;
        Vector3 dogBack = -dogForward;
        Vector3 left = -transform.right;
        Vector3 right = transform.right;

        // 앞발은 몸 앞쪽으로 살짝 나오고, 뒷발은 몸 뒤쪽으로 벌어진 뒤 아래로 떨어집니다.
        Vector3 frontLeftDirection = (gravityDirection * 0.82f + dogForward * 0.16f + left * 0.08f).normalized;
        Vector3 frontRightDirection = (gravityDirection * 0.82f + dogForward * 0.16f + right * 0.08f).normalized;
        Vector3 hindLeftDirection = (gravityDirection * 0.94f + dogBack * 0.02f + left * 0.04f).normalized;
        Vector3 hindRightDirection = (gravityDirection * 0.94f + dogBack * 0.02f + right * 0.04f).normalized;
        float hindInfluence = Mathf.Clamp01(limbInfluence * 1.7f);

        // 연결 체인을 부모에서 자식 순서로 모두 회전시켜 관절 사이가 꺾이거나 빠져 보이지 않게 합니다.
        AimBoneTowardDirection(frontShoulderLeft, frontLeftDirection, limbInfluence * 0.28f, limbMaximumSagAngle * 0.35f);
        AimBoneTowardDirection(frontShoulderRight, frontRightDirection, limbInfluence * 0.28f, limbMaximumSagAngle * 0.35f);
        AimBoneTowardDirection(frontThighLeft, frontLeftDirection, limbInfluence * 0.92f, limbMaximumSagAngle);
        AimBoneTowardDirection(frontThighRight, frontRightDirection, limbInfluence * 0.92f, limbMaximumSagAngle);
        AimBoneTowardDirection(frontShinLeft, gravityDirection, limbInfluence * 0.82f, limbMaximumSagAngle * 0.9f);
        AimBoneTowardDirection(frontShinRight, gravityDirection, limbInfluence * 0.82f, limbMaximumSagAngle * 0.9f);
        AimBoneTowardDirection(frontFootLeft, (gravityDirection + dogForward * 0.18f).normalized, limbInfluence * 0.62f, limbMaximumSagAngle * 0.55f);
        AimBoneTowardDirection(frontFootRight, (gravityDirection + dogForward * 0.18f).normalized, limbInfluence * 0.62f, limbMaximumSagAngle * 0.55f);

        AimBoneTowardDirection(hindThighLeft, hindLeftDirection, hindInfluence, hindLegMaximumSagAngle);
        AimBoneTowardDirection(hindThighRight, hindRightDirection, hindInfluence, hindLegMaximumSagAngle);
        AimBoneTowardDirection(hindShinLeft, gravityDirection, hindInfluence * 0.95f, hindLegMaximumSagAngle * 0.9f);
        AimBoneTowardDirection(hindShinRight, gravityDirection, hindInfluence * 0.95f, hindLegMaximumSagAngle * 0.9f);
        AimBoneTowardDirection(hindFootLeft, (gravityDirection + dogForward * 0.08f).normalized, hindInfluence * 0.82f, hindLegMaximumSagAngle * 0.62f);
        AimBoneTowardDirection(hindFootRight, (gravityDirection + dogForward * 0.08f).normalized, hindInfluence * 0.82f, hindLegMaximumSagAngle * 0.62f);

        tailInfluence = Mathf.Clamp01(tailInfluence * 1.7f);
        Vector3 tailDirection = (gravityDirection * 0.96f + dogBack * 0.04f).normalized;
        // 엉덩이 방향을 이어받는 꼬리 뿌리는 조금만 꺾고, 끝으로 갈수록
        // 중력 영향을 크게 적용해 한 관절에서 접히지 않는 완만한 곡선을 만듭니다.
        AimBoneTowardDirection(tailBase, tailDirection, tailInfluence * 0.3f, tailMaximumSagAngle * 0.25f);
        AimBoneTowardDirection(tailMiddle, tailDirection, tailInfluence * 0.68f, tailMaximumSagAngle * 0.55f);
        AimBoneTowardDirection(tailTip, gravityDirection, tailInfluence, tailMaximumSagAngle);

        // 척추 자세를 만든 뒤 얼굴의 방향과 기울기를 별도로 잡아 줍니다.
        // 방향 벡터만 맞추던 이전 방식과 달리 옆으로 쓰러지는 회전까지 보정합니다.
        StabilizeHeldHead();
        ApplyHeldPantingAnimation();

        if (skinnedRenderer != null && neckGrabBone != null)
        {
            float currentBodyDrop = neckGrabBone.position.y - skinnedRenderer.bounds.min.y;
            heldBodyDropBelowGrip = Mathf.Max(heldBodyDropBelowGrip, currentBodyDrop);
        }
    }

    private void ApplyHeldPantingAnimation()
    {
        if (!playPantingWhileHeld || heldPantingStrength <= 0f)
            return;

        EnsureHeldPantingSampler();
        if (heldPantingClip == null || heldPantingSamplerRoot == null)
            return;

        float clipLength = Mathf.Max(0.01f, heldPantingClip.length);
        heldPantingTime += Time.deltaTime * heldPantingSpeed;
        if (heldPantingTime >= clipLength)
        {
            CaptureHeldIdleTransitionPose();
            SelectNextHeldIdleClip();
            heldPantingTime = 0f;
            heldIdleTransitionTime = 0f;
            heldIdleTransitionActive = heldIdleClips.Count > 1 &&
                heldIdleTransitionDuration > 0f;
        }

        heldPantingClip.SampleAnimation(heldPantingSamplerRoot, heldPantingTime);

        float transitionBlend = 1f;
        if (heldIdleTransitionActive)
        {
            heldIdleTransitionTime += Time.deltaTime;
            transitionBlend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(
                    heldIdleTransitionTime /
                    Mathf.Max(0.01f, heldIdleTransitionDuration)));

            if (transitionBlend >= 1f)
                heldIdleTransitionActive = false;
        }

        float blend = Mathf.Clamp01(heldPantingStrength * heldPoseBlend);
        ApplyHeldHeadIdleMotion(
            Mathf.Clamp01(heldHeadIdleStrength * heldPoseBlend),
            transitionBlend);
        CopyHeldPantingBone(
            jawBone,
            heldPantingSamplerJaw,
            heldIdleTransitionJaw,
            blend,
            transitionBlend);
        CopyHeldPantingBone(
            tongueBone,
            heldPantingSamplerTongue,
            heldIdleTransitionTongue,
            blend,
            transitionBlend);
        CopyHeldPantingBone(
            tongueMiddleBone,
            heldPantingSamplerTongueMiddle,
            heldIdleTransitionTongueMiddle,
            blend,
            transitionBlend);
        CopyHeldPantingBone(
            tongueTipBone,
            heldPantingSamplerTongueTip,
            heldIdleTransitionTongueTip,
            blend,
            transitionBlend);
    }

    private void EnsureHeldPantingSampler()
    {
        ResolveHeldIdleClips();
        if (heldPantingClip == null)
            SelectNextHeldIdleClip();

        if (heldPantingClip == null || animator == null || heldPantingSamplerRoot != null)
            return;

        heldPantingSamplerRoot = new GameObject(
            animator.gameObject.name + " Held Panting Sampler")
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        Transform samplerRoot = heldPantingSamplerRoot.transform;
        samplerRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        samplerRoot.localScale = Vector3.one;

        for (int i = 0; i < animator.transform.childCount; i++)
            CloneHeldPantingHierarchy(animator.transform.GetChild(i), samplerRoot);

        heldPantingSamplerHead = FindHeldPantingSamplerBoneAny(
            "DEF-spine.011", "head");
        heldPantingSamplerJaw = FindHeldPantingSamplerBoneAny(
            "DEF-jaw", "mouth");
        heldPantingSamplerTongue = FindHeldPantingSamplerBoneAny(
            "DEF-tongue", "tongue_1");
        heldPantingSamplerTongueMiddle = FindHeldPantingSamplerBoneAny(
            "DEF-tongue.001", "tongue_2");
        heldPantingSamplerTongueTip = FindHeldPantingSamplerBoneAny(
            "DEF-tongue.002", "tongue_3", "tongue_4");

        if (heldPantingSamplerHead != null)
            heldPantingSamplerHeadRestRotation = heldPantingSamplerHead.localRotation;
    }

    private void BeginHeldIdleSequence()
    {
        ResolveHeldIdleClips();
        heldPantingTime = 0f;
        heldIdleTransitionTime = 0f;
        heldIdleTransitionActive = false;
        SelectNextHeldIdleClip();
    }

    private void ResolveHeldIdleClips()
    {
        if (heldIdleClipsResolved)
            return;

        heldIdleClipsResolved = true;
        heldIdleClips.Clear();
        AnimationClip explicitlyAssignedClip = heldPantingClip;

        if (explicitlyAssignedClip != null)
            heldIdleClips.Add(explicitlyAssignedClip);

        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        if (heldIdleClipNames != null)
        {
            for (int nameIndex = 0; nameIndex < heldIdleClipNames.Length; nameIndex++)
            {
                string candidateName = heldIdleClipNames[nameIndex];
                if (string.IsNullOrWhiteSpace(candidateName))
                    continue;

                for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
                {
                    AnimationClip candidate = clips[clipIndex];
                    if (candidate != null &&
                        candidate.name.Equals(
                            candidateName,
                            StringComparison.OrdinalIgnoreCase) &&
                        !heldIdleClips.Contains(candidate))
                    {
                        heldIdleClips.Add(candidate);
                        break;
                    }
                }
            }
        }

        // Older dog setups only serialize one search name. Preserve that behavior
        // when none of the multi-idle names exist in their controller.
        if (heldIdleClips.Count == 0 && !string.IsNullOrWhiteSpace(heldPantingClipName))
        {
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip candidate = clips[i];
                if (candidate != null &&
                    candidate.name.IndexOf(
                        heldPantingClipName,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    heldIdleClips.Add(candidate);
                    break;
                }
            }
        }

        heldPantingClip = null;
        heldIdleClipIndex = -1;
        previousHeldIdleClipIndex = -1;
    }

    private void SelectNextHeldIdleClip()
    {
        if (heldIdleClips.Count == 0)
        {
            heldPantingClip = null;
            heldIdleClipIndex = -1;
            return;
        }

        int nextIndex = 0;
        if (heldIdleClips.Count > 1)
        {
            do
            {
                nextIndex = UnityEngine.Random.Range(0, heldIdleClips.Count);
            }
            while (nextIndex == previousHeldIdleClipIndex);
        }

        heldIdleClipIndex = nextIndex;
        previousHeldIdleClipIndex = nextIndex;
        heldPantingClip = heldIdleClips[nextIndex];
    }

    private void CaptureHeldIdleTransitionPose()
    {
        heldIdleTransitionHead = CaptureHeldIdleBonePose(heldPantingSamplerHead);
        heldIdleTransitionJaw = CaptureHeldIdleBonePose(heldPantingSamplerJaw);
        heldIdleTransitionTongue = CaptureHeldIdleBonePose(heldPantingSamplerTongue);
        heldIdleTransitionTongueMiddle = CaptureHeldIdleBonePose(
            heldPantingSamplerTongueMiddle);
        heldIdleTransitionTongueTip = CaptureHeldIdleBonePose(
            heldPantingSamplerTongueTip);
    }

    private static HeldIdleBonePose CaptureHeldIdleBonePose(Transform bone)
    {
        if (bone == null)
            return default;

        return new HeldIdleBonePose
        {
            valid = true,
            localPosition = bone.localPosition,
            localRotation = bone.localRotation,
            localScale = bone.localScale
        };
    }

    private static Transform CloneHeldPantingHierarchy(
        Transform source,
        Transform parent)
    {
        GameObject cloneObject = new GameObject(source.name)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        Transform clone = cloneObject.transform;
        clone.SetParent(parent, false);
        clone.localPosition = source.localPosition;
        clone.localRotation = source.localRotation;
        clone.localScale = source.localScale;

        for (int i = 0; i < source.childCount; i++)
            CloneHeldPantingHierarchy(source.GetChild(i), clone);

        return clone;
    }

    private Transform FindHeldPantingSamplerBone(string boneName)
    {
        if (heldPantingSamplerRoot == null)
            return null;

        Transform[] transforms =
            heldPantingSamplerRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name.Equals(
                    boneName,
                    StringComparison.OrdinalIgnoreCase))
                return transforms[i];
        }

        return null;
    }

    private Transform FindHeldPantingSamplerBoneAny(params string[] boneNames)
    {
        for (int i = 0; i < boneNames.Length; i++)
        {
            Transform bone = FindHeldPantingSamplerBone(boneNames[i]);
            if (bone != null)
                return bone;
        }

        return null;
    }

    private void ApplyHeldHeadIdleMotion(float blend, float transitionBlend)
    {
        if (headBone == null || heldPantingSamplerHead == null || blend <= 0f)
            return;

        Quaternion sampledRotation = heldPantingSamplerHead.localRotation;
        if (heldIdleTransitionActive && heldIdleTransitionHead.valid)
        {
            sampledRotation = Quaternion.Slerp(
                heldIdleTransitionHead.localRotation,
                sampledRotation,
                transitionBlend);
        }

        Quaternion sampledDelta =
            sampledRotation *
            Quaternion.Inverse(heldPantingSamplerHeadRestRotation);
        Quaternion blendedDelta = Quaternion.Slerp(
            Quaternion.identity,
            sampledDelta,
            blend);
        headBone.localRotation = blendedDelta * headBone.localRotation;
    }

    private void CopyHeldPantingBone(
        Transform target,
        Transform sampled,
        HeldIdleBonePose transitionFrom,
        float blend,
        float transitionBlend)
    {
        if (target == null || sampled == null || blend <= 0f)
            return;

        Vector3 sampledPosition = sampled.localPosition;
        Quaternion sampledRotation = sampled.localRotation;
        Vector3 sampledScale = sampled.localScale;
        if (heldIdleTransitionActive && transitionFrom.valid)
        {
            sampledPosition = Vector3.Lerp(
                transitionFrom.localPosition,
                sampledPosition,
                transitionBlend);
            sampledRotation = Quaternion.Slerp(
                transitionFrom.localRotation,
                sampledRotation,
                transitionBlend);
            sampledScale = Vector3.Lerp(
                transitionFrom.localScale,
                sampledScale,
                transitionBlend);
        }

        target.localPosition = Vector3.Lerp(
            target.localPosition,
            sampledPosition,
            blend);
        target.localRotation = Quaternion.Slerp(
            target.localRotation,
            sampledRotation,
            blend);
        target.localScale = Vector3.Lerp(
            target.localScale,
            sampledScale,
            blend);
    }

    private void StabilizeHeldHead()
    {
        if (neckGrabBone == null || headBone == null || playerCamera == null)
            return;

        Vector3 toPlayer = playerCamera.transform.position - headBone.position;
        Vector3 horizontalDirection = Vector3.ProjectOnPlane(toPlayer, Vector3.up);
        float horizontalDistance = horizontalDirection.magnitude;

        if (horizontalDistance < 0.0001f)
        {
            horizontalDirection = transform.forward;
            horizontalDistance = 0.0001f;
        }

        horizontalDirection.Normalize();

        // 잡힌 목뼈의 위치는 그대로 두고, 그 자식인 머리뼈가 위쪽과 플레이어 쪽에 오도록
        // 목 회전만 보정합니다. 이러면 턱이 하늘을 향하지 않고 카메라에서 얼굴이 보입니다.
        Vector3 uprightNeckDirection = (
            Vector3.up + horizontalDirection * heldNeckForwardLean).normalized;
        AimBoneTowardDirection(
            neckGrabBone,
            uprightNeckDirection,
            heldNeckUprightStrength * heldPoseBlend,
            heldNeckMaximumAngle);

        float rawPitch = Mathf.Atan2(toPlayer.y, horizontalDistance) * Mathf.Rad2Deg;
        float limitedPitch = Mathf.Clamp(
            rawPitch,
            -heldHeadMaximumPitch,
            heldHeadMaximumPitch);
        float pitchRadians = limitedPitch * Mathf.Deg2Rad;
        Vector3 stableLookDirection =
            horizontalDirection * Mathf.Cos(pitchRadians) +
            Vector3.up * Mathf.Sin(pitchRadians);

        Quaternion stableFaceFrame = Quaternion.LookRotation(
            stableLookDirection.normalized,
            Vector3.up);
        Vector3 currentFaceForward = headBone.TransformDirection(headForwardAxisLocal);
        Vector3 currentFaceUp = headBone.TransformDirection(headUpAxisLocal);
        Quaternion currentFaceFrame = Quaternion.LookRotation(
            currentFaceForward.normalized,
            currentFaceUp.normalized);

        // 머리뼈 자체의 +Z를 얼굴 전방이라고 가정하지 않고, 캐시한 리그별 얼굴 프레임이
        // 목표 프레임을 향하도록 델타 회전만 적용합니다. 시바처럼 얼굴축이 로컬 +Y인 경우에도
        // 180도 반대 해를 번갈아 선택하지 않아 좌우 플립이 사라집니다.
        Quaternion faceDelta = stableFaceFrame * Quaternion.Inverse(currentFaceFrame);
        Quaternion desiredRotation = faceDelta * headBone.rotation;
        Quaternion angleLimitedRotation = Quaternion.RotateTowards(
            headBone.rotation,
            desiredRotation,
            heldHeadLookAngle);
        float uprightStrength = Mathf.Lerp(
            heldHeadStiffness,
            1f,
            heldHeadUprightSupport);
        headBone.rotation = Quaternion.Slerp(
            headBone.rotation,
            angleLimitedRotation,
            Mathf.Clamp01(uprightStrength * heldPoseBlend));
    }

    private float CalculateSpineChainLength()
    {
        float length = 0f;
        length += BoneDistance(torsoBase, torsoLower != null ? torsoLower : torsoMid);

        if (torsoLower != null)
            length += BoneDistance(torsoLower, torsoMid);

        length += BoneDistance(torsoMid, torsoUpper);
        length += BoneDistance(torsoUpper, torsoNeck);
        length += BoneDistance(torsoNeck, neckBase);
        length += BoneDistance(neckBase, neckGrabBone);
        return Mathf.Max(0.1f, length);
    }

    private float GetHeldSpineHangRatio()
    {
        // 이전 씬에 낮은 값이 저장돼 있어도 안전 최소값 아래로 내려가지 않게 합니다.
        // 이 값은 뼈 길이를 늘리는 값이 아니라 목과 골반 사이의 목표 거리를 정하는 값입니다.
        return Mathf.Clamp(
            Mathf.Max(pelvisHangLengthRatio, heldSpineMinimumLengthRatio),
            0.7f,
            0.95f);
    }

    private void ShapeHeldSpine(Vector3 pelvisToNeckDirection, float influence)
    {
        if (pelvisToNeckDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 upward = Vector3.ProjectOnPlane(pelvisToNeckDirection, transform.forward);
        if (upward.sqrMagnitude < 0.0001f)
            upward = Vector3.up;
        upward.Normalize();

        float forwardAmount = Vector3.Dot(
            pelvisToNeckDirection.normalized,
            transform.forward);
        float maximumAngle = Mathf.Max(bodyMaximumSagAngle, 92f);

        // 아래쪽은 거의 수직으로 두고, 가슴과 목으로 올라갈수록 플레이어 쪽으로 아주 조금
        // 기울입니다. 모든 마디가 같은 쪽으로 이어지므로 가슴만 뒤로 솟는 역방향 굴곡이 없습니다.
        AimBoneSegmentTowardDirection(
            torsoBase,
            torsoLower != null ? torsoLower : torsoMid,
            (upward + transform.forward * forwardAmount * 0.55f).normalized,
            influence,
            maximumAngle);
        AimBoneSegmentTowardDirection(
            torsoLower,
            torsoMid,
            (upward + transform.forward * forwardAmount * 0.7f).normalized,
            influence,
            maximumAngle);
        AimBoneSegmentTowardDirection(
            torsoMid,
            torsoUpper,
            (upward + transform.forward * forwardAmount * 0.85f).normalized,
            influence,
            maximumAngle);
        AimBoneSegmentTowardDirection(
            torsoUpper,
            torsoNeck,
            pelvisToNeckDirection,
            influence,
            maximumAngle);
        AimBoneSegmentTowardDirection(
            torsoNeck,
            neckBase,
            pelvisToNeckDirection,
            influence,
            maximumAngle);
        AimBoneSegmentTowardDirection(
            neckBase,
            neckGrabBone,
            pelvisToNeckDirection,
            influence,
            maximumAngle);
    }

    private static float BoneDistance(Transform from, Transform to)
    {
        return from != null && to != null ? Vector3.Distance(from.position, to.position) : 0f;
    }

    private void SolveSpineIk(Vector3 neckTarget, float influence)
    {
        if (neckGrabBone == null || torsoBase == null)
            return;

        int iterations = Mathf.Clamp(spineIkIterations, 1, 12);
        float solveBlend = Mathf.Clamp01(
            spineIkStrength * heldPoseBlend * Mathf.Lerp(0.7f, 1f, influence));
        float maximumStepAngle = bodyMaximumSagAngle / iterations;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            RotateSpineJointTowards(neckBase, neckTarget, solveBlend, maximumStepAngle);
            RotateSpineJointTowards(torsoNeck, neckTarget, solveBlend, maximumStepAngle);
            RotateSpineJointTowards(torsoUpper, neckTarget, solveBlend, maximumStepAngle);
            RotateSpineJointTowards(torsoMid, neckTarget, solveBlend, maximumStepAngle);
            RotateSpineJointTowards(torsoLower, neckTarget, solveBlend, maximumStepAngle);
            RotateSpineJointTowards(torsoBase, neckTarget, solveBlend, maximumStepAngle);
        }
    }

    private void RotateSpineJointTowards(
        Transform joint,
        Vector3 target,
        float influence,
        float maximumAngle)
    {
        if (joint == null || neckGrabBone == null)
            return;

        Vector3 currentEndDirection = neckGrabBone.position - joint.position;
        Vector3 targetDirection = target - joint.position;
        if (currentEndDirection.sqrMagnitude < 0.0001f || targetDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion fullDelta = Quaternion.FromToRotation(
            currentEndDirection.normalized,
            targetDirection.normalized);
        Quaternion limitedDelta = Quaternion.RotateTowards(
            Quaternion.identity,
            fullDelta,
            Mathf.Max(0f, maximumAngle));
        Quaternion blendedDelta = Quaternion.Slerp(
            Quaternion.identity,
            limitedDelta,
            Mathf.Clamp01(influence));
        joint.rotation = blendedDelta * joint.rotation;
    }

    private void AimBoneTowardDirection(
        Transform bone,
        Vector3 desiredDirection,
        float influence,
        float maximumAngle)
    {
        if (bone == null || bone.childCount == 0 || desiredDirection.sqrMagnitude < 0.0001f)
            return;

        Transform child = bone.GetChild(0);
        Vector3 currentDirection = child.position - bone.position;
        if (currentDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 blendedDirection = Vector3.Slerp(
            currentDirection.normalized,
            desiredDirection.normalized,
            Mathf.Clamp01(influence));
        Quaternion delta = Quaternion.FromToRotation(
            currentDirection.normalized,
            blendedDirection.normalized);
        Quaternion desiredRotation = delta * bone.rotation;
        bone.rotation = Quaternion.RotateTowards(
            bone.rotation,
            desiredRotation,
            Mathf.Max(0f, maximumAngle));
    }

    private static void AimBoneSegmentTowardDirection(
        Transform bone,
        Transform child,
        Vector3 desiredDirection,
        float influence,
        float maximumAngle)
    {
        if (bone == null || child == null || desiredDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 currentDirection = child.position - bone.position;
        if (currentDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 blendedDirection = Vector3.Slerp(
            currentDirection.normalized,
            desiredDirection.normalized,
            Mathf.Clamp01(influence));
        Quaternion fullDelta = Quaternion.FromToRotation(
            currentDirection.normalized,
            blendedDirection.normalized);
        Quaternion limitedDelta = Quaternion.RotateTowards(
            Quaternion.identity,
            fullDelta,
            Mathf.Max(0f, maximumAngle));
        bone.rotation = limitedDelta * bone.rotation;
    }

    #endregion
}
