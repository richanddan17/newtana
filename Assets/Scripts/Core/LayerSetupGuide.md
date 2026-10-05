# Layer & Collision Matrix 설정 가이드

계획서 [공통 규약 3, 7] 준수: 레이어 구성과 Collision Matrix를 **한 번에 확정**하고 모든 문서가 참조.

---

## 1. 레이어 할당표 (Project Settings > Tags and Layers)

| Layer Index | Name | 용도 | 해당 Team |
|---|---|---|---|
| 0 | Default | 지형, 배경, 기본 오브젝트 | Neutral |
| 1 | TransparentFX | - | - |
| 2 | Ignore Raycast | UI, 마우스 통과 영역 | - |
| 3 | **Ground** | 플레이어 이동용 바닥/벽/플랫폼 | Neutral |
| 4 | Water | - | - |
| 5 | UI | - | - |
| **6** | **Player** | 플레이어 본체, Hurtbox | **Player** |
| **7** | **Enemy** | 적 본체, Hurtbox | **Enemy** |
| **8** | **Neutral** | 함정, 아이템, 상호작용 오브젝트 | **Neutral** |
| 9 | PlayerProjectile | 플레이어 발사 투사체 | Player |
| 10 | EnemyProjectile | 적 발사 투사체 | Enemy |
| 11 | RoomBoundary | 카메라 경계 (Trigger) | Neutral |
| 12 | Checkpoint | 체크포인트 (Trigger) | Neutral |
| 13~31 | (예약) | 향후 확장 | - |

> **중요**: Layer 6, 7, 8, 9, 10은 **User Layer**로 직접 추가해야 함.
> `TeamComponent` 또는 `TeamExtensions.LayerIndex()`와 **반드시 일치**시켜야 함.

---

## 2. Collision Matrix (Physics 2D Settings)

체크 = 충돌함 / 빈칸 = 충돌 안 함

| | Default (0) | Ground (3) | Player (6) | Enemy (7) | Neutral (8) | PlayerProj (9) | EnemyProj (10) | RoomBoundary (11) | Checkpoint (12) |
|---|---|---|---|---|---|---|---|---|---|
| **Default (0)** | ☐ | ☑ | ☑ | ☑ | ☑ | ☐ | ☐ | ☐ | ☐ |
| **Ground (3)** | ☑ | ☐ | ☑ | ☑ | ☑ | ☑ | ☑ | ☐ | ☐ |
| **Player (6)** | ☑ | ☑ | ☐ | ☐ | ☑ | ☐ | ☑ | ☐ | ☑ |
| **Enemy (7)** | ☑ | ☑ | ☐ | ☐ | ☑ | ☑ | ☐ | ☐ | ☐ |
| **Neutral (8)** | ☑ | ☑ | ☑ | ☑ | ☐ | ☐ | ☐ | ☐ | ☐ |
| **PlayerProjectile (9)** | ☐ | ☑ | ☐ | ☑ | ☐ | ☐ | ☐ | ☐ | ☐ |
| **EnemyProjectile (10)** | ☐ | ☑ | ☑ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| **RoomBoundary (11)** | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| **Checkpoint (12)** | ☐ | ☐ | ☑ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |

### 핵심 규칙
1. **PlayerProjectile(9) ↔ Enemy(7)**: ☑ (플레이어 총알이 적 맞힘)
2. **EnemyProjectile(10) ↔ Player(6)**: ☑ (적 총알이 플레이어 맞힘)
3. **투사체 ↔ 같은 팀**: ☐ (아군 사격 방지)
4. **투사체 ↔ 투사체**: ☐ (총알끼리 충돌 안 함)
5. **Player(6) ↔ Enemy(7)**: ☐ (직접 충돌 안 함, Hurtbox로 판정)
6. **Ground(3) ↔ 모든 이동체/투사체**: ☑
7. **RoomBoundary(11), Checkpoint(12)**: 모두 **Trigger**로만 사용 (Is Trigger = true), 충돌 매트릭스에서 모두 ☐

---

## 3. Collider 용도 분리 (계획서 [공통 규약 7, 8])

| 오브젝트 | 이동용 Collider | Hurtbox (피격판정) | 비고 |
|---|---|---|---|
| **Player** | BoxCollider2D (Layer: Player, IsTrigger=false) | Circle/BoxCollider2D (Layer: Player, IsTrigger=true) | 이동용은 Ground와 충돌, Hurtbox는 Projectile과 충돌 |
| **Enemy** | BoxCollider2D (Layer: Enemy, IsTrigger=false) | Circle/BoxCollider2D (Layer: Enemy, IsTrigger=true) | 동일 |
| **Projectile** | 없음 (Rigidbody2D Kinematic + Trigger Collider만) | 자기 자신이 Hurtbox 역할 | Layer: PlayerProjectile/EnemyProjectile |
| **Ground** | Tilemap Collider / Composite (Layer: Ground) | 없음 | 이동 판정 전용 |
| **RoomBoundary** | 없음 | BoxCollider2D (Layer: RoomBoundary, IsTrigger=true) | 카메라 Confiner용 |
| **Checkpoint** | 없음 | BoxCollider2D (Layer: Checkpoint, IsTrigger=true) | 플레이어 진입 감지용 |

---

## 4. 설정 적용 체크리스트

- [ ] Project Settings > Tags and Layers에서 User Layer 6~12 추가
- [ ] Project Settings > Physics 2D > Layer Collision Matrix 위 표대로 설정
- [ ] Player 프리팹: 이동 Collider(Layer 6, Solid) + Hurtbox(Layer 6, Trigger) 분리
- [ ] Enemy 프리팹: 이동 Collider(Layer 7, Solid) + Hurtbox(Layer 7, Trigger) 분리
- [ ] Projectile 프리팹: Layer 9 또는 10, Trigger Collider, Rigidbody2D Kinematic + Continuous
- [ ] RoomBoundary/Checkpoint: Layer 11/12, Trigger만
- [ ] `TeamComponent` 부착 시 `OnValidate`에서 레이어 자동 동기화 확인

---

## 5. 코드에서 레이어 참조 예시

```csharp
// Ground 체크 시
int groundMask = Team.Neutral.LayerMask() | (1 << 3); // Neutral + Ground

// 투사체 Raycast 시 (자신/발사자/아군 제외)
int projectileMask = ~(Team.Player.LayerMask() | Team.Enemy.LayerMask() | (1 << 2)); // Ignore Raycast도 제외

// 카메라 Confiner용
int roomBoundaryMask = 1 << 11; // RoomBoundary only
```

---

## 6. 변경 금지 원칙

**이 문서 확정 후 레이어 인덱스/이름/매트릭스 변경 금지.**
새 레이어 필요시 13번부터 추가하고, 기존 것 건드리지 말 것.