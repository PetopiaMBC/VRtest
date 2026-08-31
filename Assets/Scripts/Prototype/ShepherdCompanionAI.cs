using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 셰퍼드 전용 생활형 AI입니다.
/// 쓰다듬기나 집어 들기 기능은 포함하지 않고, 생활 상태와 플레이어 추적만 담당합니다.
/// </summary>
public sealed class ShepherdCompanionAI : MonoBehaviour
{
    #region 상태 정의

    private enum BrainState
    {
        Idle,
        Walk,
        Run,
        FollowPlayer
    }

    #endregion

    #region 인스펙터 설정

    [Header("플레이어 인식")]
    [Tooltip("이 거리 안에 플레이어가 들어오면 셰퍼드가 플레이어를 인식합니다.")]
    [SerializeField, Min(0.5f)] private float playerDetectionDistance = 3.5f;
    [Tooltip("플레이어에게 이 거리까지 다가가면 멈추고 고개만 플레이어를 바라봅니다.")]
    [SerializeField, Min(0.4f)] private float playerStopDistance = 1.7f;
    [Tooltip("플레이어가 이 거리보다 멀리 있으면 걷는 대신 달려서 따라갑니다.")]
    [SerializeField, Min(0f)] private float playerRunDistance = 2.7f;
    [Tooltip("걷기와 달리기 사이에서 플레이어가 조금 움직여도 애니메이션이 반복해서 바뀌지 않도록 하는 완충 거리입니다. 값이 클수록 상태 전환이 안정적입니다.")]
    [SerializeField, Min(0f)] private float runWalkHysteresis = 0.35f;
    [Tooltip("몸통은 움직이지 않고 고개만 좌우로 돌릴 수 있는 최대 각도입니다.")]
    [SerializeField, Min(1f)] private float headYawLimit = 42f;
    [Tooltip("고개가 목표를 향해 돌아가는 속도입니다. 값이 클수록 빠르게 바라봅니다.")]
    [SerializeField, Min(0f)] private float headTurnSpeed = 7f;
    [Tooltip("비워 두면 씬에서 플레이어를 자동으로 찾습니다.")]
    [SerializeField] private Transform playerTransform;
    [Tooltip("비워 두면 Main Camera를 자동으로 사용합니다.")]
    [SerializeField] private Camera playerCamera;

    [Header("휘파람 호출")]
    [Tooltip("Q 키로 셰퍼드를 불렀을 때 이 거리까지 다가오면 플레이어 앞에서 멈춥니다.")]
    [SerializeField, Min(0.4f)] private float whistleArrivalDistance = 1.7f;
    [Tooltip("Q 키로 부른 뒤 플레이어 앞에서 Idle 상태를 유지하는 최소 시간입니다.")]
    [SerializeField, Min(0.5f)] private float whistleIdleDurationMin = 2f;
    [Tooltip("Q 키로 부른 뒤 플레이어 앞에서 Idle 상태를 유지하는 최대 시간입니다.")]
    [SerializeField, Min(0.5f)] private float whistleIdleDurationMax = 3f;

    [Header("이동")]
    [Tooltip("셰퍼드가 걷는 속도입니다.")]
    [SerializeField, Min(0.05f)] private float walkSpeed = 0.9f;
    [Tooltip("셰퍼드가 달리는 속도입니다.")]
    [SerializeField, Min(0.05f)] private float runSpeed = 1.35f;
    [Tooltip("몸통이 이동 방향으로 회전하는 속도입니다.")]
    [SerializeField, Min(1f)] private float bodyTurnSpeed = 210f;
    [Tooltip("플레이어가 멀리 있을 때 셰퍼드가 돌아다닐 수 있는 중심점 주변 반경입니다.")]
    [SerializeField, Min(0f)] private float wanderRadius = 4.5f;
    [Tooltip("발이 바닥에 묻히지 않도록 모델 루트에 더하는 높이입니다.")]
    [SerializeField, Min(0f)] private float groundOffset = 0.05f;

    [Header("장애물 회피")]
    [Tooltip("셰퍼드가 이동하기 전에 앞쪽 장애물을 확인하는 거리입니다. 벽에 닿기 전에 충분히 멈추려면 셰퍼드 몸통 길이보다 크게 설정하세요.")]
    [SerializeField, Min(0.1f)] private float obstacleCheckDistance = 0.75f;
    [Tooltip("장애물을 확인할 때 사용하는 셰퍼드 몸통의 가로 반지름입니다. 너무 작으면 몸이 벽을 일부 통과할 수 있습니다.")]
    [SerializeField, Min(0.05f)] private float obstacleCheckRadius = 0.28f;
    [Tooltip("장애물 확인 캡슐의 중심 높이입니다. 바닥이 아니라 벽과 가구의 몸통 부분을 확인하도록 설정합니다.")]
    [SerializeField, Min(0.1f)] private float obstacleCheckHeight = 0.45f;
    [Tooltip("장애물을 발견했을 때 잠시 옆 방향으로 빠지는 시간입니다. 이 시간 동안 다시 벽 쪽으로 방향을 잡지 않습니다.")]
    [SerializeField, Min(0.1f)] private float obstacleAvoidanceDuration = 1.2f;
    [Tooltip("장애물 앞에서 왼쪽 또는 오른쪽으로 돌아나갈 때 사용하는 회전 각도입니다.")]
    [SerializeField, Range(45f, 120f)] private float obstacleAvoidanceAngle = 90f;
    [Tooltip("장애물을 만났을 때 Idle Breathing으로 잠시 멈추는 시간입니다. 너무 짧으면 제자리에서 걷는 것처럼 보일 수 있습니다.")]
    [SerializeField, Min(0.05f)] private float obstaclePauseDuration = 0.2f;
    [Tooltip("장애물을 피한 뒤 벽에서 떨어져 새 목적지를 만들 때 사용할 최대 거리입니다.")]
    [SerializeField, Min(0.5f)] private float obstacleEscapeDistance = 2.5f;
    [Tooltip("회피 후 새 목적지에 도착했다고 판단하는 거리입니다.")]
    [SerializeField, Min(0.1f)] private float obstacleRepositionStopDistance = 0.35f;

    [Header("평소 생활 패턴")]
    [Tooltip("평소 가만히 있는 최소 시간입니다.")]
    [SerializeField, Min(0.1f)] private float idleDurationMin = 2f;
    [Tooltip("평소 가만히 있는 최대 시간입니다.")]
    [SerializeField, Min(0.1f)] private float idleDurationMax = 5f;
    [Tooltip("평소 걷는 최소 시간입니다.")]
    [SerializeField, Min(0.1f)] private float walkDurationMin = 3f;
    [Tooltip("평소 걷는 최대 시간입니다.")]
    [SerializeField, Min(0.1f)] private float walkDurationMax = 7f;
    [Tooltip("평소 달리기를 할 때의 최소 시간입니다.")]
    [SerializeField, Min(0.1f)] private float runDurationMin = 1.2f;
    [Tooltip("평소 달리기를 할 때의 최대 시간입니다.")]
    [SerializeField, Min(0.1f)] private float runDurationMax = 2.6f;
    [Tooltip("플레이어를 인식하지 못할 때 달리기를 선택할 확률입니다.")]
    [SerializeField, Range(0f, 1f)] private float ambientRunChance = 0.16f;

    [Header("애니메이션")]
    [Tooltip("비워 두면 자식 오브젝트에서 Animator를 자동으로 찾습니다.")]
    [SerializeField] private Animator animator;
    [Tooltip("숨을 쉬며 가만히 있는 애니메이터 상태 이름입니다.")]
    [SerializeField] private string idleBreathingState = "1 type_Idle Breathing";
    [Tooltip("가만히 놀고 있는 애니메이터 상태 이름입니다.")]
    [SerializeField] private string idlePlayingState = "1 type_Idle_Playing";
    [Tooltip("걷기 애니메이터 상태 이름입니다.")]
    [SerializeField] private string walkState = "1 type_Walk_Loop";
    [Tooltip("달리기 애니메이터 상태 이름입니다.")]
    [SerializeField] private string runState = "1 type_Run Loop";
    [Tooltip("달리면서 오른쪽으로 방향을 바꿀 때 재생할 애니메이터 상태 이름입니다.")]
    [SerializeField] private string runLeanRightState = "1 type_Run Lean Right";
    [Tooltip("달리면서 왼쪽으로 방향을 바꿀 때 재생할 애니메이터 상태 이름입니다.")]
    [SerializeField] private string runLeanLeftState = "1 type_Run Lean Left";
    [Tooltip("걸으면서 오른쪽으로 방향을 바꿀 때 재생할 애니메이터 상태 이름입니다.")]
    [SerializeField] private string walkTurnRightState = "1 type_Walk_Turn_Right";
    [Tooltip("걸으면서 왼쪽으로 방향을 바꿀 때 재생할 애니메이터 상태 이름입니다.")]
    [SerializeField] private string walkTurnLeftState = "1 type_Walk_Turn_Left";
    [Tooltip("애니메이션 상태가 바뀔 때 섞이는 시간입니다. 너무 짧으면 끊겨 보일 수 있습니다.")]
    [SerializeField, Min(0.01f)] private float animationBlendTime = 0.2f;
    [Tooltip("달리기 애니메이션의 재생 속도입니다. 캐릭터의 실제 이동 속도와 별개로, 다리 움직임이 느려 보일 때 올려주세요.")]
    [SerializeField, Min(0.1f)] private float runAnimationSpeed = 1.3f;
    [Tooltip("달리기 중 방향 전환 애니메이션을 재생할 최소 회전 각도입니다.")]
    [SerializeField, Min(1f)] private float runTurnAngleThreshold = 12f;
    [Tooltip("달리기 중 좌우 방향 전환 애니메이션을 유지하는 시간입니다.")]
    [SerializeField, Min(0.05f)] private float runTurnAnimationDuration = 0.35f;
    [Tooltip("걷기 중 방향 전환 애니메이션을 재생할 최소 회전 각도입니다.")]
    [SerializeField, Min(1f)] private float walkTurnAngleThreshold = 12f;
    [Tooltip("걷기 중 좌우 방향 전환 애니메이션을 유지하는 시간입니다.")]
    [SerializeField, Min(0.05f)] private float walkTurnAnimationDuration = 0.35f;

    #endregion

    #region 내부 상태

    // 프로젝트마다 플레이어 태그가 없을 수 있으므로 태그와 오브젝트 이름을 순서대로 확인합니다.
    private const string PlayerTag = "Player";
    private static readonly string[] PlayerNames = { "XR Origin Hands (Play)", "XR Origin", "Player" };

    private Transform headBone;
    private Transform neckBone;
    private Vector3 homePosition;
    private Vector3 wanderTarget;
    private float stateTimeRemaining;
    private BrainState state;
    private string currentAnimation;
    private bool playerInRange;
    private bool hadPlayerInRange;
    private Vector3 obstacleAvoidanceDirection;
    private float obstacleAvoidanceTimeRemaining;
    private float obstacleIdleTimeRemaining;
    private bool preferRightAvoidance;
    private bool obstacleRepositionActive;
    private Vector3 obstacleRepositionTarget;
    private bool whistleResponseActive;
    private float whistleIdleTimeRemaining;
    private bool movementUsesRunAnimation;
    private bool followUsesRunAnimation;
    private bool followAnimationModeInitialized;
    private bool turnAnimationActive;
    private bool turnAnimationUsesRun;
    private float turnAnimationElapsed;
    private Vector3 turnTargetDirection;
    private Quaternion turnStartRotation;

    #endregion

    #region 생명주기

    private void Awake()
    {
        homePosition = transform.position;
        ResolveReferences();

        if (animator != null)
            animator.applyRootMotion = false;

        EnterAmbientState(true);
    }

    private void Update()
    {
        ResolveReferences();
        // 프로젝트의 활성 입력 방식인 새 Input System으로 휘파람 입력을 읽습니다.
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
            StartWhistleResponse();

        obstacleAvoidanceTimeRemaining = Mathf.Max(0f, obstacleAvoidanceTimeRemaining - Time.deltaTime);
        UpdateTurnAnimationProgress();
        playerInRange = IsPlayerInRange();

        if (whistleResponseActive)
        {
            // 휘파람 호출 중에는 평소 감지 거리와 관계없이 플레이어의 현재 위치를 계속 추적합니다.
            playerInRange = true;
            UpdateWhistleResponse();
            hadPlayerInRange = playerInRange;
            KeepOnGround();
            return;
        }

        if (obstacleIdleTimeRemaining > 0f)
        {
            obstacleIdleTimeRemaining -= Time.deltaTime;
            turnAnimationActive = false;
            PlayAnimation(idleBreathingState);
            hadPlayerInRange = playerInRange;
            KeepOnGround();
            return;
        }

        if (!playerInRange && hadPlayerInRange && !obstacleRepositionActive)
            EnterAmbientState(false);

        if (obstacleRepositionActive && obstacleAvoidanceTimeRemaining <= 0f)
        {
            if (HasReachedObstacleRepositionTarget())
            {
                FinishObstacleReposition();
            }
            else
            {
                UpdateObstacleReposition();
                hadPlayerInRange = playerInRange;
                KeepOnGround();
                return;
            }
        }

        if (playerInRange)
            UpdatePlayerFollow();
        else
            UpdateAmbientBehavior();

        hadPlayerInRange = playerInRange;
        KeepOnGround();
    }

    private void UpdateTurnAnimationProgress()
    {
        if (!turnAnimationActive)
            return;

        turnAnimationElapsed += Time.deltaTime;
        float duration = turnAnimationUsesRun ? runTurnAnimationDuration : walkTurnAnimationDuration;
        if (turnAnimationElapsed < duration)
            return;

        turnAnimationActive = false;
        PlayAnimation(turnAnimationUsesRun ? runState : walkState);
    }

    private void UpdateWhistleResponse()
    {
        if (obstacleIdleTimeRemaining > 0f)
        {
            obstacleIdleTimeRemaining -= Time.deltaTime;
            turnAnimationActive = false;
            PlayAnimation(idleBreathingState);
            return;
        }

        if (obstacleRepositionActive && obstacleAvoidanceTimeRemaining <= 0f)
        {
            if (HasReachedObstacleRepositionTarget())
                FinishObstacleReposition();
            else
            {
                UpdateObstacleReposition();
                return;
            }
        }

        if (whistleIdleTimeRemaining > 0f)
        {
            whistleIdleTimeRemaining -= Time.deltaTime;
            turnAnimationActive = false;
            movementUsesRunAnimation = false;
            PlayAnimation(idleBreathingState);
            FacePlayerWhileIdle();

            if (whistleIdleTimeRemaining <= 0f)
                FinishWhistleResponse();

            return;
        }

        Vector3 direction = GetPlayerPosition() - transform.position;
        direction.y = 0f;
        float distance = direction.magnitude;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            StartWhistleArrivalIdle();
            return;
        }

        if (distance <= whistleArrivalDistance)
        {
            StartWhistleArrivalIdle();
            return;
        }

        state = BrainState.FollowPlayer;
        movementUsesRunAnimation = true;
        if (!turnAnimationActive)
            PlayAnimation(runState);

        TurnBodyToward(direction.normalized);
        MoveForward(runSpeed);
    }

    private void StartWhistleResponse()
    {
        whistleResponseActive = true;
        whistleIdleTimeRemaining = 0f;
        obstacleIdleTimeRemaining = 0f;
        obstacleRepositionActive = false;
        obstacleAvoidanceDirection = Vector3.zero;
        obstacleAvoidanceTimeRemaining = 0f;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        state = BrainState.FollowPlayer;
        movementUsesRunAnimation = true;
    }

    private void StartWhistleArrivalIdle()
    {
        state = BrainState.Idle;
        movementUsesRunAnimation = false;
        turnAnimationActive = false;
        whistleIdleTimeRemaining = Random.Range(whistleIdleDurationMin, whistleIdleDurationMax);
        PlayAnimation(idleBreathingState);
        FacePlayerWhileIdle();
    }

    private void FinishWhistleResponse()
    {
        whistleResponseActive = false;
        whistleIdleTimeRemaining = 0f;
        obstacleRepositionActive = false;
        obstacleAvoidanceDirection = Vector3.zero;
        obstacleAvoidanceTimeRemaining = 0f;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        EnterAmbientState(false);
    }

    private void FacePlayerWhileIdle()
    {
        Vector3 direction = GetPlayerPosition() - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            bodyTurnSpeed * Time.deltaTime);
    }

    private void UpdateObstacleReposition()
    {
        state = BrainState.Walk;
        movementUsesRunAnimation = false;
        wanderTarget = obstacleRepositionTarget;

        Vector3 direction = obstacleRepositionTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= obstacleRepositionStopDistance * obstacleRepositionStopDistance)
            return;

        if (!turnAnimationActive)
            PlayAnimation(walkState);

        TurnBodyToward(direction.normalized);
        MoveForward(walkSpeed);
    }

    private bool HasReachedObstacleRepositionTarget()
    {
        Vector3 offset = obstacleRepositionTarget - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= obstacleRepositionStopDistance * obstacleRepositionStopDistance;
    }

    private void FinishObstacleReposition()
    {
        obstacleRepositionActive = false;
        obstacleAvoidanceDirection = Vector3.zero;
        obstacleAvoidanceTimeRemaining = 0f;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        wanderTarget = transform.position;
    }

    private void LateUpdate()
    {
        if (playerInRange)
            UpdateHeadLook();
    }

    #endregion

    #region 참조 찾기

    private void ResolveReferences()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            playerCamera = Camera.main;

        if (playerTransform == null)
            playerTransform = ResolvePlayerTransform();

        if (headBone == null)
            headBone = FindBone("DEF-spine.011");

        if (neckBone == null)
            neckBone = FindBone("DEF-spine.010");
    }

    private Transform ResolvePlayerTransform()
    {
        try
        {
            GameObject taggedPlayer = GameObject.FindGameObjectWithTag(PlayerTag);
            if (taggedPlayer != null)
                return taggedPlayer.transform;
        }
        catch (UnityException)
        {
            // 프로젝트에 Player 태그가 없으면 이름 검색으로 넘어갑니다.
        }

        for (int i = 0; i < PlayerNames.Length; i++)
        {
            GameObject namedPlayer = GameObject.Find(PlayerNames[i]);
            if (namedPlayer != null)
                return namedPlayer.transform;
        }

        return playerCamera != null ? playerCamera.transform.root : null;
    }

    private Transform FindBone(string boneName)
    {
        Transform[] bones = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].name.Equals(boneName, System.StringComparison.OrdinalIgnoreCase))
                return bones[i];
        }

        return null;
    }

    #endregion

    #region 플레이어 인식과 추적

    private bool IsPlayerInRange()
    {
        Vector3 playerPosition = GetPlayerPosition();
        Vector3 horizontalOffset = playerPosition - transform.position;
        horizontalOffset.y = 0f;
        return horizontalOffset.sqrMagnitude <= playerDetectionDistance * playerDetectionDistance;
    }

    private Vector3 GetPlayerPosition()
    {
        if (playerCamera != null)
            return playerCamera.transform.position;

        return playerTransform != null ? playerTransform.position : transform.position;
    }

    private void UpdatePlayerFollow()
    {
        state = BrainState.FollowPlayer;
        Vector3 targetPosition = GetPlayerPosition();
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;
        if (distance <= 0.001f)
        {
            followUsesRunAnimation = false;
            followAnimationModeInitialized = true;
            movementUsesRunAnimation = false;
            turnAnimationActive = false;
            PlayAnimation(idleBreathingState);
            return;
        }

        if (distance <= playerStopDistance)
        {
            // 가만히 있는 애니메이션을 재생하는 동안에는 이동과 회전을 모두 하지 않습니다.
            followUsesRunAnimation = false;
            followAnimationModeInitialized = true;
            movementUsesRunAnimation = false;
            turnAnimationActive = false;
            PlayAnimation(idleBreathingState);
            return;
        }

        if (!followAnimationModeInitialized)
        {
            // 플레이어를 처음 인식한 순간에는 현재 거리로만 시작 상태를 정합니다.
            followUsesRunAnimation = playerRunDistance > playerStopDistance &&
                                     distance >= playerRunDistance;
            followAnimationModeInitialized = true;
        }
        else if (followUsesRunAnimation)
        {
            // 달리기 중에는 기준 거리보다 충분히 가까워졌을 때만 걷기로 내립니다.
            float runExitDistance = Mathf.Max(
                playerStopDistance,
                playerRunDistance - runWalkHysteresis);
            if (distance <= runExitDistance)
                followUsesRunAnimation = false;
        }
        else
        {
            // 걷기 중에는 기준 거리보다 충분히 멀어졌을 때만 다시 달리기로 올립니다.
            float runEnterDistance = playerRunDistance + runWalkHysteresis;
            if (distance >= runEnterDistance)
                followUsesRunAnimation = true;
        }

        bool shouldRun = followUsesRunAnimation;
        float speed = shouldRun ? runSpeed : walkSpeed;

        // 달리기에서 걷기로 바뀌는 순간 이전 달리기 방향전환을 계속 유지하면
        // 걷기 루프가 늦게 재생되거나 전환 애니메이션의 중간 자세에서 멈춘 것처럼 보일 수 있습니다.
        // 이동 모드가 바뀌면 이전 모드의 방향전환을 즉시 취소하고 새 루프를 시작합니다.
        if (turnAnimationActive && turnAnimationUsesRun != shouldRun)
        {
            turnAnimationActive = false;
            turnAnimationElapsed = 0f;
        }

        movementUsesRunAnimation = shouldRun;
        if (!turnAnimationActive)
            PlayAnimation(shouldRun ? runState : walkState);
        TurnBodyToward(direction.normalized);
        MoveForward(speed);
    }

    #endregion

    #region 평소 생활 상태

    private void UpdateAmbientBehavior()
    {
        stateTimeRemaining -= Time.deltaTime;

        if (state == BrainState.Idle)
        {
            // Idle Playing은 비루프 클립이 끝난 뒤 마지막 프레임에 머물 수 있으므로,
            // 재생이 끝나는 즉시 다음 생활 상태를 선택합니다.
            if (currentAnimation == idlePlayingState && IsCurrentAnimationFinished())
            {
                EnterAmbientState(false, true);
                return;
            }

            // Idle Breathing과 Idle Playing은 모두 완전 정지 상태입니다.
            movementUsesRunAnimation = false;
            turnAnimationActive = false;
            if (stateTimeRemaining <= 0f)
                EnterAmbientState(false);
            return;
        }

        Vector3 direction = wanderTarget - transform.position;
        direction.y = 0f;
        if (stateTimeRemaining <= 0f || direction.sqrMagnitude <= 0.25f)
        {
            EnterAmbientState(false);
            return;
        }

        movementUsesRunAnimation = state == BrainState.Run;
        TurnBodyToward(direction.normalized);
        MoveForward(state == BrainState.Run ? runSpeed : walkSpeed);
    }

    private void EnterAmbientState(bool immediate, bool preventIdlePlaying = false)
    {
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        obstacleIdleTimeRemaining = 0f;
        movementUsesRunAnimation = false;
        followUsesRunAnimation = false;
        followAnimationModeInitialized = false;
        float roll = Random.value;
        if (roll < ambientRunChance)
        {
            state = BrainState.Run;
            stateTimeRemaining = Random.Range(runDurationMin, runDurationMax);
            wanderTarget = PickWanderTarget();
            PlayAnimation(runState);
        }
        else if (roll < 0.58f)
        {
            state = BrainState.Walk;
            stateTimeRemaining = Random.Range(walkDurationMin, walkDurationMax);
            wanderTarget = PickWanderTarget();
            PlayAnimation(walkState);
        }
        else
        {
            state = BrainState.Idle;
            stateTimeRemaining = Random.Range(idleDurationMin, idleDurationMax);

            // Idle Playing은 플레이어 앞에 가까이 도착했을 때만 후보에 넣고,
            // 한 번 재생한 직후에는 같은 애니메이션을 연속으로 고르지 않습니다.
            bool canPlayIdlePlaying = !preventIdlePlaying &&
                                      IsPlayerFrontIdlePosition() &&
                                      Random.value >= 0.72f;
            PlayAnimation(canPlayIdlePlaying ? idlePlayingState : idleBreathingState);
        }

        if (immediate)
            stateTimeRemaining = Mathf.Max(0.5f, stateTimeRemaining);
    }

    private Vector3 PickWanderTarget()
    {
        Vector2 randomOffset = Random.insideUnitCircle * wanderRadius;
        return homePosition + new Vector3(randomOffset.x, 0f, randomOffset.y);
    }

    private bool IsPlayerFrontIdlePosition()
    {
        if (!playerInRange)
            return false;

        Vector3 playerToDog = transform.position - GetPlayerPosition();
        playerToDog.y = 0f;
        float maxDistance = playerStopDistance + 0.35f;
        if (playerToDog.sqrMagnitude <= 0.0001f ||
            playerToDog.sqrMagnitude > maxDistance * maxDistance)
            return false;

        Transform playerView = playerCamera != null ? playerCamera.transform : playerTransform;
        if (playerView == null)
            return false;

        Vector3 playerForward = playerView.forward;
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude <= 0.0001f)
            return false;

        return Vector3.Dot(playerForward.normalized, playerToDog.normalized) >= 0.2f;
    }

    #endregion

    #region 방향 전환과 이동

    private void TurnBodyToward(Vector3 direction)
    {
        if (obstacleAvoidanceTimeRemaining > 0f && obstacleAvoidanceDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 desiredDirection = direction;
            desiredDirection.y = 0f;
            if (desiredDirection.sqrMagnitude > 0.0001f)
            {
                desiredDirection.Normalize();
                // 플레이어 방향을 따라갈 수는 있지만, 벽 쪽으로 되돌아가는 방향은 차단합니다.
                if (Vector3.Dot(desiredDirection, obstacleAvoidanceDirection) < 0.15f)
                    direction = obstacleAvoidanceDirection;
            }
        }

        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.y = 0f;
        direction.Normalize();

        if (!turnAnimationActive)
        {
            float signedTurnAngle = Vector3.SignedAngle(transform.forward, direction, Vector3.up);
            float turnAngleThreshold = movementUsesRunAnimation ? runTurnAngleThreshold : walkTurnAngleThreshold;
            bool canUseWalkTurn = state == BrainState.Walk || state == BrainState.FollowPlayer;
            bool shouldPlayTurn = movementUsesRunAnimation || canUseWalkTurn;

            if (shouldPlayTurn && Mathf.Abs(signedTurnAngle) >= turnAngleThreshold)
                BeginDirectionTurn(direction, signedTurnAngle, movementUsesRunAnimation);
        }

        if (turnAnimationActive)
        {
            // 전환 애니메이션 초반에는 기존 진행 방향을 유지하고, 후반부부터 목표 방향으로 몸을 돌립니다.
            float duration = turnAnimationUsesRun ? runTurnAnimationDuration : walkTurnAnimationDuration;
            float rotationStartTime = duration * 0.35f;
            float rotationProgress = Mathf.InverseLerp(rotationStartTime, duration, turnAnimationElapsed);
            if (rotationProgress > 0f)
            {
                Quaternion turnTargetRotation = Quaternion.LookRotation(turnTargetDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(turnStartRotation, turnTargetRotation, rotationProgress);
            }

            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            bodyTurnSpeed * Time.deltaTime);
    }

    private void BeginDirectionTurn(Vector3 direction, float signedTurnAngle, bool useRunTurn)
    {
        turnAnimationActive = true;
        turnAnimationUsesRun = useRunTurn;
        turnAnimationElapsed = 0f;
        turnTargetDirection = direction.normalized;
        turnStartRotation = transform.rotation;

        if (useRunTurn)
            PlayAnimation(signedTurnAngle > 0f ? runLeanRightState : runLeanLeftState);
        else
            PlayAnimation(signedTurnAngle > 0f ? walkTurnRightState : walkTurnLeftState);
    }

    private void MoveForward(float speed)
    {
        if (turnAnimationActive)
        {
            float duration = turnAnimationUsesRun ? runTurnAnimationDuration : walkTurnAnimationDuration;
            if (turnAnimationElapsed < duration * 0.35f)
                return;
        }

        bool isEscapingObstacle = obstacleAvoidanceTimeRemaining > 0f &&
                                  obstacleAvoidanceDirection.sqrMagnitude > 0.0001f;
        float alignment = Vector3.Dot(transform.forward, GetPlanarDirectionToPlayerOrTarget());
        // 휘파람 추적 중에는 달리기 애니메이션만 재생되고 제자리에서 멈추지 않도록,
        // 몸이 목표 방향으로 전환된 뒤에는 정렬 감속을 적용하지 않습니다.
        float movementScale = isEscapingObstacle || whistleResponseActive
            ? 1f
            : Mathf.Clamp01((alignment + 0.2f) / 1.2f);
        float movementDistance = speed * movementScale * Time.deltaTime;
        if (movementDistance <= 0.0001f)
            return;

        // 이동 직전에 몸통 크기만큼 캡슐을 미리 검사하여 벽 안으로 들어가는 것을 막습니다.
        if (IsObstacleAhead(transform.forward, obstacleCheckDistance + movementDistance))
        {
            bool currentAvoidanceBlocked = obstacleAvoidanceDirection.sqrMagnitude > 0.0001f &&
                                            IsObstacleAhead(
                                                obstacleAvoidanceDirection,
                                                obstacleCheckDistance + obstacleCheckRadius);

            // 이미 탈출 방향을 확보했다면 같은 벽을 다시 만났다고 Idle을 반복하지 않고,
            // 현재 탈출 방향을 유지하면서 몸을 돌릴 시간을 줍니다.
            if (isEscapingObstacle && !currentAvoidanceBlocked)
                return;

            // 진행 중인 방향 전환보다 장애물 회피를 우선합니다.
            turnAnimationActive = false;
            turnAnimationElapsed = 0f;
            obstacleIdleTimeRemaining = obstaclePauseDuration;
            PlayAnimation(idleBreathingState);

            if (currentAvoidanceBlocked)
                preferRightAvoidance = !preferRightAvoidance;

            if (obstacleAvoidanceTimeRemaining <= 0f || currentAvoidanceBlocked ||
                obstacleAvoidanceDirection.sqrMagnitude < 0.0001f)
            {
                if (TryGetObstacleAvoidanceDirection(out Vector3 avoidanceDirection))
                {
                    obstacleAvoidanceDirection = avoidanceDirection;
                    obstacleAvoidanceTimeRemaining = obstacleAvoidanceDuration;
                    obstacleRepositionTarget = CreateObstacleRepositionTarget(avoidanceDirection);
                    obstacleRepositionActive = true;
                }
                else
                {
                    obstacleAvoidanceDirection = Vector3.zero;
                    obstacleAvoidanceTimeRemaining = 0f;
                    obstacleRepositionActive = false;
                }
            }

            return;
        }

        transform.position += transform.forward * movementDistance;
    }

    private bool IsObstacleAhead(Vector3 direction, float distance)
    {
        if (direction.sqrMagnitude < 0.0001f)
            return false;

        Vector3 origin = transform.position + Vector3.up * obstacleCheckHeight;
        return TryGetNearestExternalSphereCastHit(
            origin,
            obstacleCheckRadius,
            direction.normalized,
            distance,
            out _);
    }

    private bool TryGetNearestExternalSphereCastHit(
        Vector3 origin,
        float radius,
        Vector3 direction,
        float distance,
        out RaycastHit nearestHit)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            radius,
            direction,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        bool found = false;
        float nearestDistance = float.PositiveInfinity;
        nearestHit = default;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (IsOwnCollider(hitCollider) || hits[i].distance >= nearestDistance)
                continue;

            nearestDistance = hits[i].distance;
            nearestHit = hits[i];
            found = true;
        }

        return found;
    }

    private bool IsOwnCollider(Collider candidate)
    {
        if (candidate == null)
            return false;

        Transform candidateTransform = candidate.transform;
        return candidateTransform == transform || candidateTransform.IsChildOf(transform);
    }

    private bool TryGetObstacleAvoidanceDirection(out Vector3 avoidanceDirection)
    {
        Vector3 forward = transform.forward;
        Vector3 left = Quaternion.AngleAxis(-obstacleAvoidanceAngle, Vector3.up) * forward;
        Vector3 right = Quaternion.AngleAxis(obstacleAvoidanceAngle, Vector3.up) * forward;
        float checkDistance = obstacleCheckDistance + obstacleCheckRadius;

        bool leftBlocked = IsObstacleAhead(left, checkDistance);
        bool rightBlocked = IsObstacleAhead(right, checkDistance);

        if (!leftBlocked && !rightBlocked)
        {
            avoidanceDirection = preferRightAvoidance ? right : left;
            return true;
        }

        if (!leftBlocked)
        {
            preferRightAvoidance = false;
            avoidanceDirection = left;
            return true;
        }

        if (!rightBlocked)
        {
            preferRightAvoidance = true;
            avoidanceDirection = right;
            return true;
        }

        Vector3 backward = -forward;
        if (!IsObstacleAhead(backward, checkDistance))
        {
            avoidanceDirection = backward;
            return true;
        }

        avoidanceDirection = Vector3.zero;
        return false;
    }

    private Vector3 CreateObstacleRepositionTarget(Vector3 direction)
    {
        Vector3 normalizedDirection = direction.normalized;
        Vector3 origin = transform.position + Vector3.up * obstacleCheckHeight;
        float safeDistance = obstacleEscapeDistance;

        if (TryGetNearestExternalSphereCastHit(
            origin,
            obstacleCheckRadius,
            normalizedDirection,
            obstacleEscapeDistance,
            out RaycastHit hit))
        {
            safeDistance = Mathf.Max(
                obstacleRepositionStopDistance,
                hit.distance - obstacleCheckRadius - 0.1f);
        }

        return transform.position + normalizedDirection * safeDistance;
    }

    private Vector3 GetPlanarDirectionToPlayerOrTarget()
    {
        Vector3 target = state == BrainState.FollowPlayer ? GetPlayerPosition() : wanderTarget;
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
    }

    #endregion

    #region 고개 추적

    private void UpdateHeadLook()
    {
        if (headBone == null && neckBone == null)
            return;

        Vector3 targetPosition = GetPlayerPosition();
        Vector3 targetDirection = targetPosition - transform.position;
        targetDirection.y = 0f;
        if (targetDirection.sqrMagnitude < 0.0001f)
            return;

        targetDirection.Normalize();
        float signedYaw = Vector3.SignedAngle(transform.forward, targetDirection, Vector3.up);
        float limitedYaw = Mathf.Clamp(signedYaw, -headYawLimit, headYawLimit);
        Quaternion targetRotation = Quaternion.AngleAxis(limitedYaw, Vector3.up) * transform.rotation;
        float blend = 1f - Mathf.Exp(-headTurnSpeed * Time.deltaTime);

        if (neckBone != null)
            neckBone.rotation = Quaternion.Slerp(neckBone.rotation, targetRotation, blend * 0.55f);
        if (headBone != null)
            headBone.rotation = Quaternion.Slerp(headBone.rotation, targetRotation, blend);
    }

    #endregion

    #region 바닥 유지와 애니메이션

    private void KeepOnGround()
    {
        Ray ray = new Ray(transform.position + Vector3.up * 1.5f, Vector3.down);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            4f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        bool foundGround = false;
        float nearestDistance = float.PositiveInfinity;
        RaycastHit groundHit = default;

        for (int i = 0; i < hits.Length; i++)
        {
            if (IsOwnCollider(hits[i].collider) || hits[i].distance >= nearestDistance)
                continue;

            nearestDistance = hits[i].distance;
            groundHit = hits[i];
            foundGround = true;
        }

        if (foundGround)
        {
            Vector3 position = transform.position;
            position.y = groundHit.point.y + groundOffset;
            transform.position = position;
        }
    }

    private void PlayAnimation(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
            return;

        // 달리기 상태에서만 애니메이션 재생 속도를 높이고, 아이들/걷기 상태로 돌아오면 기본 속도로 복원합니다.
        animator.speed = IsRunAnimationState(stateName) ? runAnimationSpeed : 1f;

        if (currentAnimation == stateName && IsAnimatorInRequestedState(stateName))
            return;

        animator.CrossFadeInFixedTime(stateName, animationBlendTime, 0);
        currentAnimation = stateName;
    }

    private bool IsAnimatorInRequestedState(string stateName)
    {
        int stateHash = Animator.StringToHash(stateName);
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (currentState.shortNameHash == stateHash)
            return true;

        if (!animator.IsInTransition(0))
            return false;

        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        return nextState.shortNameHash == stateHash;
    }

    private bool IsCurrentAnimationFinished()
    {
        if (animator == null)
            return true;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        if (!stateInfo.IsName(idlePlayingState) &&
            stateInfo.shortNameHash != Animator.StringToHash(idlePlayingState))
            return false;

        return stateInfo.normalizedTime >= 0.98f;
    }

    private bool IsRunAnimationState(string stateName)
    {
        return stateName == runState ||
               stateName == runLeanRightState ||
               stateName == runLeanLeftState;
    }

    #endregion

    #region 디버그 표시

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, playerDetectionDistance);
        Gizmos.color = new Color(1f, 0.75f, 0.15f, 0.35f);
        Gizmos.DrawWireSphere(Application.isPlaying ? homePosition : transform.position, wanderRadius);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * obstacleCheckHeight, obstacleCheckRadius);
        Gizmos.DrawLine(
            transform.position + Vector3.up * obstacleCheckHeight,
            transform.position + Vector3.up * obstacleCheckHeight + transform.forward * obstacleCheckDistance);
    }

    #endregion
}
