using UnityEngine;

/// <summary>
/// 무기 데이터 - 계획서 [공통 규약 5] ScriptableObject로 분리.
/// 플레이어와 적이 같은 구조 사용.
/// Phase 8 확장: 무기 특성, 특수 효과, 업그레이드 시스템 지원.
/// </summary>
[CreateAssetMenu(menuName = "RunGun/Weapon Data", fileName = "WeaponData_")]
public class WeaponData : ScriptableObject
{
    [Header("Identity")]
    public string WeaponName;
    public string Description; // 툴팁용
    public GameObject ProjectilePrefab; // ProjectileData 컴포넌트 포함 필수
    public WeaponCategory Category = WeaponCategory.Primary;

    public enum WeaponCategory
    {
        Primary,    // 주무기 (라이플, 샷건 등)
        Secondary,  // 보조무기 (권총 등)
        Heavy,      // 중화기 (로켓런처 등)
        Special     // 특수 (레이저, 플라즈마 등)
    }

    [Header("Stats")]
    public float Damage = 10f;
    public float FireRate = 0.2f;          // 초당 발사 수 = 1/FireRate
    public FireMode FireMode = FireMode.Auto;
    public int BurstCount = 3;             // Burst 모드일 때
    public float BurstInterval = 0.05f;    // 버스트 내 발사 간격

    [Header("Ballistics")]
    public float ProjectileSpeed = 20f;
    public float MaxRange = 15f;           // 수명 = MaxRange / Speed
    public float SpreadAngle = 0f;         // 탄 퍼짐 (도)
    public int Penetration = 0;            // 관통 횟수 (0=관통 없음)
    public bool UseGravity = false;        // 탄도에 중력 적용 (로켓 등)
    public float GravityScale = 1f;        // 중력 배율

    [Header("Knockback & Effects")]
    public float KnockbackForce = 3f;
    public HitType HitType = HitType.Bullet;
    public GameObject MuzzleFlashPrefab;   // 머즐 플래시 이펙트
    public GameObject HitEffectPrefab;     // 피격 이펙트
    public AudioClip FireSound;            // 발사 사운드
    public AudioClip ReloadSound;          // 재장전 사운드

    [Header("Ammo (Optional)")]
    public bool UseAmmo = false;
    public int MagazineSize = 30;
    public float ReloadTime = 1.5f;
    public int StartingAmmo = 90;          // 예비 탄약
    public bool InfiniteReserveAmmo = false; // 예비 탄약 무한

    [Header("Recoil (Optional)")]
    public bool UseRecoil = false;
    public float RecoilForce = 2f;
    public float RecoilRecovery = 10f;
    public Vector2 RecoilDirection = Vector2.left; // 반동 방향 (로컬)

    [Header("Special Effects")]
    public bool Explosive = false;         // 폭발 탄
    public float ExplosionRadius = 3f;     // 폭발 반경
    public float ExplosionForce = 10f;     // 폭발 넉백
    public bool PierceArmor = false;       // 방어구 관통
    public bool IgniteTarget = false;      // 화염 (DoT)
    public float BurnDamage = 5f;          // 초당 화염 데미지
    public float BurnDuration = 3f;        // 화염 지속 시간
    public bool FreezeTarget = false;      // 빙결 (속도 감소)
    public float FreezeSlow = 0.5f;        // 빙결 시 속도 배율
    public float FreezeDuration = 2f;      // 빙결 지속 시간

    [Header("Visual")]
    public Sprite Icon;                    // UI 아이콘
    public Color TracerColor = Color.yellow; // 트레이서 색상
    public float TracerWidth = 0.1f;       // 트레이서 두께

    /// <summary>초당 발사 수</summary>
    public float RoundsPerSecond => FireMode == FireMode.Auto ? 1f / Mathf.Max(FireRate, 0.001f) : 0f;

    public enum FireMode
    {
        Semi,       // 단발
        Auto,       // 연사
        Burst,      // 버스트
        Shotgun,    // 산탄 (한 번에 여러 발)
        Charge,     // 차징 (누르면 차징, 떼면 발사)
        Beam        // 빔 (지속 발사)
    }
}

/// <summary>
/// 무기 업그레이드 데이터 - 무기 개조용
/// </summary>
[CreateAssetMenu(menuName = "RunGun/Weapon Upgrade", fileName = "WeaponUpgrade_")]
public class WeaponUpgrade : ScriptableObject
{
    public string UpgradeName;
    public string Description;
    public UpgradeType Type;
    public float Value; // 곱셈/덧셈 값
    public int Cost;    // 업그레이드 비용
    public Sprite Icon;

    public enum UpgradeType
    {
        DamageMult,        // 데미지 배율
        FireRateMult,      // 연사속도 배율
        MagazineSizeAdd,   // 탄창 크기 증가
        ReloadTimeMult,    // 재장전 시간 배율
        RangeMult,         // 사거리 배율
        SpreadMult,        // 탄 퍼짐 배율
        PenetrationAdd,    // 관통 수 증가
        KnockbackMult,     // 넉백 배율
        RecoilMult,        // 반동 배율
        AddExplosive,      // 폭발 탄 추가
        AddBurn,           // 화염 추가
        AddFreeze,         // 빙결 추가
    }
}

/// <summary>
/// 투사체 런타임 데이터 - 풀링 시 초기화용
/// </summary>
[Serializable]
public struct ProjectileRuntimeData
{
    public Team OwnerTeam;
    public float Damage;
    public float Speed;
    public float MaxRange;
    public float KnockbackForce;
    public HitType HitType;
    public int PenetrationRemaining;
    public int PenetrationMax;
    public GameObject Owner; // 발사자 (자기 피격 방지용)
    
    // 특수 효과
    public bool Explosive;
    public float ExplosionRadius;
    public float ExplosionForce;
    public bool PierceArmor;
    public bool IgniteTarget;
    public float BurnDamage;
    public float BurnDuration;
    public bool FreezeTarget;
    public float FreezeSlow;
    public float FreezeDuration;
}