# COMBAT PLAN MASTER

> 최우선 계획서: `combat_plan_v1_8.txt`  
> 기준: 현재 구현(`combat_spec_v6`)과 충돌하는 부분을 우선 수정  
> 목표: 계획서에 명시된 확정 규칙대로 전투 시스템을 정리

## 작업 순서 (우선순위)

| 순서 | 파일 | 목표 | 상태 |
|---|---|---|---|
| 1 | `01_VFX_OFFSETS_AND_COORDINATES.md` | **최우선** 슬래시/강공격 VFX 좌표를 Foot기준 중심좌표로 교체. `PlayerMeleeVfx` 버그 수정 | [진행중] |
| 2 | `02_INPUT_SYSTEM_REWORK.md` | 키바인딩 개편 (Q=대시, E=마법모드, W/S+좌클릭 Up/Down Attack, 마우스 스크롤 순환, 우클릭 그래플) | [대기] |
| 3 | `03_SWORD_COMBO_EXPANSION.md` | 검 콤보 확장, 점프공격/Up/Down Attack, 강공격(power_burst), 이동디버프, 피격이펙트(release/big_release) | [대기] |
| 4 | `04_MAGIC_REWORK.md` | 마법 플로우 개편 (gather 0.5s, 기본모드 Rapid, release/bigRelease 제거, aura 조건 변경) | [대기] |
| 5 | `05_BOW_REWORK.md` | 활 플로우 개편 (windup 역재생, 5~10 반복, 10→4→0 마무리 후 종료, arrow_release 매발사) | [대기] |
| 6 | `06_MOVEMENT_VFX_AND_AURA.md` | 이동 VFX(jump_dust/land_dust/dash_dust/air_jump_ring), 마나회복중 aura 연동 | [대기] |
| 7 | `07_HITBOX_DAMAGE_HITSTOP.md` | 히트박스/피격박스, 적별 개별 피격이펙트, HitStop 연결 | [대기] |
| 8 | `08_IMPLEMENTATION_CHECKLIST.md` | 검증용 체크리스트 | [대기] |

## 원칙

- `combat_spec_v6/`는 **현재 상태 분석용**으로만 참고
- `combat_plan_v1_8.txt`의 확정값(`[확정]`, `[측정]`)을 우선 적용
- 구현 충돌 시 `[구현수정]` 항목을 최우선 처리
- 소켓/회전은 **자식 로컬** 기준. 좌우반전 시 x부호 일관성 유지
- VFX 부착 규칙 준수 (자식: 슬래시/모션추적, 월드: 충돌이펙트)

## 주요 파일 맵

| 경로 | 용도 |
|---|---|
| `Assets/Scripts/Core/CombatAnimationDemo.cs` | 전투 입력/흐름 메인 (수정 다수 예상) |
| `Assets/Scripts/Core/PlayerAnimation.cs` / `PlayerAnimation.Playback.cs` | 클립 재생/우선순위 |
| `Assets/Scripts/Core/PlayerMeleeVfx.cs` | **01번에서 수정** - 슬래시 좌표/부착 |
| `Assets/Scripts/Core/PlayerCastFx.cs` / `PlayerCastShot.cs` | 마법 VFX/탄 (04번) |
| `Assets/Scripts/Core/PlayerBowShot.cs` / `BowProjectile.cs` / `BowSpriteAnimator.cs` | 활/탄 (05번) |
| `Assets/Scripts/Core/PlayerState.cs` / `PlayerStateController.cs` | 상태/권한 (02번 입력체계와 연계) |
| `Assets/PlayerAnimationSet.asset` | 클립/VFX 참조 |
| `Assets/sprite/Player/player/vfx/*` | VFX 스프라이트 |

## 참조

- 원본: `99_COMBAT_PLAN_V1_8_RAW.txt`
- 현황: `../combat_spec_v6/`
