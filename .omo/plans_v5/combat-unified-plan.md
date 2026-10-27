# Combat Unified Plan (v1)

> Single source of truth. Code implementation is out of scope for this document. Priorities: `attack_plan` > `combat_motion_frames_v2` > `Heavy attack animation plan` > `casting_plan`.  
> Coordinate system: foot = center x bottom of sprite, y-up in world/plan notation (canvas y-down converted).  
> Units: dots = game pixels at sprite scale.  
> PPU: motion/attack/heavy VFX = 1200. New rainbow 1x VFX = 300 (or 4x scale preserved).  

## 1) Asset Inventory

### Motion sheets (new/changed)
| id | file | grid | frames | notes |
|---|---|---|---|---|
| `heavyAttack` | `motion/23_heavy_attack.PNG` | 5x6 | 27 (480x456px cells, 40x38 dots @12x equiv.) | **Replaces** `17_heavy_attack-ezgif.com-gif-to-sprite-converter.png` (11 frames). High duplication risk (same id). Importer must disambiguate. |
| `attack1` | `motion/11_attack_1-...png` | 5x2 | 8 | from v2 |
| `attack2` | `motion/12_attack_2-...png` | 5x1 | 5 | from v2 |
| `attack3` | `motion/13_attack_3-...png` | 5x2 | 10 | from v2 |
| `bow` | `motion/18_bow-...png` | 5x5 | 22 | from v2 |
| `cast` | `motion/19_cast-...png` | 5x3 | 11 | from v2 |

Heavy frame mapping (by sheet index): `heavy_0`->char21, `heavy_1`->char22, `heavy_2`->char23, `heavy_3`->char24, `heavy_4`->char25, `heavy_5`->char26.  
Charge loop: sheet indices 3-20 = 18 frames (gather starts at index 3). Full charge ~28 frames (10 gather + 18 loop), release from index 21.

### Attack VFX (per-frame PNGs)
| attack | files | sizes (px) | notes |
|---|---|---|---|
| attack1 | `vfx/attack1/attack1_3.png..attack1_6.png` | 660x348, 576x348, 228x240, 72x72 | 4 frames (matches attack_plan) |
| attack2 | `vfx/attack2/attack2_1.png..attack2_4.png` | 612x456, 348x564, 264x432, 96x108 | 4 frames |
| attack3 | `vfx/attack3/attack3_5.png..attack3_8.png` | 816x252, 792x240, 480x84, 60x12 | 4 frames (attack_plan replaces B's 5-frame with 4-frame; last body frame 9 has no VFX) |
| heavy | `vfx/heavy/heavy_1.png..heavy_5.png` | 1140x792, 948x840, 624x828, 504x768, 180x192 | 5 frames for release; `vfx/Dash/heavy_0.png` (240x84, 20x7) must move/reference into `vfx/heavy/` |
| misc | `vfx/Dash/*` | varies | check references |

Notes: attack_plan sheets like `attack1_outlined.png`, `heavy_outlined.png` don't exist as actual assets - use per-frame PNGs. Per-frame VFX are cropped; pivot bottom-left with per-frame `(x0,y0)` in foot coords (y-up).

### Rainbow 1x VFX (analysis)
| file | dims | frames (est.) | notes |
|---|---|---|---|
| `09_basic_bolt.png` | 192x15 | 4 x 48x15 | est. |
| `12_rapid_bolt.png` | 168x15 | unclear (connected) | **[UNCONFIRMED]** frame boundaries unknown |
| `15_big_orb.png` | 492x39 | 6 x 82x39 | est. |
| `17_arrow_release.png` | 63x27 | 3 x 21x27 | est. |
| `18_energy_arrow.png` | 198x15 | 3 x 66x15 | est. |
| `19_arrow_impact.png` | 192x51 | 4 x 48x51 | est. |

Rainbow pack vfx (from combat_motion_frames_v2): gather 10, charge_loop 8, release 6, big_loop 8, big_release 6, aura 16, power_burst 10, basic_muzzle 4x3 variants, basic_impact 7, rapid_muzzle 3x4 variants, rapid_impact 5, big_muzzle 6, big_impact 8, jump_dust 6, land_dust 7, dash_dust 7, air_jump_ring 7.

## 2) Conventions (Single Source)

- **Frame notation**: use `frameIndex` (0-based) as single source; also show `F#` for readability.
- **Coords**: foot = (x center bottom, y bottom). y-up. FlipX for facing.
- **PPU**: motion/attack/heavy VFX = 1200. New rainbow 1x VFX = 300 (preserve world size).
- **Pivots**: manifest `[0.5,0.5]` wrong for muzzle/dust; use normalized pivots from B: `basic_muzzle (13.5/49,21/43)`, `rapid_muzzle (10.5/49,18/37)`, `big_muzzle (34.5/121,54/109)`, `jump_dust (36/73,34/37)`, `land_dust (63/127,25/28)`, `dash_dust (34.5/70,37/40)`, `air_jump_ring (33/67,18/37)`.
- **Blend**: alpha/Normal from v2; **discard** C's additive.
- **Projectile speed** (initial): BASIC `175 dots/s`, BIG `125 dots/s` (D). "16px/tick@20fps" (B) unit unclear → rejected as initial.
- **Fire rate/interval**: rapid interval `100ms` (20fps 2 ticks) (B); D's `166ms` rejected.
- **Aim**: 5-directional (B); fixed Y (D) rejected.
- **Hitstop**: 2 frames (100ms) on confirmed hits. Impact VFX: attack1/2 `basic_impact`, attack3/heavy `big_impact`.
- **Per-frame attack VFX placement**: bottom-left pivot, use per-frame `(x0,y0)` foot coords (y-up).

## 3) Melee Attacks

### Timing/frames (20fps base)
| attack | body frames (idx) | duration (ms) | hit window (body) | world move (B) | VFX frames | notes |
|---|---|---|---|---|---|---|
| attack1 | 3,4,5,6 | 50/50/50/100 | 3-4 | +4 | 4 (`attack1_3..6`) | last body frame 9 no VFX (attack3) |
| attack2 | 1,2,3,4 | 50/50/50/100 | 1-2 | +4 | 4 (`attack2_1..4`) |  |
| attack3 | 5,6,7,8 | 50/50/50/100 | 5-6 | +8 | 4 (`attack3_5..8`) | B's 5-frame replaced by 4-frame per A; body frame 9 no VFX |
| heavy (charged release) | 21,22,23,24,25,26 | 50/50/50/100/100/100 | 22-23 (24 optional/weak) | +6/+8/+6 = +20 total (y unchanged) | 6 (`heavy_1..5` + context) | heavy_0/4/5 no hit |

Combo: attack1->attack2->attack3 chains as per v2 (1-frame link at end). Heavy is separate state (charged).

### Attack VFX hitboxes (foot coords, y-up) - attack_plan
**attack1** (frames 3-6): window x +5.5~+60.5, y -2.0~+27.0 (hits on frames 3-4 primarily)
**attack2** (frames 1-4): x +5.0~+56.0, y -2.0~+36.0 (hits 1-2)
**attack3** (frames 5-8): x +22.8~+90.8, y -1.0~+20.0 (hits 5-6)
**heavy** (frames 21-26): per-frame ranges  
- heavy_0: x -32.6..-12.6, y +4..+11  
- heavy_1: x -18.6..+76.4, y -2..+64  
- heavy_2: x +3.4..+82.4, y -5..+65  
- heavy_3: x +30.4..+82.4, y -5..+64  
- heavy_4: x +34.4..+76.4, y -4..+60  
- heavy_5: x +59.4..+74.4, y +4..+20  

Hitstop 2 frames on confirmed hits. Impact: attack1/2 basic_impact, attack3/heavy big_impact.

## 4) Heavy Attack (Charged)

- **Sheet**: `motion/23_heavy_attack.PNG` 2400x2736, 5x6, 27 frames, cell 480x456 (40x38 dots @12x). Replaces `17_heavy_attack...`.
- **Charge**: indices 3-20 = 18 frames loop. Gather starts at index 3. Full charge = 28 frames (10 gather + 18 loop). Release from index 21.
- **Timings**: 50/50/50/100/100/100ms for indices 21-26 (A measured; replace C's 70/80ms).
- **World move**: +6/+8/+6 = +20 dots over 21-23, y unchanged (sprite jump baked in, hurtbox rises).
- **Hits**: heavy_1+heavy_2 confirmed (100ms each), heavy_3 optional/weak (100ms). heavy_0/4/5 no hit.
- **Hurtbox (y-up, foot-relative)**: 0:(-9,0)-(8,24),1:(-10,0)-(6,23),2:(-11,0)-(5,23),3-20:(-8,0)-(7,22),21:(-5,0)-(12,23),22:(-5,6)-(12,29),23:(-5,7)-(11,31),24:(-8,6)-(11,30),25:(-8,1)-(14,27),26:(-6,0)-(12,23).
- **VFX**: gather at F3 (char index context), charge_loop during 3-20, release/big_release at 21+, slash_heavy frames 7-11 of release sequence with big_muzzle/big_impact.
- **VFX placement (Heavy plan)**: VFX cells 115x70, offset from char pivot (-12,-28) in cell space → world offset relative to foot pivot becomes (+27.5,-30) in y-down/GIF terms (mapped to y-up). FlipX mirrored.

## 5) Casting (Bow/Staff)

- **FPS/timeline**: 20fps base per v2 (cast_plan notes 12fps in parts; v2 is canonical).
- **Cast frames (19_cast, 11 frames 0-10)**:
  0: idle/windup (83ms), 1: windup (83ms), 2: gather start/prep (83ms, gather), 3: gather (83ms), 4: gather max/charge (83ms, charge_loop start), 5: transition (66ms), 6: transition (50ms), 7: fire (100ms, locked), 8: recover (83ms), 9: recover (83ms), 10: recover/end (83ms)
- **Cancel**: 0-6 cancelable, 7 locked, 8-10 cancelable. Basic cancelable ~3 ticks after fire, Big ~5 ticks after fire.
- **Sockets (foot-relative, y-up, cells 12x)**: charge `(+8.25,+8.75)`, fire `(+13.9,+11.0)`. (Pivot ref 176,295 in 19_cast sheet.)
- **Charge**: gather 10 frames (500ms), charge_loop 8 frames loop, big_loop 8 frames loop. Full charge ~28 frames (10+18) as per B; 1.5s approximate replaced by 28-frame rule.
- **VFX sequence**: gather→charge_loop→release / big_loop→big_release, aura optional during big charge, power_burst on full charge.
- **Shots**:
  - **BASIC**: gather→charge_loop→release. Muzzle basic_muzzle (4x3 variants), bolt basic_bolt, impact basic_impact (7). Speed 175 dots/s. Fire from frame 7.
  - **RAPID**: short charge (gather + partial charge_loop), rapid fire every 100ms (2 ticks), 13 shots. Muzzle rapid_muzzle (3x4), bolt rapid_bolt, impact rapid_impact (5). Speed ~80% of basic? (v2 says 80%).
  - **BIG**: longer charge to big_loop (full charge), big_release. Muzzle big_muzzle (6), orb big_orb (6 frames est.), impact big_impact (8) + ground effect. Speed 125 dots/s. Aura/power_burst on full charge.

## 6) Bow

- **Frames**: bow 22 frames (F0-F21). v2 timeline:
  - F0-F2: equip/windup
  - F3-F4: draw (2 frames)
  - F5-F10: draw cycle (6 frames) - loop
  - F11+: repeat cycle pattern; F11-F16 repeat, F17-F21 last 5 frames (wrap/ending)
- **Firing**: arrow_release/energy_arrow/arrow_impact. Current mapping is placeholder (basic_muzzle/impact used as temp) until arrow VFX frames confirmed.
- **[UNCONFIRMED]**: exact fire frame in each cycle; final handling of F17-F21. **Default tentative**: loop F5-F10, end with F17-F21 as finish (5 frames). Needs confirmation.

## 7) Movement/Dust VFX

| VFX | frames | pivot | spawn |
|---|---|---|---|
| jump_dust | 6 | feet_ground (36/73,34/37) | jump frame ~F2 |
| land_dust | 7 | feet_ground (63/127,25/28) | land/crouch frame ~F2 |
| dash_dust | 7 | feet_ground (34.5/70,37/40) | dash F1, flip with facing, behind character |
| air_jump_ring | 7 | ring_center (33/67,18/37) | double jump ~F5 |

## 8) Codebase Integration Points (Gaps)

| area | current state | required change |
|---|---|---|
| `PlayerState` | no Attack/Heavy/Cast/Bow states | add states/machine |
| `PlayerStateController` | only Shooting triggers attack1 | combo/heavy/cast/bow triggers, input buffering |
| `PlayerAnimationSet` | no slash VFX clip fields; muzzle basic only | add slash VFX clips, per-attack/heavy/cast clips |
| `PlayerAnimation` / `.Playback` | uniform `frameInterval`, body/castFx/auraFx | per-frame hold/timeline events |
| `PlayerCastFx` | API exists, no gameplay trigger | wire to cast flow |
| `WeaponController` | projectile-based, `OnFire` no payload, muzzle basic fixed | payloads, melee hitbox path |
| `Hitbox` | none (melee) | add melee hitbox system; `HitStop`/`Hurtbox`/`DamageInfo`/`Health` exist |
| `PlayerInputHandler`/`PlayerInputBuffer` | move/look/attack/jump/dash/interact/crouch only | melee/cast/bow inputs |
| `PlayerSheetGridDetector` | scans only top-level `motion/`, `vfx/` (non-recursive) | scan subdirs (`attack1/2/3/heavy`) or adjust; handle duplicate ids |
| `PlayerAnimationSheetImporter` | manifest-based, ppu/pivot | per-entry PPU/pivot support if needed; handle duplicate `heavyAttack` (17 vs 23) |
| `AnimationPreviewRunner`/`Builder` | 22 body clips only | preview heavy/cast/VFX |
| `player_animation_manifest.json` | ppu 1200, old vfx entries may point to deleted files | regenerate/update for new assets; new PNGs need .meta/import |

**Critical risks:**
- `heavyAttack` id collision (17 and 23) → importer may pick wrong one (FirstOrDefault/sorting). Must disambiguate (prefer 23 or rename).
- Attack VFX in subfolders ignored by non-recursive scan.
- Manifest vfx entries may reference deleted files until reimport.
- `12_rapid_bolt` frame boundaries unknown.
- Bow F17-F21 and exact fire frames uncertain.
- Speed/interval units need validation in code.

## 9) Implementation Phases (Plan-Only)

1. **Asset prep**: move `vfx/Dash/heavy_0.png` to `vfx/heavy/` if needed; ensure new PNGs imported with correct settings. Regenerate/update manifest.
2. **Importer fixes**: recursive scan for `vfx/attack*`, `vfx/heavy/`; disambiguate `heavyAttack` (prefer new sheet `23_heavy_attack.PNG`); support per-entry PPU/pivot if needed.
3. **Animation data**: extend `PlayerAnimationSet`/`PlayerAnimationClip` for slash VFX and per-frame events/timings (50/50/50/100/100/100 for heavy 21-26, attack timings). Add hitstop/event timeline.
4. **State/input**: add Attack/Heavy/Cast/Bow states to `PlayerState`, wire `PlayerStateController`, add inputs to `PlayerInputHandler`/`PlayerInputBuffer`.
5. **Melee**: implement `Hitbox` system, hit windows per table, world move application, impact VFX selection (basic/big), 2-frame hitstop.
6. **Casting**: wire `PlayerCastFx`, charge/cancel rules, muzzle/bolt/impact per shot type, speeds 175/125, rapid 100ms interval.
7. **Bow**: implement bow timeline with tentative loop (F5-F10 repeat, F17-F21 finish) pending confirmation; placeholder arrow VFX until resolved.
8. **VFX/dust**: integrate dust/aura/power_burst with correct pivots.
9. **Preview/tools**: update preview to cover new clips/VFX.
10. **Validation**: verify frame counts, pivots, hit windows against tables.

## 10) Open Issues / [UNCONFIRMED]

- [ ] **rapid_bolt frames**: `12_rapid_bolt.png` connected, boundaries unknown (est 4x42) → need pixel gap analysis.
- [ ] **basic_bolt**: `09_basic_bolt.png` est 4x48 → confirm.
- [ ] **Bow fire timing**: exact fire frame per cycle, handling of F17-F21 (tentative: loop F5-F10, finish F17-F21).
- [ ] **Projectile speeds**: validate 175/125 dots/s against existing codebase units.
- [ ] **Rapid interval**: confirm 100ms matches gameplay feel vs 166ms.
- [ ] **Heavy VFX mapping**: confirm `heavy_0.png` location/reference and full slash_heavy frame mapping.
- [ ] **Manifest cleanup**: remove/update entries pointing to deleted old files before reimport.
- [ ] **Duplicate heavyAttack**: must prevent 17 from being selected.
- [ ] **Per-frame attack VFX**: confirm bottom-left pivot + per-frame (x0,y0) matches renderer coordinates exactly.

## Resolution Log (Conflicts Resolved)

| conflict | resolution | source |
|---|---|---|
| Heavy timing | use A measured 50/50/50/100/100/100ms (21-26) over C's 70/80ms | attack_plan |
| Heavy frames/sheet | use new `23_heavy_attack.PNG` (27 frames) replacing 17 | Heavy attack plan |
| Attack VFX frames | use attack_plan per-frame counts (attack3 4 frames, not B's 5) | attack_plan |
| World move (attacks) | use B's +4/+4/+8 | combat_motion_frames_v2 |
| Cast FPS/timeline | use v2 20fps/cancel rules | combat_motion_frames_v2 |
| Full charge | use B's 28 frames (10+18) | combat_motion_frames_v2 |
| Projectile speed/interval/aim | use D's BASIC 175/BIG 125, B's 100ms rapid, B's 5-directional | casting_plan + v2 (priority) |
| Blend | use alpha/Normal, discard additive | v2 |
| PPU/pivots | use manifest ppu 1200 + corrected normalized pivots for muzzle/dust | v2 + plans |
| Frame notation | use frameIndex 0-based + F# | all |

---

**File**: `.omo/plans_v5/combat-unified-plan.md`  
**Status**: Decision-complete for planning; code changes require addressing open issues above.
