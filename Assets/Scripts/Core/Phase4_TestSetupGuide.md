# Phase 4 테스트 설정 가이드

Weapon, Projectile, Hitbox, Health 시스템 통합 테스트용 프리팹/씬 구성 방법.

---

## 1. Layer 설정 (Project Settings > Tags and Layers)

```
Layer 6: Player
Layer 7: Enemy  
Layer 8: Neutral
Layer 9: PlayerProjectile
Layer 10: EnemyProjectile
Layer 11: RoomBoundary
Layer 12: Checkpoint
Layer 3: Ground (이미 있음)
```

**Collision Matrix** (`LayerSetupGuide.md` 참조) 필수 설정.

---

## 2. Player 프리팹 구성

### GameObject 구조
```
Player (Layer: Player)
├── SpriteRenderer (Body)
├── BoxCollider2D (이동용, Solid, Layer: Player)
├── CircleCollider2D (Hurtbox, Trigger, Layer: Player) → Hurtbox 컴포넌트
├── Rigidbody2D (Kinematic/Continuous, Freeze Rotation)
├── PlayerStateController
├── PlayerInputHandler
├── PlayerMovement
├── GroundCheck (자식: GroundCheckPoint)
├── PlayerAnimation
│   └── Animator
├── WeaponController
├── Health
├── DamageFlash
└── TeamComponent (Team: Player)
```

### Muzzle 자식 오브젝트
```
Player
└── Muzzle (자식, 위치: 무기 앞쪽)
```

---

## 3. Enemy 프리팹 구성

```
Enemy (Layer: Enemy)
├── SpriteRenderer
├── BoxCollider2D (이동용, Solid, Layer: Enemy)
├── CircleCollider2D (Hurtbox, Trigger, Layer: Enemy) → Hurtbox 컴포넌트
├── Rigidbody2D (Kinematic/Continuous)
├── EnemyHealth
├── EnemyAI (Phase 5에서 구현)
└── TeamComponent (Team: Enemy)
```

---

## 4. Projectile 프리팹 (PlayerProjectile / EnemyProjectile)

### 공통 구조
```
Projectile (Layer: PlayerProjectile 또는 EnemyProjectile)
├── SpriteRenderer (총알 스프라이트)
├── CircleCollider2D (Trigger, Radius: 0.1~0.2)
├── Rigidbody2D (Kinematic, Continuous, Interpolate)
└── ProjectileData
```

### 프리팹 2개 생성
- `PlayerBullet` → Layer: PlayerProjectile (9)
- `EnemyBullet` → Layer: EnemyProjectile (10)

---

## 5. ScriptableObject 생성

### WeaponData Asset
1. 우클릭 > Create > RunGun > Weapon Data
2. 이름: `WeaponData_Pistol`
3. 설정 예시:
```
WeaponName: "Pistol"
ProjectilePrefab: PlayerBullet 프리팹
Damage: 10
FireRate: 0.2 (5발/초)
FireMode: Auto
ProjectileSpeed: 25
MaxRange: 20
SpreadAngle: 0
Penetration: 0
KnockbackForce: 3
HitType: Bullet
UseAmmo: false (초기 테스트)
```

### 추가 무기 예시
- `WeaponData_Shotgun`: FireMode: Shotgun, SpreadAngle: 15, Penetration: 0
- `WeaponData_Rifle`: FireRate: 0.1, ProjectileSpeed: 30, Penetration: 1

---

## 6. ProjectilePool 설정

1. 빈 GameObject `ProjectilePool` 생성
2. `ProjectilePool` 컴포넌트 부착
3. `Projectile Prefabs` 배열에 `PlayerBullet`, `EnemyBullet` 프리팹 등록
4. `Initial Pool Size`: 50, `Max Pool Size`: 200

---

## 7. Player 인스펙터 연결

### PlayerStateController
- `InputHandler`: PlayerInputHandler
- `GroundCheckPoint`: GroundCheckPoint (자식 Transform)
- `GroundLayerMask`: Ground (3)

### PlayerMovement
- `StateController`: PlayerStateController
- `GroundCheck`: GroundCheck 컴포넌트
- 이동 파라미터 조정

### PlayerAnimation
- `StateController`: PlayerStateController
- `Movement`: PlayerMovement
- `Animator`: Animator
- `SpriteRenderer`: Body SpriteRenderer
- `MuzzleTransform`: Muzzle 자식 Transform
- 파라미터 이름 Animator와 일치 확인

### WeaponController
- `PlayerAnimation`: PlayerAnimation
- `StateController`: PlayerStateController
- `ProjectilePool`: ProjectilePool (씬의 오브젝트)
- `CurrentWeaponData`: WeaponData_Pistol
- `AvailableWeapons`: 리스트에 무기들 추가

### Health
- `TeamComponent`: TeamComponent
- `SpriteRenderer`: Body SpriteRenderer
- `Movement`: PlayerMovement
- `StateController`: PlayerStateController

### DamageFlash
- `SpriteRenderers`: Body SpriteRenderer (배열)
- `Health`: Health

---

## 8. Enemy 인스펙터 연결

### EnemyHealth
- `TeamComponent`: TeamComponent
- `SpriteRenderer`: Body SpriteRenderer
- `EnemyAI`: (나중에 연결)

### Hurtbox
- `Damageable`: EnemyHealth
- `TeamComponent`: TeamComponent

---

## 9. Input System 확인

`Assets/InputSystem_Actions.inputactions`의 Player 맵에 다음 액션 확인:
- Move (Vector2) - WASD/LeftStick
- Look (Vector2) - RightStick/Mouse Delta → 조준용
- Attack (Button) - LeftClick/Gamepad West → 사격
- Jump (Button) - Space/Gamepad South
- Sprint (Button) - LeftShift/LeftStickPress → 대시
- Interact (Button) - E/Gamepad North → 재장전

---

## 10. 테스트 시나리오

### 기본 이동/사격
1. Play 모드
2. WASD 이동, 마우스/우측스틱 조준
3. 좌클릭/Attack 버튼으로 연사
4. 스페이스 점프, Shift 대시

### 피격 테스트
1. Enemy 프리팹에 EnemyHealth, Hurtbox 확인
2. Player 총알이 Enemy 맞히면:
   - EnemyHealth.TakeDamage 호출됨
   - Poise 감소, 임계값 이하 시 Stagger
   - 체력 0 시 비활성화

### 플레이어 피격 테스트
1. EnemyBullet 프리팹으로 Enemy가 발사 (EnemyAI 구현 후)
2. Player Hurtbox가 맞으면:
   - Health.TakeDamage → 무적 0.8초
   - StateController.EnterHurtState() → Hurt 상태
   - DamageFlash 깜빡임
   - 넉백 적용

### 재장전 테스트 (UseAmmo=true일 때)
1. WeaponData.UseAmmo = true
2. 탄약 소진 후 Interact(E키) 누르기
3. Reloading 오버레이 진입, 재장전 시간 후 탄약 채워짐

---

## 11. 디버그 체크리스트

- [ ] Layer 충돌 매트릭스 정확함
- [ ] Player/Enemy Hurtbox가 Trigger임
- [ ] Projectile이 ProjectileData 컴포넌트 가짐
- [ ] ProjectilePool에 프리팹 등록됨
- [ ] WeaponController의 ProjectilePool 참조 연결됨
- [ ] PlayerAnimation의 MuzzleTransform 연결됨
- [ ] Animator 파라미터 이름 일치 (Speed, VelocityY, IsGrounded, IsShooting, AimDir, IsDashing, Hurt, IsDead, IsReloading)
- [ ] TeamComponent가 올바른 Team 설정함
- [ ] Health의 OnDamaged/OnDied 이벤트 구독 확인

---

## 12. 다음 단계 (Phase 5)

EnemyAI 구현 시 필요:
- `EnemyAI` 컴포넌트 (상태 머신, 감지, 이동, 사격)
- `EnemyHealth`의 `enemyAI` 참조 연결
- `EnemyAI`에서 `EnterStagger()`, `ExitStagger()`, `EnterDead()` 메서드 호출