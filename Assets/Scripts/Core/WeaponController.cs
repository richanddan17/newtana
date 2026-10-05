using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 무기 컨트롤러 - 발사 흐름, 쿨다운, 탄약, 재장전 관리.
/// 계획서 weapon-projectile-system.md [1]~[10], [공통 규약 5] 반영.
/// PlayerAnimation에서 Muzzle 위치/조준 방향 가져옴.
/// </summary>
[DisallowMultipleComponent]
public class WeaponController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerAnimation playerAnimation;
    [SerializeField] PlayerStateController stateController;
    [SerializeField] ProjectilePool projectilePool;

    [Header("Weapon Data")]
    [SerializeField] WeaponData currentWeaponData;
    [SerializeField] List<WeaponData> availableWeapons = new(); // 무기 슬롯

    [Header("Runtime Ammo")]
    [SerializeField] int currentAmmoInMag = -1;
    [SerializeField] int currentReserveAmmo = -1;
    [SerializeField] bool isReloading = false;

    [Header("Fire Point Override (옵션)")]
    [SerializeField] Transform firePointOverride; // Muzzle 대신 쓸 커스텀 포인트

    // 상태
    float nextFireTime = 0f;
    int burstShotsRemaining = 0;
    float burstNextFireTime = 0f;
    bool isBurstFiring = false;

    // 이벤트 (피드백 연결용)
    public event Action<WeaponData> OnWeaponChanged;
    public event Action OnFire;                    // 발사 시 (머즐 플래시, 사운드 등)
    public event Action OnReloadStart;             // 재장전 시작
    public event Action OnReloadComplete;          // 재장전 완료
    public event Action<int, int> OnAmmoChanged;   // current, reserve

    // 프로퍼티
    public WeaponData CurrentWeapon => currentWeaponData;
    public bool IsReloading => isReloading;
    public int CurrentAmmoInMag => currentAmmoInMag;
    public int CurrentReserveAmmo => currentReserveAmmo;
    public float FireRate => currentWeaponData?.FireRate ?? 0.2f;
    public bool CanFire => CanFireInternal();

    void Awake()
    {
        if (playerAnimation == null) playerAnimation = GetComponent<PlayerAnimation>();
        if (stateController == null) stateController = GetComponent<PlayerStateController>();
        if (projectilePool == null) projectilePool = FindAnyObjectByType<ProjectilePool>();

        // 초기 무기 설정
        if (currentWeaponData == null && availableWeapons.Count > 0)
        {
            EquipWeapon(availableWeapons[0]);
        }
        else if (currentWeaponData != null)
        {
            InitializeAmmo(currentWeaponData);
        }
    }

    void Update()
    {
        if (currentWeaponData == null) return;

        HandleBurstFire();
        HandleReloadInput();
    }

    #region Weapon Management

    /// <summary>무기 장착</summary>
    public void EquipWeapon(WeaponData weaponData)
    {
        if (weaponData == null) return;

        currentWeaponData = weaponData;
        InitializeAmmo(weaponData);
        nextFireTime = 0f;
        isBurstFiring = false;
        burstShotsRemaining = 0;
        isReloading = false;

        OnWeaponChanged?.Invoke(weaponData);
    }

    /// <summary>무기 인덱스로 교체 (Next/Previous 입력용)</summary>
    public void SwitchWeapon(int direction) // +1: Next, -1: Previous
    {
        if (availableWeapons.Count <= 1) return;
        if (isReloading) return; // 재장전 중 교체 불가 (옵션)

        int currentIndex = availableWeapons.IndexOf(currentWeaponData);
        int newIndex = (currentIndex + direction + availableWeapons.Count) % availableWeapons.Count;
        EquipWeapon(availableWeapons[newIndex]);
    }

    void InitializeAmmo(WeaponData weapon)
    {
        if (weapon.UseAmmo)
        {
            currentAmmoInMag = weapon.MagazineSize;
            currentReserveAmmo = weapon.StartingAmmo;
        }
        else
        {
            currentAmmoInMag = -1; // 무한
            currentReserveAmmo = -1;
        }
        OnAmmoChanged?.Invoke(currentAmmoInMag, currentReserveAmmo);
    }

    #endregion

    #region Fire Logic

    /// <summary>발사 시도 (입력 홀드 시 매 프레임 호출)</summary>
    public void TryFire()
    {
        if (!CanFireInternal()) return;

        // 발사 방향: PlayerAnimation의 조준 방향
        Vector2 fireDir = playerAnimation?.GetCurrentAimDirectionVector() ?? Vector2.right;
        Vector3 firePos = GetFirePosition();

        // 발사 실행
        Fire(firePos, fireDir);
    }

    bool CanFireInternal()
    {
        if (currentWeaponData == null) return false;
        if (isReloading) return false;
        if (Time.time < nextFireTime) return false;
        if (!stateController.CanShoot) return false; // 상태 시스템 확인 (Hurt/Death/Dash 등)
        
        // 탄약 확인
        if (currentWeaponData.UseAmmo && currentAmmoInMag <= 0)
        {
            // 자동 재장전 옵션 (여기선 수동만)
            return false;
        }
        return true;
    }

    void Fire(Vector3 position, Vector2 direction)
    {
        // 발사 간격 설정
        nextFireTime = Time.time + currentWeaponData.FireRate;

        // 탄약 소모
        if (currentWeaponData.UseAmmo)
        {
            currentAmmoInMag--;
            OnAmmoChanged?.Invoke(currentAmmoInMag, currentReserveAmmo);
        }

        // 연사 모드 처리
        switch (currentWeaponData.FireMode)
        {
            case WeaponData.FireMode.Semi:
                // 단발: 다음 발사까지 대기
                break;
            case WeaponData.FireMode.Auto:
                // 연사: TryFire가 매 프레임 호출되면 자동 연사
                break;
            case WeaponData.FireMode.Burst:
                StartBurst();
                break;
            case WeaponData.FireMode.Shotgun:
                FireShotgun(position, direction);
                break;
        }

        // 단발/연사 첫 발 발사
        if (currentWeaponData.FireMode != WeaponData.FireMode.Burst &&
            currentWeaponData.FireMode != WeaponData.FireMode.Shotgun)
        {
            SpawnProjectile(position, direction);
        }

        // 이벤트 발생
        OnFire?.Invoke();

        // Phase 6: 발사 피드백
        TriggerFireFeedback(direction);

        // 리코일 (옵션)
        if (currentWeaponData.UseRecoil && stateController != null)
        {
            Vector2 recoilDir = -direction * currentWeaponData.RecoilForce;
            stateController.ApplyKnockback(recoilDir, 0.1f);
        }
    }

    void TriggerFireFeedback(Vector2 direction)
    {
        // CameraShake: 발사 반동
        if (CameraShake.Instance != null)
        {
            CameraShake.OnPlayerShoot(direction);
        }

        // HitStop: 아주 짧게 (옵션)
        // HitStop.Request(0.01f);
    }

    void StartBurst()
    {
        isBurstFiring = true;
        burstShotsRemaining = currentWeaponData.BurstCount;
        burstNextFireTime = Time.time;
    }

    void HandleBurstFire()
    {
        if (!isBurstFiring) return;

        if (Time.time >= burstNextFireTime && burstShotsRemaining > 0)
        {
            Vector2 fireDir = playerAnimation?.GetCurrentAimDirectionVector() ?? Vector2.right;
            Vector3 firePos = GetFirePosition();
            SpawnProjectile(firePos, fireDir);

            // Phase 6: 버스트 각 발사마다 피드백
            TriggerFireFeedback(fireDir);

            burstShotsRemaining--;
            burstNextFireTime = Time.time + currentWeaponData.BurstInterval;

            if (burstShotsRemaining <= 0)
            {
                isBurstFiring = false;
            }
        }
    }

    void FireShotgun(Vector3 position, Vector2 baseDir)
    {
        // 산탄: 여러 발 퍼뜨림
        int pelletCount = 5; // WeaponData에 추가 가능
        float spread = currentWeaponData.SpreadAngle;

        for (int i = 0; i < pelletCount; i++)
        {
            float angleOffset = Random.Range(-spread, spread);
            Vector2 dir = Quaternion.Euler(0, 0, angleOffset) * baseDir;
            SpawnProjectile(position, dir);
        }

        // Phase 6: 산탄 발사 피드백 (한 번만)
        TriggerFireFeedback(baseDir);
    }

    void SpawnProjectile(Vector3 position, Vector2 direction)
    {
        if (projectilePool == null || currentWeaponData.ProjectilePrefab == null)
        {
            Debug.LogWarning("[WeaponController] ProjectilePool or Prefab not set!");
            return;
        }

        var projectile = projectilePool.Get();
        if (projectile == null) return;

        // 런타임 데이터 구성
        var runtimeData = new ProjectileRuntimeData
        {
            OwnerTeam = GetComponent<TeamComponent>()?.Team ?? Team.Player,
            Damage = currentWeaponData.Damage,
            Speed = currentWeaponData.ProjectileSpeed,
            MaxRange = currentWeaponData.MaxRange,
            KnockbackForce = currentWeaponData.KnockbackForce,
            HitType = currentWeaponData.HitType,
            PenetrationMax = currentWeaponData.Penetration,
            PenetrationRemaining = currentWeaponData.Penetration,
            Owner = gameObject
        };

        projectile.transform.position = position;
        projectile.transform.right = direction;
        projectile.Initialize(runtimeData);
    }

    Vector3 GetFirePosition()
    {
        if (firePointOverride != null) return firePointOverride.position;
        return playerAnimation?.GetMuzzleWorldPosition() ?? transform.position;
    }

    #endregion

    #region Reload

    void HandleReloadInput()
    {
        if (!currentWeaponData.UseAmmo) return;
        if (isReloading) return;
        if (currentAmmoInMag >= currentWeaponData.MagazineSize) return;
        if (currentReserveAmmo <= 0) return;

        // 재장전 입력: Interact 키 (StateController의 오버레이 Reloading으로 처리됨)
        // 여기서는 상태 시스템의 오버레이 변경을 감지해서 처리
        if (stateController != null && stateController.CurrentOverlayState == PlayerState.Reloading)
        {
            StartReload();
        }
    }

    public void StartReload()
    {
        if (!currentWeaponData.UseAmmo) return;
        if (isReloading) return;
        if (currentAmmoInMag >= currentWeaponData.MagazineSize) return;
        if (currentReserveAmmo <= 0) return;

        isReloading = true;
        OnReloadStart?.Invoke();

        // 재장전 코루틴
        StartCoroutine(ReloadCoroutine());
    }

    System.Collections.IEnumerator ReloadCoroutine()
    {
        yield return new WaitForSeconds(currentWeaponData.ReloadTime);

        // 재장전 완료
        int needed = currentWeaponData.MagazineSize - currentAmmoInMag;
        int actual = Mathf.Min(needed, currentReserveAmmo);
        
        currentAmmoInMag += actual;
        currentReserveAmmo -= actual;

        isReloading = false;
        OnReloadComplete?.Invoke();
        OnAmmoChanged?.Invoke(currentAmmoInMag, currentReserveAmmo);
    }

    /// <summary>외부에서 재장전 취소 (피격, 대시 등)</summary>
    public void CancelReload()
    {
        if (!isReloading) return;
        isReloading = false;
        StopAllCoroutines();
        // 상태 시스템의 오버레이도 해제 필요
    }

    #endregion

    #region Public API

    /// <summary>탄약 추가 (아이템 획득 시)</summary>
    public void AddAmmo(int amount)
    {
        if (!currentWeaponData.UseAmmo) return;
        currentReserveAmmo += amount;
        OnAmmoChanged?.Invoke(currentAmmoInMag, currentReserveAmmo);
    }

    /// <summary>부활/리셋 시 상태 초기화</summary>
    public void ResetWeaponState()
    {
        isReloading = false;
        isBurstFiring = false;
        burstShotsRemaining = 0;
        nextFireTime = 0f;
        StopAllCoroutines();

        if (currentWeaponData != null)
            InitializeAmmo(currentWeaponData);
    }

    #endregion
}