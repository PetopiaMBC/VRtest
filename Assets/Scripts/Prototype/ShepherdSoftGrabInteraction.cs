using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 셰퍼드의 목 뒤를 손으로 잡아 들어 올리는 시험용 기능입니다.
/// 잡은 본은 플레이어 앞의 고정점에 맞추고, 그 아래의 몸통·다리·꼬리는
/// 애니메이션 대신 실제 중력 방향을 기준으로 처지도록 변형합니다.
/// </summary>
public sealed class ShepherdSoftGrabInteraction : MonoBehaviour
{
    #region 인스펙터 설정

    [Header("잡기 입력")]
    [Tooltip("키보드의 G 키를 눌러 목 뒤를 잡거나 놓습니다. 기존 셰퍼드의 Q 휘파람 기능과 겹치지 않습니다.")]
    [SerializeField] private bool useKeyboardGrab = true;
    [Tooltip("잡기 판정에 사용할 플레이어 카메라입니다. 비워 두면 활성화된 Main Camera를 자동으로 찾습니다.")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("카메라에서 이 거리 안에 있는 셰퍼드만 잡을 수 있습니다.")]
    [SerializeField, Min(0.5f)] private float grabDistance = 2.2f;
    [Tooltip("실제로 손이 걸리는 본 이름입니다. 기본값은 머리 뒤쪽 목 본입니다.")]
    [SerializeField] private string grabBoneName = "DEF-spine.010";
    [Tooltip("목 본의 중심에서 실제 손바닥이 닿는 목덜미 표면까지의 로컬 오프셋입니다. Z를 음수로 하면 머리 뒤쪽으로 이동합니다.")]
    [SerializeField] private Vector3 grabPointLocalOffset = new Vector3(0f, 0.015f, -0.055f);

    [Header("목 뒤 고정점 위치")]
    [Tooltip("잡은 목 본이 플레이어 카메라 앞에 유지될 거리입니다. 값이 클수록 강아지가 멀리 보입니다.")]
    [SerializeField, Min(1f)] private float holdDistance = 3.6f;
    [Tooltip("목 고정점을 카메라 기준으로 위아래로 옮깁니다. 음수이면 화면 중앙보다 조금 아래에 놓입니다.")]
    [SerializeField] private float holdVerticalOffset = -0.2f;
    [Tooltip("목 고정점이 목표 위치를 따라가는 부드러움입니다.")]
    [SerializeField, Min(1f)] private float holdFollowSpeed = 12f;
    [Tooltip("강아지가 플레이어를 바라보도록 회전하는 부드러움입니다.")]
    [SerializeField, Min(1f)] private float holdRotationSpeed = 10f;
    [Tooltip("중력 포즈를 적용한 뒤에도 실제 목 본이 손 위치에서 벗어나지 않게 보정하는 속도입니다.")]
    [SerializeField, Min(1f)] private float grabPointCorrectionSpeed = 24f;

    [Header("중력과 피부 늘어남")]
    [Tooltip("잡은 목 주변의 스킨 본을 늘려 손으로 잡아당긴 듯한 느낌을 냅니다. 너무 크게 하면 피부가 과하게 늘어납니다.")]
    [SerializeField, Range(0f, 0.35f)] private float skinStretchAmount = 0.16f;
    [Tooltip("잡은 지점 주변 피부를 바깥쪽으로 도톰하게 부풀리는 정도입니다. 피부가 들어가 보일 때 조금씩 올립니다.")]
    [SerializeField, Range(0f, 0.3f)] private float grabSkinBulge = 0.1f;
    [Tooltip("잡은 지점 주변의 목 피부를 중력 반대 방향으로 들어 올리는 거리입니다. 너무 높이면 목이 늘어져 보일 수 있습니다.")]
    [SerializeField, Range(0f, 0.15f)] private float grabSkinLift = 0.045f;
    [Tooltip("전체 중력 포즈의 최종 세기입니다. 0이면 중력 변형을 끄고, 0.3~0.5 정도면 약하고 자연스럽게 처집니다.")]
    [SerializeField, Range(0f, 1f)] private float gravityStrength = 0.35f;
    [Tooltip("몸통이 목 고정점 아래로 떨어지는 정도입니다. 높을수록 몸통이 중력 방향으로 더 많이 처집니다.")]
    [SerializeField, Range(0f, 1f)] private float bodyGravityInfluence = 0.85f;
    [Tooltip("앞다리와 뒷다리가 중력 방향으로 늘어지는 정도입니다.")]
    [SerializeField, Range(0f, 1f)] private float limbGravityInfluence = 0.7f;
    [Tooltip("꼬리 각 마디가 아래쪽으로 처지는 정도입니다.")]
    [SerializeField, Range(0f, 1f)] private float tailGravityInfluence = 0.85f;
    [Tooltip("몸통 본 하나가 원래 방향에서 중력 방향으로 꺾일 수 있는 최대 각도입니다.")]
    [SerializeField, Range(0f, 60f)] private float bodyMaximumSagAngle = 32f;
    [Tooltip("다리 본 하나가 원래 방향에서 꺾일 수 있는 최대 각도입니다. 낮게 유지하면 앞발이 뒤집히지 않습니다.")]
    [SerializeField, Range(0f, 45f)] private float limbMaximumSagAngle = 18f;
    [Tooltip("꼬리 본 하나가 원래 방향에서 꺾일 수 있는 최대 각도입니다.")]
    [SerializeField, Range(0f, 45f)] private float tailMaximumSagAngle = 24f;

    [Header("잡기 판정 영역")]
    [Tooltip("모델 전체를 감싸는 보이지 않는 Trigger 크기에 더하는 여유입니다. 이 영역은 물리 충돌을 막지 않습니다.")]
    [SerializeField, Min(0f)] private float grabColliderPadding = 0.08f;

    #endregion

    #region 내부 상태

    private sealed class BoneRestPose
    {
        public Transform bone;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    private readonly List<BoneRestPose> restPoses = new List<BoneRestPose>();

    private Animator animator;
    private ShepherdCompanionAI shepherdAI;
    private BoxCollider grabCollider;

    private Transform grabBone;
    private Transform torsoBase;
    private Transform torsoMid;
    private Transform torsoUpper;
    private Transform torsoNeck;
    private Transform neckBase;
    private Transform frontShoulderLeft;
    private Transform frontShoulderRight;
    private Transform frontShinLeft;
    private Transform frontShinRight;
    private Transform hindThighLeft;
    private Transform hindThighRight;
    private Transform hindShinLeft;
    private Transform hindShinRight;
    private Transform tailBase;
    private Transform tailMiddle;
    private Transform tailTip;
    private Transform tailEnd;

    private bool holding;
    private Vector3 holdVelocity;
    private Vector3 grabPointLocalPosition;
    private Vector3 currentGrabTarget;

    private bool animatorWasEnabled;
    private bool animatorWasApplyRootMotion;
    private float animatorWasSpeed;
    private bool aiWasEnabled;

    #endregion

    #region Unity 생명주기

    private void Awake()
    {
        ResolveReferences();
        CacheBones();
        CreateGrabCollider();
    }

    private void Update()
    {
        ResolveReferences();

        if (useKeyboardGrab && Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
        {
            if (holding)
                ReleaseShepherd();
            else if (CanGrabShepherd())
                GrabShepherd();
        }
    }

    private void LateUpdate()
    {
        if (!holding || playerCamera == null || grabBone == null)
            return;

        UpdateHeldTransform();
        ApplyHeldGravityPose();

        // 본을 회전시킨 뒤에도 목 뒤 고정점이 손 위치에 남도록 마지막에 다시 맞춥니다.
        KeepGrabPointAtTarget();
    }

    #endregion

    #region 참조와 잡기 판정

    private void ResolveReferences()
    {
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            playerCamera = Camera.main;

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (shepherdAI == null)
            shepherdAI = GetComponent<ShepherdCompanionAI>();

        if (grabBone == null)
            grabBone = FindBone(grabBoneName);
    }

    private bool CanGrabShepherd()
    {
        if (playerCamera == null)
            return false;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                grabDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide))
            return false;

        return hit.collider == grabCollider || hit.collider.transform.IsChildOf(transform);
    }

    private void GrabShepherd()
    {
        if (holding || grabBone == null)
            return;

        ResolveReferences();
        holding = true;
        holdVelocity = Vector3.zero;
        grabPointLocalPosition = transform.InverseTransformPoint(GetGrabPointWorldPosition());

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
            animator.speed = 1f;
            animator.CrossFadeInFixedTime("1 type_Idle Breathing", 0.08f, 0);
            // 잡고 있는 동안에는 애니메이션이 본을 되돌리지 않도록 정지합니다.
            animator.enabled = false;
        }
    }

    private void ReleaseShepherd()
    {
        if (!holding)
            return;

        RestoreBonePose();
        holding = false;
        holdVelocity = Vector3.zero;

        if (animator != null)
        {
            animator.applyRootMotion = animatorWasApplyRootMotion;
            animator.speed = animatorWasSpeed;
            animator.enabled = animatorWasEnabled;
        }

        if (shepherdAI != null)
            shepherdAI.enabled = aiWasEnabled;
    }

    private void CreateGrabCollider()
    {
        grabCollider = GetComponent<BoxCollider>();
        if (grabCollider == null)
            grabCollider = gameObject.AddComponent<BoxCollider>();

        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
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

        if (hasBounds)
        {
            grabCollider.center = transform.InverseTransformPoint(bounds.center);
            grabCollider.size = bounds.size + Vector3.one * grabColliderPadding;
        }

        // 잡기용 Trigger만 만들고, 셰퍼드의 이동을 막는 물리 충돌체로 사용하지 않습니다.
        grabCollider.isTrigger = true;
    }

    #endregion

    #region 본 캐시

    private void CacheBones()
    {
        restPoses.Clear();

        // DEF-로 시작하는 리깅 본을 모두 저장하면 잡는 동안 애니메이터가 남긴 포즈가
        // 다음 프레임에 되살아나지 않습니다. 놓을 때는 저장한 포즈를 그대로 복구합니다.
        Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            if (allTransforms[i].name.StartsWith("DEF-", StringComparison.OrdinalIgnoreCase))
                CacheBone(allTransforms[i]);
        }

        grabBone = FindBone(grabBoneName);
        // .004에서 .008까지가 엉덩이에서 목으로 이어지는 실제 몸통 체인입니다.
        torsoBase = FindBone("DEF-spine.004");
        torsoMid = FindBone("DEF-spine.006");
        torsoUpper = FindBone("DEF-spine.007");
        torsoNeck = FindBone("DEF-spine.008");
        neckBase = FindBone("DEF-spine.009");
        frontShoulderLeft = FindBone("DEF-shoulder.L");
        frontShoulderRight = FindBone("DEF-shoulder.R");
        frontShinLeft = FindBone("DEF-front_shin.L");
        frontShinRight = FindBone("DEF-front_shin.R");
        hindThighLeft = FindBone("DEF-thigh.L");
        hindThighRight = FindBone("DEF-thigh.R");
        hindShinLeft = FindBone("DEF-shin.L");
        hindShinRight = FindBone("DEF-shin.R");
        // .003에서 끝의 DEF-spine까지가 몸통 뒤쪽으로 이어지는 꼬리 체인입니다.
        tailBase = FindBone("DEF-spine.003");
        tailMiddle = FindBone("DEF-spine.002");
        tailTip = FindBone("DEF-spine.001");
        tailEnd = FindBone("DEF-spine");
    }

    private void CacheBone(Transform bone)
    {
        if (bone == null || FindRestPose(bone) != null)
            return;

        restPoses.Add(new BoneRestPose
        {
            bone = bone,
            localPosition = bone.localPosition,
            localRotation = bone.localRotation,
            localScale = bone.localScale
        });
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

    private BoneRestPose FindRestPose(Transform bone)
    {
        for (int i = 0; i < restPoses.Count; i++)
        {
            if (restPoses[i].bone == bone)
                return restPoses[i];
        }

        return null;
    }

    #endregion

    #region 목 고정점 이동

    private void UpdateHeldTransform()
    {
        currentGrabTarget = playerCamera.transform.position +
                             playerCamera.transform.forward * holdDistance +
                             playerCamera.transform.up * holdVerticalOffset;

        Vector3 faceDirection = playerCamera.transform.position - currentGrabTarget;
        faceDirection.y = 0f;
        if (faceDirection.sqrMagnitude < 0.0001f)
        {
            faceDirection = -playerCamera.transform.forward;
            faceDirection.y = 0f;
        }

        if (faceDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(faceDirection.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                1f - Mathf.Exp(-holdRotationSpeed * Time.deltaTime));
        }

        // 루트 자체를 카메라에 고정하지 않고, 루트에서 목 본까지의 실제 오프셋을
        // 역산합니다. 그래서 플레이어가 방향을 돌리면 목을 잡은 강아지도 함께 따라옵니다.
        Vector3 desiredRootPosition = currentGrabTarget - transform.rotation * grabPointLocalPosition;
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredRootPosition,
            ref holdVelocity,
            1f / holdFollowSpeed);
    }

    private void KeepGrabPointAtTarget()
    {
        if (grabBone == null)
            return;

        Vector3 correction = currentGrabTarget - GetGrabPointWorldPosition();
        float correctionBlend = 1f - Mathf.Exp(-grabPointCorrectionSpeed * Time.deltaTime);
        transform.position += correction * correctionBlend;
    }

    private Vector3 GetGrabPointWorldPosition()
    {
        return grabBone != null
            ? grabBone.TransformPoint(grabPointLocalOffset)
            : transform.position;
    }

    #endregion

    #region 중력 포즈와 국소 스킨 늘어남

    private void ApplyHeldGravityPose()
    {
        ResetBonePose();

        // 목 본의 월드 포즈를 먼저 기억합니다. 몸통은 목의 부모 척추에
        // 연결되어 있으므로, 몸통을 중력 방향으로 꺾은 뒤 목 본을 다시
        // 원래의 월드 위치에 돌려놓아야 몸통을 잡은 것처럼 보이지 않습니다.
        Vector3 neckAnchorPosition = grabBone != null ? grabBone.position : transform.position;
        Quaternion neckAnchorRotation = grabBone != null ? grabBone.rotation : transform.rotation;

        Vector3 gravityDirection = Physics.gravity.sqrMagnitude > 0.001f
            ? Physics.gravity.normalized
            : Vector3.down;
        Vector3 dogForward = transform.forward;
        Vector3 dogBack = -dogForward;
        Vector3 left = -transform.right;
        Vector3 right = transform.right;

        // 목 바로 아래에서 시작해 몸통 끝으로 갈수록 중력 영향을 키웁니다.
        // 몸통 본의 자식 방향은 엉덩이에서 목으로 향합니다. 따라서 이 방향을
        // 무조건 아래로 돌리면 목이 뒤집히므로, 중력의 반대 방향을 기준으로
        // 조금만 보정해야 목은 위에 남고 몸통만 아래로 처집니다.
        Vector3 bodyToNeck = torsoBase != null && grabBone != null
            ? grabBone.position - torsoBase.position
            : dogForward;
        if (bodyToNeck.sqrMagnitude < 0.0001f)
            bodyToNeck = dogForward;
        bodyToNeck.Normalize();

        Vector3 bodyDropDirection = Vector3.Slerp(
            bodyToNeck,
            -gravityDirection,
            gravityStrength * bodyGravityInfluence).normalized;
        float bodyInfluence = gravityStrength * bodyGravityInfluence;

        AimBoneTowardChild(torsoBase, torsoMid, bodyDropDirection, bodyInfluence, bodyMaximumSagAngle);
        AimBoneTowardChild(torsoMid, torsoUpper, bodyDropDirection, bodyInfluence, bodyMaximumSagAngle);
        AimBoneTowardChild(torsoUpper, torsoNeck, bodyDropDirection, bodyInfluence, bodyMaximumSagAngle);
        AimBoneTowardChild(torsoNeck, neckBase, bodyDropDirection, bodyInfluence * 0.75f, bodyMaximumSagAngle * 0.75f);
        AimBoneTowardChild(neckBase, grabBone, bodyDropDirection, bodyInfluence * 0.35f, bodyMaximumSagAngle * 0.5f);

        // 목 주변은 살짝 늘리고, 몸통으로 갈수록 늘어남을 약하게 이어 줍니다.
        ApplyGripSkinDeformation(gravityDirection);

        float limbInfluence = gravityStrength * limbGravityInfluence;
        Vector3 frontLeftDirection = (gravityDirection * 0.78f + dogForward * 0.12f + left * 0.1f).normalized;
        Vector3 frontRightDirection = (gravityDirection * 0.78f + dogForward * 0.12f + right * 0.1f).normalized;
        Vector3 hindLeftDirection = (gravityDirection * 0.82f + dogBack * 0.1f + left * 0.08f).normalized;
        Vector3 hindRightDirection = (gravityDirection * 0.82f + dogBack * 0.1f + right * 0.08f).normalized;

        AimBoneTowardChild(frontShoulderLeft, frontLeftDirection, limbInfluence * 0.45f, limbMaximumSagAngle);
        AimBoneTowardChild(frontShoulderRight, frontRightDirection, limbInfluence * 0.45f, limbMaximumSagAngle);
        AimBoneTowardChild(frontShinLeft, gravityDirection, limbInfluence * 0.3f, limbMaximumSagAngle);
        AimBoneTowardChild(frontShinRight, gravityDirection, limbInfluence * 0.3f, limbMaximumSagAngle);
        AimBoneTowardChild(hindThighLeft, hindLeftDirection, limbInfluence * 0.45f, limbMaximumSagAngle);
        AimBoneTowardChild(hindThighRight, hindRightDirection, limbInfluence * 0.45f, limbMaximumSagAngle);
        AimBoneTowardChild(hindShinLeft, gravityDirection, limbInfluence * 0.3f, limbMaximumSagAngle);
        AimBoneTowardChild(hindShinRight, gravityDirection, limbInfluence * 0.3f, limbMaximumSagAngle);

        float tailInfluence = gravityStrength * tailGravityInfluence;
        Vector3 tailDirection = Vector3.Slerp(
            dogBack,
            gravityDirection,
            tailInfluence).normalized;
        AimBoneTowardChild(tailBase, tailMiddle, tailDirection, tailInfluence, tailMaximumSagAngle);
        AimBoneTowardChild(tailMiddle, tailTip, tailDirection, tailInfluence, tailMaximumSagAngle);
        AimBoneTowardChild(tailTip, tailEnd, gravityDirection, tailInfluence, tailMaximumSagAngle);

        // 몸통 부모 본이 아무리 회전해도 머리 뒤쪽 목은 고정점에 남깁니다.
        // 이 보정으로 목과 몸통 사이가 잡아당겨지는 국소적인 스킨 변형도 생깁니다.
        RestoreBoneWorldPose(grabBone, neckAnchorPosition, neckAnchorRotation);
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

    private void StretchBone(Transform bone, float amount)
    {
        if (bone == null)
            return;

        BoneRestPose rest = FindRestPose(bone);
        if (rest == null)
            return;

        // 이 리그의 본 길이 방향이 로컬 Y이므로 Y 스케일만 늘립니다.
        bone.localScale = new Vector3(
            rest.localScale.x,
            rest.localScale.y * (1f + Mathf.Clamp(amount, 0f, 0.5f)),
            rest.localScale.z);
    }

    private void ApplyGripSkinDeformation(Vector3 gravityDirection)
    {
        ScaleBoneAroundGrip(grabBone, grabSkinBulge, skinStretchAmount * 1.15f, grabSkinBulge);
        ScaleBoneAroundGrip(torsoNeck, grabSkinBulge * 0.55f, skinStretchAmount * 0.65f, grabSkinBulge * 0.55f);
        ScaleBoneAroundGrip(torsoUpper, grabSkinBulge * 0.25f, skinStretchAmount * 0.3f, grabSkinBulge * 0.25f);

        Vector3 liftDirection = gravityDirection.sqrMagnitude > 0.001f
            ? -gravityDirection.normalized
            : Vector3.up;

        // 목 고정점은 마지막에 다시 고정하므로, 바로 아래 본만 위로 올립니다.
        // 이 차이로 잡은 지점 주변 피부가 안쪽으로 꺼지지 않고 올라와 보입니다.
        if (neckBase != null)
            neckBase.position += liftDirection * grabSkinLift;
        if (torsoNeck != null)
            torsoNeck.position += liftDirection * (grabSkinLift * 0.45f);
    }

    private void ScaleBoneAroundGrip(Transform bone, float widthAmount, float lengthAmount, float depthAmount)
    {
        if (bone == null)
            return;

        BoneRestPose rest = FindRestPose(bone);
        if (rest == null)
            return;

        bone.localScale = new Vector3(
            rest.localScale.x * (1f + Mathf.Clamp(widthAmount, 0f, 0.5f)),
            rest.localScale.y * (1f + Mathf.Clamp(lengthAmount, 0f, 0.5f)),
            rest.localScale.z * (1f + Mathf.Clamp(depthAmount, 0f, 0.5f)));
    }

    private void RestoreBonePose()
    {
        ResetBonePose();
    }

    private static void RestoreBoneWorldPose(
        Transform bone,
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        if (bone == null)
            return;

        bone.SetPositionAndRotation(worldPosition, worldRotation);
    }

    private static void AimBoneTowardChild(
        Transform bone,
        Vector3 desiredDirection,
        float influence,
        float maximumAngle)
    {
        if (bone == null || bone.childCount == 0 || desiredDirection.sqrMagnitude < 0.001f)
            return;

        AimBoneTowardChild(bone, bone.GetChild(0), desiredDirection, influence, maximumAngle);
    }

    private static void AimBoneTowardChild(
        Transform bone,
        Transform child,
        Vector3 desiredDirection,
        float influence,
        float maximumAngle)
    {
        if (bone == null || child == null || desiredDirection.sqrMagnitude < 0.001f)
            return;

        Vector3 currentDirection = child.position - bone.position;
        if (currentDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 blendedDirection = Vector3.Slerp(
            currentDirection.normalized,
            desiredDirection.normalized,
            Mathf.Clamp01(influence));
        Quaternion desiredRotation = Quaternion.FromToRotation(
            currentDirection.normalized,
            blendedDirection) * bone.rotation;
        bone.rotation = Quaternion.RotateTowards(
            bone.rotation,
            desiredRotation,
            Mathf.Max(0f, maximumAngle));
    }

    #endregion
}
