# 01. VFX OFFSETS AND COORDINATES

> 우선순위: **1** (최우선)  
> 상태: **진행중**  
> 근거: `combat_plan_v1_8.txt` 4-2, 4-7, 11-1 항목 14번 / `combat_spec_v6/04_VFX_FRAME_TABLES.txt` 버그후보 A

## 목표
슬래시(공격 검기) VFX의 배치 좌표를 **Foot 기준 중심 좌표**로 맞춘다.

현재는 `frameOffsets`를 스프라이트 중심으로 사용하는 구조상, **bbox 좌하단** 기준값이 그대로 들어가있어 절반 크기만큼 어긋남.

## 배경 (분석)
- per-frame 공격 PNG: PPU 1200, pivot (0.5,0.5), 내용만 크롭
- `PlayerMeleeVfx.UpdateTransform` 은 `clip.frameOffsets[frameIndex]`를 발 앵커에 더해 위치 결정
- plans_v5/v1.8은 `좌하단(x0,y0)`을 기준으로 측정, 실제 적용시에는 **중심(xc,yc)** 으로 변환해야 함
- 우리가 계산한 delta가 정확히 절반 크기만큼 치우침

## 수정 대상
| 파일 | 수정 내용 |
|---|---|
| `Assets/Scripts/Core/PlayerMeleeVfx.cs` | `UpdateTransform()`에서 footAnchored일 때 중심 좌표 계산으로 변경 |
| `Assets/Scripts/Core/PlayerAnimationClip.cs` | 필요시 좌표계 주석 추가 |
| `Assets/PlayerAnimationSet.asset` | (선택) frameOffsets를 중심값으로 교체. 우선은 **코드 수정**으로 호환성 유지 |

## 좌표 변환 규칙
- 원점: Foot = 다리 중앙 x, 바닥선 y (y-up)
- 단위: 도트. 1도트 = 0.01 유닛
- pivot (0.5,0.5)의 크롭 스프라이트에서 중심 좌표 적용
- 좌우반전은 기존 로직(`facing * parentFacing`) 유지

## 변환식
```
bbox 좌하단(x0,y0), 크기(w,h) in dots
중심(xc,yc) = (x0 + w/2, y0 + h/2)
```

계획서 `4-3`, `4-4`, `4-5`, `4-7`의 **중심(도트)** 열 값을 기준으로 적용

## 적용할 중심 좌표 (계획서 기준)
### attack1Fx
| idx | 파일 | 중심(도트) |
|---|---|---|
| 0 | attack1_3 | (+33.0, +12.5) |
| 1 | attack1_4 | (+41.5, +12.5) |
| 2 | attack1_5 | (+52.0, +9.0) |
| 3 | attack1_6 | (+57.5, +8.0) |

### attack2Fx
| idx | 파일 | 중심(도트) |
|---|---|---|
| 0 | attack2_1 | (+30.5, +17.0) |
| 1 | attack2_2 | (+42.5, +18.5) |
| 2 | attack2_3 | (+45.0, +19.0) |
| 3 | attack2_4 | (+50.0, +26.5) |

### attack3Fx
| idx | 파일 | 중심(도트) |
|---|---|---|
| 0 | attack3_6 | (+56.8, +9.5) |
| 1 | attack3_7 | (+58.8, +9.0) |
| 2 | attack3_8 | (+72.8, +7.5) |
| 3 | attack3_9 | (+58.3, +3.5) |

### heavyFx
| idx | 파일 | 중심(도트) |
|---|---|---|
| 0 | heavy_0 | (-22.6, +7.5) |
| 1 | heavy_1 | (+28.9, +31.0) |
| 2 | heavy_2 | (+42.9, +30.0) |
| 3 | heavy_3 | (+56.4, +29.5) |
| 4 | heavy_4 | (+55.4, +28.0) |
| 5 | heavy_5 | (+66.9, +12.0) |

> heavy_0은 `vfx/Dash/`에 위치

## 구현 방안
**방안 A (권장, 유연)**: `PlayerMeleeVfx`에서 스프라이트 bounds를 이용해 중심 보정
```csharp
// 기존: foot += offset.x * facing, foot += offset.y
// 변경: 
Vector2 spriteSize = effect.sprite.rect.size / 1200f; // dots to units
Vector2 centerOffset = offset + spriteSize * 0.5f;
foot.x += centerOffset.x * facing;
foot.y += centerOffset.y;
```

**방안 B**: `PlayerAnimationSet.asset`의 `frameOffsets`를 직접 중심값으로 교체 (에디터 필요)

우선 **방안 A**를 코드에 적용합니다. 기존 에셋과 호환되면서도 계획서 규칙에 맞출 수 있습니다.

## 작업 목록
- [x] 문제 분석 및 좌표 계산
- [ ] `PlayerMeleeVfx.cs` 수정
- [ ] 컴파일 확인
- [ ] 에디터에서 attack1/2/3/heavy 위치 눈으로 검증
- [ ] 필요시 asset값도 중심값으로 동기화

## 검증
- attack1 3~6프레임: 휘두르는 검날이 자연스러워야 함
- attack2: 옆구리~상단 궤적이 부자연스럽지 않게
- attack3: 넓은 범위가 캐릭터 몸통을 가리지 않고 맞게
- heavy 21~26: 판정표(`05_COMBAT_TIMELINES`)와 시각적 정렬 확인
- 좌우반전(facing)시 위치 정확히 뒤집히는지 확인

## 다음 단계
1단계 완료 후 `02_INPUT_SYSTEM_REWORK.md` 진행
