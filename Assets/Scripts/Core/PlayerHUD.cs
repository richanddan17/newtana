using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 HUD - 체력, 탄약, 무기, 미니맵 표시.
/// 계획서 Phase 8 확장.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Health playerHealth;
    [SerializeField] WeaponController playerWeapon;
    [SerializeField] PlayerStateController playerState;
    [SerializeField] RoomManager roomManager;

    [Header("Health UI")]
    [SerializeField] Slider healthBar;
    [SerializeField] Image healthBarFill;
    [SerializeField] Gradient healthBarGradient; // 체력 비율에 따른 색상
    [SerializeField] Text healthText;
    [SerializeField] GameObject healthWarningEffect; // 낮은 체력 경고

    [Header("Ammo UI")]
    [SerializeField] Text currentAmmoText;
    [SerializeField] Text reserveAmmoText;
    [SerializeField] GameObject ammoContainer;
    [SerializeField] Image ammoIcon;
    [SerializeField] Slider reloadProgressBar; // 재장전 진행도
    [SerializeField] GameObject noAmmoWarning;

    [Header("Weapon UI")]
    [SerializeField] Text weaponNameText;
    [SerializeField] Image weaponIcon;
    [SerializeField] Text fireModeText;

    [Header("Minimap")]
    [SerializeField] RawImage minimapRenderTexture;
    [SerializeField] Camera minimapCamera;
    [SerializeField] RectTransform playerMinimapIcon;
    [SerializeField] RectTransform[] enemyMinimapIcons;
    [SerializeField] RectTransform roomBoundsRect;
    [SerializeField] float minimapZoom = 1f;

    [Header("State Indicators")]
    [SerializeField] GameObject dashCooldownOverlay;
    [SerializeField] GameObject invincibilityOverlay;
    [SerializeField] Text dashCooldownText;

    [Header("Animation")]
    [SerializeField] float healthLerpSpeed = 5f;
    [SerializeField] float ammoPulseScale = 1.2f;
    [SerializeField] float ammoPulseDuration = 0.1f;

    // 상태
    float targetHealthRatio;
    Coroutine ammoPulseCoroutine;

    void Awake()
    {
        // 자동 참조
        if (playerHealth == null) playerHealth = FindAnyObjectByType<Health>();
        if (playerWeapon == null) playerWeapon = FindAnyObjectByType<WeaponController>();
        if (playerState == null) playerState = FindAnyObjectByType<PlayerStateController>();
        if (roomManager == null) roomManager = FindAnyObjectByType<RoomManager>();

        // 이벤트 구독
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += OnHealthChanged;
            playerHealth.OnDamaged += OnPlayerDamaged;
        }
        if (playerWeapon != null)
        {
            playerWeapon.OnAmmoChanged += OnAmmoChanged;
            playerWeapon.OnWeaponChanged += OnWeaponChanged;
            playerWeapon.OnReloadStart += OnReloadStart;
            playerWeapon.OnReloadComplete += OnReloadComplete;
            playerWeapon.OnFire += OnFire;
        }
        if (playerState != null)
        {
            PlayerStateEvents.OnOverlayChanged += OnOverlayChanged;
        }
        if (roomManager != null)
        {
            roomManager.OnRoomChanged += OnRoomChanged;
        }

        // 초기화
        InitializeHUD();
    }

    void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= OnHealthChanged;
            playerHealth.OnDamaged -= OnPlayerDamaged;
        }
        if (playerWeapon != null)
        {
            playerWeapon.OnAmmoChanged -= OnAmmoChanged;
            playerWeapon.OnWeaponChanged -= OnWeaponChanged;
            playerWeapon.OnReloadStart -= OnReloadStart;
            playerWeapon.OnReloadComplete -= OnReloadComplete;
            playerWeapon.OnFire -= OnFire;
        }
        if (playerState != null)
        {
            PlayerStateEvents.OnOverlayChanged -= OnOverlayChanged;
        }
        if (roomManager != null)
        {
            roomManager.OnRoomChanged -= OnRoomChanged;
        }
    }

    void Update()
    {
        // 체력 바 부드러운 업데이트
        if (healthBar != null && Mathf.Abs(healthBar.value - targetHealthRatio) > 0.01f)
        {
            healthBar.value = Mathf.Lerp(healthBar.value, targetHealthRatio, healthLerpSpeed * Time.deltaTime);
            
            // 그라디언트 색상 적용
            if (healthBarFill != null && healthBarGradient != null)
            {
                healthBarFill.color = healthBarGradient.Evaluate(healthBar.value);
            }
        }

        // 대시 쿨다운 표시
        UpdateDashCooldown();

        // 미니맵 업데이트
        UpdateMinimap();
    }

    void InitializeHUD()
    {
        if (playerHealth != null)
        {
            targetHealthRatio = playerHealth.HealthRatio;
            if (healthBar != null) healthBar.value = targetHealthRatio;
            if (healthBarFill != null && healthBarGradient != null)
                healthBarFill.color = healthBarGradient.Evaluate(targetHealthRatio);
            if (healthText != null) healthText.text = $"{Mathf.CeilToInt(playerHealth.CurrentHealth)}/{Mathf.CeilToInt(playerHealth.MaxHealth)}";
        }

        if (playerWeapon != null && playerWeapon.CurrentWeapon != null)
        {
            UpdateWeaponUI(playerWeapon.CurrentWeapon);
            OnAmmoChanged(playerWeapon.CurrentAmmoInMag, playerWeapon.CurrentReserveAmmo);
        }

        // 재장전 바 초기 숨김
        if (reloadProgressBar != null) reloadProgressBar.gameObject.SetActive(false);
        if (noAmmoWarning != null) noAmmoWarning.SetActive(false);
    }

    #region Health Events

    void OnHealthChanged(float currentHealth)
    {
        if (playerHealth == null) return;
        
        targetHealthRatio = playerHealth.HealthRatio;
        
        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(currentHealth)}/{Mathf.CeilToInt(playerHealth.MaxHealth)}";

        // 낮은 체력 경고
        if (healthWarningEffect != null)
        {
            bool showWarning = targetHealthRatio <= 0.3f;
            healthWarningEffect.SetActive(showWarning);
            
            // 깜빡임 효과
            if (showWarning)
            {
                StartCoroutine(FlashWarning());
            }
        }
    }

    System.Collections.IEnumerator FlashWarning()
    {
        if (healthWarningEffect == null) yield break;
        
        var img = healthWarningEffect.GetComponent<Image>();
        if (img == null) yield break;

        Color original = img.color;
        for (int i = 0; i < 3; i++)
        {
            img.color = new Color(original.r, original.g, original.b, 0.8f);
            yield return new WaitForSeconds(0.1f);
            img.color = new Color(original.r, original.g, original.b, 0.3f);
            yield return new WaitForSeconds(0.1f);
        }
        img.color = original;
    }

    void OnPlayerDamaged(DamageInfo info)
    {
        // 피격 시 화면 플래시 (별도 시스템 연동)
        // CameraShake, HitStop 등은 별도 시스템에서 처리
    }

    #endregion

    #region Ammo/Weapon Events

    void OnAmmoChanged(int current, int reserve)
    {
        if (currentAmmoText != null)
            currentAmmoText.text = current >= 0 ? current.ToString() : "∞";

        if (reserveAmmoText != null)
            reserveAmmoText.text = reserve >= 0 ? reserve.ToString() : "∞";

        // 탄약 부족 경고
        if (noAmmoWarning != null)
        {
            bool showWarning = current >= 0 && current <= 3 && reserve <= 0;
            noAmmoWarning.SetActive(showWarning);
        }

        // 탄약 변경 펄스 애니메이션
        if (ammoPulseCoroutine != null) StopCoroutine(ammoPulseCoroutine);
        ammoPulseCoroutine = StartCoroutine(PulseAmmoText());
    }

    System.Collections.IEnumerator PulseAmmoText()
    {
        if (currentAmmoText == null) yield break;

        Vector3 originalScale = currentAmmoText.transform.localScale;
        Vector3 targetScale = originalScale * ammoPulseScale;

        float elapsed = 0f;
        while (elapsed < ammoPulseDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / ammoPulseDuration;
            currentAmmoText.transform.localScale = Vector3.Lerp(originalScale, targetScale, t);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < ammoPulseDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / ammoPulseDuration;
            currentAmmoText.transform.localScale = Vector3.Lerp(targetScale, originalScale, t);
            yield return null;
        }

        currentAmmoText.transform.localScale = originalScale;
    }

    void OnWeaponChanged(WeaponData weapon)
    {
        UpdateWeaponUI(weapon);
    }

    void UpdateWeaponUI(WeaponData weapon)
    {
        if (weaponNameText != null)
            weaponNameText.text = weapon.WeaponName;

        if (weaponIcon != null && weapon.Icon != null)
            weaponIcon.sprite = weapon.Icon;

        if (fireModeText != null)
            fireModeText.text = weapon.Mode.ToString();
    }

    void OnReloadStart()
    {
        if (reloadProgressBar != null)
        {
            reloadProgressBar.gameObject.SetActive(true);
            reloadProgressBar.value = 0f;
            StartCoroutine(UpdateReloadProgress());
        }
    }

    System.Collections.IEnumerator UpdateReloadProgress()
    {
        // 재장전 비활성화됨 (에셋에 클립 없음) - 무기 데이터에서 UseAmmo=true이고 재장전 로직 추가 시 활성화
        yield break;
    }

    void OnReloadComplete()
    {
        // 재장전 완료 시 탄약 UI 갱신은 OnAmmoChanged에서 처리
    }

    void OnFire()
    {
        // 발사 시 탄약 펄스
        if (ammoPulseCoroutine != null) StopCoroutine(ammoPulseCoroutine);
        ammoPulseCoroutine = StartCoroutine(PulseAmmoText());
    }

    #endregion

    #region Overlay Events

    void OnOverlayChanged(PlayerState overlay, bool active)
    {
        switch (overlay)
        {
            case PlayerState.Shooting:
                // 사격 중 UI 강조 (선택적)
                break;
            // case PlayerState.Reloading: // 재장전 비활성화됨 (에셋에 클립 없음)
            //     // 재장전 중 UI 표시
            //     break;
        }
    }

    #endregion

    #region Dash/Cooldown

    void UpdateDashCooldown()
    {
        // 대시 비활성화됨 (에셋에 클립 없음) - 대시 기능 추가 시 활성화
    }

    #endregion

    #region Minimap

    void OnRoomChanged(Room newRoom, Room oldRoom)
    {
        UpdateMinimapBounds(newRoom);
    }

    void UpdateMinimap()
    {
        if (minimapCamera == null || roomManager == null) return;

        // 플레이어 위치 업데이트
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && playerMinimapIcon != null)
        {
            UpdateMinimapIcon(playerMinimapIcon, player.transform.position);
        }

        // 적 위치 업데이트 (풀에서 활성 적만)
        // 별도 시스템에서 관리 권장
    }

    void UpdateMinimapBounds(Room room)
    {
        if (room == null || roomBoundsRect == null) return;

        Bounds bounds = room.Bounds;
        Camera cam = minimapCamera;
        if (cam == null) return;

        // 미니맵 카메라 크기 조정
        float aspect = (float)Screen.width / Screen.height;
        float mapWidth = bounds.size.x;
        float mapHeight = bounds.size.y;

        cam.orthographicSize = Mathf.Max(mapWidth / aspect, mapHeight) * 0.5f * minimapZoom;
        cam.transform.position = new Vector3(bounds.center.x, bounds.center.y, cam.transform.position.z);

        // RoomBoundsRect 크기 조정 (UI 스케일 고려)
        if (roomBoundsRect != null)
        {
            // Canvas 스케일 고려하여 RectTransform 크기 설정
            // 실제 구현은 Canvas 설정에 따라 다름
        }
    }

    void UpdateMinimapIcon(RectTransform icon, Vector3 worldPos)
    {
        if (minimapCamera == null || icon == null) return;

        // 월드 → 뷰포트 → UI 로컬 좌표 변환
        Vector3 viewportPos = minimapCamera.WorldToViewportPoint(worldPos);
        Vector2 uiPos = new Vector2(
            (viewportPos.x - 0.5f) * minimapRenderTexture.rectTransform.rect.width,
            (viewportPos.y - 0.5f) * minimapRenderTexture.rectTransform.rect.height
        );
        icon.anchoredPosition = uiPos;

        // 방향 표시 (회전)
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            float angle = Mathf.Atan2(player.transform.right.y, player.transform.right.x) * Mathf.Rad2Deg;
            icon.rotation = Quaternion.Euler(0, 0, -angle);
        }
    }

    #endregion

    #region Public API

    /// <summary>무기 아이콘 스프라이트 설정 (런타임 변경용)</summary>
    public void SetWeaponIcon(Sprite icon)
    {
        if (weaponIcon != null) weaponIcon.sprite = icon;
    }

    /// <summary>체력 바 색상 그라디언트 설정</summary>
    public void SetHealthGradient(Gradient gradient)
    {
        if (healthBarGradient != null) healthBarGradient = gradient;
    }

    #endregion
}