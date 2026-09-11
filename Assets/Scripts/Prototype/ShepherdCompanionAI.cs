using UnityEngine;

/// <summary>
/// 시바견의 생활 행동, 공놀이, 자동 욕구 행동과 쓰다듬기 반응을 조정합니다.
/// 키보드 입력은 VR 손 상호작용을 연결하기 전의 테스트 경로로만 사용합니다.
/// </summary>
[RequireComponent(typeof(PetNeedsSystem))]
public sealed class ShepherdCompanionAI : MonoBehaviour
{
    #region 상태 정의

    private enum BrainState
    {
        Idle,
        Walk,
        Trot,
        Run,
        FollowPlayer
    }

    private enum IdleSequencePhase
    {
        None,
        Start,
        Loop,
        End
    }

    private enum ScheduledRoutinePhase
    {
        None,
        MovingToFood,
        AligningToFood,
        Eating,
        FinishingMeal,
        MovingToSleep,
        AligningToSleep,
        EnteringBed,
        LyingDown,
        FallingAsleep,
        Sleeping,
        WakingUp,
        StandingUp,
        LeavingBed
    }

    private enum FetchPhase
    {
        None,
        ApproachingHeldBall,
        WatchingHeldBall,
        ChasingBall,
        PickingUpBall,
        TurningToPlayerWithBall,
        ReturningBall,
        DroppingBall
    }

    private enum PettingPhase
    {
        None,
        Enjoying,
        SettlingRestPose,
        SettlingCurrentIdle,
        StandingUpAfterPetting,
        ReleasingToRandomBehavior
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
    [Tooltip("공이나 플레이어를 바라볼 때 고개를 위아래로 들 수 있는 최대 각도입니다.")]
    [SerializeField, Range(5f, 55f)] private float headPitchLimit = 38f;
    [Tooltip("고개가 목표를 향해 돌아가는 속도입니다. 값이 클수록 빠르게 바라봅니다.")]
    [SerializeField, Min(0f)] private float headTurnSpeed = 7f;
    [Tooltip("How long the dog keeps its last gaze after losing the look target.")]
    [SerializeField, Min(0f)] private float headLookReturnDelay = 0.12f;
    [Tooltip("How smoothly the head returns to the animation pose after losing the look target.")]
    [SerializeField, Min(0.1f)] private float headLookReturnSpeed = 3.2f;
    [Tooltip("들고 있는 공의 움직임을 머리가 따라가는 전용 반응 속도입니다.")]
    [SerializeField, Min(0f)] private float heldBallHeadTurnSpeed = 15f;
    [Tooltip("플레이어가 든 공을 볼 때 목과 머리가 사용할 수 있는 좌우 회전 범위입니다.")]
    [SerializeField, Range(30f, 85f)] private float heldBallHeadYawLimit = 68f;
    [Tooltip("플레이어가 든 공을 볼 때 목과 머리가 사용할 수 있는 상하 회전 범위입니다.")]
    [SerializeField, Range(20f, 70f)] private float heldBallHeadPitchLimit = 55f;
    [Tooltip("Idle 애니메이션의 고개 흔들림을 상쇄하고 실제 입 방향을 공에 고정하는 강도입니다.")]
    [SerializeField, Range(0.75f, 1f)] private float heldBallLookCorrectionStrength = 0.96f;
    [Tooltip("공 시선 보정을 목과 머리에 나누어 적용할 때 목이 담당하는 비율입니다.")]
    [SerializeField, Range(0.15f, 0.55f)] private float heldBallNeckCorrectionShare = 0.35f;
    [Tooltip("이 각도 이하는 몸을 돌리지 않고 머리와 시선만 사용합니다.")]
    [SerializeField, Range(0f, 45f)] private float smallLookAngle = 18f;
    [Tooltip("이 각도를 넘는 방향 변화가 유지될 때만 몸 회전을 준비합니다.")]
    [SerializeField, Range(20f, 120f)] private float largeLookAngle = 58f;
    [Tooltip("큰 방향 변화가 이 시간 이상 유지되어야 발 애니메이션과 함께 몸을 돌립니다.")]
    [SerializeField, Min(0.1f)] private float sustainedBodyTurnDelay = 0.65f;
    [Tooltip("비워 두면 씬에서 플레이어를 자동으로 찾습니다.")]
    [SerializeField] private Transform playerTransform;
    [Tooltip("비워 두면 Main Camera를 자동으로 사용합니다.")]
    [SerializeField] private Camera playerCamera;

    [Header("이동")]
    [Tooltip("셰퍼드가 걷는 속도입니다.")]
    [SerializeField, Min(0.05f)] private float walkSpeed = 0.9f;
    [Tooltip("셰퍼드가 달리는 속도입니다.")]
    [SerializeField, Min(0.05f)] private float runSpeed = 2.25f;
    [Tooltip("걷기와 달리기 사이의 중간 이동 속도입니다.")]
    [SerializeField, Min(0.05f)] private float trotSpeed = 1.45f;
    [Tooltip("플레이어가 이 거리보다 멀어지면 걷기 대신 트롯을 사용합니다.")]
    [SerializeField, Min(0f)] private float playerTrotDistance = 2.2f;
    [Tooltip("정지/걷기/달리기 속도가 바뀔 때 초당 가감속량입니다.")]
    [SerializeField, Min(0.1f)] private float locomotionAcceleration = 4f;
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

    [Header("안전 복구")]
    [SerializeField] private Vector3 mapRecoveryCenter = Vector3.zero;
    [SerializeField] private float minimumValidWorldY = -0.65f;
    [SerializeField, Min(1f)] private float maximumDistanceFromMapCenter = 7.25f;
    [SerializeField, Min(0.05f)] private float wallPenetrationGrace = 0.3f;
    [SerializeField, Min(1f)] private float unreachableBallRecoveryDelay = 4.5f;
    [SerializeField, Min(0.01f)] private float fetchProgressThreshold = 0.08f;

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

    [Header("수동 식사와 수면")]
    [Tooltip("비워 두면 씬의 Bowl_1 오브젝트를 자동으로 찾습니다.")]
    [SerializeField] private Transform foodBowl;
    [Tooltip("비워 두면 씬의 CT_Dog_bed 오브젝트를 자동으로 찾습니다.")]
    [SerializeField] private Transform sleepSpot;
    [SerializeField, Min(0.1f)] private float eatingDuration = 10f;
    [Tooltip("식사 자세에서 강아지 루트와 밥그릇 중앙 사이의 거리입니다.")]
    [SerializeField, Min(0.1f)] private float foodStoppingDistance = 0.4f;
    [SerializeField, Min(0.1f)] private float sleepingDuration = 30f;
    [Tooltip("침대 앞쪽 입구에서 바깥으로 떨어지는 거리입니다.")]
    [SerializeField, Min(0f)] private float bedEntranceClearance = 0.08f;
    [Tooltip("침대 뒤쪽에서 입구로 이동할 때 옆 벽을 피하는 여유 거리입니다.")]
    [SerializeField, Min(0.05f)] private float bedRouteClearance = 0.85f;
    [Tooltip("침대 렌더러의 아래쪽에서 위쪽 사이 중 매트리스 표면으로 사용할 높이 비율입니다.")]
    [SerializeField, Range(0f, 1f)] private float sleepSurfaceHeightRatio = 0.43f;
    [Tooltip("침대 기준 수면 위치를 미세 조정할 로컬 오프셋입니다.")]
    [SerializeField] private Vector3 sleepPoseLocalOffset = Vector3.zero;
    [Tooltip("앞쪽 입구에서 중앙 수면 위치까지 올라가는 시간입니다.")]
    [SerializeField, Min(0.1f)] private float bedEntryDuration = 0.65f;
    [Tooltip("잠에서 깬 뒤 침대 밖으로 내려오는 시간입니다.")]
    [SerializeField, Min(0.1f)] private float bedExitDuration = 0.65f;
    [SerializeField] private string eatLoopState = "Arm_Shiba|Eat_loop";
    [SerializeField] private string eatEndState = "Arm_Shiba|EatDrink_end";
    [SerializeField] private string sleepStartState = "Arm_Shiba|Lie_belly_sleep_start";
    [SerializeField] private string sleepLoopState = "Arm_Shiba|Lie_belly_sleep";
    [SerializeField] private string sleepEndState = "Arm_Shiba|Lie_belly_sleep_end";

    [Header("쓰다듬기")]
    [SerializeField, Range(1.5f, 6f)] private float pettingDuration = 3.4f;
    [SerializeField, Range(1, 5)] private int pettingHeadTiltCycles = 3;
    [SerializeField, Range(3f, 25f)] private float pettingHeadTiltAngle = 13f;
    [Tooltip("쓰다듬기 반응이 현재 전신 자세에서 차분한 자세로 녹아드는 시간입니다.")]
    [SerializeField, Range(0.2f, 0.8f)] private float pettingEntryBlendDuration = 0.4f;
    [Tooltip("쓰다듬기 종료 때 전신 애니메이션을 부드럽게 풀어 주는 시간입니다.")]
    [SerializeField, Range(0.2f, 0.8f)] private float pettingReleaseBlendDuration = 0.6f;
    [Tooltip("쓰다듬는 동안 플레이어를 따라보는 머리의 최대 좌우 각도입니다.")]
    [SerializeField, Range(20f, 65f)] private float pettingHeadLookYawLimit = 48f;
    [Tooltip("쓰다듬는 동안 플레이어를 따라보는 머리의 최대 위아래 각도입니다.")]
    [SerializeField, Range(10f, 50f)] private float pettingHeadLookPitchLimit = 32f;
    [Tooltip("열린 눈꺼풀 기준으로 감긴 눈 자세까지의 로컬 X 회전량입니다. 시바견 수면 애니메이션에서 측정한 값입니다.")]
    [SerializeField, Range(-80f, -20f)] private float pettingEyelidCloseOffset = -56.2f;
    [SerializeField, Range(1, 10)] private int minimumPettingHearts = 3;
    [SerializeField, Range(1, 10)] private int maximumPettingHearts = 6;

    [Header("공놀이")]
    [Tooltip("플레이어가 공을 든 동안 이 거리까지 다가온 뒤 공을 바라봅니다.")]
    [SerializeField, Min(0.5f)] private float heldBallWaitDistance = 1.45f;
    [Tooltip("던진 공에 이 거리까지 접근하면 공을 뭅니다.")]
    [SerializeField, Min(0.1f)] private float ballPickupDistance = 0.42f;
    [Tooltip("공을 무는 애니메이션을 시작하는 거리입니다. 실제 접촉 거리보다 크게 두면 달리기에서 점프 물기로 자연스럽게 이어집니다.")]
    [SerializeField, Min(0.1f)] private float ballBiteStartDistance = 0.82f;
    [Tooltip("물기 점프가 공에 가까워질 때 유지할 최소 전진 속도입니다.")]
    [SerializeField, Min(0.05f)] private float ballBiteLungeSpeed = 1.2f;
    [Tooltip("공이 벽이나 구석에 있을 때 강아지가 설 수 있는 안쪽 접근점 거리입니다.")]
    [SerializeField, Min(0.25f)] private float cornerBallApproachDistance = 0.52f;
    [Tooltip("구석 공을 입으로 가져올 수 있는 최대 거리입니다.")]
    [SerializeField, Min(0.3f)] private float cornerBallPickupReach = 0.78f;
    [Tooltip("공을 물고 돌아온 뒤 플레이어에게서 이 거리에서 멈춥니다.")]
    [SerializeField, Min(0.5f)] private float ballReturnDistance = 1.25f;
    [Tooltip("공을 무는 동작과 내려놓는 동작에 사용하는 짧은 대기 시간입니다.")]
    [SerializeField, Min(0.1f)] private float ballBiteDuration = 1.05f;
    [SerializeField, Range(0.05f, 0.95f)] private float ballBiteAttachRatio = 0.48f;
    [Tooltip("입 중심과 공 표면 사이가 이 거리 안에 들어와야 물기 보정을 시작합니다.")]
    [SerializeField, Min(0.01f)] private float ballMouthContactPadding = 0.16f;
    [Tooltip("접촉한 공을 입 중심으로 부드럽게 정렬하는 시간입니다.")]
    [SerializeField, Min(0.03f)] private float ballMouthCaptureDuration = 0.13f;
    [Tooltip("굴러가는 공을 향해 물기를 시작할 때 사용할 짧은 위치 예측 시간입니다.")]
    [SerializeField, Range(0f, 0.5f)] private float ballPickupPredictionTime = 0.28f;
    [Tooltip("공을 무는 점프 애니메이션의 재생 속도입니다.")]
    [SerializeField, Min(0.1f)] private float ballBiteAnimationSpeed = 1.4f;
    [Tooltip("공을 완전히 문 뒤 방향 전환을 시작하기 전에 자세를 안정시키는 시간입니다.")]
    [SerializeField, Min(0f)] private float ballPickupSettleDuration = 0.18f;
    [Tooltip("Seconds to return to a stable chase pose before another bite attempt after a miss.")]
    [SerializeField, Min(0f)] private float failedBallPickupRetryDelay = 0.25f;
    [Tooltip("공을 문 채 플레이어 쪽 회전을 끝낸 것으로 판단하는 각도입니다.")]
    [SerializeField, Min(0.1f)] private float ballDropDuration = 1.05f;
    [SerializeField, Range(0.05f, 0.95f)] private float ballDropReleaseRatio = 0.42f;
    [SerializeField] private string ballBiteState = "Arm_Shiba|Attack_Bite_IP";
    [SerializeField] private string ballDropState = "Arm_Shiba|EatDrink_end";
    [Tooltip("입에 문 공의 입뼈 기준 좌우/위아래/앞뒤 위치 보정입니다.")]
    [SerializeField] private Vector3 ballMouthOffset = new Vector3(0f, -0.09f, 0.13f);
    [Tooltip("강아지가 플레이어 손의 공을 알아볼 수 있는 최대 거리입니다.")]
    [SerializeField, Min(0.5f)] private float heldBallNoticeDistance = 7f;
    [Tooltip("강아지가 플레이어 손의 공을 볼 수 있는 전체 수평 시야각입니다.")]
    [SerializeField, Range(20f, 180f)] private float heldBallFieldOfView = 120f;

    [Header("애니메이션")]
    [Tooltip("비워 두면 자식 오브젝트에서 Animator를 자동으로 찾습니다.")]
    [SerializeField] private Animator animator;
    [Tooltip("숨을 쉬며 가만히 있는 애니메이터 상태 이름입니다.")]
    [SerializeField] private string idleBreathingState = "1 type_Idle Breathing";
    [Tooltip("가만히 놀고 있는 애니메이터 상태 이름입니다.")]
    [SerializeField] private string idlePlayingState = "1 type_Idle_Playing";
    [Tooltip("비워 두면 기존 Idle Breathing/Playing 방식을 사용합니다. 등록된 상태는 같은 상태를 연속 선택하지 않고 동일 확률로 한 사이클씩 재생합니다.")]
    [SerializeField] private string[] idleStates = new string[0];
    [Tooltip("시작-반복-마무리로 구성된 특수 Idle의 시작 상태입니다. 세 상태가 모두 지정되면 일반 Idle 한 개와 같은 확률로 선택됩니다.")]
    [SerializeField] private string idleSequenceStartState = "";
    [SerializeField] private string idleSequenceLoopState = "";
    [SerializeField] private string idleSequenceEndState = "";
    [Tooltip("Idle을 새로 고를 때 앉기 또는 엎드리기 휴식 시퀀스를 선택할 확률입니다.")]
    [SerializeField, Range(0f, 0.5f)] private float restIdleChance = 0.18f;
    [SerializeField] private string sittingStartState = "";
    [SerializeField] private string[] sittingLoopStates = new string[0];
    [SerializeField] private string sittingEndState = "";
    [SerializeField] private string lyingStartState = "";
    [SerializeField] private string[] lyingLoopStates = new string[0];
    [SerializeField] private string lyingEndState = "";
    [Tooltip("내려놓은 직후 완전히 재생한 다음 이동을 허용할 애니메이터 상태입니다. 비워 두면 바로 기본 Idle로 복귀합니다.")]
    [SerializeField] private string releaseState = "";
    [Tooltip("걷기 애니메이터 상태 이름입니다.")]
    [SerializeField] private string walkState = "1 type_Walk_Loop";
    [Tooltip("달리기 애니메이터 상태 이름입니다.")]
    [SerializeField] private string runState = "1 type_Run Loop";
    [Tooltip("걷기와 달리기 사이의 트롯 애니메이터 상태 이름입니다.")]
    [SerializeField] private string trotState = "Arm_Shiba|Trot_F_IP";
    [Tooltip("달리면서 오른쪽으로 방향을 바꿀 때 재생할 애니메이터 상태 이름입니다.")]
    [SerializeField] private string runLeanRightState = "1 type_Run Lean Right";
    [Tooltip("달리면서 왼쪽으로 방향을 바꿀 때 재생할 애니메이터 상태 이름입니다.")]
    [SerializeField] private string runLeanLeftState = "1 type_Run Lean Left";
    [Tooltip("걸으면서 오른쪽으로 방향을 바꿀 때 재생할 애니메이터 상태 이름입니다.")]
    [SerializeField] private string walkTurnRightState = "1 type_Walk_Turn_Right";
    [Tooltip("걸으면서 왼쪽으로 방향을 바꿀 때 재생할 애니메이터 상태 이름입니다.")]
    [SerializeField] private string walkTurnLeftState = "1 type_Walk_Turn_Left";
    [Tooltip("큰 방향 전환에 사용할 왼쪽 180도 회전 상태입니다.")]
    [SerializeField] private string turnLeft180State = "Arm_Shiba|Turn_L180_IP";
    [Tooltip("큰 방향 전환에 사용할 오른쪽 180도 회전 상태입니다.")]
    [SerializeField] private string turnRight180State = "Arm_Shiba|Turn_R180_IP";
    [SerializeField, Range(60f, 170f)] private float turn180AngleThreshold = 105f;
    [SerializeField, Min(0.1f)] private float turn180AnimationDuration = 1f;
    [Tooltip("애니메이션 상태가 바뀔 때 섞이는 시간입니다. 너무 짧으면 끊겨 보일 수 있습니다.")]
    [SerializeField, Min(0.01f)] private float animationBlendTime = 0.2f;
    [Tooltip("걷기와 달리기 사이에서 발 디딤 위상을 유지하며 섞는 시간입니다.")]
    [SerializeField, Min(0.05f)] private float locomotionBlendTime = 0.28f;
    [Tooltip("달리기 애니메이션의 재생 속도입니다. 캐릭터의 실제 이동 속도와 별개로, 다리 움직임이 느려 보일 때 올려주세요.")]
    [SerializeField, Min(0.1f)] private float runAnimationSpeed = 1.55f;
    [SerializeField, Min(0.1f)] private float trotAnimationSpeed = 1.35f;
    [Tooltip("걷기와 달리기 재생 속도가 바뀔 때 초당 보간 속도입니다.")]
    [SerializeField, Min(0.1f)] private float animationSpeedChangeRate = 3f;
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
    private Transform mouthBone;
    private Transform leftEyelidBone;
    private Transform rightEyelidBone;
    private Quaternion leftEyelidOpenRotation;
    private Quaternion rightEyelidOpenRotation;
    private bool eyelidOpenPoseCached;
    private Transform calibratedMuzzleHeadBone;
    private Vector3 headLocalMuzzleForward;
    private float smoothedHeadYaw;
    private float smoothedHeadPitch;
    private float headLookReturnElapsed;
    private bool headLookReturnActive;
    private float sustainedLargeLookTime;
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
    private bool movementUsesRunAnimation;
    private bool followUsesRunAnimation;
    private bool followUsesTrotAnimation;
    private bool followAnimationModeInitialized;
    private bool turnAnimationActive;
    private bool turnAnimationUsesRun;
    private bool turnAnimationUses180;
    private float turnAnimationElapsed;
    private Vector3 turnTargetDirection;
    private Quaternion turnStartRotation;
    private string turnAnimationResumeState;
    private float releaseSettleTimeRemaining;
    private bool releaseAnimationActive;
    private float targetAnimatorSpeed = 1f;
    private float currentMoveSpeed;
    private string currentIdleState;
    private int lastIdleChoice = -1;
    private IdleSequencePhase idleSequencePhase;
    private string activeIdleSequenceLoopState;
    private string activeIdleSequenceEndState;
    private bool activeIdleSequenceRequiresStandUp;
    private bool lastIdleWasRestPose;
    private float restIdleSuppressedUntilTime;
    private ScheduledRoutinePhase scheduledRoutinePhase;
    private Vector3 scheduledRoutineTarget;
    private Vector3 mealPosePosition;
    private Vector3 mealBedEntrancePosition;
    private Vector3 mealBedCornerPosition;
    private Vector3 sleepPosePosition;
    private Vector3 bedEntrancePosition;
    private Vector3 bedFrontCornerWaypoint;
    private Vector3 bedTransitionStartPosition;
    private Quaternion sleepPoseRotation;
    private Quaternion bedTransitionStartRotation;
    private Quaternion bedExitRotation;
    private float scheduledRoutineElapsed;
    private int mealApproachStage = -1;
    private int bedApproachStage = -1;
    private bool mealRequested;
    private bool sleepRequested;
    private bool routineInterruptedByGrab;
    private FetchPhase fetchPhase;
    private FetchBallInteraction fetchBall;
    private float fetchActionElapsed;
    private bool fetchBallAttachedDuringAction;
    private bool fetchBallCaptureAssisting;
    private float fetchBallCaptureElapsed;
    private bool fetchBallReleasedDuringAction;
    private float fetchPickupEntrySpeed;
    private float fetchPickupRetryTimeRemaining;
    private Vector3 fetchPickupLockedTarget;
    private Quaternion fetchPickupLockedRotation;
    private Vector3 fetchApproachTarget;
    private bool fetchUsesCornerApproach;
    private FetchBallInteraction pendingFetchBall;
    private bool pendingFetchIsThrown;
    private ShepherdVertexGrabInteraction grabInteraction;
    private PetNeedsSystem needsSystem;
    private PettingPhase pettingPhase;
    private float pettingElapsed;
    private float pettingExpressionElapsed;
    private float pettingFaceBlendWeight;
    private float pettingHeadReleaseElapsed;
    private bool pettingPreservesCurrentPose;
    private string pettingStartIdleState;
    private float pettingStartIdleNormalizedTime;
    private bool pettingStartIdleProgressCaptured;
    private int pettingInterruptedIdleChoice = -1;
    private bool automaticMealQueued;
    private bool automaticSleepQueued;
    private float invalidPositionElapsed;
    private float fetchNoProgressElapsed;
    private float bestFetchDistance = float.PositiveInfinity;

    public float Hunger => needsSystem != null ? needsSystem.Hunger : PetNeedsSystem.MaximumValue;
    public float Fatigue => needsSystem != null ? needsSystem.Fatigue : PetNeedsSystem.MaximumValue;
    public bool IsBeingPetted => pettingPhase != PettingPhase.None;

    public void ApplyPetSpaceContract(PetSpaceContractSnapshot snapshot, bool applySpawnPose)
    {
        if (snapshot.State != PetSpaceContractState.Valid)
            return;

        if (snapshot.HasWalkArea)
        {
            mapRecoveryCenter = snapshot.WalkArea.center;
            Vector2 planarExtents = new Vector2(
                Mathf.Max(0.5f, snapshot.WalkArea.extents.x),
                Mathf.Max(0.5f, snapshot.WalkArea.extents.z));
            float minimumHalfExtent = Mathf.Min(planarExtents.x, planarExtents.y);
            // The room contract is rectangular. A radius based on only the
            // shortest half-extent incorrectly classifies valid room corners as
            // out of bounds and teleports a returning dog to the center.
            maximumDistanceFromMapCenter = planarExtents.magnitude + obstacleCheckRadius;
            wanderRadius = Mathf.Max(0.25f, minimumHalfExtent * 0.85f);
        }
        else if (snapshot.HasRecoveryPosition)
        {
            mapRecoveryCenter = snapshot.RecoveryPosition.position;
        }

        if (snapshot.HasSpawnPosition)
        {
            homePosition = snapshot.SpawnPosition.position;
            if (applySpawnPose && (grabInteraction == null || !grabInteraction.IsHolding))
            {
                transform.SetPositionAndRotation(
                    snapshot.SpawnPosition.position,
                    snapshot.SpawnPosition.rotation);
            }
        }

        if (!snapshot.HasWalkArea && snapshot.HasDestinationPosition && snapshot.HasSpawnPosition)
        {
            float destinationDistance = Vector3.Distance(
                snapshot.SpawnPosition.position,
                snapshot.DestinationPosition.position);
            wanderRadius = Mathf.Max(0.5f, destinationDistance);
            maximumDistanceFromMapCenter = Mathf.Max(1f, destinationDistance * 1.5f);
        }
    }

    #endregion

    #region 생명주기

    public float GetGroundedRootY(float groundHeight)
    {
        return groundHeight + groundOffset;
    }

    public Vector3 GetBallCarryPoint()
    {
        ResolveReferences();
        Transform anchor = mouthBone != null ? mouthBone : headBone;
        Vector3 anchorPosition = anchor != null
            ? anchor.position
            : transform.position + Vector3.up * 0.45f + transform.forward * 0.45f;
        GetBallCarryFrame(out Vector3 muzzleRight, out Vector3 muzzleUp, out Vector3 muzzleForward);

        return anchorPosition +
               muzzleRight * ballMouthOffset.x +
               muzzleUp * ballMouthOffset.y +
               muzzleForward * ballMouthOffset.z;
    }

    public Quaternion GetBallCarryRotation()
    {
        ResolveReferences();
        GetBallCarryFrame(out _, out Vector3 muzzleUp, out Vector3 muzzleForward);
        return Quaternion.LookRotation(muzzleForward, muzzleUp);
    }

    private void EnsureMuzzleDirectionCalibrated()
    {
        if (headBone == null || calibratedMuzzleHeadBone == headBone)
            return;

        // The Shiba's mouth transform is a jaw pivot, not a muzzle-tip marker.
        // Store the visual forward direction in head-local space once, then let
        // animation-driven head rotation move that direction naturally.
        headLocalMuzzleForward = headBone.InverseTransformDirection(transform.forward).normalized;
        calibratedMuzzleHeadBone = headBone;
    }

    private Vector3 GetCurrentMuzzleForward()
    {
        EnsureMuzzleDirectionCalibrated();
        if (headBone == null || headLocalMuzzleForward.sqrMagnitude < 0.0001f)
            return transform.forward;

        return headBone.TransformDirection(headLocalMuzzleForward).normalized;
    }

    private void GetBallCarryFrame(
        out Vector3 muzzleRight,
        out Vector3 muzzleUp,
        out Vector3 muzzleForward)
    {
        muzzleForward = GetCurrentMuzzleForward();

        muzzleRight = Vector3.ProjectOnPlane(transform.right, muzzleForward).normalized;
        if (muzzleRight.sqrMagnitude < 0.0001f)
            muzzleRight = transform.right;
        // LookRotation이 기대하는 실제 위쪽 축입니다. 반대 순서로 외적하면
        // 아래쪽 축이 만들어져 음수 Y 보정이 공을 얼굴 위로 올리게 됩니다.
        muzzleUp = Vector3.Cross(muzzleForward, muzzleRight).normalized;
        if (Vector3.Dot(muzzleUp, transform.up) < 0f)
            muzzleUp = -muzzleUp;
    }

    public void BeginWatchingHeldBall(FetchBallInteraction ball)
    {
        if (ball == null)
            return;

        if (fetchBall == ball && fetchPhase != FetchPhase.None)
            return;

        pendingFetchBall = ball;
        pendingFetchIsThrown = false;
        TryStartPendingFetch();
    }

    public void BeginChasingThrownBall(FetchBallInteraction ball)
    {
        if (ball == null)
            return;

        if (fetchBall == ball && fetchPhase != FetchPhase.None)
            return;

        pendingFetchBall = ball;
        pendingFetchIsThrown = true;
        TryStartPendingFetch();
    }

    public bool WillPlayPostGrabCompletionAnimation()
    {
        bool interruptedMeal =
            scheduledRoutinePhase == ScheduledRoutinePhase.Eating ||
            scheduledRoutinePhase == ScheduledRoutinePhase.FinishingMeal;
        bool interruptedSleep =
            scheduledRoutinePhase >= ScheduledRoutinePhase.LyingDown &&
            scheduledRoutinePhase <= ScheduledRoutinePhase.StandingUp;
        if (interruptedMeal || interruptedSleep)
            return true;

        // 새 식사나 수면 예약을 바로 이어갈 때는 Put Down을 재생하지 않습니다.
        return !sleepRequested &&
               !mealRequested &&
               !string.IsNullOrEmpty(releaseState);
    }

    public void ResumeAfterGrab(float settleDuration)
    {
        ResolveReferences();
        bool interruptedMeal =
            scheduledRoutinePhase == ScheduledRoutinePhase.Eating ||
            scheduledRoutinePhase == ScheduledRoutinePhase.FinishingMeal;
        bool interruptedSleep =
            scheduledRoutinePhase >= ScheduledRoutinePhase.LyingDown &&
            scheduledRoutinePhase <= ScheduledRoutinePhase.StandingUp;

        scheduledRoutinePhase = ScheduledRoutinePhase.None;
        scheduledRoutineElapsed = 0f;
        routineInterruptedByGrab = interruptedMeal || interruptedSleep;
        if (routineInterruptedByGrab)
        {
            // 들기 전에 진행하던 예약을 다시 시작하지 않습니다. 내려놓은 자리에서
            // 해당 행동의 마무리 애니메이션만 재생한 뒤 평상시 Idle로 돌아갑니다.
            mealRequested = false;
            sleepRequested = false;
        }

        bool urgentRoutinePending = sleepRequested || mealRequested;
        releaseAnimationActive = !routineInterruptedByGrab &&
                                 !urgentRoutinePending &&
                                 !string.IsNullOrEmpty(releaseState);
        releaseSettleTimeRemaining = releaseAnimationActive
            ? 0f
            : urgentRoutinePending
                ? 0f
                : Mathf.Max(
                releaseSettleTimeRemaining,
                Mathf.Max(0f, settleDuration));
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        movementUsesRunAnimation = false;
        currentMoveSpeed = 0f;
        followAnimationModeInitialized = false;
        followUsesRunAnimation = false;
        followUsesTrotAnimation = false;
        currentAnimation = null;
        ResetActiveIdleSequence();
        targetAnimatorSpeed = 1f;

        if (animator != null)
        {
            animator.speed = 1f;
            if (interruptedMeal)
            {
                scheduledRoutinePhase = ScheduledRoutinePhase.FinishingMeal;
                PlayAnimation(eatEndState);
            }
            else if (interruptedSleep)
            {
                scheduledRoutinePhase = ScheduledRoutinePhase.WakingUp;
                PlayAnimation(sleepEndState);
            }
            else
            {
                PlayAnimation(releaseAnimationActive ? releaseState : idleBreathingState);
            }
        }
    }

    private void Awake()
    {
        homePosition = transform.position;
        ResolveReferences();
        grabInteraction = GetComponent<ShepherdVertexGrabInteraction>();
        needsSystem = GetComponent<PetNeedsSystem>();
        if (needsSystem == null)
            needsSystem = gameObject.AddComponent<PetNeedsSystem>();

        if (animator != null)
            animator.applyRootMotion = false;

        EnterAmbientState(true);
    }

    private void Update()
    {
        ResolveReferences();
        if (TryRecoverInvalidPosition())
            return;

        obstacleAvoidanceTimeRemaining = Mathf.Max(0f, obstacleAvoidanceTimeRemaining - Time.deltaTime);
        UpdateTurnAnimationProgress();
        playerInRange = IsPlayerInRange();
        UpdateAnimatorPlaybackSpeed();

        QueueAutomaticNeeds();
        if (UpdatePetting())
        {
            hadPlayerInRange = playerInRange;
            KeepOnGround();
            return;
        }

        if (TryStartQueuedNeedsRoutine())
        {
            hadPlayerInRange = playerInRange;
            KeepOnGround();
            return;
        }

        TryStartPendingFetch();

        if (UpdateBallPlay())
        {
            hadPlayerInRange = playerInRange;
            KeepOnGround();
            return;
        }

        if (UpdateScheduledRoutine())
        {
            hadPlayerInRange = playerInRange;
            KeepOnGround();
            return;
        }

        if (releaseAnimationActive)
        {
            turnAnimationActive = false;
            movementUsesRunAnimation = false;
            currentMoveSpeed = 0f;
            if (IsAnimationCycleFinished(releaseState))
            {
                releaseAnimationActive = false;
                restIdleSuppressedUntilTime = Time.time + 2.5f;
                currentAnimation = null;
                ResetActiveIdleSequence();
                UpdateStationaryIdleAnimation();
            }

            hadPlayerInRange = playerInRange;
            KeepOnGround();
            return;
        }

        if (releaseSettleTimeRemaining > 0f)
        {
            releaseSettleTimeRemaining = Mathf.Max(
                0f,
                releaseSettleTimeRemaining - Time.deltaTime);
            turnAnimationActive = false;
            currentMoveSpeed = 0f;
            PlayAnimation(idleBreathingState);
            hadPlayerInRange = playerInRange;
            KeepOnGround();
            return;
        }

        if (obstacleIdleTimeRemaining > 0f)
        {
            obstacleIdleTimeRemaining -= Time.deltaTime;
            turnAnimationActive = false;
            currentMoveSpeed = 0f;
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
        float duration = GetCurrentTurnAnimationDuration();
        if (turnAnimationElapsed < duration)
            return;

        if (turnTargetDirection.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(turnTargetDirection, Vector3.up);

        turnAnimationActive = false;
        turnAnimationUses180 = false;
        PlayAnimation(string.IsNullOrEmpty(turnAnimationResumeState)
            ? GetCurrentLocomotionState()
            : turnAnimationResumeState);
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
        if (pettingPhase != PettingPhase.None)
        {
            bool isEnjoying = pettingPhase == PettingPhase.Enjoying;
            float visualBlendDuration = isEnjoying
                ? Mathf.Max(0.25f, pettingEntryBlendDuration)
                : Mathf.Max(0.55f, pettingReleaseBlendDuration);
            pettingFaceBlendWeight = Mathf.MoveTowards(
                pettingFaceBlendWeight,
                isEnjoying ? 1f : 0f,
                Time.deltaTime / visualBlendDuration);

            if (isEnjoying)
            {
                // Animator가 만든 자세 위에 머리 추적을 천천히 겹칩니다.
                // 몸 전체를 즉시 돌리지 않아 손 아래에서 방향이 튀지 않습니다.
                headLookReturnActive = true;
                UpdateHeadLookAt(
                    GetPlayerPosition(),
                    Mathf.Max(35f, pettingHeadLookYawLimit),
                    Mathf.Max(24f, pettingHeadLookPitchLimit),
                    false);
            }
            else
            {
                // 표정과 같은 구간에 평소 머리 자세도 자연스럽게 복원합니다.
                headLookReturnActive = true;
                UpdateHeadLookReturn();
            }

            if (pettingFaceBlendWeight > 0.001f)
                ApplyPettingFacePose(pettingFaceBlendWeight);
            return;
        }

        bool trackingPlayerHeldBall = fetchBall != null &&
                                      fetchBall.IsHeldByPlayer &&
                                      (fetchPhase == FetchPhase.ApproachingHeldBall ||
                                       fetchPhase == FetchPhase.WatchingHeldBall);
        if (trackingPlayerHeldBall)
            UpdateHeadLookAt(
                fetchBall.WorldPosition,
                heldBallHeadYawLimit,
                heldBallHeadPitchLimit,
                true);
        else if (fetchPhase == FetchPhase.DroppingBall && fetchBall != null)
            UpdateHeadLookAt(fetchBall.WorldPosition);
        else if (playerInRange && CanLookAtPlayerWithoutFightingMovement())
            UpdateHeadLook();
        else
            UpdateHeadLookReturn();

        // Animator와 고개 보정이 모두 끝난 최종 입 위치를 사용합니다.
        // 공 쪽 LateUpdate에서 갱신하면 실행 순서에 따라 한 프레임 늦거나 옆으로 밀릴 수 있습니다.
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
            headBone = FindBone("DEF-spine.011") ?? FindBone("head");

        if (neckBone == null)
            neckBone = FindBone("DEF-spine.010") ?? FindBone("neck");

        if (mouthBone == null)
            mouthBone = FindBone("mouth") ?? FindBone("DEF-jaw") ?? FindBone("jaw");

        if (leftEyelidBone == null)
            leftEyelidBone = FindBone("eyelid.L");

        if (rightEyelidBone == null)
            rightEyelidBone = FindBone("eyelid.R");

        if (!eyelidOpenPoseCached && leftEyelidBone != null && rightEyelidBone != null)
        {
            leftEyelidOpenRotation = leftEyelidBone.localRotation;
            rightEyelidOpenRotation = rightEyelidBone.localRotation;
            eyelidOpenPoseCached = true;
        }

        EnsureMuzzleDirectionCalibrated();

        if (foodBowl == null)
        {
            GameObject bowlObject = GameObject.Find("Bowl_1");
            if (bowlObject != null)
                foodBowl = bowlObject.transform;
        }

        if (sleepSpot == null)
        {
            GameObject bedObject = GameObject.Find("CT_Dog_bed");
            if (bedObject != null)
                sleepSpot = bedObject.transform;
        }
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

    #region 쓰다듬기

    /// <summary>
    /// Starts the shared petting reaction. A future VR hand-contact script can call
    /// this method instead of duplicating the animation and heart behavior.
    /// </summary>
    public bool RequestPetting()
    {
        ResolveReferences();
        if (pettingPhase != PettingPhase.None ||
            (grabInteraction != null && grabInteraction.IsHolding) ||
            fetchPhase != FetchPhase.None ||
            scheduledRoutinePhase != ScheduledRoutinePhase.None ||
            mealRequested || sleepRequested ||
            releaseAnimationActive ||
            (needsSystem != null &&
             (needsSystem.RequiresImmediateMeal || needsSystem.RequiresSleep)))
            return false;

        CapturePettingIdleProgress();
        obstacleIdleTimeRemaining = 0f;
        obstacleRepositionActive = false;
        obstacleAvoidanceTimeRemaining = 0f;
        obstacleAvoidanceDirection = Vector3.zero;
        followUsesRunAnimation = false;
        followUsesTrotAnimation = false;
        followAnimationModeInitialized = false;
        pettingElapsed = 0f;
        // 현재 Idle 자세는 그대로 이어 가고, 걷기/달리기 중이었다면 차분한
        // 호흡 자세로 크로스페이드한 뒤 표정 반응을 겹칩니다.
        bool canPreserveCurrentIdlePose = animator != null &&
                                          !animator.IsInTransition(0) &&
                                          !string.IsNullOrEmpty(currentIdleState) &&
                                          currentAnimation == currentIdleState &&
                                          IsConfiguredIdleState(currentIdleState) &&
                                          IsAnimatorInRequestedState(currentIdleState);
        BeginPettingReaction(canPreserveCurrentIdlePose);

        return true;
    }

    public void NotifyGrabStarted()
    {
        ResolveReferences();
        needsSystem?.RegisterGrabbed();
        pettingPhase = PettingPhase.None;
        pettingElapsed = 0f;
        ResetPettingBlendState();
    }

    private bool UpdatePetting()
    {
        if (pettingPhase == PettingPhase.None)
            return false;

        if (pettingPhase == PettingPhase.SettlingRestPose)
        {
            currentMoveSpeed = 0f;
            movementUsesRunAnimation = false;
            turnAnimationActive = false;
            pettingHeadReleaseElapsed += Time.deltaTime;
            if (IsAnimationAtNaturalTransitionPoint(currentIdleState))
                BeginStandingUpAfterPetting();
            return true;
        }

        if (pettingPhase == PettingPhase.SettlingCurrentIdle)
        {
            currentMoveSpeed = 0f;
            movementUsesRunAnimation = false;
            turnAnimationActive = false;
            pettingElapsed += Time.deltaTime;
            pettingHeadReleaseElapsed += Time.deltaTime;

            float maximumSettleDuration = Mathf.Max(
                0.5f,
                GetAnimationClipLength(currentIdleState) + animationBlendTime);
            if (IsAnimationAtNaturalTransitionPoint(currentIdleState) ||
                pettingElapsed >= maximumSettleDuration)
            {
                StartCalmIdleAfterPetting();
            }
            return true;
        }

        if (pettingPhase == PettingPhase.StandingUpAfterPetting)
        {
            currentMoveSpeed = 0f;
            movementUsesRunAnimation = false;
            turnAnimationActive = false;
            pettingHeadReleaseElapsed += Time.deltaTime;
            if (IsAnimationCycleFinished(currentIdleState))
            {
                ResetActiveIdleSequence();
                StartCalmIdleAfterPetting();
            }
            return true;
        }

        if (pettingPhase == PettingPhase.ReleasingToRandomBehavior)
        {
            pettingHeadReleaseElapsed += Time.deltaTime;
            if (pettingHeadReleaseElapsed >= Mathf.Max(0.55f, pettingReleaseBlendDuration))
            {
                pettingPhase = PettingPhase.None;
                ResetPettingBlendState();
            }
            return true;
        }

        currentMoveSpeed = 0f;
        movementUsesRunAnimation = false;
        turnAnimationActive = false;
        pettingElapsed += Time.deltaTime;
        pettingExpressionElapsed += Time.deltaTime;
        if (pettingElapsed < Mathf.Max(0.1f, pettingDuration))
            return true;

        CompletePettingReaction();
        return true;
    }

    private void BeginPettingReaction(bool preserveCurrentPose)
    {
        pettingPhase = PettingPhase.Enjoying;
        pettingElapsed = 0f;
        pettingExpressionElapsed = 0f;
        pettingFaceBlendWeight = 0f;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        currentMoveSpeed = 0f;
        movementUsesRunAnimation = false;
        state = BrainState.Idle;
        pettingPreservesCurrentPose = preserveCurrentPose;
        if (!preserveCurrentPose)
        {
            ResetActiveIdleSequence();
            currentIdleState = idleBreathingState;
            PlayAnimationFromBeginning(
                idleBreathingState,
                Mathf.Max(0.3f, pettingEntryBlendDuration));
        }
        needsSystem?.RegisterPetting();
        PetHeartEffect.Play(
            headBone != null ? headBone : transform,
            playerCamera,
            minimumPettingHearts,
            Mathf.Max(minimumPettingHearts, maximumPettingHearts));
    }

    private void CapturePettingIdleProgress()
    {
        pettingStartIdleState = null;
        pettingStartIdleNormalizedTime = 0f;
        pettingStartIdleProgressCaptured = false;
        pettingInterruptedIdleChoice = GetIdleChoiceIndex(currentIdleState);

        if (animator == null || animator.IsInTransition(0) ||
            string.IsNullOrEmpty(currentIdleState) ||
            currentAnimation != currentIdleState ||
            !IsConfiguredIdleState(currentIdleState))
            return;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        int stateHash = Animator.StringToHash(currentIdleState);
        if (!stateInfo.IsName(currentIdleState) && stateInfo.shortNameHash != stateHash)
            return;

        pettingStartIdleState = currentIdleState;
        pettingStartIdleNormalizedTime = stateInfo.normalizedTime;
        pettingStartIdleProgressCaptured = true;
    }

    private bool HasCurrentIdlePassedTransitionDuringPetting()
    {
        if (!pettingStartIdleProgressCaptured || animator == null ||
            animator.IsInTransition(0) ||
            currentIdleState != pettingStartIdleState)
            return false;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        int stateHash = Animator.StringToHash(currentIdleState);
        if (!stateInfo.IsName(currentIdleState) && stateInfo.shortNameHash != stateHash)
            return false;

        if (!stateInfo.loop)
            return stateInfo.normalizedTime >= 0.98f;

        float startTime = Mathf.Max(0f, pettingStartIdleNormalizedTime);
        float startCycle = Mathf.Floor(startTime);
        float startPhase = Mathf.Repeat(startTime, 1f);
        float nextNaturalTransition = startCycle + (startPhase < 0.92f ? 0.92f : 1.92f);
        return stateInfo.normalizedTime >= nextNaturalTransition;
    }

    private void CompletePettingReaction()
    {
        pettingElapsed = 0f;
        pettingHeadReleaseElapsed = 0f;
        pettingPreservesCurrentPose = false;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        currentMoveSpeed = 0f;
        restIdleSuppressedUntilTime = Time.time + 2.5f;

        if (TryBeginNaturalRestExitAfterPetting())
            return;

        // 쓰다듬는 동안 이미 자연 종료 지점을 지났다면 다음 루프 끝을
        // 다시 기다리지 않아 같은 동작이 반복되는 느낌을 막습니다.
        if (CanFinishCurrentIdleNaturallyAfterPetting() &&
            !HasCurrentIdlePassedTransitionDuringPetting())
        {
            pettingPhase = PettingPhase.SettlingCurrentIdle;
            pettingElapsed = 0f;
            return;
        }

        StartCalmIdleAfterPetting();
    }

    private bool CanFinishCurrentIdleNaturallyAfterPetting()
    {
        return !string.IsNullOrEmpty(currentIdleState) &&
               currentIdleState != idleBreathingState &&
               IsConfiguredIdleState(currentIdleState) &&
               currentAnimation == currentIdleState &&
               IsAnimatorInRequestedState(currentIdleState);
    }

    private bool TryBeginNaturalRestExitAfterPetting()
    {
        if (!activeIdleSequenceRequiresStandUp ||
            idleSequencePhase == IdleSequencePhase.None)
            return false;

        if (idleSequencePhase != IdleSequencePhase.End &&
            HasCurrentIdlePassedTransitionDuringPetting())
        {
            BeginStandingUpAfterPetting();
            return true;
        }

        if (idleSequencePhase == IdleSequencePhase.End)
        {
            if (IsAnimationCycleFinished(currentIdleState))
            {
                ResetActiveIdleSequence();
                return false;
            }

            pettingPhase = PettingPhase.StandingUpAfterPetting;
            return true;
        }

        if (idleSequencePhase == IdleSequencePhase.Start)
        {
            if (string.IsNullOrEmpty(activeIdleSequenceLoopState))
            {
                BeginStandingUpAfterPetting();
                return true;
            }

            idleSequencePhase = IdleSequencePhase.Loop;
            currentIdleState = activeIdleSequenceLoopState;
            PlayAnimationFromBeginning(currentIdleState);
        }

        pettingPhase = PettingPhase.SettlingRestPose;
        return true;
    }

    private void BeginStandingUpAfterPetting()
    {
        if (string.IsNullOrEmpty(activeIdleSequenceEndState))
        {
            ResetActiveIdleSequence();
            StartCalmIdleAfterPetting();
            return;
        }

        idleSequencePhase = IdleSequencePhase.End;
        currentIdleState = activeIdleSequenceEndState;
        pettingPhase = PettingPhase.StandingUpAfterPetting;
        PlayAnimationFromBeginning(
            currentIdleState,
            Mathf.Max(0.55f, pettingReleaseBlendDuration));
    }

    private void StartCalmIdleAfterPetting()
    {
        state = BrainState.Idle;
        stateTimeRemaining = Random.Range(idleDurationMin, idleDurationMax);
        movementUsesRunAnimation = false;
        followUsesRunAnimation = false;
        followUsesTrotAnimation = false;
        followAnimationModeInitialized = true;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        currentMoveSpeed = 0f;
        ResetActiveIdleSequence();

        // 쓰다듬기 종료 직후 다른 랜덤 전신 포즈를 강제로 시작하면 현재 포즈와
        // 무관한 자세로 튀어 보입니다. 현재 애니메이션을 기준으로 기본 호흡
        // 자세에 긴 크로스페이드로 돌아가 끊김을 줄입니다.
        PlayAnimation(
            idleBreathingState,
            Mathf.Max(0.55f, pettingReleaseBlendDuration));
        currentIdleState = idleBreathingState;
        int calmIdleChoice = GetIdleChoiceIndex(idleBreathingState);
        if (calmIdleChoice >= 0)
            lastIdleChoice = calmIdleChoice;
        lastIdleWasRestPose = false;

        pettingPhase = PettingPhase.ReleasingToRandomBehavior;
        pettingElapsed = 0f;
    }

    private void ResetPettingBlendState()
    {
        pettingExpressionElapsed = 0f;
        pettingFaceBlendWeight = 0f;
        pettingHeadReleaseElapsed = 0f;
        pettingPreservesCurrentPose = false;
        pettingStartIdleState = null;
        pettingStartIdleNormalizedTime = 0f;
        pettingStartIdleProgressCaptured = false;
    }

    private void ApplyPettingFacePose(float visualBlendWeight)
    {
        float duration = Mathf.Max(0.1f, pettingDuration);
        float normalizedTime = Mathf.Clamp01(pettingExpressionElapsed / duration);
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.14f, normalizedTime));
        float poseWeight = Mathf.Clamp01(visualBlendWeight) * fadeIn;

        if (headBone != null)
        {
            float tilt = Mathf.Sin(
                normalizedTime * Mathf.Max(1, pettingHeadTiltCycles) *
                Mathf.PI * 2f) * pettingHeadTiltAngle * poseWeight;
            headBone.rotation = Quaternion.AngleAxis(tilt, transform.forward) * headBone.rotation;
        }

        if (!eyelidOpenPoseCached)
            return;

        Quaternion closeOffset = Quaternion.AngleAxis(pettingEyelidCloseOffset, Vector3.right);
        if (leftEyelidBone != null)
        {
            Quaternion closedRotation = leftEyelidOpenRotation * closeOffset;
            leftEyelidBone.localRotation = Quaternion.Slerp(
                leftEyelidBone.localRotation,
                closedRotation,
                poseWeight);
        }

        if (rightEyelidBone != null)
        {
            Quaternion closedRotation = rightEyelidOpenRotation * closeOffset;
            rightEyelidBone.localRotation = Quaternion.Slerp(
                rightEyelidBone.localRotation,
                closedRotation,
                poseWeight);
        }
    }

    #endregion

    #region 예약된 식사와 수면

    public void RequestMeal()
    {
        mealRequested = true;
    }

    public void RequestSleep()
    {
        sleepRequested = true;
    }

    private void QueueAutomaticNeeds()
    {
        if (needsSystem == null)
            return;

        automaticMealQueued = needsSystem.RequiresImmediateMeal;
        automaticSleepQueued = needsSystem.RequiresSleep;
    }

    private bool TryStartQueuedNeedsRoutine()
    {
        if (needsSystem == null ||
            scheduledRoutinePhase != ScheduledRoutinePhase.None ||
            mealRequested || sleepRequested ||
            fetchPhase != FetchPhase.None ||
            pettingPhase != PettingPhase.None ||
            releaseAnimationActive || releaseSettleTimeRemaining > 0f)
            return false;

        bool mealIsMandatory = automaticMealQueued && needsSystem.RequiresImmediateMeal;
        bool sleepIsMandatory = automaticSleepQueued && needsSystem.RequiresSleep;
        if (!mealIsMandatory && !sleepIsMandatory)
            return false;

        // 앉기/엎드리기 중에는 현재 루프의 끝을 기다린 뒤 종료 애니메이션으로
        // 일어납니다. 루프 중간에서 갑자기 자세가 섞이지 않게 합니다.
        if (activeIdleSequenceRequiresStandUp &&
            idleSequencePhase != IdleSequencePhase.None)
        {
            if (!IsAnimationAtNaturalTransitionPoint(currentAnimation))
                return false;
            if (FinishRestPoseBeforeMovement())
                return true;
        }

        // 평상시 애니메이션도 한 주기의 끝까지 보여 준 다음 이동을 시작합니다.
        if (animator != null &&
            !string.IsNullOrEmpty(currentAnimation) &&
            !IsAnimationAtNaturalTransitionPoint(currentAnimation))
            return false;

        // 배고픔 0과 피곤함 20 이하가 겹치면 식사를 먼저 완료합니다.
        if (mealIsMandatory)
        {
            automaticMealQueued = false;
            mealRequested = true;
            StartMealRoutine();
            return true;
        }

        automaticSleepQueued = false;
        sleepRequested = true;
        StartSleepRoutine();
        return true;
    }

    private bool TryStartOptionalMealAtAmbientBoundary()
    {
        if (needsSystem == null ||
            scheduledRoutinePhase != ScheduledRoutinePhase.None ||
            mealRequested || sleepRequested ||
            fetchPhase != FetchPhase.None ||
            pettingPhase != PettingPhase.None ||
            needsSystem.RequiresSleep ||
            automaticSleepQueued ||
            !needsSystem.ShouldChooseOptionalMeal(Random.value))
            return false;

        mealRequested = true;
        StartMealRoutine();
        return scheduledRoutinePhase != ScheduledRoutinePhase.None;
    }

    private bool StartHighestPriorityQueuedNeedsRoutineImmediately()
    {
        // 식사 재시도 및 배고픔 0이 수면보다 항상 우선입니다.
        if (automaticMealQueued)
        {
            automaticMealQueued = false;
            mealRequested = true;
            StartMealRoutine();
            if (scheduledRoutinePhase != ScheduledRoutinePhase.None)
                return true;

            automaticMealQueued = true;
            return false;
        }

        if (automaticSleepQueued)
        {
            automaticSleepQueued = false;
            sleepRequested = true;
            StartSleepRoutine();
            if (scheduledRoutinePhase != ScheduledRoutinePhase.None)
                return true;

            automaticSleepQueued = true;
        }

        return false;
    }

    private bool UpdateScheduledRoutine()
    {
        bool sleepRoutineActive = IsSleepRoutineActive();
        if (sleepRequested && !sleepRoutineActive)
            StartSleepRoutine();

        if (scheduledRoutinePhase == ScheduledRoutinePhase.None)
        {
            if (sleepRequested)
                StartSleepRoutine();
            else if (mealRequested)
                StartMealRoutine();
        }

        if (scheduledRoutinePhase == ScheduledRoutinePhase.None)
            return false;

        if (scheduledRoutinePhase == ScheduledRoutinePhase.MovingToFood ||
            scheduledRoutinePhase == ScheduledRoutinePhase.MovingToSleep)
        {
            UpdateScheduledRoutineMovement();
            return true;
        }

        if (scheduledRoutinePhase == ScheduledRoutinePhase.AligningToFood)
        {
            if (UpdateScheduledRoutineAlignment(foodBowl))
                BeginEating();
            return true;
        }

        if (scheduledRoutinePhase == ScheduledRoutinePhase.AligningToSleep)
        {
            if (UpdateScheduledRoutineAlignment(sleepSpot))
                BeginEnteringBed();
            return true;
        }

        if (scheduledRoutinePhase == ScheduledRoutinePhase.EnteringBed)
        {
            UpdateBedEntry();
            return true;
        }

        if (scheduledRoutinePhase == ScheduledRoutinePhase.LeavingBed)
        {
            UpdateBedExit();
            return true;
        }

        UpdateScheduledRoutineAnimation();
        return true;
    }

    private bool IsSleepRoutineActive()
    {
        return scheduledRoutinePhase >= ScheduledRoutinePhase.MovingToSleep;
    }

    private void StartMealRoutine()
    {
        ResolveReferences();
        if (foodBowl == null)
            return;

        CancelLowerPriorityActions();
        scheduledRoutinePhase = ScheduledRoutinePhase.MovingToFood;
        scheduledRoutineElapsed = 0f;
        bedApproachStage = -1;
        // 모델 자세에 따라 달라지는 렌더러 경계를 사용하지 않고, 사진에서 확인한
        // 고정 거리로 접근해야 매번 코가 음식 위의 같은 위치에 놓입니다.
        float mealPoseDistance = Mathf.Clamp(foodStoppingDistance, 0.1f, 0.4f);
        CreateMealRoutinePositions(mealPoseDistance, out scheduledRoutineTarget);
        wanderTarget = scheduledRoutineTarget;
        currentAnimation = null;
    }

    private void StartSleepRoutine()
    {
        ResolveReferences();
        if (sleepSpot == null)
            return;

        CancelLowerPriorityActions();
        scheduledRoutinePhase = ScheduledRoutinePhase.MovingToSleep;
        scheduledRoutineElapsed = 0f;
        mealApproachStage = -1;
        CreateBedRoutinePositions(
            out scheduledRoutineTarget,
            out sleepPosePosition,
            out sleepPoseRotation);
        wanderTarget = scheduledRoutineTarget;
        currentAnimation = null;
    }

    private void CancelLowerPriorityActions()
    {
        releaseAnimationActive = false;
        releaseSettleTimeRemaining = 0f;
        obstacleIdleTimeRemaining = 0f;
        obstacleRepositionActive = false;
        obstacleAvoidanceTimeRemaining = 0f;
        obstacleAvoidanceDirection = Vector3.zero;
        turnAnimationActive = false;
        turnAnimationUses180 = false;
        turnAnimationElapsed = 0f;
        movementUsesRunAnimation = false;
        followUsesRunAnimation = false;
        followUsesTrotAnimation = false;
        followAnimationModeInitialized = false;
        currentMoveSpeed = 0f;
        ResetActiveIdleSequence();
    }

    private void CreateMealRoutinePositions(float mealPoseDistance, out Vector3 firstTarget)
    {
        Vector3 awayFromBed = sleepSpot != null
            ? foodBowl.position - sleepSpot.position
            : transform.position - foodBowl.position;
        awayFromBed.y = 0f;
        if (awayFromBed.sqrMagnitude < 0.0001f)
            awayFromBed = Vector3.right;
        awayFromBed.Normalize();

        // 식사 자세는 현재 위치와 무관하게 항상 침대 반대편의 열린 면에 둡니다.
        mealPosePosition = foodBowl.position + awayFromBed * mealPoseDistance;
        mealPosePosition.y = transform.position.y;
        mealApproachStage = 2;
        firstTarget = mealPosePosition;

        if (sleepSpot == null)
            return;

        Bounds bedBounds;
        if (!TryGetRendererBounds(sleepSpot, out bedBounds))
            return;

        Vector3 bedRight = sleepSpot.right;
        Vector3 bedForward = sleepSpot.forward;
        bedRight.y = 0f;
        bedForward.y = 0f;
        bedRight = bedRight.sqrMagnitude > 0.0001f ? bedRight.normalized : Vector3.right;
        bedForward = bedForward.sqrMagnitude > 0.0001f ? bedForward.normalized : Vector3.forward;

        float rightExtent = GetBoundsExtentAlong(bedBounds, bedRight);
        float forwardExtent = GetBoundsExtentAlong(bedBounds, bedForward);
        float entranceClearance = Mathf.Clamp(bedEntranceClearance, 0.04f, 0.15f);
        float routeClearance = Mathf.Max(0.85f, bedRouteClearance);
        Vector3 entranceDirection = bedForward;
        Vector3 foodSideDirection = Vector3.Dot(foodBowl.position - bedBounds.center, bedRight) >= 0f
            ? bedRight
            : -bedRight;

        mealBedEntrancePosition = bedBounds.center +
                                  entranceDirection * (forwardExtent + entranceClearance);
        mealBedEntrancePosition.y = transform.position.y;
        mealBedCornerPosition = bedBounds.center +
                                foodSideDirection * (rightExtent + routeClearance) +
                                entranceDirection * (forwardExtent + entranceClearance);
        mealBedCornerPosition.y = transform.position.y;

        Vector3 fromBedCenter = transform.position - bedBounds.center;
        fromBedCenter.y = 0f;
        float rightProjection = Mathf.Abs(Vector3.Dot(fromBedCenter, bedRight));
        float forwardProjection = Mathf.Abs(Vector3.Dot(fromBedCenter, bedForward));
        bool insideBed = rightProjection <= rightExtent + 0.12f &&
                         forwardProjection <= forwardExtent + 0.12f;
        bool nearBed = rightProjection <= rightExtent + 1.1f &&
                       forwardProjection <= forwardExtent + 1.1f;
        float openFoodSideProjection = Vector3.Dot(
            transform.position - foodBowl.position,
            awayFromBed);

        // 이미 그릇의 열린 면에 있다면 침대 우회 경로를 탈 필요가 없습니다.
        if (openFoodSideProjection >= 0f)
            return;

        if (insideBed)
        {
            // 침대 안에서는 반드시 낮은 입구까지 먼저 나온 뒤 옆으로 빠집니다.
            mealApproachStage = 0;
            firstTarget = mealBedEntrancePosition;
        }
        else if (nearBed)
        {
            // 이미 입구 밖에 있다면 침대 앞을 따라 그릇 쪽 모서리로 이동합니다.
            mealApproachStage = 1;
            firstTarget = mealBedCornerPosition;
        }
    }

    private void CreateBedRoutinePositions(
        out Vector3 approachPosition,
        out Vector3 posePosition,
        out Quaternion poseRotation)
    {
        Bounds bedBounds;
        if (!TryGetRendererBounds(sleepSpot, out bedBounds))
            bedBounds = new Bounds(sleepSpot.position, new Vector3(1f, 0.4f, 1f));

        Vector3 bedRight = sleepSpot.right;
        Vector3 bedForward = sleepSpot.forward;
        bedRight.y = 0f;
        bedForward.y = 0f;
        bedRight = bedRight.sqrMagnitude > 0.0001f ? bedRight.normalized : Vector3.right;
        bedForward = bedForward.sqrMagnitude > 0.0001f ? bedForward.normalized : Vector3.forward;

        float rightExtent = GetBoundsExtentAlong(bedBounds, bedRight);
        float forwardExtent = GetBoundsExtentAlong(bedBounds, bedForward);
        Vector3 entranceDirection = bedForward;
        float entranceClearance = Mathf.Clamp(bedEntranceClearance, 0.04f, 0.15f);
        // 침대 오른쪽의 밥그릇과 장애물 검사 반경까지 합친 폭보다 바깥으로 돕니다.
        // 이전 씬 값(0.22)이 남아 있어도 안전 폭 아래로 줄어들지 않습니다.
        float routeClearance = Mathf.Max(0.85f, bedRouteClearance);

        // 사진에서 확인한 낮은 앞쪽 벽(+로컬 Z)을 유일한 침대 입구로 사용합니다.
        bedEntrancePosition = bedBounds.center +
                              entranceDirection * (forwardExtent + entranceClearance);
        bedEntrancePosition.y = transform.position.y;
        approachPosition = bedEntrancePosition;

        Vector3 fromCenter = transform.position - bedBounds.center;
        fromCenter.y = 0f;
        float entranceSideProjection = Vector3.Dot(fromCenter, entranceDirection);
        if (entranceSideProjection >= forwardExtent + entranceClearance)
        {
            // 이미 입구 쪽에 있으면 우회하지 않고 입구 중앙으로 바로 갑니다.
            bedApproachStage = 1;
        }
        else
        {
            // 침대 뒤나 옆에 있으면 가까운 앞쪽 모서리로 바로 이동합니다.
            // 오른쪽에 배치된 밥그릇 쪽에서 출발해도 침대 뒤로 되돌아가지 않습니다.
            Vector3 sideDirection = Vector3.Dot(fromCenter, bedRight) >= 0f
                ? bedRight
                : -bedRight;
            bedFrontCornerWaypoint = bedBounds.center +
                                     sideDirection * (rightExtent + routeClearance) +
                                     entranceDirection * (forwardExtent + entranceClearance);
            bedFrontCornerWaypoint.y = transform.position.y;
            approachPosition = bedFrontCornerWaypoint;
            bedApproachStage = 0;
        }

        posePosition = bedBounds.center + sleepSpot.TransformVector(sleepPoseLocalOffset);
        posePosition.y = Mathf.Lerp(
                             bedBounds.min.y,
                             bedBounds.max.y,
                             sleepSurfaceHeightRatio) +
                         groundOffset + sleepPoseLocalOffset.y;
        // 입구에서는 정면으로 올라간 뒤 침대 안에서 긴 축 방향으로 짧게 회전해 눕습니다.
        Vector3 poseForward = rightExtent >= forwardExtent ? -bedRight : bedForward;
        poseRotation = Quaternion.LookRotation(poseForward, Vector3.up);
    }

    private static bool TryGetRendererBounds(Transform target, out Bounds combinedBounds)
    {
        combinedBounds = default;
        if (target == null)
            return false;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer candidate = renderers[i];
            if (candidate == null || !candidate.enabled)
                continue;

            if (!found)
            {
                combinedBounds = candidate.bounds;
                found = true;
            }
            else
            {
                combinedBounds.Encapsulate(candidate.bounds);
            }
        }

        return found;
    }

    private static float GetBoundsExtentAlong(Bounds bounds, Vector3 direction)
    {
        Vector3 extents = bounds.extents;
        return Mathf.Abs(direction.x) * extents.x +
               Mathf.Abs(direction.y) * extents.y +
               Mathf.Abs(direction.z) * extents.z;
    }

    private void UpdateScheduledRoutineMovement()
    {
        // 일반 배회용 장애물 재배치가 예약 경로를 덮어쓰면 침대 주변을 계속 도는
        // 상태가 될 수 있습니다. 식사/수면은 별도의 안전 경유점을 사용하므로 해제합니다.
        obstacleIdleTimeRemaining = 0f;
        obstacleRepositionActive = false;
        obstacleAvoidanceTimeRemaining = 0f;
        obstacleAvoidanceDirection = Vector3.zero;

        Vector3 direction = scheduledRoutineTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0025f)
        {
            currentMoveSpeed = 0f;
            turnAnimationActive = false;
            if (scheduledRoutinePhase == ScheduledRoutinePhase.MovingToSleep &&
                AdvanceBedApproachRoute())
            {
                PlayAnimation(idleBreathingState);
                return;
            }

            if (scheduledRoutinePhase == ScheduledRoutinePhase.MovingToFood &&
                AdvanceMealApproachRoute())
            {
                PlayAnimation(idleBreathingState);
                return;
            }

            scheduledRoutinePhase = scheduledRoutinePhase == ScheduledRoutinePhase.MovingToSleep
                ? ScheduledRoutinePhase.AligningToSleep
                : ScheduledRoutinePhase.AligningToFood;
            PlayAnimation(idleBreathingState);
            return;
        }

        state = BrainState.Trot;
        movementUsesRunAnimation = false;
        wanderTarget = scheduledRoutineTarget;
        if (!turnAnimationActive)
            PlayAnimation(trotState);
        TurnBodyToward(direction.normalized);
        MoveScheduledRoutineForward(trotSpeed, direction.normalized);
    }

    private void MoveScheduledRoutineForward(float speed, Vector3 targetDirection)
    {
        if (turnAnimationActive)
        {
            float duration = GetCurrentTurnAnimationDuration();
            float movementStartRatio = turnAnimationUses180 ? 0.92f : 0.35f;
            if (turnAnimationElapsed < duration * movementStartRatio)
                return;
        }

        float alignment = Vector3.Dot(transform.forward, targetDirection);
        float movementScale = Mathf.Clamp01((alignment + 0.2f) / 1.2f);
        currentMoveSpeed = Mathf.MoveTowards(
            currentMoveSpeed,
            speed,
            locomotionAcceleration * Time.deltaTime);
        float movementDistance = currentMoveSpeed * movementScale * Time.deltaTime;
        if (movementDistance > 0.0001f)
            transform.position += transform.forward * movementDistance;
    }

    private bool AdvanceBedApproachRoute()
    {
        if (bedApproachStage == 0)
        {
            bedApproachStage = 1;
            scheduledRoutineTarget = bedEntrancePosition;
            wanderTarget = scheduledRoutineTarget;
            return true;
        }

        return false;
    }

    private bool AdvanceMealApproachRoute()
    {
        if (mealApproachStage == 0)
        {
            mealApproachStage = 1;
            scheduledRoutineTarget = mealBedCornerPosition;
            wanderTarget = scheduledRoutineTarget;
            return true;
        }

        if (mealApproachStage == 1)
        {
            mealApproachStage = 2;
            scheduledRoutineTarget = mealPosePosition;
            wanderTarget = scheduledRoutineTarget;
            return true;
        }

        return false;
    }

    private bool UpdateScheduledRoutineAlignment(Transform target)
    {
        currentMoveSpeed = 0f;
        movementUsesRunAnimation = false;
        state = BrainState.Walk;
        if (target == null)
            return true;

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return true;

        float angle = Mathf.Abs(Vector3.SignedAngle(
            transform.forward,
            direction.normalized,
            Vector3.up));
        if (!turnAnimationActive && angle <= 6f)
            return true;

        if (!turnAnimationActive)
            PlayAnimation(idleBreathingState);
        TurnBodyToward(direction.normalized);
        return false;
    }

    private void BeginEating()
    {
        scheduledRoutinePhase = ScheduledRoutinePhase.Eating;
        scheduledRoutineElapsed = 0f;
        currentMoveSpeed = 0f;
        SnapToMealPose();
        PlayAnimation(eatLoopState);
    }

    private void BeginEnteringBed()
    {
        scheduledRoutinePhase = ScheduledRoutinePhase.EnteringBed;
        scheduledRoutineElapsed = 0f;
        currentMoveSpeed = 0f;
        bedTransitionStartPosition = transform.position;
        bedTransitionStartRotation = transform.rotation;
        PlayAnimation(walkState);
    }

    private void UpdateBedEntry()
    {
        scheduledRoutineElapsed += Time.deltaTime;
        float entryDuration = Mathf.Clamp(bedEntryDuration, 0.1f, 0.7f);
        float t = Mathf.Clamp01(scheduledRoutineElapsed / entryDuration);
        float smoothT = t * t * (3f - 2f * t);
        transform.position = Vector3.Lerp(bedTransitionStartPosition, sleepPosePosition, smoothT);
        transform.rotation = Quaternion.Slerp(bedTransitionStartRotation, sleepPoseRotation, smoothT);
        currentMoveSpeed = walkSpeed;

        if (t < 1f)
            return;

        transform.SetPositionAndRotation(sleepPosePosition, sleepPoseRotation);
        BeginLyingDownForSleep();
    }

    private void BeginLyingDownForSleep()
    {
        scheduledRoutinePhase = ScheduledRoutinePhase.LyingDown;
        scheduledRoutineElapsed = 0f;
        currentMoveSpeed = 0f;
        PlayAnimation(lyingStartState);
    }

    private void BeginLeavingBed()
    {
        scheduledRoutinePhase = ScheduledRoutinePhase.LeavingBed;
        scheduledRoutineElapsed = 0f;
        bedTransitionStartPosition = transform.position;
        bedTransitionStartRotation = transform.rotation;
        Vector3 exitDirection = scheduledRoutineTarget - sleepPosePosition;
        exitDirection.y = 0f;
        bedExitRotation = exitDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(exitDirection.normalized, Vector3.up)
            : transform.rotation;
        PlayAnimation(walkState);
    }

    private void UpdateBedExit()
    {
        scheduledRoutineElapsed += Time.deltaTime;
        float exitDuration = Mathf.Clamp(bedExitDuration, 0.1f, 0.7f);
        float t = Mathf.Clamp01(scheduledRoutineElapsed / exitDuration);
        float smoothT = t * t * (3f - 2f * t);
        transform.position = Vector3.Lerp(bedTransitionStartPosition, scheduledRoutineTarget, smoothT);
        transform.rotation = Quaternion.Slerp(bedTransitionStartRotation, bedExitRotation, smoothT);
        currentMoveSpeed = walkSpeed;

        if (t >= 1f)
            CompleteSleepRoutine();
    }

    private void SnapToMealPose()
    {
        Vector3 position = transform.position;
        position.x = scheduledRoutineTarget.x;
        position.z = scheduledRoutineTarget.z;
        transform.position = position;

        if (foodBowl == null)
            return;

        Vector3 direction = foodBowl.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void MaintainRoutinePose()
    {
        if (routineInterruptedByGrab)
            return;

        if (scheduledRoutinePhase == ScheduledRoutinePhase.Eating ||
            scheduledRoutinePhase == ScheduledRoutinePhase.FinishingMeal)
        {
            SnapToMealPose();
            return;
        }

        if (scheduledRoutinePhase >= ScheduledRoutinePhase.LyingDown &&
            scheduledRoutinePhase <= ScheduledRoutinePhase.StandingUp)
        {
            transform.SetPositionAndRotation(sleepPosePosition, sleepPoseRotation);
        }
    }

    private void UpdateScheduledRoutineAnimation()
    {
        currentMoveSpeed = 0f;
        movementUsesRunAnimation = false;
        MaintainRoutinePose();

        switch (scheduledRoutinePhase)
        {
            case ScheduledRoutinePhase.Eating:
                scheduledRoutineElapsed += Time.deltaTime;
                if (scheduledRoutineElapsed >= Mathf.Max(0.1f, eatingDuration))
                {
                    scheduledRoutinePhase = ScheduledRoutinePhase.FinishingMeal;
                    PlayAnimation(eatEndState);
                }
                break;

            case ScheduledRoutinePhase.FinishingMeal:
                if (IsAnimationCycleFinished(eatEndState))
                    CompleteMealRoutine();
                break;

            case ScheduledRoutinePhase.LyingDown:
                if (IsAnimationCycleFinished(lyingStartState))
                {
                    scheduledRoutinePhase = ScheduledRoutinePhase.FallingAsleep;
                    PlayAnimation(sleepStartState);
                }
                break;

            case ScheduledRoutinePhase.FallingAsleep:
                if (IsAnimationCycleFinished(sleepStartState))
                {
                    scheduledRoutinePhase = ScheduledRoutinePhase.Sleeping;
                    scheduledRoutineElapsed = 0f;
                    PlayAnimation(sleepLoopState);
                }
                break;

            case ScheduledRoutinePhase.Sleeping:
                scheduledRoutineElapsed += Time.deltaTime;
                if (scheduledRoutineElapsed >= Mathf.Max(0.1f, sleepingDuration))
                {
                    scheduledRoutinePhase = ScheduledRoutinePhase.WakingUp;
                    PlayAnimation(sleepEndState);
                }
                break;

            case ScheduledRoutinePhase.WakingUp:
                if (IsAnimationCycleFinished(sleepEndState))
                {
                    scheduledRoutinePhase = ScheduledRoutinePhase.StandingUp;
                    PlayAnimation(lyingEndState);
                }
                break;

            case ScheduledRoutinePhase.StandingUp:
                if (IsAnimationCycleFinished(lyingEndState))
                {
                    if (routineInterruptedByGrab)
                        CompleteSleepRoutine();
                    else
                        BeginLeavingBed();
                }
                break;
        }
    }

    private void CompleteMealRoutine()
    {
        bool completedNaturally = !routineInterruptedByGrab;
        needsSystem?.RegisterMealFinished(completedNaturally);
        routineInterruptedByGrab = false;
        mealRequested = false;
        scheduledRoutinePhase = ScheduledRoutinePhase.None;
        scheduledRoutineElapsed = 0f;
        currentAnimation = null;
        QueueAutomaticNeeds();
        // 들어 올려져 취소된 식사는 수치와 무관하게 내려놓은 뒤 다시 시도합니다.
        if (!completedNaturally)
            automaticMealQueued = true;

        if (StartHighestPriorityQueuedNeedsRoutineImmediately())
            return;

        if (sleepRequested || mealRequested)
            PlayAnimation(idleBreathingState);
        else
            EnterAmbientState(false);
    }

    private void CompleteSleepRoutine()
    {
        bool completedNaturally = !routineInterruptedByGrab;
        needsSystem?.RegisterSleepFinished(completedNaturally);
        routineInterruptedByGrab = false;
        sleepRequested = false;
        scheduledRoutinePhase = ScheduledRoutinePhase.None;
        scheduledRoutineElapsed = 0f;
        currentAnimation = null;
        QueueAutomaticNeeds();
        if (StartHighestPriorityQueuedNeedsRoutineImmediately())
            return;

        if (mealRequested || sleepRequested)
            PlayAnimation(idleBreathingState);
        else
            EnterAmbientState(false);
    }

    #endregion

    #region 공놀이

    private bool IsFetchDeferredByRoutine()
    {
        return scheduledRoutinePhase == ScheduledRoutinePhase.Eating ||
               scheduledRoutinePhase == ScheduledRoutinePhase.FinishingMeal ||
               automaticMealQueued ||
               automaticSleepQueued ||
               pettingPhase != PettingPhase.None ||
               (scheduledRoutinePhase >= ScheduledRoutinePhase.EnteringBed &&
                scheduledRoutinePhase <= ScheduledRoutinePhase.LeavingBed);
    }

    private void TryStartPendingFetch()
    {
        if (pendingFetchBall == null ||
            fetchPhase != FetchPhase.None ||
            IsFetchDeferredByRoutine())
            return;

        FetchBallInteraction ball = pendingFetchBall;
        if (ball.IsHeldByPlayer)
        {
            if (!CanSeeHeldBall(ball))
                return;

            pendingFetchBall = null;
            pendingFetchIsThrown = false;
            BeginBallPlay(ball, FetchPhase.ApproachingHeldBall);
            return;
        }

        if (pendingFetchIsThrown && ball.WasThrown && !ball.IsCarriedByDog)
        {
            pendingFetchBall = null;
            pendingFetchIsThrown = false;
            BeginBallPlay(ball, FetchPhase.ChasingBall);
            return;
        }

        pendingFetchBall = null;
        pendingFetchIsThrown = false;
    }

    private bool CanSeeHeldBall(FetchBallInteraction ball)
    {
        if (ball == null || !ball.IsHeldByPlayer)
            return false;

        ResolveReferences();
        Vector3 sightOrigin = headBone != null
            ? headBone.position
            : transform.position + Vector3.up * 0.45f;
        Vector3 toBall = ball.WorldPosition - sightOrigin;
        float distance = toBall.magnitude;
        if (distance < 0.001f || distance > heldBallNoticeDistance)
            return false;

        Vector3 planarDirection = Vector3.ProjectOnPlane(toBall, Vector3.up);
        if (planarDirection.sqrMagnitude > 0.0001f &&
            Vector3.Angle(transform.forward, planarDirection) > heldBallFieldOfView * 0.5f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            sightOrigin,
            toBall / distance,
            distance + 0.08f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (ball.OwnsCollider(candidate))
                return true;
            if (IsOwnCollider(candidate) || IsPlayerCollider(candidate))
                continue;
            return false;
        }

        // 작은 공의 콜라이더를 레이가 스치더라도, 중간에 가리는 물체가 없으면 보인 것으로 처리합니다.
        return true;
    }

    private bool IsPlayerCollider(Collider candidate)
    {
        if (candidate == null || playerTransform == null)
            return false;

        Transform candidateTransform = candidate.transform;
        return candidateTransform == playerTransform || candidateTransform.IsChildOf(playerTransform);
    }

    private void BeginBallPlay(FetchBallInteraction ball, FetchPhase phase)
    {
        ResolveReferences();

        // 일반 생활 Idle에서 앉거나 엎드려 있던 경우에는 공놀이 우선순위가
        // 올라가는 즉시 일어서기 애니메이션으로 전환합니다. 식사/수면 루틴은
        // TryStartPendingFetch에서 완료될 때까지 연기되므로 여기에 포함되지 않습니다.
        string immediateFetchStandUpState =
            activeIdleSequenceRequiresStandUp &&
            idleSequencePhase != IdleSequencePhase.None
                ? activeIdleSequenceEndState
                : null;
        bool fetchStandUpAlreadyPlaying =
            !string.IsNullOrEmpty(immediateFetchStandUpState) &&
            idleSequencePhase == IdleSequencePhase.End;

        bool leavingBedRequired = scheduledRoutinePhase >= ScheduledRoutinePhase.EnteringBed &&
                                  scheduledRoutinePhase <= ScheduledRoutinePhase.LeavingBed;
        if (leavingBedRequired && sleepSpot != null)
        {
            CreateBedRoutinePositions(
                out _,
                out _,
                out _);
            transform.position = bedEntrancePosition;
        }

        scheduledRoutinePhase = ScheduledRoutinePhase.None;
        scheduledRoutineElapsed = 0f;
        mealRequested = false;
        sleepRequested = false;
        routineInterruptedByGrab = false;
        CancelLowerPriorityActions();

        fetchBall = ball;
        fetchPhase = phase;
        fetchActionElapsed = 0f;
        fetchBallAttachedDuringAction = false;
        fetchBallCaptureAssisting = false;
        fetchBallCaptureElapsed = 0f;
        fetchPickupRetryTimeRemaining = 0f;
        fetchBallReleasedDuringAction = false;
        fetchUsesCornerApproach = false;
        fetchApproachTarget = ball.WorldPosition;
        ResetFetchProgressTracking();
        currentAnimation = null;
        state = BrainState.Run;
        wanderTarget = ball.WorldPosition;

        if (!string.IsNullOrEmpty(immediateFetchStandUpState))
        {
            idleSequencePhase = IdleSequencePhase.End;
            currentIdleState = immediateFetchStandUpState;
            activeIdleSequenceLoopState = null;
            activeIdleSequenceEndState = immediateFetchStandUpState;
            activeIdleSequenceRequiresStandUp = true;

            if (fetchStandUpAlreadyPlaying)
                currentAnimation = immediateFetchStandUpState;
            else
                PlayAnimation(immediateFetchStandUpState);
        }
    }

    private bool UpdateBallPlay()
    {
        if (fetchPhase == FetchPhase.None)
            return false;

        if (fetchBall == null)
        {
            EndBallPlay();
            return false;
        }

        fetchPickupRetryTimeRemaining = Mathf.Max(
            0f,
            fetchPickupRetryTimeRemaining - Time.deltaTime);

        if (fetchBall.IsHeldByPlayer &&
            fetchPhase != FetchPhase.ApproachingHeldBall &&
            fetchPhase != FetchPhase.WatchingHeldBall)
        {
            fetchPhase = FetchPhase.ApproachingHeldBall;
            fetchActionElapsed = 0f;
            turnAnimationActive = false;
            turnAnimationElapsed = 0f;
        }
        else if (fetchBall.IsCarriedByDog &&
                 fetchPhase != FetchPhase.PickingUpBall &&
                 fetchPhase != FetchPhase.TurningToPlayerWithBall &&
                 fetchPhase != FetchPhase.ReturningBall &&
                 fetchPhase != FetchPhase.DroppingBall)
        {
            fetchPhase = FetchPhase.TurningToPlayerWithBall;
            fetchActionElapsed = 0f;
        }

        switch (fetchPhase)
        {
            case FetchPhase.ApproachingHeldBall:
                if (!fetchBall.IsHeldByPlayer)
                {
                    if (fetchBall.WasThrown)
                        fetchPhase = FetchPhase.ChasingBall;
                    else
                        EndBallPlay();
                    return true;
                }

                if (WaitForFetchStandUp())
                    return true;

                if (MoveTowardFetchTarget(
                        GetPlayerPosition(),
                        heldBallWaitDistance,
                        true))
                {
                    fetchPhase = FetchPhase.WatchingHeldBall;
                    UpdateHeldBallWatchPose();
                }
                return true;

            case FetchPhase.WatchingHeldBall:
                if (!fetchBall.IsHeldByPlayer)
                {
                    if (fetchBall.WasThrown)
                        fetchPhase = FetchPhase.ChasingBall;
                    else
                        EndBallPlay();
                    return true;
                }

                Vector3 playerOffset = GetPlayerPosition() - transform.position;
                playerOffset.y = 0f;
                if (playerOffset.sqrMagnitude >
                    (heldBallWaitDistance + 0.45f) * (heldBallWaitDistance + 0.45f))
                {
                    fetchPhase = FetchPhase.ApproachingHeldBall;
                    return true;
                }

                UpdateHeldBallWatchPose();
                return true;

            case FetchPhase.ChasingBall:
                if (fetchBall.IsHeldByPlayer)
                {
                    fetchPhase = FetchPhase.ApproachingHeldBall;
                    return true;
                }

                if (WaitForFetchStandUp())
                    return true;

                UpdateFetchBallApproachTarget();
                float ballDistance = GetPlanarDistance(fetchBall.WorldPosition, transform.position);
                if (UpdateUnreachableBallRecovery(ballDistance))
                    return true;

                float biteStartDistance = Mathf.Max(ballPickupDistance, ballBiteStartDistance);
                float pickupReach = fetchUsesCornerApproach
                    ? Mathf.Max(biteStartDistance, cornerBallPickupReach)
                    : biteStartDistance;
                bool reachedApproachPoint = GetPlanarDistance(
                    fetchApproachTarget,
                    transform.position) <= 0.12f;
                bool canReachBall = fetchUsesCornerApproach
                    ? reachedApproachPoint && ballDistance <= pickupReach
                    : ballDistance <= biteStartDistance;
                if (canReachBall &&
                    fetchPickupRetryTimeRemaining <= 0f &&
                    fetchBall.CanBePickedUpByDog)
                {
                    BeginBallPickupAction();
                    return true;
                }

                MoveTowardFetchTarget(
                    fetchApproachTarget,
                    fetchUsesCornerApproach ? 0.08f : biteStartDistance,
                    true);
                return true;

            case FetchPhase.PickingUpBall:
                if (fetchBall.IsHeldByPlayer)
                {
                    fetchPhase = FetchPhase.ApproachingHeldBall;
                    return true;
                }

                float actionPickupReach = fetchUsesCornerApproach
                    ? Mathf.Max(ballBiteStartDistance, cornerBallPickupReach)
                    : Mathf.Max(ballPickupDistance, ballBiteStartDistance) * 1.8f;
                if (!fetchBallAttachedDuringAction &&
                    !fetchBallCaptureAssisting &&
                    GetPlanarDistance(fetchBall.WorldPosition, transform.position) >
                    actionPickupReach)
                {
                    RecoverFromFailedBallPickup();
                    return true;
                }

                fetchActionElapsed += Time.deltaTime;
                float biteDuration = Mathf.Max(
                    ballBiteDuration,
                    GetAnimationClipLength(ballBiteState) /
                    Mathf.Max(0.1f, ballBiteAnimationSpeed));
                float attachTime = Mathf.Max(0.05f, biteDuration * ballBiteAttachRatio);
                MaintainFetchPickupPose(attachTime);

                float contactWindowStart = biteDuration * Mathf.Clamp01(
                    ballBiteAttachRatio - 0.32f);
                float contactWindowEnd = biteDuration * Mathf.Clamp01(
                    ballBiteAttachRatio + 0.32f);
                if (!fetchBallAttachedDuringAction &&
                    !fetchBallCaptureAssisting &&
                    fetchActionElapsed >= contactWindowStart &&
                    fetchActionElapsed <= contactWindowEnd &&
                    fetchBall.CanBePickedUpByDog &&
                    IsBallWithinMouthCaptureRange() &&
                    fetchBall.TryBeginDogMouthCapture(this))
                {
                    fetchBallCaptureAssisting = true;
                    fetchBallCaptureElapsed = 0f;
                }

                if (fetchBallCaptureAssisting)
                {
                    fetchBallCaptureElapsed += Time.deltaTime;
                    float captureProgress = Mathf.Clamp01(
                        fetchBallCaptureElapsed /
                        Mathf.Max(0.03f, ballMouthCaptureDuration));
                    fetchBall.UpdateDogMouthCapture(this, captureProgress);

                    if (captureProgress >= 1f)
                    {
                        fetchBallAttachedDuringAction =
                            fetchBall.CompleteDogMouthCapture(this);
                        fetchBallCaptureAssisting = false;
                        if (fetchBallAttachedDuringAction)
                        {
                            // 공놀이 비용은 던진 순간이 아니라 공이 실제 입에 고정된
                            // 단 한 번의 포획 완료 시점에만 적용합니다.
                            needsSystem?.RegisterBallCaught();
                        }
                        else
                        {
                            fetchBall.CancelDogMouthCapture();
                        }
                    }
                }

                if (!fetchBallCaptureAssisting &&
                    fetchActionElapsed >= Mathf.Max(attachTime, biteDuration))
                {
                    if (fetchBallAttachedDuringAction)
                    {
                        fetchPhase = FetchPhase.TurningToPlayerWithBall;
                        fetchActionElapsed = 0f;
                    }
                    else
                    {
                        RecoverFromFailedBallPickup();
                    }
                }
                return true;

            case FetchPhase.TurningToPlayerWithBall:
                if (!fetchBall.IsCarriedByDog)
                {
                    EndBallPlay();
                    return true;
                }

                UpdateFetchReturnTurn();
                return true;

            case FetchPhase.ReturningBall:
                if (!fetchBall.IsCarriedByDog)
                {
                    EndBallPlay();
                    return true;
                }

                if (MoveTowardFetchTarget(
                        GetPlayerPosition(),
                        ballReturnDistance,
                        true,
                        false))
                {
                    fetchPhase = FetchPhase.DroppingBall;
                    fetchActionElapsed = 0f;
                    fetchBallReleasedDuringAction = false;
                    currentMoveSpeed = 0f;
                    turnAnimationActive = false;
                    PlayAnimation(ballDropState);
                }
                return true;

            case FetchPhase.DroppingBall:
                fetchActionElapsed += Time.deltaTime;
                MaintainFetchActionPose(GetPlayerPosition());
                float releaseTime = Mathf.Max(0.05f, ballDropDuration * ballDropReleaseRatio);
                if (!fetchBallReleasedDuringAction && fetchActionElapsed >= releaseTime)
                {
                    ReleaseFetchBallFromMouth();
                    fetchBallReleasedDuringAction = true;
                }

                if (fetchActionElapsed >= Mathf.Max(releaseTime, ballDropDuration))
                    EndBallPlay();
                return true;
        }

        EndBallPlay();
        return false;
    }

    private bool WaitForFetchStandUp()
    {
        if (!FinishRestPoseBeforeMovement())
            return false;

        state = BrainState.Idle;
        movementUsesRunAnimation = false;
        currentMoveSpeed = 0f;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        return true;
    }

    private void UpdateFetchReturnTurn()
    {
        state = BrainState.Idle;
        movementUsesRunAnimation = false;
        currentMoveSpeed = 0f;
        fetchActionElapsed += Time.deltaTime;

        // Keep the completed bite pose briefly so the dog visibly secures the ball
        // before a separate foot-planted turn begins.
        if (fetchActionElapsed < ballPickupSettleDuration)
            return;

        // 공을 확보한 뒤 제자리에서 방향 전환하지 않습니다. 복귀 달리기를 먼저
        // 시작하고, 이동 중 조향으로 플레이어 방향을 향하게 해야 발이 미끄러지지 않습니다.
        BeginFetchReturnMovement();
    }

    private void BeginFetchReturnMovement()
    {
        fetchPhase = FetchPhase.ReturningBall;
        fetchActionElapsed = 0f;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        turnAnimationUses180 = false;
        state = BrainState.Run;
        movementUsesRunAnimation = true;
        currentMoveSpeed = 0f;
        currentAnimation = null;
        PlayAnimation(runState);
    }

    private bool MoveTowardFetchTarget(
        Vector3 targetPosition,
        float stopDistance,
        bool run,
        bool useTurnAnimation = true)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        wanderTarget = targetPosition;
        if (direction.sqrMagnitude <= stopDistance * stopDistance)
        {
            UpdateStationaryFetchPose(targetPosition);
            return true;
        }

        state = run ? BrainState.Run : BrainState.Trot;
        movementUsesRunAnimation = run;
        if (!useTurnAnimation)
        {
            turnAnimationActive = false;
            turnAnimationElapsed = 0f;
            turnAnimationUses180 = false;
        }

        if (!turnAnimationActive)
            PlayAnimation(run ? runState : trotState);

        if (useTurnAnimation)
            TurnBodyToward(direction.normalized);
        else
            SteerBodyWhileMoving(direction.normalized);

        float remainingDistance = Mathf.Max(0f, direction.magnitude - stopDistance);
        MoveForward(run ? runSpeed : trotSpeed, remainingDistance);
        return false;
    }

    private void SteerBodyWhileMoving(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            bodyTurnSpeed * Time.deltaTime);
    }

    private void UpdateStationaryFetchPose(Vector3 lookTarget, bool usePlayingIdle = false)
    {
        state = BrainState.Idle;
        movementUsesRunAnimation = false;
        currentMoveSpeed = 0f;
        if (!turnAnimationActive)
            PlayAnimation(usePlayingIdle ? idlePlayingState : idleBreathingState);

        Vector3 direction = lookTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            TurnBodyToward(direction.normalized);
    }

    private void UpdateHeldBallWatchPose()
    {
        state = BrainState.Idle;
        movementUsesRunAnimation = false;
        currentMoveSpeed = 0f;

        // 공을 든 플레이어 앞에 도착한 뒤에는 발이 고정된 Idle 상태를 유지합니다.
        // 남아 있던 회전 애니메이션과 루트 회전을 끝내고, 공 추적은 LateUpdate의
        // 목/머리 회전에만 맡겨 제자리에서 발이 미끄러지는 모습을 방지합니다.
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        UpdateStationaryIdleAnimation();
    }

    private void BeginBallPickupAction()
    {
        fetchPhase = FetchPhase.PickingUpBall;
        fetchActionElapsed = 0f;
        fetchBallAttachedDuringAction = false;
        fetchBallCaptureAssisting = false;
        fetchBallCaptureElapsed = 0f;
        fetchPickupEntrySpeed = Mathf.Max(currentMoveSpeed, ballBiteLungeSpeed);
        currentMoveSpeed = 0f;
        movementUsesRunAnimation = false;
        turnAnimationActive = false;
        float expectedBiteDuration = Mathf.Max(
            ballBiteDuration,
            GetAnimationClipLength(ballBiteState) /
            Mathf.Max(0.1f, ballBiteAnimationSpeed));
        float expectedContactDelay = expectedBiteDuration *
                                     Mathf.Clamp01(ballBiteAttachRatio);
        float predictionTime = Mathf.Clamp(
            Mathf.Max(ballPickupPredictionTime, expectedContactDelay * 0.8f),
            0f,
            0.5f);
        Vector3 predictedTravel = Vector3.ProjectOnPlane(
            fetchBall.CurrentVelocity,
            Vector3.up) * predictionTime;
        predictedTravel = Vector3.ClampMagnitude(
            predictedTravel,
            Mathf.Max(0.05f, ballBiteStartDistance * 0.75f));
        fetchPickupLockedTarget = fetchBall.WorldPosition + predictedTravel;
        FaceFetchTargetImmediately(fetchPickupLockedTarget);
        fetchPickupLockedRotation = transform.rotation;
        // A failed attempt can re-enter this state while the Animator still holds
        // the final frame of the previous bite. Always restart the bite explicitly.
        PlayAnimationFromBeginning(ballBiteState);
    }

    private void RecoverFromFailedBallPickup()
    {
        if (fetchBall != null && fetchBall.IsDogMouthCaptureInProgress)
            fetchBall.CancelDogMouthCapture();

        fetchPhase = FetchPhase.ChasingBall;
        fetchActionElapsed = 0f;
        fetchBallAttachedDuringAction = false;
        fetchBallCaptureAssisting = false;
        fetchBallCaptureElapsed = 0f;
        fetchPickupRetryTimeRemaining = Mathf.Max(0f, failedBallPickupRetryDelay);
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        currentMoveSpeed = 0f;
        currentAnimation = null;
        PlayAnimation(runState);
        ResetFetchProgressTracking();
    }

    private void MaintainFetchActionPose(Vector3 lookTarget)
    {
        state = BrainState.Idle;
        movementUsesRunAnimation = false;
        currentMoveSpeed = 0f;
        FaceFetchTargetImmediately(lookTarget);
    }

    private void MaintainFetchPickupPose(float attachTime)
    {
        state = BrainState.Idle;
        movementUsesRunAnimation = false;
        currentMoveSpeed = 0f;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;

        // 물기 점프가 시작된 뒤에는 움직이는 공을 따라 루트 회전을 갱신하지 않습니다.
        // 진입 순간에 확정한 방향을 유지해야 점프 도중 몸과 발이 옆으로 미끄러지지 않습니다.
        transform.rotation = fetchPickupLockedRotation;
        if (fetchBallAttachedDuringAction || fetchBallCaptureAssisting)
            return;

        // 물기 애니메이션이 시작된 자리에서 굳어 있지 않도록, 실제 입 위치가
        // 시작 순간에 확정한 공 위치에 닿을 때까지만 몸통을 수평으로 짧게 전진시킵니다.
        Vector3 mouthToBall = fetchPickupLockedTarget - GetBallCarryPoint();
        mouthToBall.y = 0f;
        float forwardGap = Vector3.Dot(mouthToBall, transform.forward);
        if (forwardGap <= 0.002f)
            return;

        float progress = Mathf.Clamp01(fetchActionElapsed / Mathf.Max(0.05f, attachTime));
        float easedProgress = progress * progress * (3f - 2f * progress);
        // 추격 중 속도를 물기 점프 초반까지 이어받고 접촉 직전에만 감속합니다.
        // 공 앞에서 완전히 정지한 뒤 갑자기 뛰는 인상을 없애기 위한 이동 곡선입니다.
        float lungeSpeed = Mathf.Lerp(
            Mathf.Max(fetchPickupEntrySpeed, ballBiteLungeSpeed),
            ballBiteLungeSpeed * 0.55f,
            easedProgress);
        float maxStep = lungeSpeed * Time.deltaTime;
        Vector3 desiredMove = transform.forward * Mathf.Min(forwardGap, maxStep);
        transform.position += GetCollisionLimitedMove(desiredMove);
    }

    private bool IsBallWithinMouthCaptureRange()
    {
        if (fetchBall == null)
            return false;

        float captureDistance = fetchBall.CollisionRadius +
                                Mathf.Max(0.01f, ballMouthContactPadding);
        return Vector3.Distance(fetchBall.WorldPosition, GetBallCarryPoint()) <=
               captureDistance;
    }

    private void FaceFetchTargetImmediately(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void UpdateFetchBallApproachTarget()
    {
        fetchApproachTarget = fetchBall.WorldPosition;
        fetchApproachTarget.y = transform.position.y;
        fetchUsesCornerApproach = TryFindClearFetchApproachPoint(
            fetchBall.WorldPosition,
            out Vector3 clearPoint);
        if (fetchUsesCornerApproach)
            fetchApproachTarget = clearPoint;
    }

    private bool TryFindClearFetchApproachPoint(
        Vector3 ballPosition,
        out Vector3 approachPoint)
    {
        approachPoint = ballPosition;
        approachPoint.y = transform.position.y;
        if (!IsFetchBallNearObstacle(ballPosition))
            return false;

        float bestScore = float.PositiveInfinity;
        bool found = false;
        Vector3 dogToBall = transform.position - ballPosition;
        dogToBall.y = 0f;
        Vector3 preferredDirection = dogToBall.sqrMagnitude > 0.0001f
            ? dogToBall.normalized
            : -transform.forward;

        const int directionCount = 16;
        for (int i = 0; i < directionCount; i++)
        {
            Vector3 direction = Quaternion.AngleAxis(
                i * (360f / directionCount),
                Vector3.up) * Vector3.forward;
            Vector3 candidate = ballPosition + direction * cornerBallApproachDistance;
            candidate.y = transform.position.y;
            if (!IsFetchApproachPointClear(candidate) ||
                HasFetchBlockingHit(
                    ballPosition + Vector3.up * 0.18f,
                    direction,
                    cornerBallApproachDistance - 0.08f,
                    0.06f))
                continue;

            float travelDistance = GetPlanarDistance(transform.position, candidate);
            float directionPenalty = 1f - Vector3.Dot(direction, preferredDirection);
            float score = travelDistance + directionPenalty * 0.3f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            approachPoint = candidate;
            found = true;
        }

        return found;
    }

    private bool IsFetchBallNearObstacle(Vector3 ballPosition)
    {
        const int directionCount = 8;
        for (int i = 0; i < directionCount; i++)
        {
            Vector3 direction = Quaternion.AngleAxis(
                i * (360f / directionCount),
                Vector3.up) * Vector3.forward;
            if (HasFetchBlockingHit(
                    ballPosition + Vector3.up * 0.18f,
                    direction,
                    0.48f,
                    0.07f))
                return true;
        }

        return false;
    }

    private bool IsFetchApproachPointClear(Vector3 point)
    {
        Vector3 bottom = point + Vector3.up * 0.3f;
        Vector3 top = point + Vector3.up * 0.68f;
        Collider[] overlaps = Physics.OverlapCapsule(
            bottom,
            top,
            Mathf.Max(0.18f, obstacleCheckRadius * 0.82f),
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            if (IsOwnCollider(overlaps[i]) ||
                (fetchBall != null && fetchBall.OwnsCollider(overlaps[i])))
                continue;

            return false;
        }

        return true;
    }

    private bool HasFetchBlockingHit(
        Vector3 origin,
        Vector3 direction,
        float distance,
        float radius)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            radius,
            direction.normalized,
            Mathf.Max(0.01f, distance),
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (IsOwnCollider(candidate) ||
                (fetchBall != null && fetchBall.OwnsCollider(candidate)))
                continue;

            return true;
        }

        return false;
    }

    private void ReleaseFetchBallFromMouth()
    {
        if (fetchBall == null || !fetchBall.IsCarriedByDog)
            return;

        Vector3 releasePosition = fetchBall.WorldPosition + transform.forward * 0.04f;
        Vector3 releaseVelocity = transform.forward * 0.28f + Vector3.down * 0.55f;
        Vector3 releaseSpin = transform.right * 4.5f + Vector3.up * 1.5f;
        fetchBall.DropFromDog(releasePosition, releaseVelocity, releaseSpin);
    }

    private void EndBallPlay()
    {
        if (fetchBall != null && fetchBall.IsDogMouthCaptureInProgress)
            fetchBall.CancelDogMouthCapture();

        fetchPhase = FetchPhase.None;
        fetchBall = null;
        fetchActionElapsed = 0f;
        fetchBallCaptureAssisting = false;
        fetchBallCaptureElapsed = 0f;
        fetchPickupRetryTimeRemaining = 0f;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        movementUsesRunAnimation = false;
        currentMoveSpeed = 0f;
        ResetFetchProgressTracking();
        currentAnimation = null;
        EnterAmbientState(false);
    }

    private static float GetPlanarDistance(Vector3 a, Vector3 b)
    {
        Vector3 offset = a - b;
        offset.y = 0f;
        return offset.magnitude;
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
            followUsesTrotAnimation = false;
            followAnimationModeInitialized = true;
            movementUsesRunAnimation = false;
            turnAnimationActive = false;
            currentMoveSpeed = 0f;
            UpdateStationaryIdleAnimation();
            return;
        }

        if (distance <= playerStopDistance)
        {
            // 가만히 있는 애니메이션을 재생하는 동안에는 이동과 회전을 모두 하지 않습니다.
            followUsesRunAnimation = false;
            followUsesTrotAnimation = false;
            followAnimationModeInitialized = true;
            movementUsesRunAnimation = false;
            currentMoveSpeed = 0f;
            UpdateStationaryPlayerFacing(direction);
            return;
        }

        sustainedLargeLookTime = 0f;

        if (FinishRestPoseBeforeMovement())
        {
            movementUsesRunAnimation = false;
            turnAnimationActive = false;
            currentMoveSpeed = 0f;
            return;
        }

        if (FinishIdleAnimationBeforeMovement())
        {
            movementUsesRunAnimation = false;
            turnAnimationActive = false;
            currentMoveSpeed = 0f;
            return;
        }

        if (!followAnimationModeInitialized)
        {
            // 플레이어를 처음 인식한 순간에는 현재 거리로 걷기, 트롯, 달리기 상태를 정합니다.
            followUsesRunAnimation = distance >= playerRunDistance;
            followUsesTrotAnimation = !followUsesRunAnimation &&
                                      distance >= playerTrotDistance;
            followAnimationModeInitialized = true;
        }
        else if (followUsesRunAnimation)
        {
            // 달리기 중에는 충분히 가까워졌을 때 트롯 또는 걷기로 내립니다.
            float runExitDistance = Mathf.Max(
                playerTrotDistance,
                playerRunDistance - runWalkHysteresis);
            if (distance <= runExitDistance)
            {
                followUsesRunAnimation = false;
                followUsesTrotAnimation = distance > Mathf.Max(
                    playerStopDistance,
                    playerTrotDistance - runWalkHysteresis);
            }
        }
        else if (followUsesTrotAnimation)
        {
            if (distance >= playerRunDistance + runWalkHysteresis)
            {
                followUsesRunAnimation = true;
                followUsesTrotAnimation = false;
            }
            else if (distance <= Mathf.Max(
                playerStopDistance,
                playerTrotDistance - runWalkHysteresis))
            {
                followUsesTrotAnimation = false;
            }
        }
        else
        {
            if (distance >= playerRunDistance + runWalkHysteresis)
                followUsesRunAnimation = true;
            else if (distance >= playerTrotDistance + runWalkHysteresis)
                followUsesTrotAnimation = true;
        }

        bool shouldRun = followUsesRunAnimation;
        bool shouldTrot = !shouldRun && followUsesTrotAnimation;
        float speed = shouldRun ? runSpeed : shouldTrot ? trotSpeed : walkSpeed;
        string locomotionState = shouldRun ? runState : shouldTrot ? trotState : walkState;

        // 걷기, 트롯, 달리기 모드가 바뀌는 순간 이전 방향 전환을 계속 유지하면
        // 새 루프가 늦게 재생되거나 전환 애니메이션 중간 자세에서 멈춘 것처럼 보일 수 있습니다.
        // 이동 모드가 바뀌면 이전 방향 전환을 취소하고 새 루프를 시작합니다.
        if (turnAnimationActive && turnAnimationUsesRun != shouldRun)
        {
            turnAnimationActive = false;
            turnAnimationElapsed = 0f;
        }

        movementUsesRunAnimation = shouldRun;
        if (!turnAnimationActive)
            PlayAnimation(locomotionState);
        TurnBodyToward(direction.normalized);
        MoveForward(speed);
    }

    private void UpdateStationaryPlayerFacing(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            sustainedLargeLookTime = 0f;
            turnAnimationActive = false;
            UpdateStationaryIdleAnimation();
            return;
        }

        float signedAngle = Vector3.SignedAngle(
            transform.forward,
            direction.normalized,
            Vector3.up);
        float absoluteAngle = Mathf.Abs(signedAngle);

        if (turnAnimationActive)
        {
            TurnBodyToward(direction.normalized);
            return;
        }

        // 작은 각도에서는 시선/머리만, 중간 각도에서는 머리가 먼저 반응합니다.
        // 두 경우 모두 뿌리 회전은 건드리지 않고 LateUpdate의 머리 추적에 맡깁니다.
        if (absoluteAngle <= smallLookAngle)
        {
            sustainedLargeLookTime = 0f;
            UpdateStationaryIdleAnimation();
            return;
        }

        if (absoluteAngle <= largeLookAngle)
        {
            sustainedLargeLookTime = 0f;
            UpdateStationaryIdleAnimation();
            return;
        }

        sustainedLargeLookTime += Time.deltaTime;
        if (sustainedLargeLookTime < sustainedBodyTurnDelay)
        {
            UpdateStationaryIdleAnimation();
            return;
        }

        BeginDirectionTurn(direction.normalized, signedAngle, false);
        turnAnimationResumeState = idleBreathingState;
        sustainedLargeLookTime = 0f;
    }

    #endregion

    #region 평소 생활 상태

    private void UpdateAmbientBehavior()
    {
        stateTimeRemaining -= Time.deltaTime;

        if (state == BrainState.Idle)
        {
            if (HasIdleVariations())
            {
                if (UpdateIdleVariation())
                {
                    if (stateTimeRemaining <= 0f)
                        EnterAmbientState(false);
                    else
                        PlayRandomIdleVariation();
                }

                movementUsesRunAnimation = false;
                turnAnimationActive = false;
                currentMoveSpeed = 0f;
                return;
            }

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
            currentMoveSpeed = 0f;
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
        followUsesTrotAnimation = false;
        followAnimationModeInitialized = false;
        ResetActiveIdleSequence();

        // 30 이하에서는 생활 행동을 새로 고르는 경계에서만 식사를 확률 후보로 넣습니다.
        // 21~30: 20%, 11~20: 40%, 1~10: 70%이며 0은 위의 강제 루틴이 담당합니다.
        if (TryStartOptionalMealAtAmbientBoundary())
            return;

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

            if (HasIdleVariations())
            {
                PlayRandomIdleVariation();
            }
            else
            {
                // Idle Playing은 플레이어 앞에 가까이 도착했을 때만 후보에 넣고,
                // 한 번 재생한 직후에는 같은 애니메이션을 연속으로 고르지 않습니다.
                bool canPlayIdlePlaying = !preventIdlePlaying &&
                                          IsPlayerFrontIdlePosition() &&
                                          Random.value >= 0.72f;
                PlayAnimation(canPlayIdlePlaying ? idlePlayingState : idleBreathingState);
            }
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
            bool canUseWalkTurn = state == BrainState.Walk ||
                                  state == BrainState.Trot ||
                                  state == BrainState.FollowPlayer;
            bool shouldPlayTurn = movementUsesRunAnimation || canUseWalkTurn;

            if (shouldPlayTurn && Mathf.Abs(signedTurnAngle) >= turnAngleThreshold)
                BeginDirectionTurn(direction, signedTurnAngle, movementUsesRunAnimation);
        }

        if (turnAnimationActive)
        {
            // 전환 애니메이션의 동작에 맞춰 목표 방향으로 몸을 부드럽게 돌립니다.
            float duration = GetCurrentTurnAnimationDuration();
            float rotationStartTime = duration * (turnAnimationUses180 ? 0.12f : 0.35f);
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
        turnAnimationUses180 = Mathf.Abs(signedTurnAngle) >= turn180AngleThreshold &&
                               !string.IsNullOrEmpty(turnLeft180State) &&
                               !string.IsNullOrEmpty(turnRight180State);
        turnAnimationElapsed = 0f;
        turnTargetDirection = direction.normalized;
        turnStartRotation = transform.rotation;
        turnAnimationResumeState = GetCurrentLocomotionState();

        if (turnAnimationUses180)
            PlayAnimation(signedTurnAngle > 0f ? turnRight180State : turnLeft180State);
        else if (useRunTurn)
            PlayAnimation(signedTurnAngle > 0f ? runLeanRightState : runLeanLeftState);
        else
            PlayAnimation(signedTurnAngle > 0f ? walkTurnRightState : walkTurnLeftState);
    }

    private float GetCurrentTurnAnimationDuration()
    {
        if (turnAnimationUses180)
            return Mathf.Max(0.1f, turn180AnimationDuration);

        return turnAnimationUsesRun
            ? Mathf.Max(0.05f, runTurnAnimationDuration)
            : Mathf.Max(0.05f, walkTurnAnimationDuration);
    }

    private string GetCurrentLocomotionState()
    {
        if (movementUsesRunAnimation || state == BrainState.Run)
            return runState;

        if (state == BrainState.Trot ||
            (state == BrainState.FollowPlayer && followUsesTrotAnimation))
            return trotState;

        return walkState;
    }

    private void MoveForward(float speed, float obstacleProbeLimit = float.PositiveInfinity)
    {
        if (turnAnimationActive)
        {
            float duration = GetCurrentTurnAnimationDuration();
            float movementStartRatio = turnAnimationUses180 ? 0.92f : 0.35f;
            if (turnAnimationElapsed < duration * movementStartRatio)
                return;
        }

        bool isEscapingObstacle = obstacleAvoidanceTimeRemaining > 0f &&
                                  obstacleAvoidanceDirection.sqrMagnitude > 0.0001f;
        float alignment = Vector3.Dot(transform.forward, GetPlanarDirectionToPlayerOrTarget());
        // 휘파람 추적 중에는 달리기 애니메이션만 재생되고 제자리에서 멈추지 않도록,
        // 몸이 목표 방향으로 전환된 뒤에는 정렬 감속을 적용하지 않습니다.
        float movementScale = isEscapingObstacle
            ? 1f
            : Mathf.Clamp01((alignment + 0.2f) / 1.2f);
        currentMoveSpeed = Mathf.MoveTowards(
            currentMoveSpeed,
            speed,
            locomotionAcceleration * Time.deltaTime);
        float movementDistance = currentMoveSpeed * movementScale * Time.deltaTime;
        if (movementDistance <= 0.0001f)
            return;

        // 이동 직전에 몸통 크기만큼 캡슐을 미리 검사하여 벽 안으로 들어가는 것을 막습니다.
        float obstacleProbeDistance = Mathf.Min(
            obstacleCheckDistance + movementDistance,
            Mathf.Max(movementDistance, obstacleProbeLimit));
        if (IsObstacleAhead(transform.forward, obstacleProbeDistance))
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

    private Vector3 GetCollisionLimitedMove(Vector3 desiredMove)
    {
        float distance = desiredMove.magnitude;
        if (distance <= 0.0001f)
            return Vector3.zero;

        Vector3 direction = desiredMove / distance;
        Vector3 origin = transform.position + Vector3.up * obstacleCheckHeight;
        if (!TryGetNearestExternalSphereCastHit(
                origin,
                obstacleCheckRadius,
                direction,
                distance,
                out RaycastHit hit))
            return desiredMove;

        float safeDistance = Mathf.Max(0f, hit.distance - 0.02f);
        return direction * Mathf.Min(distance, safeDistance);
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
            if (IsOwnCollider(hitCollider) ||
                IsScheduledRoutineTargetCollider(hitCollider) ||
                (fetchBall != null && fetchBall.OwnsCollider(hitCollider)) ||
                hits[i].distance >= nearestDistance)
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

    private bool IsScheduledRoutineTargetCollider(Collider candidate)
    {
        if (candidate == null || scheduledRoutinePhase == ScheduledRoutinePhase.None)
            return false;

        Transform candidateTransform = candidate.transform;
        bool belongsToFoodBowl = foodBowl != null &&
                                 (candidateTransform == foodBowl ||
                                  candidateTransform.IsChildOf(foodBowl));
        bool belongsToBed = sleepSpot != null &&
                            (candidateTransform == sleepSpot ||
                             candidateTransform.IsChildOf(sleepSpot));
        return belongsToFoodBowl || belongsToBed;
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
        headLookReturnActive = true;
        UpdateHeadLookAt(
            GetPlayerPosition(),
            headYawLimit,
            headPitchLimit,
            false);
    }

    private void UpdateHeadLookAt(Vector3 targetPosition)
    {
        headLookReturnActive = false;
        UpdateHeadLookAt(targetPosition, headYawLimit, headPitchLimit, false);
    }

    private void UpdateHeadLookAt(
        Vector3 targetPosition,
        float yawLimit,
        float pitchLimit,
        bool stabilizeMuzzle,
        float stabilizationWeight = 1f)
    {
        if (stabilizeMuzzle)
            headLookReturnActive = false;

        if (headBone == null && neckBone == null)
            return;

        Vector3 lookOrigin = headBone != null
            ? headBone.position
            : neckBone.position;
        Vector3 fullTargetDirection = targetPosition - lookOrigin;
        Vector3 targetDirection = Vector3.ProjectOnPlane(fullTargetDirection, Vector3.up);
        if (targetDirection.sqrMagnitude < 0.0001f ||
            fullTargetDirection.sqrMagnitude < 0.0001f)
            return;

        headLookReturnElapsed = 0f;
        float horizontalDistance = Mathf.Max(0.001f, targetDirection.magnitude);
        targetDirection.Normalize();
        float signedYaw = Vector3.SignedAngle(transform.forward, targetDirection, Vector3.up);
        float limitedYaw = Mathf.Clamp(signedYaw, -yawLimit, yawLimit);
        float effectiveHeadTurnSpeed = stabilizeMuzzle
            ? heldBallHeadTurnSpeed
            : headTurnSpeed;
        float blend = 1f - Mathf.Exp(-effectiveHeadTurnSpeed * Time.deltaTime);
        smoothedHeadYaw = Mathf.LerpAngle(smoothedHeadYaw, limitedYaw, blend);
        float pitch = Mathf.Atan2(
            fullTargetDirection.y,
            horizontalDistance) * Mathf.Rad2Deg;
        float limitedPitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);
        smoothedHeadPitch = Mathf.LerpAngle(smoothedHeadPitch, limitedPitch, blend);

        if (stabilizeMuzzle && headBone != null && mouthBone != null)
        {
            Vector3 clampedPlanarDirection =
                Quaternion.AngleAxis(smoothedHeadYaw, Vector3.up) * transform.forward;
            Vector3 pitchAxis = Vector3.Cross(Vector3.up, clampedPlanarDirection).normalized;
            Vector3 stableLookDirection =
                Quaternion.AngleAxis(-smoothedHeadPitch, pitchAxis) * clampedPlanarDirection;
            ApplyStableMuzzleLook(
                stableLookDirection.normalized,
                Mathf.Clamp01(stabilizationWeight));
            return;
        }

        // 뼈의 월드 회전을 목표 회전으로 직접 덮어쓰면 얼굴 정면축이 +Z가 아닌 리그에서
        // 애니메이터 포즈와 서로 반대 방향으로 보정되어 좌우 플립이 발생합니다.
        // 현재 애니메이션 포즈에 월드 Y축 회전량만 분배해 더하면 리그 축을 그대로 보존합니다.
        ApplyHeadLookOffsets();
    }

    private void UpdateHeadLookReturn()
    {
        if (!headLookReturnActive || (headBone == null && neckBone == null))
        {
            smoothedHeadYaw = 0f;
            smoothedHeadPitch = 0f;
            headLookReturnElapsed = 0f;
            return;
        }

        headLookReturnElapsed += Time.deltaTime;
        if (headLookReturnElapsed > headLookReturnDelay)
        {
            float blend = 1f - Mathf.Exp(-headLookReturnSpeed * Time.deltaTime);
            smoothedHeadYaw = Mathf.LerpAngle(smoothedHeadYaw, 0f, blend);
            smoothedHeadPitch = Mathf.LerpAngle(smoothedHeadPitch, 0f, blend);

            if (Mathf.Abs(Mathf.DeltaAngle(smoothedHeadYaw, 0f)) < 0.05f)
                smoothedHeadYaw = 0f;
            if (Mathf.Abs(Mathf.DeltaAngle(smoothedHeadPitch, 0f)) < 0.05f)
                smoothedHeadPitch = 0f;
        }

        ApplyHeadLookOffsets();

        if (smoothedHeadYaw == 0f && smoothedHeadPitch == 0f)
        {
            headLookReturnActive = false;
            headLookReturnElapsed = 0f;
        }
    }

    private void ApplyHeadLookOffsets()
    {
        float neckShare = headBone != null ? 0.35f : 1f;
        float headShare = neckBone != null ? 0.65f : 1f;
        if (neckBone != null)
        {
            Quaternion neckYaw = Quaternion.AngleAxis(
                smoothedHeadYaw * neckShare,
                Vector3.up);
            Quaternion neckPitch = Quaternion.AngleAxis(
                -smoothedHeadPitch * neckShare,
                transform.right);
            neckBone.rotation = neckYaw * neckPitch * neckBone.rotation;
        }
        if (headBone != null)
        {
            Quaternion headYaw = Quaternion.AngleAxis(
                smoothedHeadYaw * headShare,
                Vector3.up);
            Quaternion headPitch = Quaternion.AngleAxis(
                -smoothedHeadPitch * headShare,
                transform.right);
            headBone.rotation = headYaw * headPitch * headBone.rotation;
        }
    }

    private void ApplyStableMuzzleLook(
        Vector3 desiredLookDirection,
        float stabilizationWeight)
    {
        Vector3 currentMuzzleDirection = GetCurrentMuzzleForward();
        if (currentMuzzleDirection.sqrMagnitude < 0.0001f ||
            desiredLookDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion fullCorrection = Quaternion.FromToRotation(
            currentMuzzleDirection,
            desiredLookDirection);

        if (neckBone != null)
        {
            Quaternion neckCorrection = Quaternion.Slerp(
                Quaternion.identity,
                fullCorrection,
                heldBallNeckCorrectionShare * heldBallLookCorrectionStrength *
                stabilizationWeight);
            neckBone.rotation = neckCorrection * neckBone.rotation;
        }

        currentMuzzleDirection = GetCurrentMuzzleForward();
        if (currentMuzzleDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion headCorrection = Quaternion.FromToRotation(
            currentMuzzleDirection,
            desiredLookDirection);
        headBone.rotation = Quaternion.Slerp(
            Quaternion.identity,
            headCorrection,
            heldBallLookCorrectionStrength * stabilizationWeight) * headBone.rotation;
    }

    private bool CanLookAtPlayerWithoutFightingMovement()
    {
        if (fetchPhase != FetchPhase.None)
            return false;

        if (turnAnimationActive || obstacleRepositionActive)
            return false;

        if (state == BrainState.Walk || state == BrainState.Trot || state == BrainState.Run)
            return false;

        if (state != BrainState.FollowPlayer)
            return true;

        // While a non-locomotion animation is finishing, keep following the
        // player's movement with the head instead of dropping the gaze at the
        // stop-distance boundary.
        if (currentMoveSpeed <= 0.001f && IsConfiguredIdleState(currentAnimation))
            return true;

        Vector3 toPlayer = GetPlayerPosition() - transform.position;
        toPlayer.y = 0f;
        return toPlayer.sqrMagnitude <= playerStopDistance * playerStopDistance;
    }

    #endregion

    #region 바닥 유지와 애니메이션

    private bool TryRecoverInvalidPosition()
    {
        if (grabInteraction == null)
            grabInteraction = GetComponent<ShepherdVertexGrabInteraction>();
        if (grabInteraction != null && grabInteraction.IsHolding)
        {
            invalidPositionElapsed = 0f;
            return false;
        }

        Vector3 planarOffset = transform.position - mapRecoveryCenter;
        planarOffset.y = 0f;
        bool belowWorld = transform.position.y < minimumValidWorldY;
        bool outsideRecoveryRadius = planarOffset.sqrMagnitude >
                                     maximumDistanceFromMapCenter * maximumDistanceFromMapCenter;
        bool insideBlockingGeometry = !IsUsingBedPose() && IsInsideBlockingGeometry();

        if (!belowWorld && !outsideRecoveryRadius && !insideBlockingGeometry)
        {
            invalidPositionElapsed = 0f;
            return false;
        }

        invalidPositionElapsed += Time.deltaTime;
        // Falling through the floor still recovers immediately. Horizontal
        // boundary/overlap checks receive a grace period so a fetch path near a
        // corner cannot cause a one-frame center teleport.
        if (!belowWorld && invalidPositionElapsed < Mathf.Max(0.75f, wallPenetrationGrace))
            return false;

        RecoverDogToMapCenter();
        return true;
    }

    private bool IsUsingBedPose()
    {
        return scheduledRoutinePhase >= ScheduledRoutinePhase.EnteringBed &&
               scheduledRoutinePhase <= ScheduledRoutinePhase.LeavingBed;
    }

    private bool IsInsideBlockingGeometry()
    {
        Vector3 probeCenter = transform.position + Vector3.up * obstacleCheckHeight;
        float probeRadius = Mathf.Max(0.08f, obstacleCheckRadius * 0.72f);
        Collider[] overlaps = Physics.OverlapSphere(
            probeCenter,
            probeRadius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider candidate = overlaps[i];
            if (IsOwnCollider(candidate) ||
                IsPlayerCollider(candidate) ||
                (fetchBall != null && fetchBall.OwnsCollider(candidate)))
                continue;

            Vector3 closestPoint = candidate.ClosestPoint(probeCenter);
            if ((closestPoint - probeCenter).sqrMagnitude <= 0.0001f)
                return true;
        }

        return false;
    }

    private void RecoverDogToMapCenter()
    {
        FetchBallInteraction carriedBall =
            fetchBall != null && fetchBall.IsCarriedByDog ? fetchBall : null;

        if (fetchPhase != FetchPhase.None)
            EndBallPlay();

        pendingFetchBall = null;
        pendingFetchIsThrown = false;
        scheduledRoutinePhase = ScheduledRoutinePhase.None;
        scheduledRoutineElapsed = 0f;
        mealRequested = false;
        sleepRequested = false;
        routineInterruptedByGrab = false;
        obstacleAvoidanceDirection = Vector3.zero;
        obstacleAvoidanceTimeRemaining = 0f;
        obstacleIdleTimeRemaining = 0f;
        obstacleRepositionActive = false;
        turnAnimationActive = false;
        turnAnimationElapsed = 0f;
        currentMoveSpeed = 0f;
        sustainedLargeLookTime = 0f;
        invalidPositionElapsed = 0f;

        Vector3 recoveryPosition = FindDogRecoveryPosition(mapRecoveryCenter);
        transform.SetPositionAndRotation(recoveryPosition, Quaternion.identity);
        homePosition = recoveryPosition;
        currentAnimation = null;
        ResetActiveIdleSequence();
        EnterAmbientState(true);

        if (carriedBall != null)
            carriedBall.RecoverToFloor(mapRecoveryCenter + Vector3.forward * 0.75f, false);
    }

    private Vector3 FindDogRecoveryPosition(Vector3 preferredPosition)
    {
        Vector3[] offsets =
        {
            Vector3.zero,
            Vector3.forward * 0.75f,
            Vector3.right * 0.75f,
            Vector3.back * 0.75f,
            Vector3.left * 0.75f
        };

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector3 candidate = preferredPosition + offsets[i];
            Vector3 rayOrigin = candidate + Vector3.up * 3f;
            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                6f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
            {
                RaycastHit hit = hits[hitIndex];
                if (IsOwnCollider(hit.collider) || Vector3.Dot(hit.normal, Vector3.up) < 0.65f)
                    continue;

                Vector3 rootPosition = hit.point + Vector3.up * groundOffset;
                Vector3 bodyProbe = rootPosition + Vector3.up * obstacleCheckHeight;
                if (!Physics.CheckSphere(
                        bodyProbe,
                        obstacleCheckRadius * 0.8f,
                        Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore))
                    return rootPosition;
            }
        }

        return new Vector3(preferredPosition.x, groundOffset, preferredPosition.z);
    }

    private bool UpdateUnreachableBallRecovery(float ballDistance)
    {
        if (ballDistance + fetchProgressThreshold < bestFetchDistance)
        {
            bestFetchDistance = ballDistance;
            fetchNoProgressElapsed = 0f;
            return false;
        }

        fetchNoProgressElapsed += Time.deltaTime;
        if (fetchNoProgressElapsed < unreachableBallRecoveryDelay)
            return false;

        // 경로 정체가 발생해도 공을 맵 중앙으로 순간 이동시키지 않습니다.
        // 강아지 쪽으로 짧게 튕겨 장애물/구석 접촉을 풀고 추격을 이어갑니다.
        fetchBall.NudgeToward(transform.position, Mathf.Max(1f, trotSpeed));
        fetchApproachTarget = fetchBall.WorldPosition;
        fetchUsesCornerApproach = false;
        currentMoveSpeed = 0f;
        ResetFetchProgressTracking();
        return true;
    }

    private void ResetFetchProgressTracking()
    {
        fetchNoProgressElapsed = 0f;
        bestFetchDistance = float.PositiveInfinity;
    }

    private void KeepOnGround()
    {
        // 침대에는 콜라이더가 없어 일반 바닥 레이캐스트를 쓰면 수면 중 루트가
        // 바닥으로 다시 내려가 침대를 관통합니다. 침대 전용 자세가 Y를 직접 유지합니다.
        if (!routineInterruptedByGrab &&
            scheduledRoutinePhase >= ScheduledRoutinePhase.EnteringBed &&
            scheduledRoutinePhase <= ScheduledRoutinePhase.LeavingBed)
            return;

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
        PlayAnimation(stateName, animationBlendTime);
    }

    private void PlayAnimation(string stateName, float fixedBlendDuration)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
            return;

        if (HasIdleVariations() && !IsConfiguredIdleState(stateName))
            ResetActiveIdleSequence();

        targetAnimatorSpeed = stateName == ballBiteState
            ? ballBiteAnimationSpeed
            : stateName == trotState
                ? trotAnimationSpeed
                : IsRunAnimationState(stateName) ? runAnimationSpeed : 1f;

        if (currentAnimation == stateName && IsAnimatorInRequestedState(stateName))
            return;

        bool phaseMatchedLocomotionTransition =
            IsLocomotionState(currentAnimation) && IsLocomotionState(stateName);
        if (phaseMatchedLocomotionTransition)
        {
            AnimatorStateInfo currentStateInfo = animator.GetCurrentAnimatorStateInfo(0);
            float normalizedPhase = Mathf.Repeat(currentStateInfo.normalizedTime, 1f);
            float destinationOffset = normalizedPhase * GetAnimationClipLength(stateName);
            animator.CrossFadeInFixedTime(
                stateName,
                locomotionBlendTime,
                0,
                destinationOffset);
        }
        else
        {
            animator.CrossFadeInFixedTime(
                stateName,
                Mathf.Max(0.01f, fixedBlendDuration),
                0);
        }

        currentAnimation = stateName;
    }

    private void PlayAnimationFromBeginning(string stateName)
    {
        PlayAnimationFromBeginning(stateName, animationBlendTime);
    }

    private void PlayAnimationFromBeginning(string stateName, float fixedBlendDuration)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
            return;

        if (HasIdleVariations() && !IsConfiguredIdleState(stateName))
            ResetActiveIdleSequence();

        targetAnimatorSpeed = stateName == ballBiteState
            ? ballBiteAnimationSpeed
            : stateName == trotState
                ? trotAnimationSpeed
                : IsRunAnimationState(stateName) ? runAnimationSpeed : 1f;

        animator.CrossFadeInFixedTime(
            stateName,
            Mathf.Max(0.01f, fixedBlendDuration),
            0,
            0f);
        currentAnimation = stateName;
    }

    private void UpdateAnimatorPlaybackSpeed()
    {
        if (animator == null || !animator.enabled)
            return;

        animator.speed = Mathf.MoveTowards(
            animator.speed,
            targetAnimatorSpeed,
            animationSpeedChangeRate * Time.deltaTime);
    }

    private bool IsLocomotionState(string stateName)
    {
        if (string.IsNullOrEmpty(stateName))
            return false;

        return stateName == walkState ||
               stateName == trotState ||
               stateName == runState ||
               stateName == runLeanRightState ||
               stateName == runLeanLeftState ||
               stateName == walkTurnRightState ||
               stateName == walkTurnLeftState;
    }

    private float GetAnimationClipLength(string stateName)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return 1f;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && clips[i].name == stateName)
                return Mathf.Max(0.01f, clips[i].length);
        }

        return 1f;
    }

    private void UpdateStationaryIdleAnimation()
    {
        if (!HasIdleVariations())
        {
            PlayAnimation(idleBreathingState);
            return;
        }

        if (UpdateIdleVariation())
        {
            // 플레이어 가까이에서 Idle을 반복하는 동안에도 각 애니메이션 경계에서
            // 낮은 배고픔에 따른 확률 식사를 동일하게 평가합니다.
            if (TryStartOptionalMealAtAmbientBoundary())
                return;

            PlayRandomIdleVariation();
        }
    }

    private bool IsConfiguredIdleState(string stateName)
    {
        if (string.IsNullOrEmpty(stateName))
            return false;

        if (stateName == idleSequenceStartState ||
            stateName == idleSequenceLoopState ||
            stateName == idleSequenceEndState ||
            stateName == sittingStartState ||
            stateName == sittingEndState ||
            stateName == lyingStartState ||
            stateName == lyingEndState ||
            IsStateInArray(stateName, sittingLoopStates) ||
            IsStateInArray(stateName, lyingLoopStates))
            return true;

        if (idleStates == null)
            return false;

        for (int i = 0; i < idleStates.Length; i++)
        {
            if (stateName == idleStates[i])
                return true;
        }

        return false;
    }

    private bool HasIdleVariations()
    {
        return GetValidIdleStateCount() > 0 ||
               HasIdleSequence() ||
               HasRestIdleSequence();
    }

    private bool HasIdleSequence()
    {
        return !string.IsNullOrEmpty(idleSequenceStartState) &&
               !string.IsNullOrEmpty(idleSequenceLoopState) &&
               !string.IsNullOrEmpty(idleSequenceEndState);
    }

    private int GetValidIdleStateCount()
    {
        if (idleStates == null)
            return 0;

        int count = 0;
        for (int i = 0; i < idleStates.Length; i++)
        {
            if (!string.IsNullOrEmpty(idleStates[i]))
                count++;
        }

        return count;
    }

    private int GetIdleChoiceIndex(string stateName)
    {
        if (string.IsNullOrEmpty(stateName))
            return -1;

        int validIndex = 0;
        if (idleStates != null)
        {
            for (int i = 0; i < idleStates.Length; i++)
            {
                if (string.IsNullOrEmpty(idleStates[i]))
                    continue;
                if (idleStates[i] == stateName)
                    return validIndex;
                validIndex++;
            }
        }

        if (HasIdleSequence() &&
            (stateName == idleSequenceStartState ||
             stateName == idleSequenceLoopState ||
             stateName == idleSequenceEndState))
            return validIndex;

        return -1;
    }

    private bool HasRestIdleSequence()
    {
        return HasCompleteIdleSequence(
                   sittingStartState,
                   sittingLoopStates,
                   sittingEndState) ||
               HasCompleteIdleSequence(
                   lyingStartState,
                   lyingLoopStates,
                   lyingEndState);
    }

    private static bool HasCompleteIdleSequence(
        string startState,
        string[] loopStates,
        string endState)
    {
        return !string.IsNullOrEmpty(startState) &&
               !string.IsNullOrEmpty(endState) &&
               GetValidStateCount(loopStates) > 0;
    }

    private static int GetValidStateCount(string[] states)
    {
        if (states == null)
            return 0;

        int count = 0;
        for (int i = 0; i < states.Length; i++)
        {
            if (!string.IsNullOrEmpty(states[i]))
                count++;
        }

        return count;
    }

    private static bool IsStateInArray(string stateName, string[] states)
    {
        if (states == null)
            return false;

        for (int i = 0; i < states.Length; i++)
        {
            if (stateName == states[i])
                return true;
        }

        return false;
    }

    private void PlayRandomIdleVariation()
    {
        if (TryPlayRestIdleSequence())
            return;

        int regularCount = GetValidIdleStateCount();
        int choiceCount = regularCount + (HasIdleSequence() ? 1 : 0);
        if (choiceCount <= 0)
        {
            pettingInterruptedIdleChoice = -1;
            PlayAnimation(idleBreathingState);
            return;
        }

        int availableChoiceCount = 0;
        for (int i = 0; i < choiceCount; i++)
        {
            if (i != lastIdleChoice && i != pettingInterruptedIdleChoice)
                availableChoiceCount++;
        }

        int choice;
        if (availableChoiceCount > 0)
        {
            int availableIndex = Random.Range(0, availableChoiceCount);
            choice = 0;
            for (int i = 0; i < choiceCount; i++)
            {
                if (i == lastIdleChoice || i == pettingInterruptedIdleChoice)
                    continue;
                if (availableIndex-- == 0)
                {
                    choice = i;
                    break;
                }
            }
        }
        else
        {
            choice = Random.Range(0, choiceCount);
        }

        lastIdleChoice = choice;
        pettingInterruptedIdleChoice = -1;

        if (choice >= regularCount)
        {
            lastIdleWasRestPose = false;
            BeginIdleSequence(
                idleSequenceStartState,
                new[] { idleSequenceLoopState },
                idleSequenceEndState,
                false);
            return;
        }

        int validIndex = 0;
        for (int i = 0; i < idleStates.Length; i++)
        {
            if (string.IsNullOrEmpty(idleStates[i]))
                continue;
            if (validIndex++ != choice)
                continue;

            idleSequencePhase = IdleSequencePhase.None;
            activeIdleSequenceLoopState = null;
            activeIdleSequenceEndState = null;
            activeIdleSequenceRequiresStandUp = false;
            lastIdleWasRestPose = false;
            currentIdleState = idleStates[i];
            PlayAnimation(currentIdleState);
            return;
        }
    }

    private bool TryPlayRestIdleSequence()
    {
        if (!HasRestIdleSequence() || lastIdleWasRestPose ||
            Time.time < restIdleSuppressedUntilTime ||
            Random.value >= restIdleChance)
            return false;

        bool hasSitting = HasCompleteIdleSequence(
            sittingStartState,
            sittingLoopStates,
            sittingEndState);
        bool hasLying = HasCompleteIdleSequence(
            lyingStartState,
            lyingLoopStates,
            lyingEndState);
        bool chooseSitting = hasSitting && (!hasLying || Random.value < 0.5f);

        if (chooseSitting)
        {
            BeginIdleSequence(
                sittingStartState,
                sittingLoopStates,
                sittingEndState,
                true);
        }
        else
        {
            BeginIdleSequence(
                lyingStartState,
                lyingLoopStates,
                lyingEndState,
                true);
        }

        lastIdleWasRestPose = true;
        lastIdleChoice = -1;
        pettingInterruptedIdleChoice = -1;
        return true;
    }

    private void BeginIdleSequence(
        string startState,
        string[] loopStates,
        string endState,
        bool requiresStandUp)
    {
        int validLoopCount = GetValidStateCount(loopStates);
        int loopChoice = validLoopCount > 0 ? Random.Range(0, validLoopCount) : -1;
        int validIndex = 0;
        activeIdleSequenceLoopState = null;

        if (loopStates != null)
        {
            for (int i = 0; i < loopStates.Length; i++)
            {
                if (string.IsNullOrEmpty(loopStates[i]))
                    continue;
                if (validIndex++ == loopChoice)
                {
                    activeIdleSequenceLoopState = loopStates[i];
                    break;
                }
            }
        }

        activeIdleSequenceEndState = endState;
        activeIdleSequenceRequiresStandUp = requiresStandUp;
        idleSequencePhase = IdleSequencePhase.Start;
        currentIdleState = startState;
        PlayAnimation(currentIdleState);
    }

    private bool UpdateIdleVariation()
    {
        if (string.IsNullOrEmpty(currentIdleState))
        {
            PlayRandomIdleVariation();
            return false;
        }

        if (!IsAnimationCycleFinished(currentIdleState))
            return false;

        if (idleSequencePhase == IdleSequencePhase.Start)
        {
            idleSequencePhase = IdleSequencePhase.Loop;
            currentIdleState = activeIdleSequenceLoopState;
            PlayAnimation(currentIdleState);
            return false;
        }

        if (idleSequencePhase == IdleSequencePhase.Loop)
        {
            idleSequencePhase = IdleSequencePhase.End;
            currentIdleState = activeIdleSequenceEndState;
            PlayAnimation(currentIdleState);
            return false;
        }

        ResetActiveIdleSequence();
        return true;
    }

    private void ResetActiveIdleSequence()
    {
        idleSequencePhase = IdleSequencePhase.None;
        currentIdleState = null;
        activeIdleSequenceLoopState = null;
        activeIdleSequenceEndState = null;
        activeIdleSequenceRequiresStandUp = false;
    }

    private bool FinishRestPoseBeforeMovement()
    {
        if (!activeIdleSequenceRequiresStandUp ||
            idleSequencePhase == IdleSequencePhase.None)
            return false;

        if (!IsAnimationCycleFinished(currentIdleState))
            return true;

        if (idleSequencePhase == IdleSequencePhase.Start ||
            idleSequencePhase == IdleSequencePhase.Loop)
        {
            idleSequencePhase = IdleSequencePhase.End;
            currentIdleState = activeIdleSequenceEndState;
            PlayAnimation(currentIdleState);
            return true;
        }

        ResetActiveIdleSequence();
        currentAnimation = null;
        return false;
    }

    private bool FinishIdleAnimationBeforeMovement()
    {
        if (activeIdleSequenceRequiresStandUp ||
            string.IsNullOrEmpty(currentIdleState) ||
            currentIdleState == idleBreathingState ||
            currentAnimation != currentIdleState ||
            !IsConfiguredIdleState(currentIdleState))
            return false;

        // Never hold movement forever if another system has already replaced the
        // expected idle state without updating the idle sequence bookkeeping.
        if (!IsAnimatorInRequestedState(currentIdleState))
        {
            ResetActiveIdleSequence();
            return false;
        }

        if (idleSequencePhase == IdleSequencePhase.End)
        {
            if (!IsAnimationCycleFinished(currentIdleState))
                return true;

            ResetActiveIdleSequence();
            return false;
        }

        if (!IsAnimationAtNaturalTransitionPoint(currentIdleState))
            return true;

        if ((idleSequencePhase == IdleSequencePhase.Start ||
             idleSequencePhase == IdleSequencePhase.Loop) &&
            !string.IsNullOrEmpty(activeIdleSequenceEndState))
        {
            idleSequencePhase = IdleSequencePhase.End;
            currentIdleState = activeIdleSequenceEndState;
            PlayAnimationFromBeginning(currentIdleState);
            return true;
        }

        ResetActiveIdleSequence();
        return false;
    }

    private bool IsAnimationCycleFinished(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName) || animator.IsInTransition(0))
            return false;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        int stateHash = Animator.StringToHash(stateName);
        if (!stateInfo.IsName(stateName) && stateInfo.shortNameHash != stateHash)
            return false;

        return stateInfo.normalizedTime >= 0.98f;
    }

    private bool IsAnimationAtNaturalTransitionPoint(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
            return true;
        if (animator.IsInTransition(0))
            return false;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        int stateHash = Animator.StringToHash(stateName);
        if (!stateInfo.IsName(stateName) && stateInfo.shortNameHash != stateHash)
            return false;

        if (!stateInfo.loop)
            return stateInfo.normalizedTime >= 0.98f;

        // Loop clips keep increasing normalizedTime beyond 1.0, so comparing only
        // against 0.98 would allow a transition from any point after the first loop.
        return Mathf.Repeat(stateInfo.normalizedTime, 1f) >= 0.92f;
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
