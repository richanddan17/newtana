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
