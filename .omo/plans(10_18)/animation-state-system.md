# Animation State System Plan (v5 - 최종 플레이어 에셋 기준, 스크립트 스프라이트 교체)

Unity 6 2D 횡스크롤 총게임의 플레이어 애니메이션 상태 시스템을 구현해줘.

에셋 규격(경로, 셀 크기, pivot, 프레임 수, Muzzle 보정)은 sprite-asset-spec.md를 따른다.
이 문서는 플레이어 애니메이션을 다룬다. 적 애니메이션은 enemy-ai-system.md에서 같은 원칙으로 처리한다.

==================================================
[0. v4에서 v5로 바뀐 점 (중요)]
==================================================

최종 플레이어 에셋(Assets/sprite/Player)이 확정되면서 구조가 아래처럼 바뀌었다.

- Body/Arm 분리 구조 **폐기**. 에셋에 arm/ 폴더가 없다. 상태별 전신 스프라이트가 조준 방향별로 존재한다
  (예: body_run_up45.png). 따라서 플레이어 비주얼은 스프라이트 1개 + Muzzle 자식 오브젝트뿐이다.
- Animator + Animation Clip 구조 **폐기**. 프로젝트에 .controller / .anim / .prefab이 0개이고,
  방향별 시트를 상태×방향 조합으로 고르는 작업은 스크립트가 하는 편이 단순하다.
  → 본 문서의 재생 방식은 **스크립트 스프라이트 교체**로 확정. Animator 파라미터는 더 이상 쓰지 않는다.
- 조준 방향 **5방향 확정** (front / up45 / up90 / down45 / down90). 에셋이 5방향을 모두 제공한다.
  현재 코드의 SnapTo3Directions(3방향)은 이 플랜에 맞춰 5방향으로 바뀐다. (10번 항목)
- Crouch **완전 제외**. 클립이 없고 입력 계획(player-state-input-system.md)에도 포함하지 않는다.
- 사격은 전신 스프라이트(body_shoot_<방향>, 4프레임)로 교체된다. 팔 오버레이가 아니다.
- 달리기 중 사격: 합본 스프라이트는 없지만 **이동은 계속하면서 shoot 모션을 재생**한다. (5번 항목)

==================================================
[1. 구조 (확정)]
==================================================

Player (루트, scale.x로 좌우 반전. PlayerMovement가 단일 권한)
 ├ Body  : SpriteRenderer 1개 + PlayerAnimation 스크립트 (상태×조준 방향 스프라이트를 코드로 교체)
 │   ├ Muzzle     : 빈 Transform (총알 생성 위치. 방향별 localPosition)
 │   └ MuzzleFX   : SpriteRenderer (vfx/fx_<방향>_muzzle4, 3프레임 연출)
 └ Collider / Hurtbox / GroundCheck / PlayerStateController 등 기존 컴포넌트

- 스프라이트가 전신(총+손 포함)이므로 팔을 그리는 별도 오브젝트가 없다.
- Avatar Mask, Animator Layer, Animation Event는 사용하지 않는다.
- Muzzle은 Body의 자식이다. (v4의 "Muzzle은 Arm의 자식" → 이제 Body의 자식)

==================================================
[2. 상태 목록과 실제 에셋 매핑]
==================================================

이동(또는 교체) 상태와 스프라이트 대응. 정확한 경로/프레임 수는 sprite-asset-spec.md [2] 표를 따른다.

| 상태 | 스프라이트 | 조준 방향별? | 프레임 |
|---|---|---|---|
| Idle (대기) | body/aim/body_aim_<dir> | O (5방향) | 1 |
| Run | body/run/body_run_<dir> | O (5방향) | 8 |
| Jump (상승) | body/jump/body_jump_<dir> | O (5방향) | 1 |
| Fall (하강) | body/fall/body_fall_<dir> | O (5방향) | 1 |
| 사격 중 | body/shoot/body_shoot_<dir> | O (5방향) | 4 |
| Hurt | body/hurt/body_hurt_<dir> | O (5방향) | 2 |
| Death | body/death.png | X | 6 |
| Wake (부활) | body/wake.png | X | 5 |
| Dormant (시작 전/사후 대기) | body/idle_dormant.png | X | 1 |
| Dash (활성화 시에만) | body/dash/dash_128wide.png | X | 13 |

- Idle에 aim 시트를 쓴다. (독립된 idle 스프라이트가 없고, aim이 조준 대기 자세다.)
- 사격은 이동 상태 위에 겹치는 **오버레이**다. (PlayerState.Shooting)
- 삭제된 상태: Parry, Counter, Combo, 근접 Attack, Crouch, Charge, Reload 연출(클립 없음).
- 현재 프로젝트에 실제 존재하는 애니메이션만 사용하고, 없는 상태는 새로 강제 생성하지 않는다.

==================================================
[3. 스프라이트 선택 로직 (핵심)]
==================================================

애니메이션은 Animator 파라미터가 아니라 **(상태, AimDir) 2차원 조회**로 스프라이트를 고른다.

선택 순서:

1. 사망이면 death 시트 (방향 무시, 루트 반전으로 처리)
2. 부활 연출이면 wake 시트
3. Hurt이면 hurt 시트 (현재 AimDir 사용)
4. 사격 오버레이(Shooting) 활성 → shoot 시트 + 4프레임 연출 재생
   (이동 중이어도 실행. 위치/속도는 이동 로직이 그대로 담당한다)
5. 그 외 → 이동 상태 시트 (Idle→aim, Run→run, Jump→jump, Fall→fall) × AimDir

- 스프라이트는 **값이 바뀔 때만** 교체한다. 매 프레임 같은 스프라이트를 다시 대입하지 않는다.
- 프레임 진행(8프레임 루프, 4프레임 연출 등)은 스크립트가 타이머로 관리한다.
- 상태 우선순위 자체는 player-state-input-system.md [2]를 따른다. (여러 문서에 복사하지 않는다)

==================================================
[4. 로직과 애니메이션의 분리]
==================================================

게임 로직이 상태를 판단하고, 애니메이션은 그 결과값만 읽는다.

애니메이션이 읽는 입력 (Set 프로퍼티 없음, 전부 읽기 전용):

- stateController.CurrentMoveState / CurrentOverlayState / AimDirectionIndex
- movement.HorizontalSpeed / VerticalSpeed / IsGrounded / MoveInput / FacingDirection

애니메이션이 반응하는 이벤트 (player-state-input-system [4]):

- PlayerStateEvents.OnMoveStateChanged(from, to)
- PlayerStateEvents.OnOverlayChanged(overlay, active)

애니메이션은 이동/사격 로직을 직접 호출하지 않는다.

Animator 파라미터(Speed, AimDir, IsShooting 등)는 v5에서 **삭제 대상**이다.
PlayerAnimation에서 Animator 참조/RequireComponent/파라미터 세팅을 제거한다. (10번 항목)

==================================================
[5. 사격 연출과 달리기 중 사격]
==================================================

- 발사 가능 여부, 연사 속도, 탄약은 Weapon 시스템이 결정한다.
- Weapon이 OnFire 이벤트를 보내면, 애니메이션은 shoot 시트 4프레임 연출만 재생한다.
- 달리기 중 사격 (확정): 이동을 멈추지 않는다. 스프라이트만 shoot 시트로 바꿔 4프레임을 재생하고,
  재생이 끝나면 사격 오버레이가 아직 켜져 있으면 다시 1프레임부터, 꺼지면 이동 상태 시트로 복귀한다.
  합본(달리기+사격) 스프라이트는 없으므로 다리 동작은 shoot 프레임에 그려진 자세를 따른다.
- 연사 간격이 연출 길이(4프레임 × 0.06초 = 0.24초)보다 짧으면,
  같은 방향이면 이어서(처음부터 재생하지 않고 프레임 유지), 방향이 바뀌면 새 방향 스프라이트로 처음부터 재생한다.
  연출이 튀지 않게 한다.
- 조준 방향이 바뀌면 스프라이트와 Muzzle localPosition을 **같은 프레임에 같이** 교체한다.
  (한 프레임이라도 어긋나면 총알이 엉뚱한 곳에서 나간다.)

총구(Muzzle):

- 발사 위치는 Muzzle Transform 기준. WeaponController.GetFirePosition() → PlayerAnimation.GetMuzzleWorldPosition().
- 방향마다 총구 위치가 다르므로 AimDir이 바뀔 때 localPosition을 sprite-asset-spec.md [4] 표에서 가져와 갱신한다.
- 좌우 반전은 루트 scale.x이므로 Muzzle도 자동으로 뒤집힌다. 월드 좌표를 하드코딩하지 않는다.

==================================================
[6. 조준 방향 (5방향 확정)]
==================================================

조준 방향 계산과 스냅은 입력 시스템이 하고, 애니메이션은 결과값(AimDirectionIndex)만 받는다.

| AimDir | 방향 | 스프라이트 접미사 | 발사 벡터 |
|---|---|---|---|
| 0 | 정면 | _front | (±1, 0) |
| 1 | 위 45° | _up45 | (±0.707, 0.707) |
| 2 | 위 90° | _up90 | (0, 1) |
| 3 | 아래 45° | _down45 | (±0.707, -0.707) |
| 4 | 아래 90° | _down90 | (0, -1) |

- 왼쪽 조준은 별도 스프라이트 없이 루트 scale.x = -1로 처리한다. (± = 루트 방향)
- 아래 방향(3, 4)은 공중에서만 허용이 기본값이다. 지상에서는 정면 계열로 스냅. (옵션은 input 문서)
- 5방향 모두 에셋이 실존하므로 "없는 방향 대체" 규칙은 더 이상 필요 없다.
- 정면 스냅 범위는 ±22.5°, 그 외 대각 구간은 45° 방향으로 스냅한다.

==================================================
[7. Hurt / Death / 부활 연출]
==================================================

Hurt:

- Hurt 진입 시 사격 연출을 중단하고 hurt 시트(2프레임)로 교체한다.
- 피격 애니메이션이 끝났다고 무조건 Idle으로 전환하지 말고 실제 게임 상태(지상/공중, 입력)를 다시 판단한다.
- 짧은 연속 피격에서 Hurt가 계속 재시작되어 멈춘 것처럼 보이지 않도록 무적 시간과 연동한다.
- 깜빡임(Flash/Blink)은 DamageFlash 컴포넌트가 별도로 처리한다. (현재 코드와 동일)

Death / Dormant / Wake:

- 사망 판정과 사망 애니메이션을 분리한다. (Health.Die() → EnterDeathState, 애니메이션은 이벤트 반응)
- Death 진입 후 자동 전환 없음. death 6프레임 재생 후 Dormant(idle_dormant) 유지.
- 부활은 Checkpoint/RespawnSystem이 처리하며, wake 5프레임을 재생한 뒤 정상 상태로 복귀한다.
- Death/Dormant/Wake 중에는 사격 연출을 재개하지 않는다.
- 부활 시 ResetAnimation()으로 사격/피격 잔여 상태를 정리한다. (PlayerAnimation 공개 API 유지)

==================================================
[8. 프레임 타이밍]
==================================================

| 연출 | 프레임 수 | 프레임 간격 | 루프 |
|---|---|---|---|
| Run | 8 | 0.08초 (약 12.5fps) | O |
| Shoot | 4 | 0.06초 | X (연속 발사 시 유지/재시작 규칙은 5번) |
| Hurt | 2 | 0.08초 | X (무적 시간과 연동) |
| Death | 6 | 0.10초 | X |
| Wake | 5 | 0.08초 | X |
| Dash | 13 | 대시 지속 시간 / 13 | X |
| MuzzleFX | 3 | 0.04초 | X |
| Idle(aim) / Jump / Fall | 1 | 정지 | - |

- 모든 값은 Inspector에서 조정 가능하게 열어두고, 위는 기본값이다.
- pivot 일관성: 에셋은 프레임마다 바운딩 박스 크기가 다르고 pivot은 중앙이다.
  런 애니메이션이 좌우/상하로 떨어지는지 반드시 플레이해서 확인하고,
  떨리면 sprite-asset-spec.md [6]의 피봇 보정 절차를 따른다.

==================================================
[9. 방향 전환]
==================================================

- 루트 scale.x 반전. 단일 권한은 PlayerMovement.UpdateFacingDirection()이다.
  (조준 입력 우선 → 이동 입력 → 유지. player-state-input-system.md [5] 참조)
- PlayerAnimation은 scale을 쓰지 않고 읽기만 한다. (현재 코드와 동일, 유지)
- SpriteRenderer.flipX는 사용하지 않는다. (Collider/Muzzle이 안 뒤집힌다)
- 애니메이션별로 스케일을 임의로 바꿔 캐릭터 크기가 튀지 않게 한다.
  (dash_128wide는 셀 폭이 128px로 다르므로 스프라이트 스케일 차이가 없도록 pivot/PPU 확인)

==================================================
[10. 구현 목표와 코드 수정 항목]
==================================================

구현 목표:

1. 스크립트 스프라이트 교체 코어 (상태×AimDir 2차원 조회 + 값 변경 시에만 교체 + 프레임 타이머)
2. 스프라이트 시트 등록 (sprite-asset-spec [2] 표 기준으로 배열 세팅)
3. 5방향 AimDir 반영 (스프라이트 + Muzzle 위치 동시 갱신)
4. 사격 오버레이 연출 (OnFire → 4프레임, 달리기 중에도 이동 유지)
5. MuzzleFX 3프레임 연출 (OnFire와 동시 시작)
6. Hurt/Death/Dormant/Wake 연동 (Death 후 자동 전환 없음, 부활 시 wake 재생)
7. 방향 전환은 기존 PlayerMovement 규칙 유지
8. dash는 상태 시스템이 실제로 사용할 때만 연결

현재 코드(PlayerAnimation.cs 등)에서 플래와 맞추기 위해 필요한 수정:

- [ ] Animator 의존성 제거: RequireComponent(Animator), Animator 파라미터 세팅(SetFloat/SetBool/SetInteger), 파라미터 이름 필드 전부 삭제
- [ ] 3방향 → 5방향: PlayerInputHandler.SnapTo3Directions()의 스냅 각도를 5구간(±22.5° 기준)으로 변경,
      PlayerStateController.AimDirectionIndex을 0~4로 산출, PlayerAnimation.GetCurrentAimDirectionVector()에 45° 벡터 추가
- [ ] Crouch 제거 정리: IsCrouching 파라미터, muzzleOffsets의 Crouch 항목, PlayerAnimation의 Crouch 분기
      (PlayerState enum의 Crouch 값 자체는 다른 문서 범위이므로 여기서는 애니메이션 경로만 제거)
- [ ] Hurt 연동: Hurt 클립이 실존하므로 TriggerHurtFlash() 단독 → OnMoveStateChanged(Hurt)에서 hurt 시트로 교체하도록 변경
- [ ] MuzzleOffsetSet 표를 sprite-asset-spec [4]의 5방향 표로 교체 (이동 상태별 오프셋 표는 삭제. 방향별만 유지)
- [ ] upperBodySeparated(상체분리) 플래그와 MuzzleOffsetSet의 moveState 필드 삭제 (더 이상 의미 없음)

적 애니메이션(EnemyAnimation.cs)도 같은 방향으로 정리하되, 범위는 enemy-ai-system.md를 따른다.
