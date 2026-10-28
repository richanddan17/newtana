# 콤보 수정 목록

- 기준: **Attack_4 `combo_plan.md` / `combo_plan.json`** (최신 계획, 거리 값은 dot 단위)
- 단위 환산: **1유닛 = 100dot** (PPU 1200, 12px/dot)
- 기준 몸 높이: **24dot** (0.24유닛, `PlayerAnimationSheetImporter.cs`)
- 작성일: 2026-10-28

---

## 1. 인스펙터 값만 바꾸면 되는 것

대상: `Assets/Prefabs/Player.prefab`

> 프리팹에 저장된 값이 스크립트 기본값을 덮어씁니다. 반드시 인스펙터에서 직접 바꿔야 합니다.

### 1-1. 1타 대시 (`PlayerCombatController`)

| 항목 | 현재 | 변경 | 의미 |
|---|---|---|---|
| `attack1DashStopDistance` | 0.8 | **0.1** | 적 앞 10dot에서 정지 |
| `attack1DashRange` | 7.2 | **1.0** | 100dot 안의 적만 대시 대상 |
| `attack1DashDuration` | 0.15 | 0.15 (유지) | 시간 고정, 거리에 따라 속도가 변함 |

동작: **이동거리 = |적거리| − 10dot**
- 적이 90dot → 80dot 이동
- 적이 50dot → 40dot 이동

### 1-2. 3타 순간이동 (`PlayerCombatController`)

| 항목 | 현재 | 변경 | 의미 |
|---|---|---|---|
| `attack3TeleportBehindOffset` | 1.6 | **0.2** | 적 뒤 20dot에 착지 |
| `attack3TeleportRange` | 7.2 | **1.0** | 100dot 안의 적만 대상 |
| `attack3TeleportFrame` | 4 | 4 (유지) | 4번 프레임 시작(1520ms) |

### 1-3. 공격 반경 (`PlayerCombatController`)

| 항목 | 현재 | 변경 | 의미 |
|---|---|---|---|
| `attack1Radius` | 1.1 | **0.5** | 50dot |
| `attack2Radius` | 1.1 | **0.5** | 50dot |
| `attack3Radius` | 1.1 | **0.5** | 50dot |
| `heavyRadius` | 1.6 | **0.8** | 80dot |

> 시작값입니다. 검기 이펙트 길이에 맞춰 인게임에서 조정하세요.

### 1-4. 강공격 전진 (`CombatAnimationDemo`)

| 항목 | 현재 | 변경 | 의미 |
|---|---|---|---|
| `heavyLungeDistance` | 1.6 | **0** | 강공격은 위치 변화 없음 (Attack_4 기준) |

> 0이면 `ApplyLunge`가 무시됩니다 (`distance <= 0` 검사).

### 1-5. 넉백 (`PlayerCombatController`)

| 항목 | 현재 | 변경 | 목표 적 이동 |
|---|---|---|---|
| `attack1Knockback` | 6 | **1.0** | 약 6dot |
| `attack2Knockback` | 4 | **0.7** | 약 4dot |
| `attack3Knockback` | 6 | **1.0** | 약 6dot |
| `heavyKnockback` | 32 | **5.0** | 약 32dot |

> 실제 이동량 = `knockback × 0.3 / 적 질량`, 그 뒤 감쇠가 적용됩니다.
> 적 Rigidbody2D의 질량과 감쇠에 따라 달라지므로, 인게임에서 목표 거리가 나오도록 다시 맞춰야 합니다.

### 1-6. 카메라 흔들림 세기 (`PlayerCombatController`)

| 항목 | 현재 | 변경 | 의미 |
|---|---|---|---|
| `attack3ShakeAmplitude` | 0.08 | **0.01** | 1dot |
| `attack3ShakeDuration` | 0.26 | 0.26 (유지) | 260ms |
| `heavyShakeAmplitude` | 0.16 | **0.02** | 2dot |
| `heavyShakeDuration` | 0.42 | 0.42 (유지) | 420ms |

### 1-7. 연사 간격 — **결정 필요**

| 항목 | 현재 | 후보 |
|---|---|---|
| `rapidShotInterval` (`CombatAnimationDemo`) | 0.2 | 0.12 (계획 v2.0) 또는 0.2 (현재 프리팹) |

---

## 2. 코드를 고쳐야 하는 것

| # | 파일 | 현재 | 변경 | 비고 |
|---|---|---|---|---|
| 2-1 | `EnemyHealth.cs` `FlashCoroutine` | `WaitForSeconds(0.05f)` 50ms 하드코딩 | `0.1f` | 인스펙터 값이 없어서 스크립트에서 수정 |
| 2-2 | `PlayerCombatController.cs` `ApplyAttack` | `CameraShake.Shake(new Vector2(facing, 0f), ...)` 항상 가로 | 강공격만 세로 축 | 축 방향 확정 필요 (계획서 미정) |
| 2-3 | `HitStop.cs` | `Time.timeScale = 0`으로 전체 정지 | 적 떨림(±1dot, 20ms)과 이펙트는 실시간 | 구현 범위가 커서 별도 작업 |
| 2-4 | `EnemyHealth.cs` 넉백 | `AddForce(Impulse)` 물리 방식 | 계획의 "히트스톱 후 ease-out 누적 밀림"으로 바꿀지 결정 | 2-3과 함께 검토 |

---

## 3. 인게임에서 확인할 것

- [ ] 적 Rigidbody2D **질량·감쇠** 확인 → 넉백 목표 거리(6/4/6/32dot) 맞추기
- [ ] 적 **판정 콜라이더 크기·위치** 확인 → 반경 0.5 / 0.8이 닿는지
- [ ] 1타: 대시 후 적 앞 10dot에서 판정이 닿는지
- [ ] 2타: 1타 넉백 후에도 판정이 닿는지
- [ ] 3타: 순간이동 후(적 뒤 20dot) 판정이 닿는지 — **좁은 콜라이더 위험 (09 문서 P0)**
- [ ] 강공격: 제자리에서 판정이 닿는지
- [ ] 대시 먼지 VFX(`22_dash_dust`) 발밑 위치, 피벗 (34.5, 37.0)
- [ ] 대시 시트 5프레임 × 30ms, 발 기준점 12dot

---

## 4. 계획서와 맞지 않는 항목 (정리 필요)

| # | 문서 | 내용 | 조치 |
|---|---|---|---|
| 4-1 | `omo/combat_plan_v2_0v.txt` §1.7 | "heavy lunge 1.6유닛 / 0.18초" | 강공격 전진 없음으로 수정 |
| 4-2 | `omo/combat_plan_v2_0v.txt` | 대시·순간이동·반경을 유닛으로 기록 | Attack_4 기준 dot 값으로 통일 |
| 4-3 | `09_REMAINING_TASKS.md` P1 | "강공격 이동 규칙: 전방 런지 유지" | Attack_4 기준으로 결정 후 항목 종료 |
| 4-4 | 대시 잔상 | 파일 없음 | 대시 먼지 VFX(`22_dash_dust`)로 대체 |
| 4-5 | 대시 VFX `IMG_1823` | zip과 저장소 어디에도 없음 | 사용하지 않음 |

---

## 5. 우선순위

1. **1-1 ~ 1-4** (대시, 순간이동, 반경, 강공격 전진) — 인스펙터만 바꾸면 됨
2. **1-5, 1-6** (넉백, 흔들림) — 바꾼 뒤 인게임에서 확인
3. **2-1** (플래시 100ms) — 간단한 코드 수정
4. **2-2** (강공격 세로 흔들림) — 축 결정 후 수정
5. **1-7** (연사 간격) — 결정 후 입력
6. **2-3, 2-4** (히트스톱 실시간, 넉백 방식) — 별도 작업

---

## 6. 결정이 필요한 것

- [ ] 연사 간격: 0.12 또는 0.2
- [ ] 강공격 흔들림 축: 세로(−85°) 맞는지
- [ ] 넉백을 물리 방식으로 둘지, 계획처럼 누적 밀림으로 바꿀지
