# MyLittlePet · VRtest

**가상 강아지의 행동과 사용자 상호작용을 구현하는 Unity 기반 VR/MR 프로토타입입니다.**

MyLittlePet 팀 프로젝트에 사용할 핵심 기능을 먼저 개발하고 시험하기 위한 저장소입니다. 강아지가 사용자를 바라보고 따라오는 행동부터 쓰다듬기와 공놀이까지 하나의 플레이 흐름으로 연결합니다.

이 저장소의 담당 개발자는 [mooner1213](https://github.com/mooner1213)입니다. 강아지 행동 AI와 상호작용 로직을 중심으로 입양 화면 및 PC·XR 테스트 기능을 개발했습니다. 아래 내용은 **이 저장소에서 구현한 범위**를 설명하며 팀 통합본의 최종 반영 범위와는 별도로 관리합니다.

> 핵심 상호작용을 개발하고 Quest 환경 테스트와 복구 회귀 시험을 진행한 프로토타입입니다. 담당 기능의 구현·검증 범위와 팀 내 다른 담당 영역을 구분해 설명합니다.

## 담당 역할과 AI 활용 방식

개발자는 동물의 자연스러운 움직임과 반응을 목표로 동작을 관찰하고 문제를 재현한 뒤 구체적인 수정 방향을 제시했습니다. AI를 코드 작성과 수정에 활용했으며 결과를 직접 확인하고 반복 피드백을 주는 방식으로 상호작용을 다듬었습니다.

- **문제 관찰:** 애니메이션의 끊김과 조건 중첩에 따른 동일 동작 반복 및 이동 속도 불일치로 인한 발 미끄러짐을 확인했습니다.
- **수정 방향 구체화:** 문제가 발생하는 상황과 기대하는 전환·움직임을 세부적으로 설명하고 AI에 수정안을 전달했습니다.
- **반복 검증:** 수정된 결과를 플레이하며 남은 어색함을 확인하고 추가 피드백을 반복했습니다.
- **시각 참고 자료 활용:** 강아지를 들었을 때의 자세를 개선하기 위해 참고 사진과 그림으로 원하는 형태와 움직임을 전달했습니다.
- **표현 확장:** 들고 있는 동안 정지된 인형처럼 보이지 않도록 기본 Idle의 머리 부분 적용과 꼬리 움직임을 추가했습니다. 리깅 수정과 새로운 애니메이션 제작도 시도했습니다.
- **상호작용 설계:** 공놀이와 쓰다듬기를 호감도·배고픔·피곤함 상태 및 강아지의 반응 변화와 연결했습니다.

## 주요 기능

| 구분 | 구현 내용 | 관련 코드 |
| --- | --- | --- |
| 강아지 행동 AI | 자율 배회와 사용자 추적. 걷기·트롯·달리기 전환과 장애물 회피 | [ShepherdCompanionAI.cs](Assets/Scripts/Prototype/ShepherdCompanionAI.cs) |
| 시선·애니메이션 | 사용자와 공을 향한 시선 보정. 머리와 몸의 방향 조절. 현재 동작의 종료를 고려한 상태 전환 | [ShepherdCompanionAI.cs](Assets/Scripts/Prototype/ShepherdCompanionAI.cs) |
| 쓰다듬기 | 컨트롤러 접촉과 이동 거리·시간을 이용한 제스처 판정. 반응 애니메이션과 하트 효과 | [XRControllerContactInteractor.cs](Assets/Scripts/XR/XRControllerContactInteractor.cs) · [PetHeartEffect.cs](Assets/Scripts/Prototype/PetHeartEffect.cs) |
| 공놀이 | 공 잡기·던지기와 강아지의 추격·물기·반환. 포획 실패 및 경계 이탈 복구 | [FetchBallInteraction.cs](Assets/Scripts/Prototype/FetchBallInteraction.cs) |
| 강아지 들기 | 잡기와 내려놓기. 들린 자세의 뼈대 보정 및 척추 IK. 이동 중 충돌 처리 | [ShepherdVertexGrabInteraction.cs](Assets/Scripts/Prototype/ShepherdVertexGrabInteraction.cs) |
| 돌봄 행동 | 시간과 상호작용에 따른 내부 허기·피로 변화. 식사·수면 행동과 회복 | [PetNeedsSystem.cs](Assets/Scripts/Prototype/PetNeedsSystem.cs) |
| 관계·상태 시스템 | 호감도·배고픔·피곤함 게이지와 공놀이·쓰다듬기의 연동. 호감도에 따른 애니메이션과 행동 AI 변화 | 아래 관계·상태 시스템 설명 참고 |
| 입양 도입 | 이름 입력·저장과 강아지 미리보기. 한글 IME 조합을 반영한 길이 검증. 플레이 씬 전환 | [AdoptionIntroController.cs](Assets/Scripts/Prototype/AdoptionIntroController.cs) |
| 기기별 실행 구조 | 공통 플레이 씬에 Quest 2 가상 공간과 Quest 3 MR 공간 설정을 연결 | [PlatformGameplaySceneBootstrap.cs](Assets/Scripts/XR/PlatformGameplaySceneBootstrap.cs) |

## 핵심 구현과 문제 해결

### 1. 강아지 행동과 애니메이션의 연결

초기 애니메이션 적용 단계에서 전환이 뚝뚝 끊기거나 여러 조건이 겹칠 때 같은 애니메이션만 반복되는 문제가 있었습니다. 걷기 애니메이션과 실제 이동 속도가 맞지 않아 발이 바닥에서 미끄러지는 현상도 발생했습니다. 문제 상황별로 수정 방향을 구체화해 AI에 전달하고 결과를 반복 확인하면서 동물의 움직임을 다듬었습니다.

사용자가 멀어졌다는 이유로 진행 중인 동작을 즉시 끊으면 강아지의 행동이 부자연스럽게 보입니다. 현재 동작의 전환 지점과 일어나기 등의 종료 동작을 고려한 뒤 추적을 시작하도록 처리했습니다.

- 앉기·엎드리기에서 이동으로 넘어갈 때 종료 동작을 연결합니다.
- 걷기·트롯·달리기 전환에 거리 히스테리시스를 적용해 경계에서 상태가 반복 전환되는 현상을 줄입니다.
- 시선 추적이 끝날 때 머리를 즉시 정면으로 돌리지 않고 부드럽게 복귀시킵니다.
- 쓰다듬기와 공놀이 등 상호작용 중에는 해당 행동에 맞는 시선과 자세를 적용합니다.

### 2. 쓰다듬기 오인식과 반복 반응 방지

컨트롤러가 강아지에 닿아 있기만 하거나 미세하게 떨리는 상황을 쓰다듬기로 인식하지 않도록 처리했습니다.

- 접촉 시작점 대비 이동 거리와 최소 동작 시간을 함께 확인합니다.
- 기본 단일 스트로크 판정에서는 프레임별 미세 이동의 누적 대신 시작점으로부터의 변위를 사용합니다.
- 반응 후에는 접촉 해제와 재반응 대기 시간을 이용해 연속 발동을 억제합니다.
- 공이나 강아지를 들고 있는 동안에는 쓰다듬기 입력과의 충돌을 제한합니다.

관련 코드: [XRControllerContactInteractor.cs](Assets/Scripts/XR/XRControllerContactInteractor.cs)

### 3. 공놀이 실패 상황 복구

공놀이를 추격 → 물기 → 반환의 단계로 연결하고 각 단계에서 멈출 수 있는 상황을 처리했습니다.

- 컨트롤러에서 공을 놓을 때 손의 선속도와 각속도를 공의 물리에 전달합니다.
- 물기 애니메이션과 입 위치를 연결해 공을 포획합니다.
- 물기에 실패하면 포획 진행값을 초기화하고 재추격하도록 처리합니다.
- 공이 플레이 영역의 경계를 넘으면 위치 제한과 반사를 적용합니다.
- 바닥 아래로 빠진 공은 가까운 안전 바닥으로 복구합니다.
- 접근 정체 시에는 공을 짧게 튕겨 재추격을 유도합니다.

관련 코드: [FetchBallInteraction.cs](Assets/Scripts/Prototype/FetchBallInteraction.cs) · [ShepherdCompanionAI.cs](Assets/Scripts/Prototype/ShepherdCompanionAI.cs)

### 4. VR과 MR 공간의 공통 인터페이스

기기별 공간 설정을 공통 플레이 로직에 전달하기 위해 `IPetSpaceContractProvider`를 사용합니다.

- **Quest 2 VR:** 가상 방의 이동 영역과 생성·휴식·복구 위치를 제공합니다.
- **Quest 3 MR:** MRUK의 공간 데이터와 NavMesh를 이용해 후보 위치 및 목적지·복구 경로를 검증합니다.
- 공간 데이터의 상태를 `Pending` / `Invalid` / `Valid`로 구분합니다.
- 강아지와 공은 공통 스냅샷을 통해 공간 정보를 전달받습니다.

관련 코드: [PetSpaceContract.cs](Assets/Scripts/Integration/PetSpaceContract.cs) · [Q3NavMeshPetSpaceProvider.cs](Assets/Scripts/Integration/Q3NavMeshPetSpaceProvider.cs)

### 5. 들기 자세와 부분 애니메이션 개선

강아지를 들었을 때 자세가 어색하거나 움직임이 거의 없어 생명감이 떨어지는 문제가 있었습니다. 참고 사진과 그림으로 원하는 자세를 구체화하고 수정 결과를 반복 확인했습니다.

- 들린 몸의 자세와 내려놓는 과정의 어색함을 조정했습니다.
- 기존 기본 Idle 애니메이션을 머리 부분에 적용하고 꼬리 움직임을 추가했습니다.
- 전신 자세를 유지하면서도 머리와 꼬리에서는 작은 움직임이 이어지도록 표현을 보강했습니다.
- 리깅 수정과 새로운 애니메이션 제작을 시도하며 표현 방법을 탐색했습니다. 이 시도는 신규 리깅·애니메이션 제작 전체가 완료됐다는 의미와 구분합니다.

관련 코드: [ShepherdVertexGrabInteraction.cs](Assets/Scripts/Prototype/ShepherdVertexGrabInteraction.cs)

### 6. 관계·상태 시스템과 상호작용의 연결

호감도·배고픔·피곤함 게이지 시스템을 구성하고 공놀이와 쓰다듬기 등의 상호작용을 상태 변화와 연결했습니다. 호감도에 따라 강아지의 애니메이션과 행동 AI가 달라지도록 구현했습니다.

배고픔과 피곤함은 식사·수면 행동 및 회복 흐름과 연결합니다. 각 수치는 사용자 행동 이후 강아지의 반응이 달라지는 상호작용 구조를 만드는 데 활용했습니다.

호감도·게이지 표시와 관계에 따른 반응 변화는 개발자가 설명한 구현 범위입니다. 공개된 `main`에서 확인한 내부 허기·피로 처리와 구분합니다. 시연 영상과 시험 결과는 작성되어 있으며 이 README에는 자료 링크를 아직 연결하지 않았습니다.

## 개발 환경

저장소의 `ProjectVersion.txt`와 `Packages/manifest.json` 기준입니다.

| 항목 | 버전 / 구성 |
| --- | --- |
| Unity Editor | **6000.3.13f1** |
| 언어 | C# |
| 렌더링 | Universal Render Pipeline 17.3.0 |
| 입력 | Input System 1.19.0 |
| XR Interaction Toolkit | 3.4.1 |
| OpenXR Plugin | 1.16.1 |
| AR Foundation | 6.4.1 |
| Meta XR Core SDK / MR Utility Kit | 205.0.0 |
| AI Navigation | 2.0.14 |
| 개발·시험 대상 | PC Editor / Quest 2 VR / Quest 3 MR |

## 실행 방법

### PC Editor에서 기능 확인

1. 저장소를 내려받습니다.

   ```bash
   git clone https://github.com/PetopiaMBC/VRtest.git
   ```

2. Unity Hub에서 프로젝트 폴더를 추가하고 **Unity 6000.3.13f1**로 엽니다.
3. 패키지 복원과 에셋 임포트가 끝날 때까지 기다립니다.
4. [AdoptionScene](Assets/Scenes/AdoptionScene.unity)을 열어 입양 도입부터 확인합니다.
5. 핵심 플레이를 바로 확인하려면 [PlayScene](Assets/Scenes/PlayScene/PlayScene.unity)을 엽니다.

PC 입력은 헤드셋 없이 기능을 시험하기 위한 보조 경로입니다. PC에서의 동작 확인만으로 Quest 실기기 검증이 완료되는 것은 아닙니다.

### PC 이동 조작

아래는 `PcKeyboardLocomotion`이 활성화된 경우의 기본 조작입니다.

| 입력 | 동작 |
| --- | --- |
| WASD / 방향키 | 시점 기준 이동 |
| Shift | 달리기 |
| 마우스 이동 | 시점 회전 |
| C / Ctrl | 앉기 전환 |
| Esc | 커서 잠금 해제 |
| 마우스 왼쪽 클릭 | 해제된 커서 다시 잠금 |

### Quest 실행 경로

빌드 씬에는 다음 네 씬이 등록되어 있습니다.

| 씬 | 역할 |
| --- | --- |
| `AdoptionScene` | 입양 도입과 실행 환경에 따른 씬 선택 |
| `Q2VirtualRoomScene` | Quest 2 가상 방 구성과 이동 처리 |
| `Q3MRScene` | Quest 3 MR 공간과 권한·경로 연결 |
| `PlayScene` | 기기별 씬에서 공유하는 핵심 플레이 |

Android 빌드에는 Unity Android Build Support와 해당 XR 설정이 필요합니다. Quest 3 MR 경로는 기기의 공간 데이터 및 권한에 의존합니다. APK 빌드 성공과 실기기에서의 상호작용·성능 검증은 별도로 확인해야 합니다.

## 코드 구성

| 경로 | 역할 |
| --- | --- |
| [Assets/Scripts/Prototype](Assets/Scripts/Prototype) | 강아지 AI와 공놀이·들기·입양·돌봄 프로토타입 |
| [Assets/Scripts/XR](Assets/Scripts/XR) | XR 입력과 컨트롤러 상호작용. 기기별 씬 및 UI 처리 |
| [Assets/Scripts/Integration](Assets/Scripts/Integration) | 공간 데이터 인터페이스와 VR/MR 공급자 |
| [Assets/Scenes](Assets/Scenes) | 입양·공통 플레이·기기별 진입 씬 |

## 구현 및 검증 범위

### 담당 기능 검증 완료

개발자가 수행한 작업과 시험 결과를 기준으로 정리했습니다.

- [x] Quest 환경에서 담당 기능 테스트
- [x] 모서리와 장애물 주변의 오류 상황 및 복구 회귀 시험
- [x] 바닥 이탈 상황의 복구 회귀 시험
- [x] 시연 영상 제작과 시험 결과 정리

시연 영상과 시험 결과는 별도로 준비되어 있습니다. 이 README에는 자료 링크를 아직 연결하지 않았습니다. 테스트 기종별 범위와 반복 횟수 및 성능 수치는 이 문서에서 별도로 제시하지 않습니다.

### 담당 범위와 별도 확인 사항

| 항목 | 상태 |
| --- | --- |
| MR 공간 데이터·권한에 따른 생성 위치와 이동 경로 검증 | 다른 담당 영역 |
| 기기별 장시간 안정성 및 성능 측정 | 다른 담당 영역 |
| 공간 음향 점검과 연결 | 다른 담당 영역 |
| APK 빌드·배포 정리 | 담당 작업으로 기재하지 않음 |
| 행동 AI와 들기 로직의 책임 분리 및 구조 개선 | 완료 여부 미확인. 코드 구조 검토 필요 |

### 기록 기준

저장소의 과거 문서와 커밋 메시지는 해당 작성 시점의 상태를 나타냅니다. 2026-09-11 커밋에 기록된 Quest 2 적용 미완료 상태를 이후 테스트까지 포함한 현재 상태로 단정하지 않습니다. 위 검증 완료 항목은 개발자가 추가로 제공한 수행 내역을 반영했습니다.

## 개발 기록

- [구현 체크리스트](MyLittlePet_1차_구현_체크리스트.md)
- [1차 마스터 플랜 대비 구현 현황 · 2026-09-08](MyLittlePet_1차_마스터플랜_대비_구현현황_2026-09-08.md)
- [1차 마스터 플랜 수정 항목 · 2026-09-08](MyLittlePet_1차_마스터플랜_수정항목_2026-09-08.md)
- [작업 정리 · 2026-09-08](MyLittlePet_작업정리_2026-09-08.md)
- [커밋 이력](https://github.com/PetopiaMBC/VRtest/commits/main/)

## 외부 리소스

강아지 모델과 애니메이션을 포함한 외부 에셋 및 XR 샘플·SDK를 사용합니다. 외부 리소스와 이 저장소에서 개발한 행동·상호작용 코드는 구분합니다. 에셋과 패키지의 이용 및 재배포에는 각 제공자의 라이선스가 적용됩니다.

