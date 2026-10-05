using UnityEngine;
using System;

/// <summary>
/// 체력/피격/무적/사망 시스템.
/// 계획서 health-damage-system.md [1]~[9] 반영.
/// IDamageable 구현, 무적 시간 합산 관리, 넉백 연동.
/// Phase 6: CameraShake, HitFeedback, HitStop 연동.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour, IDamageable
{
    [Header("Health Settings")]
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float currentHealth = 100f;

    [Header("Invincibility")]
    [SerializeField] float baseInvincibilityDuration = 0.8f; // 피격 무적
    [SerializeField] bool flashDuringInvincibility = true;
    [SerializeField] float flashInterval = 0.1f;

    [Header("Knockback")]
    [SerializeField] float knockbackResistance = 1f; // 넉백 저항 (0~1)

    [Header("Death")]
    [SerializeField] bool disableOnDeath = true; // 사망 시 비활성화 (풀 반환용)
    [SerializeField] float deathCleanupDelay = 2f; // 비활성화 전 대기

    [Header("Feedback (Phase 6)")]
    [SerializeField] bool enableCameraShake = true;
    [SerializeField] bool enableHitFeedback = true;
    [SerializeField] bool enableHitStop = true;

    [Header("References")]
    [SerializeField] TeamComponent teamComponent;
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] PlayerMovement movement; // 넉백 적용용
    [SerializeField] PlayerStateController stateController; // Hurt 상태 진입용

    // 무적 시스템 (여러 소스 합산: 피격 + 부활 + 대시)
    class InvincibilitySource
    {
        public string SourceId;
        public float EndTime;
        public bool IgnoreEnvironmentalDamage;
    }

    readonly System.Collections.Generic.List<InvincibilitySource> invincibilitySources = new();
    bool isDead = false;

    // 이벤트
    public event System.Action<DamageInfo> OnDamaged;
    public event System.Action OnDied;
    public event System.Action<float> OnHealthChanged; // current health
    public event System.Action<float, float> OnMaxHealthChanged; // current, max

    // IDamageable 구현
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public Team Team => teamComponent?.Team ?? Team.None;

    public event System.Action<DamageInfo> OnDamagedEvent
    {
        add => OnDamaged += value;
        remove => OnDamaged -= value;
    }

    public event System.Action OnDiedEvent
    {
        add => OnDied += value;
        remove => OnDied -= value;
    }

    void Awake()
    {
        if (teamComponent == null) teamComponent = GetComponent<TeamComponent>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (stateController == null) stateController = GetComponent<PlayerStateController>();

        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    void Update()
    {
        UpdateInvincibility();
    }

    #region IDamageable

    public float TakeDamage(DamageInfo info)
    {
        if (isDead) return 0f;
        if (IsInvincible(info)) return 0f;

        // 데미지 계산 (방어/약점 확장 가능)
        float finalDamage = CalculateDamage(info);
        if (finalDamage <= 0f) return 0f;

        // 체력 감소
        currentHealth = Mathf.Max(0f, currentHealth - finalDamage);
        OnHealthChanged?.Invoke(currentHealth);

        // 피격 처리
        ProcessHit(info, finalDamage);

        // 사망 체크
        if (currentHealth <= 0f && !isDead)
        {
            Die();
        }

        return finalDamage;
    }

    public void Heal(float amount)
    {
        if (isDead) return;
        float oldHealth = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        if (currentHealth != oldHealth)
            OnHealthChanged?.Invoke(currentHealth);
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        isDead = false;
        invincibilitySources.Clear();
        OnHealthChanged?.Invoke(currentHealth);
        OnMaxHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    #endregion

    #region Damage Processing

    float CalculateDamage(DamageInfo info)
    {
        // 기본: 데미지 그대로 적용
        // 확장: 방어력, 약점, 크리티컬 등
        return info.Damage;
    }

    void ProcessHit(DamageInfo info, float appliedDamage)
    {
        // 무적 시간 추가 (피격 무적)
        AddInvincibility("Hit", baseInvincibilityDuration, false);

        // 넉백 적용 (이동 시스템 경유)
        if (info.KnockbackForce > 0f && movement != null)
        {
            Vector2 knockbackDir = info.HitDirection * (info.KnockbackForce * (1f - knockbackResistance));
            movement.AddForce(knockbackDir, ForceMode2D.Impulse);
        }

        // 상태 시스템: Hurt 상태 진입
        if (stateController != null && stateController.CurrentMoveState != PlayerState.Death)
        {
            stateController.EnterHurtState();
        }

        // 이벤트 발생 (UI, 이펙트, 사운드 등)
        OnDamaged?.Invoke(info);

        // Phase 6: 피드백 시스템 연동
        TriggerFeedback(info);

        // 피격 플래시 (별도 컴포넌트도 가능)
        if (flashDuringInvincibility && spriteRenderer != null)
        {
            StartCoroutine(FlashCoroutine());
        }
    }

    void TriggerFeedback(DamageInfo info)
    {
        // HitStop
        if (enableHitStop)
        {
            HitStop.Request(0.05f);
        }

        // CameraShake
        if (enableCameraShake && CameraShake.Instance != null)
        {
            CameraShake.OnPlayerHit(info.HitDirection);
        }

        // HitFeedback (이펙트/사운드)
        if (enableHitFeedback)
        {
            HitFeedback.Play(info.HitType, transform.position, info.HitDirection, info.Attacker);
        }
    }

    bool IsInvincible(DamageInfo info)
    {
        if (invincibilitySources.Count == 0) return false;

        // 환경 데미지(낙사, 즉사)는 무적 무시 옵션 확인
        if (info.HitType == HitType.FallDamage || info.HitType == HitType.InstantKill)
        {
            foreach (var src in invincibilitySources)
            {
                if (!src.IgnoreEnvironmentalDamage) return false; // 하나라도 무시 안 하면 무적 적용
            }
            return true; // 모두 무시하면 무적 적용 안 함
        }

        return true; // 일반 데미지는 무적 중이면 막음
    }

    #endregion

    #region Invincibility System

    /// <summary>무적 시간 추가 (소스별 관리, 합산 방식)</summary>
    public void AddInvincibility(string sourceId, float duration, bool ignoreEnvironmentalDamage = false)
    {
        // 기존 동일 소스 있으면 갱신
        var existing = invincibilitySources.Find(s => s.SourceId == sourceId);
        float endTime = Time.time + duration;

        if (existing != null)
        {
            existing.EndTime = Mathf.Max(existing.EndTime, endTime);
            existing.IgnoreEnvironmentalDamage = ignoreEnvironmentalDamage;
        }
        else
        {
            invincibilitySources.Add(new InvincibilitySource
            {
                SourceId = sourceId,
                EndTime = endTime,
                IgnoreEnvironmentalDamage = ignoreEnvironmentalDamage
            });
        }
    }

    /// <summary>특정 소스의 무적 제거</summary>
    public void RemoveInvincibility(string sourceId)
    {
        invincibilitySources.RemoveAll(s => s.SourceId == sourceId);
    }

    /// <summary>현재 무적 여부</summary>
    public bool IsInvincibleNow()
    {
        UpdateInvincibility(); // 정리 후 확인
        return invincibilitySources.Count > 0;
    }

    void UpdateInvincibility()
    {
        float now = Time.time;
        invincibilitySources.RemoveAll(s => s.EndTime <= now);
    }

    /// <summary>부활 시 무적 부여</summary>
    public void GrantRespawnInvincibility(float duration)
    {
        AddInvincibility("Respawn", duration, false);
    }

    /// <summary>대시 무적 부여</summary>
    public void GrantDashInvincibility(float duration)
    {
        AddInvincibility("Dash", duration, false);
    }

    #endregion

    #region Death

    void Die()
    {
        isDead = true;
        currentHealth = 0f;

        // 사망 이벤트
        OnDied?.Invoke();

        // 상태 시스템: Death 상태 진입
        if (stateController != null)
        {
            stateController.EnterDeathState();
        }

        // Phase 6: 사망 피드백
        if (enableHitStop)
            HitStop.Request(0.15f);
        if (enableCameraShake && CameraShake.Instance != null)
            CameraShake.OnPlayerDeath();
        if (enableHitFeedback)
            HitFeedback.Play(HitType.InstantKill, transform.position, Vector3.up, null);

        // 비활성화/풀 반환
        if (disableOnDeath)
        {
            StartCoroutine(DisableAfterDelay());
        }
    }

    System.Collections.IEnumerator DisableAfterDelay()
    {
        yield return new WaitForSeconds(deathCleanupDelay);
        gameObject.SetActive(false);
    }

    #endregion

    #region Flash Effect

    System.Collections.IEnumerator FlashCoroutine()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;
        float endTime = Time.time + baseInvincibilityDuration;
        bool visible = true;

        while (Time.time < endTime)
        {
            visible = !visible;
            spriteRenderer.color = visible ? originalColor : new Color(originalColor.r, originalColor.g, originalColor.b, 0.3f);
            yield return new WaitForSeconds(flashInterval);
        }

        spriteRenderer.color = originalColor;
    }

    #endregion

    #region Public API

    /// <summary>최대 체력 변경 (업그레이드 등)</summary>
    public void SetMaxHealth(float newMaxHealth)
    {
        maxHealth = Mathf.Max(1f, newMaxHealth);
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        OnMaxHealthChanged?.Invoke(currentHealth, maxHealth);
        OnHealthChanged?.Invoke(currentHealth);
    }

    /// <summary>체력 비율 (0~1)</summary>
    public float HealthRatio => maxHealth > 0f ? currentHealth / maxHealth : 0f;

    #endregion
}