# 09. 남은 전투 작업

> 기준일: 2026-10-28
> 현재 상태: 런타임 검증 통과 (`newtana-combat-validation-v3.log`, `ALL_PASS`, exit 0)
> 기준 계획: `omo/combat_plan_v2_0v.txt`, `.omo/combat_plan/00_MASTER_PLAN.md`

## 완료된 것

- 검 공격 1→2→3→강공격 흐름과 프레임 이벤트 연결.
- 최근접 적 탐색, attack1 대시, attack3 텔레포트, 피해/히트스톱/카메라 흔들림.
- 공격별 피격 VFX (`attack1HitFx`, `attack2HitFx`, `attack3HitFx`, `heavyHitFx`) 연결.
- 강공격 차지 루프/릴리즈 슬라이스와 VFX 프레임 오프셋 수정.
  - `PlayRange(heavy, 21, 29)`, `meleeFirstFrame=0`, `meleeLastFrame=5`.
- 마법/활 클립과 프레임별 재생 시간 지원.
- `Player.prefab`에 `PlayerCombatController` 및 관련 참조 배선.
- `mainscene.unity` 충돌 해결 완료(현재 `HEAD`와 동일, 충돌 마커 0).
- 임시 검증 하네스(`CombatValidationRunner`) 제거 완료.

## P0 - 출시 전 필수

- [ ] 실제 적 프리팹으로 전투 검증
  - [ ] 적에 `TeamComponent(Enemy)`, `Hurtbox`, `EnemyHealth`, `Collider2D`가 있는지 확인.
  - [ ] 적 레이어가 `7`인지 확인.
  - [ ] attack1이 최근접 적을 향해 대시하는지 확인.
  - [ ] attack3 텔레포트 후 피해가 들어가는지 확인.
  - [ ] 강공격 피해/히트스톱/카메라 흔들림 확인.
- [ ] attack3 좁은 콜라이더 대응 검증
  - 현재 히트 중심은 대상 중심에서 약 `1.215`, 반경 `1.1`.
  - 적 폭이 `1.0` 미만이면 빗나갈 수 있음. 필요 시 중심/반경 조정.
- [ ] `CombatAssetValidator` 실행
  - `mainscene.unity` 충돌이 해결되어 이제 실행 가능.
  - 검증 항목: 30프레임 강공격, body/vfx 클립, 프리팹/씬 배선, attack별 히트 FX.
- [ ] 플레이 모드 수동 QA
  - [ ] 검: attack1→2→3→heavy 연계, 선입력/후딜/취소 확인.
  - [ ] 이동: 대시, 벽/사다리, 공중 공격, Up/Down 공격.
  - [ ] 활: 홀드 0-4 → 5-10 루프, 8프레임 발사, 반복 발사, 종료.
  - [ ] 마법: gather 0.5초, Rapid 반복, Big 차지/해제, aura.
  - [ ] 피드백: 히트스톱 중복, 근접/비근접 카메라 흔들림 분리.

## P1 - 전투 완성도

- [ ] 강공격 이동 규칙 확정
  - 구현은 `omo/combat_plan_v2_0v.txt` §1.7에 따라 전방 런지 유지.
  - `combo_plan.json`/`.md`의 "강공격 위치 변화 없음"과 충돌하므로 문서 중 하나를 정리.
- [ ] `EnemyHealth` 피드백 세분화 검증
  - 근접 피해는 공격별 연출이 담당하므로 기본 카메라 흔들림을 생략.
  - 마법/활/기타 피해는 기존 카메라 흔들림 유지 확인.
- [ ] 프레임 이벤트와 VFX 정렬 육안 확인
  - attack1/2/3/강공격의 타격 프레임과 `hit_*` 시트 시작 프레임 일치 여부.
- [ ] 루프 구간 데이터 확인
  - `loopStartFrame`, `loopEndFrame`, `durationsMs`가 manifest와 실제 시트에서 일치하는지 확인.

## P2 - 정리

- [x] 생성된 `.meta`와 `PlayerAnimationSet.asset` 커밋 완료.
- [x] `Assets/Atttack 4/__MACOSX` 하위 폴더 메타 5개 커밋 완료. (macOS 압축 부산물이므로 추후 정리 가능)
- [x] `PlayerCombatController.cs` 추적 대상 포함 확인.
- [ ] `dotnet build` 선행 오류 확인
  - 기존 `Assets/Scripts/Core/RoomCamera.cs`, `Assets/Scripts/Core/Team.cs` 누락(`CS2001`).
  - Unity 배치 컴파일은 성공하므로 빌드 스크립트 정리 시 별도 처리.
- [ ] `PlayerAnimationSheetImporter`의 obsolete 경고(`TextureImporter.spritesheet`) 정리 여부 결정.

## 검증 로그

- 컴파일: `C:\Users\midme\AppData\Local\Temp\opencode\newtana-heavy-vfx-v8.log`
- 런타임: `C:\Users\midme\AppData\Local\Temp\opencode\newtana-combat-validation-v3.log`

## 관련 파일

- `Assets/Scripts/Core/CombatAnimationDemo.cs`
- `Assets/Scripts/Core/PlayerCombatController.cs`
- `Assets/Scripts/Core/PlayerAnimation.cs`
- `Assets/Scripts/Core/PlayerAnimation.Playback.cs`
- `Assets/Scripts/Core/PlayerAnimationSet.cs`
- `Assets/Scripts/Core/PlayerMovement.cs`
- `Assets/Scripts/Core/PlayerBowShot.cs`
- `Assets/Scripts/Core/EnemyHealth.cs`
- `Assets/Editor/ApplyCombatToPlayerPrefab.cs`
- `Assets/Editor/CombatAssetValidator.cs`
- `Assets/Editor/CombatManifestBuilder.cs`
- `Assets/Scripts/Editor/PlayerAnimationSheetImporter.cs`
- `Assets/Scripts/Editor/PlayerSheetGridDetector.cs`
- `Assets/Prefabs/Player.prefab`
- `Assets/PlayerAnimationSet.asset`
- `Assets/Scenes/mainscene.unity`

##우선 변경점 무조건 우선 변견 사항
- 콤보 수정 목록 (Attack_4 계획서 기준, dot 단위 → Unity 유닛은 1유닛 = 100dot)
기준 몸 높이: 24dot (0.24유닛, PPU 1200, 12px/dot)
작성: 2026-10-28

==================================================
A. 프리팹 인스펙터 값만 바꾸면 되는 것
   대상: Assets/Prefabs/Player.prefab (PlayerCombatController, CombatAnimationDemo, PlayerMovement)
   ※ 프리팹에 저장된 값이 스크립트 기본값을 덮어쓰므로 인스펙터에서 직접 수정해야 함
==================================================

[A-1] 1타 대시 (PlayerCombatController)
  attack1DashStopDistance   현재 0.8   → 0.1    (적 앞 10dot 정지)
  attack1DashRange          현재 7.2   → 1.0    (100dot 안의 적만 대시 대상)
  attack1DashDuration       현재 0.15  → 0.15   (변경 없음, 시간 고정)
  동작: 이동거리 = |적거리| - 10dot. 90dot → 80dot 이동, 50dot → 40dot 이동.

[A-2] 3타 순간이동 (PlayerCombatController)
  attack3TeleportBehindOffset  현재 1.6  → 0.2   (적 뒤 20dot)
  attack3TeleportRange         현재 7.2  → 1.0
  attack3TeleportFrame         4 (변경 없음, 4번 프레임 시작 1520ms)

[A-3] 공격 반경 (PlayerCombatController)
  attack1Radius                현재 1.1  → 0.5   (50dot)
  attack2Radius                현재 1.1  → 0.5
  attack3Radius                현재 1.1  → 0.5
  heavyRadius                  현재 1.6  → 0.8   (80dot)
  ※ 값은 시작값. 검기 이펙트 길이에 맞춰 인게임에서 조정 필요

[A-4] 강공격 전진 (CombatAnimationDemo)
  heavyLungeDistance           현재 1.6  → 0     (강공격은 위치 변화 없음)
  ※ 0으로 두면 ApplyLunge가 무시됨 (distance <= 0 체크)

[A-5] 넉백 (PlayerCombatController)
  attack1Knockback             현재 6    → 1.0
  attack2Knockback             현재 4    → 0.7
  attack3Knockback             현재 6    → 1.0
  heavyKnockback               현재 32   → 5.0
  ※ 실제 적 이동량 = knockback × 0.3 / 적 질량 → 감쇠 적용. 적 Rigidbody2D 값에 따라 달라짐.
     인게임에서 1타 약 6dot, 2타 약 4dot, 3타 약 6dot, 강공격 약 32dot이 나오도록 재조정 필요

[A-6] 카메라 흔들림 세기 (PlayerCombatController)
  attack3ShakeAmplitude        현재 0.08 → 0.01  (1dot)
  attack3ShakeDuration         현재 0.26 (변경 없음, 260ms)
  heavyShakeAmplitude          현재 0.16 → 0.02  (2dot)
  heavyShakeDuration           현재 0.42 (변경 없음, 420ms)

[A-7] 연사 간격 (CombatAnimationDemo) — 결정 필요
  rapidShotInterval            현재 0.2  → (계획 v2.0은 0.12. 어느 쪽인지 결정 후 입력)

==================================================
B. 코드 수정이 필요한 것
==================================================

[B-1] 적 피격 플래시 시간
  파일: EnemyHealth.cs, FlashCoroutine
  현재: yield return new WaitForSeconds(0.05f);   (50ms 하드코딩)
  변경: 0.1f (100ms, 계획서)
  ※ 인스펙터에 값이 없어서 스크립트에서 직접 고쳐야 함

[B-2] 히트스톱 방식 (적 떨림 실시간)
  파일: HitStop.cs (Time.timeScale = 0 으로 전체 정지)
  현재: 히트스톱 동안 적·이펙트·애니메이션 모두 정지
  계획: 공격자(플레이어) 애니메이션만 정지, 적의 떨림(±1dot, 20ms 간격)과 이펙트는 실시간
  변경: 적 떨림 로직을 별도로 두고, timeScale 정지 대상에서 제외하거나 unscaled 시간으로 처리
  ※ 구현 범위가 커서 별도 작업으로 분리 권장

[B-3] 카메라 흔들림 축
  파일: PlayerCombatController.cs (ApplyAttack, CameraShake.Shake 호출)
  현재: CameraShake.Shake(new Vector2(facing, 0f), ...) → 항상 가로
  변경: 강공격은 세로 축으로 호출 (new Vector2(0, 1) 등). 3타는 가로 유지
  ※ 축 방향은 계획서에서 미정 항목. 확정 필요

[B-4] 적 넉백 공식 확인 (필요 시)
  파일: EnemyHealth.cs, 넉백 처리 (info.KnockbackForce * 0.3f, AddForce Impulse)
  ※ 계획서는 "히트스톱 종료 후 ease-out 누적 밀림"이라고 함.
     현재는 물리 impulse라 감쇠 방식이 다름. 계획과 맞추려면 적 이동을 직접 제어하는 방식으로 바꿔야 함

==================================================
C. 인게임에서 확인할 것 (값 조정)
==================================================

[C-1] 적 Rigidbody2D 질량과 감쇠 → 넉백 거리 확인 (목표: 6/4/6/32dot)
[C-2] 적 판정 콜라이더 크기와 위치 → 공격 반경(0.5/0.8)이 적에 닿는지 확인
[C-3] 대시 후 1타 판정이 적에 닿는지 확인 (대시 정지 10dot 기준)
[C-4] 3타 순간이동 후 3타 판정이 적에 닿는지 확인 (적 뒤 20dot 기준)
[C-5] 대시 먼지 VFX 위치 (발밑) 확인 — 22_dash_dust, 피벗 (34.5, 37.0)
[C-6] 대시 시트 5프레임 30ms, 발 기준점 12dot 확인

==================================================
D. 계획서와 맞지 않는 항목 (정리 필요)
==================================================

[D-1] 저장소 계획서 combat_plan_v2_0v.txt의 "heavy lunge 1.6유닛/0.18초" → 강공격 전진 없음으로 수정
[D-2] 저장소 계획서의 대시·순간이동·반경 값을 Attack_4 기준 dot 값으로 통일
[D-3] 잔상(대시 잔상) 파일 없음 → 대시 먼지 VFX로 대체 (확정 여부 확인)
[D-4] IMG_1823 대시 VFX는 zip/저장소에 없음 → 사용하지 않음

==================================================
E. 우선순위
==================================================
1. A-1 ~ A-4 (대시, 순간이동, 반경, 강공격 전진) — 인스펙터 값만 바꾸면 됨
2. A-5 (넉백), A-6 (흔들림) — 인스펙터 값 변경 후 인게임 확인
3. B-1 (플래시 100ms) — 간단한 코드 수정
4. B-3 (강공격 세로 흔들림) — 축 결정 후 수정
5. B-2 (히트스톱 실시간), B-4 (넉백 방식) — 구현 범위가 커서 별도 작업
6. A-7 (연사 간격) — 결정 후 입력
