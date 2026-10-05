# Phase 5 테스트 설정 가이드 (Enemy AI)

Enemy AI 시스템 통합 테스트용 프리팹/씬 구성 방법.

---

## 1. EnemyAIData Asset 생성

1. 우클릭 > Create > RunGun > Enemy AI Data
2. 이름: `EnemyAIData_BasicShooter`
3. 설정 예시:
```
EnemyName: "Basic Shooter"
DetectRange: 12
FieldOfView: 120
AttackRange: 10
MinAttackRange: 3
CanRetreat: true
AimTime: 0.6
ShootCooldown: 2.0
BurstCount: 3
BurstCooldown: 0.15
PatrolSpeed: 2
ChaseSpeed: 5
WeaponData: WeaponData_Pistol (또는 Enemy용 별도 무기)
MaxHealth: 60
MaxPoise: 15
```

---

## 2. Enemy 프리팹 구성 (Player 프리팹과 유사)

### GameObject 구조
```
Enemy_BasicShooter (Layer: Enemy)
├── SpriteRenderer (Body)
├── BoxCollider2D (이동용, Solid, Layer: Enemy)
├── CircleCollider2D (Hurtbox, Trigger, Layer: Enemy) → Hurtbox 컴포넌트
├── Rigidbody2D (Kinematic/Continuous, Freeze Rotation)
├── EnemyAI
├── EnemyVision
├── EnemyMovement
├── GroundCheck (자식: GroundCheckPoint)
├── WeaponController
├── EnemyHealth
├── TeamComponent (Team: Enemy)
└── (자식) Muzzle
└── (자식) GroundCheckFront
└── (자식) GroundCheckBack
```

### 컴포넌트 설정

#### EnemyAI
- `AI Data`: EnemyAIData_BasicShooter 할당
- `Health`: EnemyHealth
- `Vision`: EnemyVision
- `Movement`: EnemyMovement
- `Weapon Controller`: WeaponController
- `Team Component`: TeamComponent

#### EnemyVision
- `Detect Range`: 12 (AIData에서 덮어씀)
- `Field Of View`: 120
- `Eye Transform`: 머리 위치 자식 Transform (또는 루트)
- `Obstacle Layer Mask`: Ground(3) + Default(0)
- `Player Layer Mask`: Player(6)

#### EnemyMovement
- `GroundCheck`: GroundCheck 컴포넌트
- `Max Move Speed`: 5 (AIData에서 덮어씀)
- `Front Check`: GroundCheckFront 자식 Transform
- `Back Check`: GroundCheckBack 자식 Transform
- `Ground Layer Mask`: Ground(3)

#### WeaponController
- `Player Animation`: **비워둠** (적은 별도 조준 로직 필요)
- `State Controller`: **비워둠** (적용 안 함)
- `Projectile Pool`: 씬의 ProjectilePool
- `Current Weapon Data`: EnemyAIData의 weaponData와 동일
- `Fire Point Override`: Muzzle 자식 Transform

#### EnemyHealth
- `Max Health`: 60 (AIData에서 덮어씀)
- `Max Poise`: 15
- `Team Component`: TeamComponent
- `Sprite Renderer`: Body SpriteRenderer
- `Enemy AI`: EnemyAI

#### Hurtbox
- `Damageable`: EnemyHealth
- `Team Component`: TeamComponent

---

## 3. 적 전용 무기 데이터 (WeaponData_EnemyPistol)

Player 무기랑 분리 권장:
```
WeaponName: "Enemy Pistol"
ProjectilePrefab: EnemyBullet 프리팹 (Layer: EnemyProjectile)
Damage: 8
FireRate: 0.3
FireMode: Auto
ProjectileSpeed: 20
MaxRange: 18
KnockbackForce: 2
HitType: Bullet
UseAmmo: false
```

---

## 4. EnemyBullet 프리팹

PlayerBullet 복사 후 수정:
- Layer: **EnemyProjectile (10)**
- ProjectileData 컴포넌트 동일
- 스프라이트 색상 변경 (빨강/주황 등으로 구분)

---

## 5. ProjectilePool에 EnemyBullet 등록

1. ProjectilePool 오브젝트 선택
2. `Projectile Prefabs` 배열에 `EnemyBullet` 추가

---

## 6. EnemyAIData의 WeaponData 연결

EnemyAIData Asset에서 `WeaponData` 필드에 `WeaponData_EnemyPistol` 할당.

---

## 7. 씬에 적 배치 테스트

1. Enemy_BasicShooter 프리팹을 씬에 드래그
2. Player와 적당히 떨어진 위치 (감지 범위 내/외 테스트)
3. Ground 레이어 위에 배치 (GroundCheck 작동 확인)

---

## 8. 테스트 시나리오

### 순찰/대기
- AIData `StartPatrolling` true면 순찰, false면 Idle 대기
- 벽/낭떠러지에서 방향 전환 확인

### 감지/추적
- Player가 감지 범위(12) + 시야각(120°) 진입 시 Detect 상태
- 벽 뒤에 있으면 감지 안 됨 (Raycast 차단)
- 감지 시 Chase 상태로 접근

### 거리 유지/사격
- 공격 범위(10) 진입 시 Aim → Shoot
- 최소 거리(3) 이내 접근 시 Retreat (후퇴) 후 재조준
- 버스트 3발 발사 후 Cooldown (2초)

### 피격 반응
- Player 총알 맞으면:
  - EnemyHealth.TakeDamage → Poise 감소
  - Poise 임계값(15) 이하 → Stagger 상태 (0.5초)
  - 일반 피격 → Hurt 상태 (0.3초 무적)
- Stagger 중에는 이동/사격 중단

### 사망
- 체력 0 → Dead 상태
- 비활성화 (1초 후) → 체크포인트 리셋 시 재활성화

### 시야 잃음
- Player가 시야 벗어나면 Search 상태 (마지막 위치로 이동)
- 3초 후 순찰/대기 복귀

---

## 9. 디버그 체크리스트

- [ ] Enemy 레이어(7), EnemyProjectile 레이어(10) 설정
- [ ] Collision Matrix: EnemyProjectile(10) ↔ Player(6) ☑, Ground(3) ☑
- [ ] EnemyVision의 Obstacle Layer에 Ground/Default 포함
- [ ] EnemyMovement의 Front/Back Check Transform 배치 (발 앞/뒤)
- [ ] GroundCheck가 Ground 레이어 감지함
- [ ] WeaponController의 Fire Point Override = Muzzle
- [ ] EnemyAI의 AI Data 할당됨
- [ ] EnemyHealth의 EnemyAI 참조 연결됨
- [ ] EnemyBullet이 ProjectilePool에 등록됨

---

## 10. 여러 적 타입 만들기 (데이터만 교체)

| 타입 | DetectRange | AttackRange | Speed | Weapon | Health | Poise | 특징 |
|---|---|---|---|---|---|---|---|
| **Basic Shooter** | 12 | 10 | 4 | Pistol | 60 | 15 | 기본 |
| **Sniper** | 20 | 18 | 2 | Rifle | 40 | 8 | 원거리, 느림, 고데미지 |
| **Shotgunner** | 8 | 6 | 5 | Shotgun | 80 | 20 | 근접, 빠름, 산탄 |
| **Turret** | 15 | 12 | 0 | Rifle | 100 | 50 | 이동 없음, 고정 포대 |

> 같은 `EnemyAI` 코드, `EnemyAIData`만 바꿔서 프리팹 생성 가능.

---

## 11. 다음 단계 (Phase 6)

- Player Hurt/Death 애니메이션 최종 연동
- 적 애니메이션 연결 (Aim/Shoot/Hurt/Dead)
- 피격 피드백 (Hit Flash, 사운드, Hit Stop)
- 카메라 흔들림 연동