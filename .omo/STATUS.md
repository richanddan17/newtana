# Run & Gun Platformer — 진행 상황 정리 (STATUS)

> 마지막 갱신: 2026-10-07
> 기준 계획서: `.omo/plans(10_6)/` (11개)
> 검증 기준: Unity 6000.3.12f1, RunGun.Core 어셈블리 csc EXIT=0 / 0 errors

---

## 1. 현재 상태 요약

| 영역 | 코드 | 에셋/씬 | 비고 |
|---|---|---|---|
| 플레이어 입력/상태 시스템 | ✅ 완료 | — | crouch hold, shoot buffer, 3방향 조준 |
| 플레이어 이동 | ✅ 완료 | — | 코너/레지/정점 보정, 반전 감속 |
| 플레이어 애니메이션 | ✅ 코드만 | ❌ 리깅 전 | 컨트롤러/클립 0개 |
| 무기/투사체 | ✅ 완료 | ❌ | 프리팹 없음 |
| 적 AI/체력/애니메이션 | ✅ 완료 | ❌ | 적 프리팹/컨트롤러 없음 |
| 룸/카메라/체크포인트/리스폰 | ✅ 완료 | ❌ | 씬 배치 전혀 없음 |
| 타일맵 지형 | — | 🔲 **사용자 직접 작업** | Subway_tiles.png / tiles_out.png 보유 |
| 배경 (패러랠 무한) | 🔲 신규 작업 | 🔲 | nuvens_1/2/3, subway_BG, wall_subway 보유 |

**핵심 상태**: 코드(시스템)는 개편 완료 + 컴파일 검증 통과. **씬 콘텐츠/애니메이션/배경은 0%** — 씬은 Main Camera + Global Light 2D만 있는 빈 상태.

---

## 2. 완료된 것 (코드 개편, 2026-10-07)

5개 병렬 에이전트(A1~A5)로 `Assets/Scripts/Core` 13개 파일 수정. `RunGun.Core.rsp(+rsp2)` csc 검증 **EXIT=0 / 0 errors** 통과.

### A1 — 입력/상태 파이프라인 (3파일)

**`PlayerInputBuffer.cs`**
- Shoot buffer: `shootBufferDuration`(0.15f) + `shootBufferEnabled` Inspector 옵션
- Crouch hold: `IsCrouchHeld` / `SetCrouchHeld(bool)`
- `EndFrame()` 신설 — 1회성 플래그 정리 (프레임 시작이 아닌 프레임 끝)
- `Update()`는 타이머 감소만 → **프레임 플래그 소실 버그 수정** (인풋 시스템 콜백이 Update보다 먼저 발화해도 안전)
- `ClearTimers()` — 타이머만 초기화, 홀드 상태 유지. `SetBlocked` 시에도 동일
- `HasJumpBuffer` 추가

**`PlayerInputHandler.cs`**
- `AimInput` 프로퍼티, `OnCrouchPressed` 이벤트
- 콜백 람다 → 필드 델리게이트 저장 + OnDestroy 정확한 해제 (**람다 누수 수정**)
- `SnapTo3Directions` → Vector2 반환 (정면/위/공중아래, 대각선 없음), `AimIndexToVector(int)` Vector3 오버로드

**`PlayerStateController.cs`**
- 공개 API: `AimInput`, `AimDirectionIndex`(0/1/2), `HasJumpBuffer`/`ConsumeJumpBuffer()`, `HasShootBuffer`/`ConsumeShootBuffer()`, `IsCrouchHeld`, `CanUseCoyoteTime`, `NotifyJumpExecuted()`
- **Crouch 탈출 버그 수정**: `DetermineDesiredMoveState()`의 Crouch 고정 반환 제거
- `HandleCrouchInput()` → `IsCrouchHeld` 우선 (Old Input System 폴백)

### A2 — 룸/체크포인트/리스폰 감사 (4파일)

**`RoomCamera.cs`**
- Look Ahead `confineToRoom=false`에서도 독립 적용 (기존 버그)
- Player Transform 캐싱 (프레임마다 `FindGameObjectWithTag` 제거)
- `ValidateEnemyVisionRange()` — 적 감지 범위 > 카메라 시야 시 경고
- 죽은 코드 제거 (빈 `ApplyShake()`, `applyShakeOffset`, `cameraShake`)

**`RoomManager.cs`**
- 초기 Room 결정을 `Start()`로 이동 + `OnRoomChanged(currentRoom, null)` 발화 (Awake 순서 무관)
- 죽은 필드(`roomCamera`, `respawnSystem`), 빈 `OnRoomPlayerExited` 제거
- `RefreshRooms()` — 구독해제→재발견→재구독

**`Checkpoint.cs`**
- `playerLayerMask` 실사용 (레이어 필터, 0이면 태그만 = 하위 호환)
- `RespawnSystem` 캐싱, `OnDeactivated` 실제 발화

**`RespawnSystem.cs`**
- `OnRespawnStarted` 실제 발화, 리스폰 중 입력 차단(`SetInputBlocked`)
- `respawnDelay` 실사용 (페이드아웃 후 `WaitForSecondsRealtime`, HitStop 무시)
- **무적 타이밍 수정**: 페이드인 전에 `GrantRespawnInvincibility`
- 부활 위치 폴백: 체크포인트 → defaultSpawnPoint → 룸 첫 체크포인트 → 현재 위치 (**하드코딩 0,0,0 제거**)
- `GetSafeRespawnPosition()` — OverlapCircle 상향 탐색 + 룸 경계 클램프
- **카메라-룸 순서 교체**: `ForceSetCurrentRoom` 먼저 → 이전 룸 경계 클램프 방지
- `resetPlayerAmmo` 실기능화 (reserve 보존), `ClearEnemyProjectiles()` (적 총알 풀 반환)
- 사망으로 비활성화된 플레이어 GameObject 복구, 데드 필드/파라미터 제거

### A3 — 무브/애니메이션 (2파일)

**`PlayerMovement.cs`**
- **단일 지상 판정**: `IsGrounded` → `stateController.IsGrounded` (폴백: GroundCheck)
- **coyote 단일 출처**: 로컬 타이머 삭제 → `stateController.CanUseCoyoteTime`, 점프 시 `NotifyJumpExecuted()`
- 반전 감속(`turnDeceleration`/`airTurnDeceleration`/`instantTurnStop`), 공중 별도값(`airControlMultiplier` 등)
- 사격 배율(`whileShootingMultiplier`/`airWhileShootingMultiplier`/`lockAimWhileShooting` 기본 false)
- **정점 보정**: |vy| 작을 때 중력 약화 + 가속 보너스 (우선순위 최상)
- **코너 보정**: 상승 중만, 1회만, vy=0 전 / **레지 보정**: 작은 범위, 하강 중만
- 대시 중 코너 보정만, 외부 속도(넉백) 중 기본 코너 보정만 허용
- `GetAimDirection()` → `stateController.AimInput`

**`PlayerAnimation.cs`**
- `AimInput`/`AimDirectionIndex` 직접 사용 (자체 계산 제거)
- **flip 이중 기록 제거**: `flipByScale=true`면 scale 읽기만 (쓰기는 Movement 소유), `false`면 `flipX`

### A4 — 무기 (1파일)

**`WeaponController.cs`**
- `TryFire(Vector2 direction)` 오버로드 (적 AI 조준 전달용)
- **사격 미연동 버그 수정**: `Update()`에서 오버레이 Shooting + `CanFireInternal()` → `TryFire()` 자동 발사
- **NRE 수정**: `CanFireInternal()` `stateController != null` 가드 (적 대응)
- Shoot buffer 소비 (플레이어 경로 `TryFire()`에서), `canShootWhileDashing=false` 기본
- Hurt/Death/Dash 중 발사 차단, 기존 무기/재장전/탄약 로직 유지

### A5 — 적 AI/체력/애니메이션 (4파일)

**`EnemyHealth.cs`**
- **Stagger 순서 버그 수정**: 데미지 → poise 평가 → `EnterStagger()` → **그 후** `OnDamaged` 발화
  (이전: `OnDamaged`가 poise 전 발화 → `IsStaggered=false` 보고 Stagger 진입 실패)
- `EnterStagger`/`ExitStagger` 시 `enemyAI` 호출 연결 (주석 활성화)

**`EnemyAI.cs`**
- `EnemyType`/`AIData` 프로퍼티, `OnStateChanged(AIState, AIState)` 이벤트
- **조준 미전달 버그 수정**: `TickShoot()` → `TryFire(aimDir)`
- `EnterStagger()` public 메서드

**`EnemyAnimation.cs`**
- `OnStateChanged`/`OnDamaged`/`OnDied` 구독 + OnDestroy 해제
- AR=3방향(정면/위/아래), RPG/Sniper=정면 고정 (기존 5구역 제거)

**`EnemyAIData.cs`**
- `EnemyType`에 AR/RPG/Sniper 추가 (기존 값 유지), `aimDirectionCount`(기본 3)

---

## 3. 다음에 할 것

> **타일맵 제외** — 사용자가 직접 작업. 타일맵 스프라이트는 `Assets/sprite/tilemap/Subway_tiles.png`, `tiles_out.png` 이미 보유.

### 3-1. 패러랠 스크롤링 무한 배경 (신규 작업)

**목표**: 카메라 이동에 따라 레이어별 속도 차이로 입체감을 주고, 화면 폭을 넘어서도 끊김 없이 무한히 이어지는 배경.

**보유 에셋**: `Assets/sprite/background/`
- `nuvens/nuvens_1.png`, `nuvens_2.png`, `nuvens_3.png` (3레이어 — 먼/중간/가까운)
- `subway/subway_BG.png`, `subway/wall_subway.png` (지하철 테마)

**구현 항목**:
1. **`ParallaxBackground.cs`** (신규, Core 또는 Background 폴더)
   - 레이어별 `scrollFactor` (0.0 = 화면 고정, 1.0 = 카메라와 동일 속도, 그 사이 = 배경 속도)
   - 카메라 위치 추적 (RoomCamera 또는 Camera.main 기준)
   - 각 레이어 SpriteRenderer 기준 위치 + 카메라 이동량 × scrollFactor 오프셋
2. **무한 반복**
   - 스프라이트를 가로로 N개 복제해 이어 붙임 (또는 임포트 시 Wrap Mode Repeat + Material; URP 2D 기준 SpriteRenderer 반복은 복제 방식 권장)
   - 카메라가 스프라이트 폭의 절반을 넘으면 시작 위치로 리셋 (텔레포트 루프)
   - 세로 고정 (일반 횡스크롤 — 카메라 y 이동은 기본, 필요 시 scrollFactorY 별도 옵션)
3. **레이어 배치**: nuvens_3(가장 먼, factor 낮음) → nuvens_2 → nuvens_1(가장 가까움) → 필요 시 subway 배경
4. **Inspector 옵션**: `scrollFactor`, `tileWidth`, `useYScroll`, `sortingOrder` — 하드코딩 금지
5. 배경은 플레이어/적/지형과 겹치지 않도록 Sorting Layer/Layer(Neutral 등) 분리

**완료 기준**: 카메라가 좌우로 이동할 때 배경 레이어가 서로 다른 속도로 흐르고, 화면 경계에서 반복이 끊겨 보이지 않음.

### 3-2. 씬/프리팹 구성 (타일맵 외)

1. **플레이어 프리팹**: `SpriteSheet_player_sliced.png` + Animator/Rigidbody2D/Collider + 컴포넌트 세팅, Layer=6 (Player)
2. **적 프리팹 3종**: AR/RPG/Sniper (`ARMob.png`/`RPGmob.png`/`SniperMob.png`), Layer=7 (Enemy), `EnemyType`/`aimDirectionCount` 세팅
3. **룸 구성**: Room 오브젝트 + 경계 콜라이더 + 적 배치 + `EnemyVision.DetectRange` ≤ 카메라 시야 반경 확인
4. **체크포인트 배치** + RoomManager/RespawnSystem/카메라 오브젝트 씬 배치
5. ProjectSettings 확인: Layer 6/7/8 (Player/Enemy/Neutral), PhysicsMaterial2D(마찰 0)

### 3-3. 애니메이션 리깅

1. 스프라이트 슬라이싱 (Sprite Mode Multiple → `SpriteSheet_player_sliced` 확인)
2. **Animator Controller 4개**: Player / EnemyAR / EnemyRPG / EnemySniper
3. **Animation Clip 제작**: Idle/Run/Jump/Fall/Crouch/Shoot/Hurt/Die (플레이어), Idle/Move/Shoot/Hurt/Stagger/Die (적)
4. 파라미터 이름 매칭 (Inspector 지정 — 코드 하드코딩 없음):
   - Player: `speed`, `velocityY`, `grounded`, `crouch`, `dead`, `shoot`, `aimDir` (+ 상체 분리 옵션)
   - Enemy: `speed`, `velocityY`, `grounded`, `dead`, `aim`, `aimDir` + 트리거 `shoot`/`hurt`/`stagger`
5. 상태머신/트랜지션 연결 후 샘플 씬에서 동작 확인

### 3-4. 런타임 검증 (플레이 테스트)

1. 샘플 씬에 임시 바닥/벽 + 디버그(회색) 플레이어 스프라이트로 최소 씬 구성
2. 검증 포인트: shoot buffer 3방향 사격, crouch 진입/탈출, 점프/코요테, 코너 보정, stagger 순서, 리스폰 카메라, 적 조준 전달
3. **BossAI 동작 변화 확인**: `ForceStagger` 시 AI가 Stagger로 전환되는 변경 — 보스 쉴드 파괴 흐름 검증

### 3-5. 잔여 코드 갭 (범위 외로 남은 것들)

| 갭 | 설명 | 우선순위 |
|---|---|---|
| **ProjectilePool.Return 버그** | 첫 풀에 무조건 반환 — 리스폰 `ClearEnemyProjectiles`와 연관 | 높음 |
| **낙사 구제** | 절벽 걸침/즉시 부활/안전 발판 — PlayerMovement/Health 지상 판정 연동 필요 | 중간 |
| **전환 중 입력 잠금 옵션** | 계획상 선택사항, 기본값(입력 유지)은 충족 | 낮음 |
| **룸 밖 투사체 소멸** | ProjectilePool에 팀별 클리어 API 필요 | 낮음 |
| **영구 진행 디스크 저장** | 저장 시스템 자체 미존재 — 설계 필요 | 낮음 |

---

## 4. 참고 (세션 이어가기용)

- **검증 명령** (workdir = 프로젝트 루트):
  ```
  & "D:\coding\6000.3.12f1\Editor\Data\NetCoreRuntime\dotnet.exe" exec "D:/coding/6000.3.12f1/Editor/Data/DotNetSdkRoslyn/csc.dll" /nostdlib /noconfig /shared "@Library/Bee/artifacts/1900b0aE.dag/RunGun.Core.rsp" "@Library/Bee/artifacts/1900b0aE.dag/RunGun.Core.rsp2"
  ```
  EXIT=0 / 0 errors = 통과. RunGun.Core가 유일한 스크립트 어셈블리 (전체 검증).
- **MCP 파이프라인**: 포트 7800, `Library/Pipeline/.unity-pipeline-port`, evalToken으로 HTTP exec (에디터 제어/검증용)
- 스크립트 자체의 미해결 컴파일 경고는 대부분 기존 unused 필드(무해), 새 경고 `dashPressedThisFrame`은 대시 비활성 설계로 의도된 것
- 에이전트 구현 분해 기록 (재작업 시 참고):
  A1 입력/상태(3) / A2 룸·리스폰(4) / A3 무브·애니(2) / A4 무기(1) / A5 적(4)