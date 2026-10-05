# 무기 데이터 프리셋 생성 가이드 (Phase 8)

WeaponData Asset 우클릭 > Create > RunGun > Weapon Data로 생성 후 아래 값 설정.

---

## 1. 기본 무기 프리셋

### Pistol (기본 권총)
```
WeaponName: "Standard Pistol"
Category: Secondary
Damage: 12
FireRate: 0.25 (4발/초)
FireMode: Semi
ProjectileSpeed: 25
MaxRange: 20
SpreadAngle: 0
Penetration: 0
KnockbackForce: 2
UseAmmo: true
MagazineSize: 15
ReloadTime: 1.2
StartingAmmo: 60
UseRecoil: true
RecoilForce: 1.5
Icon: pistol_icon
TracerColor: Yellow
```

### Assault Rifle (돌격소총)
```
WeaponName: "Assault Rifle"
Category: Primary
Damage: 18
FireRate: 0.1 (10발/초)
FireMode: Auto
ProjectileSpeed: 30
MaxRange: 30
SpreadAngle: 2
Penetration: 0
KnockbackForce: 3
UseAmmo: true
MagazineSize: 30
ReloadTime: 2.0
StartingAmmo: 120
UseRecoil: true
RecoilForce: 2
Icon: rifle_icon
TracerColor: Yellow
```

### Shotgun (산탄총)
```
WeaponName: "Pump Shotgun"
Category: Primary
Damage: 8 (펠렛당)
FireRate: 0.8 (1.25발/초)
FireMode: Shotgun
ProjectileSpeed: 20
MaxRange: 12
SpreadAngle: 15
PelletCount: 8 (WeaponController에서 설정)
Penetration: 0
KnockbackForce: 6
UseAmmo: true
MagazineSize: 6
ReloadTime: 2.5 (한 발씩 장전)
StartingAmmo: 24
UseRecoil: true
RecoilForce: 5
Icon: shotgun_icon
TracerColor: Orange
```

### Sniper Rifle (저격총)
```
WeaponName: "Sniper Rifle"
Category: Primary
Damage: 50
FireRate: 1.5 (0.67발/초)
FireMode: Semi
ProjectileSpeed: 60
MaxRange: 50
SpreadAngle: 0
Penetration: 2 (관통 2회)
KnockbackForce: 8
UseAmmo: true
MagazineSize: 5
ReloadTime: 3.0
StartingAmmo: 20
UseRecoil: true
RecoilForce: 8
Icon: sniper_icon
TracerColor: Cyan
```

### SMG (기관단총)
```
WeaponName: "SMG"
Category: Primary
Damage: 10
FireRate: 0.08 (12.5발/초)
FireMode: Auto
ProjectileSpeed: 22
MaxRange: 18
SpreadAngle: 3
Penetration: 0
KnockbackForce: 2
UseAmmo: true
MagazineSize: 40
ReloadTime: 1.8
StartingAmmo: 160
UseRecoil: true
RecoilForce: 1.2
Icon: smg_icon
TracerColor: Yellow
```

---

## 2. 특수 무기 프리셋

### Rocket Launcher (로켓런처)
```
WeaponName: "Rocket Launcher"
Category: Heavy
Damage: 60 (직격) / 30 (폭발)
FireRate: 2.0 (0.5발/초)
FireMode: Semi
ProjectileSpeed: 15
MaxRange: 25
SpreadAngle: 0
Penetration: 0
KnockbackForce: 15
Explosive: true
ExplosionRadius: 4
ExplosionForce: 12
UseAmmo: true
MagazineSize: 1
ReloadTime: 3.5
StartingAmmo: 4
UseRecoil: true
RecoilForce: 12
Icon: rocket_icon
TracerColor: Red
```

### Plasma Rifle (플라즈마 라이플)
```
WeaponName: "Plasma Rifle"
Category: Special
Damage: 25
FireRate: 0.15 (6.67발/초)
FireMode: Auto
ProjectileSpeed: 25
MaxRange: 25
SpreadAngle: 1
Penetration: 1
KnockbackForce: 4
IgniteTarget: true
BurnDamage: 8
BurnDuration: 3
UseAmmo: true
MagazineSize: 25
ReloadTime: 2.2
StartingAmmo: 75
UseRecoil: true
RecoilForce: 2.5
Icon: plasma_icon
TracerColor: Magenta
```

### Cryo Gun (냉동총)
```
WeaponName: "Cryo Gun"
Category: Special
Damage: 5 (직격) + 빙결
FireRate: 0.3 (3.33발/초)
FireMode: Auto
ProjectileSpeed: 18
MaxRange: 15
SpreadAngle: 2
Penetration: 0
KnockbackForce: 1
FreezeTarget: true
FreezeSlow: 0.3 (30% 속도)
FreezeDuration: 2.5
UseAmmo: true
MagazineSize: 20
ReloadTime: 2.0
StartingAmmo: 60
Icon: cryo_icon
TracerColor: LightBlue
```

### Laser Rifle (레이저 라이플 - Beam 모드)
```
WeaponName: "Laser Rifle"
Category: Special
Damage: 30 (초당, Beam 모드 시)
FireRate: 0 (Beam 모드: 지속)
FireMode: Beam
ProjectileSpeed: 100 (즉시 히트)
MaxRange: 30
SpreadAngle: 0
Penetration: 5
KnockbackForce: 2
UseAmmo: true
MagazineSize: 100 (에너지 셀)
ReloadTime: 2.5
StartingAmmo: 300
UseRecoil: false
Icon: laser_icon
TracerColor: Red
```

---

## 3. 적 전용 무기 프리셋

### Enemy Pistol (적 권총)
```
WeaponName: "Enemy Pistol"
Category: Secondary
Damage: 8
FireRate: 0.3
FireMode: Auto
ProjectileSpeed: 20
MaxRange: 18
SpreadAngle: 0
Penetration: 0
KnockbackForce: 2
UseAmmo: false
```

### Enemy Rifle (적 소총)
```
WeaponName: "Enemy Rifle"
Category: Primary
Damage: 12
FireRate: 0.15
FireMode: Auto
ProjectileSpeed: 28
MaxRange: 25
SpreadAngle: 1
Penetration: 0
KnockbackForce: 3
UseAmmo: false
```

### Enemy Shotgun (적 산탄총)
```
WeaponName: "Enemy Shotgun"
Category: Primary
Damage: 6
FireRate: 1.0
FireMode: Shotgun
ProjectileSpeed: 18
MaxRange: 10
SpreadAngle: 20
PelletCount: 6
KnockbackForce: 4
UseAmmo: false
```

### Turret Cannon (포탑 포)
```
WeaponName: "Turret Cannon"
Category: Heavy
Damage: 20
FireRate: 0.5
FireMode: Auto
ProjectileSpeed: 35
MaxRange: 30
SpreadAngle: 0
Penetration: 1
KnockbackForce: 8
UseAmmo: false
```

---

## 4. 업그레이드 데이터 예시

### Damage Upgrade I
```
UpgradeName: "Enhanced Barrel"
Type: DamageMult
Value: 1.15 (15% 증가)
Cost: 100
```

### Fire Rate Upgrade I
```
UpgradeName: "Lightweight Bolt"
Type: FireRateMult
Value: 1.2 (20% 빠름)
Cost: 150
```

### Extended Magazine
```
UpgradeName: "Extended Mag"
Type: MagazineSizeAdd
Value: 10 (탄창 +10)
Cost: 200
```

### Explosive Rounds
```
UpgradeName: "Explosive Rounds"
Type: AddExplosive
Value: 1 (불리언)
Cost: 500
Description: "탄환이 폭발하여 범위 데미지 부여"
```

### Incendiary Rounds
```
UpgradeName: "Incendiary Rounds"
Type: AddBurn
Value: 1
Cost: 400
Description: "적에게 화염 효과 부여 (3초간 초당 8 데미지)"
```

---

## 5. WeaponController에서 Beam/Charge 모드 지원 추가 필요

WeaponController.cs에 다음 모드 처리 추가:

```csharp
// FireMode.Beam 처리 (지속 발사)
case WeaponData.FireMode.Beam:
    if (!isBeamFiring) StartBeam();
    break;

// FireMode.Charge 처리 (차징 후 발사)
case WeaponData.FireMode.Charge:
    HandleChargeFire();
    break;
```

---

## 6. ProjectileData에서 특수 효과 처리

ProjectileData.cs의 TryHitTarget에서:

```csharp
// 폭발 탄
if (data.Explosive)
{
    TriggerExplosion(hitPoint);
}

// 화염/빙결
if (data.IgniteTarget)
    target.ApplyBurn(data.BurnDamage, data.BurnDuration);
if (data.FreezeTarget)
    target.ApplyFreeze(data.FreezeSlow, data.FreezeDuration);
```

---

## 7. 생성 우선순위

1. **필수**: Pistol, Assault Rifle, Shotgun, Enemy Pistol/Rifle
2. **중요**: Sniper, SMG, Rocket Launcher, Enemy Shotgun
3. **확장**: Plasma, Cryo, Laser, Turret Cannon
4. **업그레이드**: Damage, FireRate, Magazine, Explosive, Burn