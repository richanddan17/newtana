# Phase 7 테스트 설정 가이드 (Room, Camera, Checkpoint)

Room, 카메라, 체크포인트, 부활 시스템 통합 테스트용 프리팹/씬 구성 방법.

---

## 1. Room 프리팹 구성

### GameObject 구조
```
Room_01_Start (Layer: Default)
├── BoxCollider2D (Bounds, Trigger=true, Layer: RoomBoundary(11)) → Room 컴포넌트
├── (자식) Enemies
│   ├── Enemy_BasicShooter_1
│   ├── Enemy_BasicShooter_2
│   └── ...
├── (자식) Checkpoints
│   └── Checkpoint_Start
├── (자식) Objects (상호작용 오브젝트 등)
└── (자식) GroundCheckPoints (선택)
```

### Room 컴포넌트 설정
- `RoomId`: "room_01_start" (자동: 게임오브젝트 이름)
- `RoomName`: "Starting Area"
- `Bounds Collider`: 자신의 BoxCollider2D
- `Auto Calculate Bounds`: true
- `Activate Enemies On Enter`: true
- `Deactivate Enemies On Exit`: true (성능)

---

## 2. Checkpoint 프리팹 구성

### GameObject 구조
```
Checkpoint_Start (Layer: Checkpoint(12))
├── BoxCollider2D (Trigger=true, Layer: Checkpoint) → Checkpoint 컴포넌트
├── SpriteRenderer (체크포인트 비주얼) → Checkpoint.spriteRenderer
├── (자식) SpawnPoint (부활 위치, 플레이어 발 위치보다 약간 위)
└── (자식) ActivationEffect (선택: 파티클 프리팹)
```

### Checkpoint 컴포넌트 설정
- `CheckpointId`: "cp_start"
- `CheckpointName`: "Start Checkpoint"
- `Parent Room`: Room_01_Start (자동 할당)
- `Spawn Point`: SpawnPoint 자식 Transform
- `Activate On Touch`: true
- `Require Grounded`: false
- `One Time Activation`: true
- `Activation Effect Prefab`: 파티클 프리팹
- `Activation Sound`: 체크포인트 사운드
- `Sprite Renderer`: 본체 SpriteRenderer
- `Inactive Color`: 회색
- `Active Color`: 청록색
- `Saved Color`: 녹색

---

## 3. RoomCamera 설정

### GameObject
- 메인 카메라에 `RoomCamera` 컴포넌트 부착
- 또는 별도 빈 GameObject에 부착하고 `Camera Transform` = Main Camera

### RoomCamera 컴포넌트 설정
- `Camera Transform`: Main Camera
- `Room Manager`: 씬의 RoomManager
- `Follow Speed`: 10
- `Follow Offset`: (0, 0.5) - 플레이어 살짝 위
- `Use Smooth Damp`: true
- `Enable Look Ahead`: true
- `Look Ahead Distance`: 3
- `Look Ahead Speed`: 5
- `Player Animation`: Player의 PlayerAnimation
- `Confine To Room`: true
- `Boundary Padding`: 0.5
- `Transition Duration`: 0.5
- `Apply Shake Offset`: true

---

## 4. RoomManager 설정

### GameObject
- 빈 GameObject `RoomManager` 생성, 컴포넌트 부착

### RoomManager 컴포넌트 설정
- `Room Camera`: RoomCamera (또는 카메라 GameObject)
- `Respawn System`: RespawnSystem
- `Auto Find Rooms`: true
- `Debug Log Transitions`: true

---

## 5. RespawnSystem 설정

### GameObject
- 빈 GameObject `RespawnSystem` 생성, 컴포넌트 부착

### RespawnSystem 컴포넌트 설정
- `Player Health`: Player의 Health
- `Player State Controller`: Player의 PlayerStateController
- `Player Movement`: Player의 PlayerMovement
- `Player Weapon`: Player의 WeaponController
- `Room Camera`: RoomCamera
- `Room Manager`: RoomManager
- `Default Spawn Point`: 씬 시작 위치 Transform (체크포인트 없을 때)
- `Respawn Delay`: 1.5
- `Fade Duration`: 0.5
- `Respawn Invincibility Duration`: 1.5
- `Enemy Reset Policy`: CurrentRoomOnly
- `Reset Player Ammo`: true
- `Keep Permanent Progress`: true
- `Fade Image`: Canvas 하위의 검은색 Image (전체 화면, Raycast Target=false)
- `Fade Color`: 검정
- `Fade Curve`: EaseInOut

---

## 6. Canvas/Fade Image 설정

### UI 구조
```
Canvas (Screen Space - Overlay)
└── FadeImage (Image, 전체 화면 커버)
    - Color: (0,0,0,0) - 투명 시작
    - Raycast Target: false
    - Preserve Aspect: false
```

RespawnSystem의 `Fade Image`에 이 Image 할당.

---

## 7. Layer 설정 확인 (Project Settings > Tags and Layers)

| Layer | Name | 용도 |
|---|---|---|
| 11 | RoomBoundary | Room 카메라 경계 (Trigger) |
| 12 | Checkpoint | 체크포인트 감지 (Trigger) |

**Collision Matrix** 추가:
- RoomBoundary(11): 모든 충돌 ☐ (Trigger만)
- Checkpoint(12): Player(6)만 ☑, 나머진 ☐

---

## 8. 씬 구성 예시

### 최소 테스트 씬
```
Scene
├── RoomManager
├── RespawnSystem
├── ProjectilePool
├── Main Camera (RoomCamera)
├── Canvas
│   └── FadeImage
├── Player (Player 프리팹)
└── Rooms
    ├── Room_01_Start
    │   ├── Bounds Collider
    │   ├── Enemies...
    │   └── Checkpoint_Start
    ├── Room_02_Corridor
    │   ├── Bounds Collider
    │   ├── Enemies...
    │   └── Checkpoint_Mid
    └── Room_03_Boss
        ├── Bounds Collider
        ├── Boss Enemy...
        └── Checkpoint_Boss
```

---

## 9. 테스트 시나리오

### 체크포인트 활성화
1. Play 모드
2. Player가 Checkpoint_Start Collider 진입
3. Checkpoint 활성화: 색상 변경(청록), 이펙트/사운드 재생
4. RespawnSystem.currentCheckpoint = Checkpoint_Start

### Room 전환
1. Player가 Room_01_Start에서 Room_02_Corridor Bounds 진입
2. RoomManager.OnRoomChanged 발생
3. RoomCamera 전환 시작 (0.5초 부드러운 이동)
4. Room_02_Corridor의 적들 자동 활성화
5. Room_01_Start의 적들 비활성화 (성능)

### 사망 → 부활
1. Player 체력 0 → Health.Die() → RespawnSystem.OnPlayerDeath()
4. 페이드 아웃 (1.5초)
5. 플레이어 상태 초기화:
   - 위치 = Checkpoint_Start.SpawnPosition
   - Health/State/Weapon 리셋 (탄약 포함)
   - 부활 무적 1.5초 부여
6. 적 리셋 (CurrentRoomOnly: Room_01_Start의 적들 ResetHealth+SetActive)
7. RoomCamera.OnPlayerRespawn() → 즉시 카메라 이동
8. RoomManager.ForceSetCurrentRoom(Room_01_Start)
9. 페이드 인 (0.5초)
10. 부활 완료

### 영구 진행 유지 확인
1. 무기 획득 → 다른 Room 이동 → 사망 → 부활
2. 획득한 무기 슬롯 유지 확인
3. 체력 업그레이드 유지 확인

---

## 10. 디버그 체크리스트

- [ ] Room Bounds Collider가 Trigger=true, Layer=RoomBoundary(11)
- [ ] Checkpoint Collider가 Trigger=true, Layer=Checkpoint(12)
- [ ] RoomManager가 모든 Room 자동 수집함
- [ ] RoomCamera가 현재 Room 경계 내로 클램프됨
- [ ] Room 전환 시 카메라 부드럽게 이동 (0.5초)
- [ ] 체크포인트 접촉 시 활성화 피드백 발생
- [ ] 사망 시 페이드 아웃 → 부활 → 페이드 인 시퀀스 동작
- [ ] 부활 위치 = 체크포인트 SpawnPoint
- [ ] 부활 후 무적 시간 적용됨
- [ ] 현재 Room의 적들만 리셋됨 (CurrentRoomOnly)
- [ ] 영구 진행(무기/업그레이드) 유지됨
- [ ] Fade Image가 전체 화면 커버, Raycast Target=false

---

## 11. 다음 단계 (Phase 8)

- 적 유형 추가 (터렛, 근접, 비행)
- 무기 종류 추가, 탄약/재장전 UI
- 보스 패턴
- 밸런싱, Inspector 값 정리
- 성능 점검 (풀링, 동시 사격 제한)