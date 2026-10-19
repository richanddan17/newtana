# Sprite Asset Spec (v5 - 최종 플레이어 에셋 실측 기준)

플레이어 스프라이트 에셋의 규격을 한곳에 정리한다.
animation-state-system.md, weapon-projectile-system.md, hitbox-hurtbox-system.md가 이 문서의 값을 참조한다.

에셋 위치: `Assets/sprite/Player/` (body/<상태>/ + vfx/)

==================================================
[0. v4에서 v5로 바뀐 점]
==================================================

- 참조 대상이 `bot_wheel_sprites`(body/arm/other + manifest.json)에서 **최종 에셋 `Assets/sprite/Player`로 교체**.
  manifest.json은 존재하지 않으므로 Muzzle 좌표는 [4]의 절차로 직접 보정한다.
- PPU 가정 16 → **실측 100**, 커스텀 pivot → **중앙 pivot (0.5, 0.5)**.
- Arm 스프라이트/폴더가 없다 → [4]의 "Arm 앵커" 표는 삭제되었다. 전신 스프라이트이므로 앵커가 필요 없다.
- 셀 크기 표준(64×32)이 없다. 프레임마다 바운딩 박스가 다르다. (프레임 크기 표는 [3])

==================================================
[1. 임포트 설정 (전 에셋 공통, 실측 일치)]
==================================================

Player와 Enemies 전 파일의 .meta가 완전히 동일하다. 불일치 없음.

- Texture Type: Sprite (2D and UI)
- Sprite Mode: Multiple (시트로 잘려 있음. `body_run_front_0` ~ `_7` 등으로 서브 스프라이트 존재)
- Pixels Per Unit: **100**
- Filter Mode: Point (no filter)
- Compression: None
- Pivot: **Center (0.5, 0.5)** (alignment 0)

PPU를 바꾸면 [4]의 좌표를 다시 계산한다. (현재는 PPU 100 확정)

==================================================
[2. 에셋 목록과 프레임 수 (실측)]
==================================================

방향 접미사: `front`, `up45`, `up90`, `down45`, `down90` (5방향 전부 실존)

| 경로 | 방향별 | 프레임 수 | 용도 |
|---|---|---|---|
| body/aim/body_aim_<dir>.png | 5 | 1 | Idle (조준 대기) |
| body/run/body_run_<dir>.png | 5 | 8 | Run |
| body/jump/body_jump_<dir>.png | 5 | 1 | Jump |
| body/fall/body_fall_<dir>.png | 5 | 1 | Fall |
| body/shoot/body_shoot_<dir>.png | 5 | 4 | 사격 오버레이 |
| body/hurt/body_hurt_<dir>.png | 5 | 2 | Hurt |
| body/dash/dash_128wide.png | - | 13 | Dash (128px 와이드 셀) |
| body/death.png | - | 6 | Death |
| body/wake.png | - | 5 | 부활 연출 |
| body/idle_dormant.png | - | 1 | Dormant (시작 전 / 사후 대기) |
| vfx/fx_<dir>_muzzle4.png | 5 | 3 | 총구 이펙트 |

주의: muzzle 파일명은 `_muzzle4`지만 실제 프레임 수는 **3**이다. 이름에 속지 말 것.
파일명의 `4`는 방향 세트 표기로 보이며, 연출 길이는 3프레임 기준으로 계산한다.

총 PNG 수: body 33 + vfx 5 = 38개.

==================================================
[3. 프레임 크기 (실측, 셀마다 다름)]
==================================================

프레임마다 바운딩 박스가 다르다. (중앙 pivot 기준이므로 위치 보정은 pivot이 담당)

| 상태 | 프레임 크기 (w × h px) |
|---|---|
| aim | 15~20 × 24 |
| run | 17~22 × 23~24 |
| jump | 17~22 × 24 |
| fall | 18~23 × 22 |
| shoot | 15~18 × 24 |
| hurt | 17~22 × 24 |
| dash | 128 × 24 (셀) |
| idle_dormant | 15 × 19 |
| vfx muzzle | 16~21 × 16~21 |

충돌 판정은 스프라이트 크기가 아니라 [6]의 고정 사이즈를 쓴다.
(스프라이트마다 크기가 다르므로 판정에 스프라이트 bbox를 쓰면 상태마다 판정이 달라진다.)

==================================================
[4. Muzzle 위치 (방향별, 보정 필요)]
==================================================

Muzzle의 localPosition은 **Body 기준**이다. (v4의 "Arm pivot 기준" → "Body 기준")

아래 값은 보정 전 시작값이다. manifest.json이 없으므로 다음 절차로 확정한다.

보정 절차:

1. Scene에서 플레이어를 정면(오른쪽)으로 서게 하고 AimDir 0 상태 스프라이트를 표시한다.
2. Scene View에서 총구 끝 픽셀 좌표를 Body 원점(피봇) 기준으로 확인한다.
3. localPosition = (px / 100, -py / 100) (PPU 100, 픽셀 y는 아래 방향이므로 부호 반전)
4. 5방향 각각 반복한 뒤 [4] 표를 갱신한다. 코드의 muzzleOffsets 배열에 입력한다.

| AimDir | 방향 | localPosition (보정 후 기입) |
|---|---|---|
| 0 | front | TBD |
| 1 | up45 | TBD |
| 2 | up90 | TBD |
| 3 | down45 | TBD |
| 4 | down90 | TBD |

- 방향마다 위치가 다르므로 AimDir이 바뀔 때 함께 갱신한다. (animation-state-system [5])
- 좌우 반전은 루트 scale.x이므로 좌향 시 자동으로 뒤집힌다. 별도 처리 없음.

==================================================
[5. 상태별 스프라이트 사용]
==================================================

경로와 프레임 수는 [2] 표, 선택 규칙은 animation-state-system.md [2][3]을 따른다.

| 상태 | 스프라이트 | 판정 |
|---|---|---|
| Idle | body/aim (5방향) | Hurtbox ON |
| Run | body/run (5방향) | Hurtbox ON |
| Jump / Fall | body/jump, body/fall (5방향) | Hurtbox ON |
| 사격 중 | body/shoot (5방향) | Hurtbox ON (이동 가능) |
| Hurt | body/hurt (5방향) | 무적 시간 연동 |
| Death | body/death | 판정 끔 |
| Dormant | body/idle_dormant | 판정 끔 |
| Wake | body/wake | 무적 |
| Dash | body/dash (128wide) | 상태 시스템이 사용할 때만 |

MuzzleFX(vfx/)는 판정이 없는 연출이다.

==================================================
[6. 확인이 필요한 항목]
==================================================

- 피봇 떨림 점검: pivot이 각 프레임 중앙이라 프레임마다 bbox가 다르면 Run이 떨어 보일 수 있다.
  플레이해서 확인하고, 떨리면 Sprite Editor에서 해당 시트 전체 프레임의 pivot을
  **동일한 커스텀 값(예: 발바닥 중앙)** 으로 통일한다. 통일 후에는 [4] 좌표도 다시 보정한다.
- dash_128wide: 폭이 128px로 다른 에셋과 다르다. pivot 통일 시 캐릭터가 좌우로 밀리지 않는지 확인.
- 총을 내린 자세(idle_carry / run_carry), charge, reload 전용 스프라이트는 최종 에셋에 없다.
  필요해지면 이 문서에 추가하고,在此之前 있는 상태를 억지로 만들지 않는다.

==================================================
[7. 충돌 판정과의 관계]
==================================================

- Hurtbox와 이동 Collider는 몸통 기준 고정 크기로 둔다. 시작값: 약 22 × 24 px
  (PPU 100에서 0.22 × 0.24 유닛). [3]의 프레임 크기 변화에 영향을 받지 않아야 한다.
- 총/손이 그려진 부분도 스프라이트에는 포함되지만 Hurtbox 크기를 키우지 않는다.
- 총구 이펙트(MuzzleFX)는 판정이 없는 연출이다.

==================================================
[8. 적 에셋 (참고)]
==================================================

Assets/sprite/Enemies/: ARMob(23f), RPGmob(14f), SniperMob(13f), Explosion_Particle(25f).
임포트 설정은 플레이어와 완전히 동일(PPU 100, Point, Center pivot, Multiple).
프로젝트 전체에 하나의 규격만 쓰인다.
