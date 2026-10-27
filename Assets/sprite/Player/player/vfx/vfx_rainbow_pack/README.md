# Rainbow VFX 팩 사용법

미리보기 GIF(`preview.gif`)와 **같은 속도·같은 패턴**으로 재생되도록 만든 게임용 에셋입니다.
`demo.html`을 브라우저로 열면 17종이 실제 코드로 재생되는 걸 볼 수 있어요(아래 코드의 기준 구현).

## 1. 타이밍 규칙 (GIF와 동일)
- **20fps = 프레임당 50ms** (`vfx_manifest.json`의 `frameMs`)
- **loop**: 프레임을 순서대로 무한 반복 (`charge_loop`, `big_loop`, `aura`)
- **oneshot**: 프레임을 한 번만 재생하고 끝 → 게임에서는 그 시점에 오브젝트 삭제.
  (GIF에서 마지막에 0.4초 멈추는 건 미리보기용 대기이고, 게임에는 필요 없음)
- **variants** (`basic_muzzle` 3개, `rapid_muzzle` 4개): 시트의 **한 줄이 한 번의 발사**. 발사할 때마다 줄을 0→1→2…로 돌려 쓰면 무지개색이 바뀜.
- 도트 1칸 = 1px 원본(`sprites_1x`). 미리보기는 **3배 확대**. 엔진에서 반드시 **정수배 + Nearest(Point) 필터**.

## 2. 이펙트 목록
| 파일 | 종류 | 프레임 | 길이 | 언제 쓰나 |
|---|---|---|---|---|
| 01_gather | oneshot | 10 | 500ms | 차지 시작(점이 중심으로 모임) |
| 02_charge_loop | loop | 8 | 400ms | 차지 홀드 중 반복 |
| 03_release | oneshot | 6 | 300ms | 차지 해제(발사) 순간 |
| 04_big_loop | loop | 8 | 400ms | 풀차지 상태 반복 |
| 05_big_release | oneshot | 6 | 300ms | 풀차지 발사 순간 |
| 06_aura | loop | 16 | 800ms | 캐릭터 버프/오라 |
| 07_power_burst | oneshot | 10 | 500ms | 필살기 발동 |
| 08_basic_muzzle | oneshot ×3 | 4 | 200ms | 기본 공격 발사(총구) |
| 10_basic_impact | oneshot | 7 | 350ms | 기본 공격 적중 |
| 11_rapid_muzzle | oneshot ×4 | 3 | 150ms | 연사 발사 |
| 13_rapid_impact | oneshot | 5 | 250ms | 연사 적중 |
| 14_big_muzzle | oneshot | 6 | 300ms | 강공격 발사 |
| 16_big_impact | oneshot | 8 | 400ms | 강공격 적중 |
| 20_jump_dust | oneshot | 6 | 300ms | 점프 먼지(발밑) |
| 21_land_dust | oneshot | 7 | 350ms | 착지 먼지(발밑) |
| 22_dash_dust | oneshot | 7 | 350ms | 대시 먼지(발밑) |
| 23_air_jump_ring | oneshot | 7 | 350ms | 공중 점프 링(발밑) |

> **없는 것 6종**: basic_bolt, rapid_bolt, big_orb, arrow_release, energy_arrow, arrow_impact — 보내주신 zip에 시트가 없어서 포함하지 못했습니다. (미리보기 GIF에서는 자리만 레퍼런스 모양을 무지개색으로 바꿔 채워둠)

## 3. 어디에 붙이나 (피벗)
`vfx_manifest.json`의 `pivot` = 스프라이트 안에서 **게임 오브젝트 위치에 맞출 점**(1x 픽셀 좌표).
- **muzzle**: 총구 빛의 중심이 피벗 → 총구 위치(노즈)에 놓고, 오른쪽이 발사 방향. 왼쪽으로 쏠 땐 `flipX`.
- **dust**: 피벗 = 발 닿는 바닥 → 캐릭터 발 위치에 놓기. 대시 먼지는 이동 방향 반대로 flip.
- 나머지(gather/release/aura/impact/ring…): 프레임 중앙 = 이펙트 중심 → 대상 중심에 놓기.
- 이펙트는 캐릭터/탄보다 **위 레이어**, 알파 블렌딩(가산 합성 아님).

## 4. 조합 레시피 (차지샷 예)
1. 버튼 누름 → `gather` 1회 → 끝나면 `charge_loop` 반복
2. 일정 시간 홀드 → `charge_loop` 대신 `big_loop` (+ 필요 시 `aura`)
3. 버튼 뗌 → `release`(풀차지면 `big_release`) + `*_muzzle` 총구 위치에 + 탄 발사
4. 탄이 맞음 → 맞은 위치에 `*_impact` (일반=basic, 연사=rapid, 강공격=big)
5. 점프/착지/대시/공중점프 → 발 위치에 각각 dust/ring. 대시나 점프 이펙트는 끝나면 삭제.

## 5. 엔진별 설정
**공통 JS(Canvas)** – `demo.html` 소스 참고 (frameW/H, `drawImage(img, f*frameW, variant*frameH, …)`).

**Phaser 3**
```js
this.load.spritesheet('release','sprites_1x/03_release.png',{frameWidth:103,frameHeight:100});
this.anims.create({key:'release',frames:this.anims.generateFrameNumbers('release',{start:0,end:5}),frameRate:20,repeat:0});
this.add.sprite(x,y,'release').setScale(3).play('release').once('animationcomplete',s=>s.destroy());
// 루프: repeat:-1.  픽셀아트: new Phaser.Game({ render:{ pixelArt:true } })
```
(frameWidth/Height는 `vfx_manifest.json`의 `frameW/frameH` 값 사용)

**Unity**: 텍스처 Import → Sprite Mode *Multiple* → Slice *Grid By Cell Size*(frameW×frameH), **Filter Mode: Point**, **Compression: None**, Pivot은 manifest의 pivot. 슬라이스한 프레임으로 Animation 생성(샘플 20), oneshot은 Loop Time 끄고 끝에 `Destroy`. 풀링 권장.

**Godot 4**: `AnimatedSprite2D` → SpriteFrames에서 *Add frames from Sprite Sheet*(가로 frameW 기준) → FPS 20, oneshot은 Loop 끔 + `animation_finished`에 `queue_free()`. Project Settings → Textures → Default Filter **Nearest**.

## 6. 팁
- 한 화면에 같은 이펙트가 많이 뜨면(연사 impact 등) **오브젝트 풀링**.
- `loop` 이펙트는 상태가 끝나면 즉시 숨기거나 짧게 페이드아웃(알파)만 추가.
- 색 바꾸기: 시트 자체가 무지개 1사이클 색이라, 다른 색이 필요하면 셰이더 Hue Shift 또는 variant 선택으로.
