using UnityEngine;

/// <summary>
/// 적 전용 체력 시스템.
/// 계획서 enemy-ai-system.md [5], health-damage-system.md [5] 반영.
/// - 누적 경직(Poise) 시스템
/// - 피격 피드백만, 경직은 임계값 넘을 때만
/// - 사망 시 비활성화 (체크포인트 리셋용)
/// Phase 6: CameraShake, HitFeedback, HitStop 연동.
/// </summary>
[DisallowMultipleComponent]
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [Header("Health Settings")]
    [SerializeField] float maxHealth = 50f;
    [SerializeField] float currentHealth = 50f;

    [Header("Poise / Stagger System")]
    [SerializeField] float maxPoise = 10f;        // 경직 임계값
    [SerializeField] float poiseRecoveryRate = 2f; // 초당 회복량
    [SerializeField] float staggerDuration = 0.5f; // 경직 지속 시간

    [Header("Invincibility")]
    [SerializeField] float hitInvincibilityDuration = 0.3f; // 피격 무적 (짧게)
    [SerializeField] bool flashOnHit = true;

    [Header("Death")]
    [SerializeField] bool disableOnDeath = true;
    [SerializeField] float deathDelay = 1f; // 사망 애니메이션 재생 시간

    [Header("Feedback (Phase 6)")]
    [SerializeField] bool enableCameraShake = true;
    [SerializeField] bool enableHitFeedback = true;
    [SerializeField] bool enableHitStop = true;

    [Header("References")]
    [SerializeField] TeamComponent teamComponent;
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] EnemyAI enemyAI; // 상태 변경용

    // 상태
    float currentPoise = 0f;
    bool isStaggered = false;
    float staggerTimer = 0f;
    float invincibilityTimer = 0f;
    bool isDead = false;

    // 이벤트
    public event System.Action<DamageInfo> OnDamaged;
    public event System.Action OnDied;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public Team Team => teamComponent?.Team ?? Team.Enemy;

    public bool IsInvincibleNow()
    {
        return invincibilityTimer > 0f;
    }

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
        if (enemyAI == null) enemyAI = GetComponent<EnemyAI>();

        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        currentPoise = maxPoise;
    }

    void Update()
    {
        // Poise 회복
        if (currentPoise < maxPoise && !isStaggered)
        {
            currentPoise = Mathf.Min(maxPoise, currentPoise + poiseRecoveryRate * Time.deltaTime);
        }

        // 경직 타이머
        if (isStaggered)
        {
            staggerTimer -= Time.deltaTime;
            if (staggerTimer <= 0f)
            {
                isStaggered = false;
                // AI에 경직 끝 알림
                if (enemyAI != null)
                {
                    enemyAI.ExitStagger();
                }
            }
        }

        // 무적 타이머
        if (invincibilityTimer > 0f)
        {
            invincibilityTimer -= Time.deltaTime;
        }
    }

    #region IDamageable

    public float TakeDamage(DamageInfo info)
    {
        if (isDead) return 0f;
        if (invincibilityTimer > 0f) return 0f;

        // 데미지 적용
        float finalDamage = info.Damage; // 방어력 계산 확장 가능
        currentHealth = Mathf.Max(0f, currentHealth - finalDamage);

        // 피격 무적
        invincibilityTimer = hitInvincibilityDuration;

        // Poise 누적 (경직 시스템)
        float poiseDamage = finalDamage * 0.5f; // 데미지의 50%를 poise 데미지로
        currentPoise -= poiseDamage;

        // 1) poise 평가 → 2) Stagger 진입 → 3) OnDamaged 발화
        // (OnDamaged가 먼저 발화되면 EnemyAI.OnDamaged가 IsStaggered==false를 보고
        //  Hurt로 전환되어 Stagger 진입이 실패하는 버그 수정)
        if (currentPoise <= 0f && !isStaggered)
        {
            EnterStagger();
        }

        OnDamaged?.Invoke(info);

        // 넉백 (약하게)
        if (info.KnockbackForce > 0f)
        {
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                Vector2 knockback = info.HitDirection * (info.KnockbackForce * 0.3f); // 적은 넉백 적게
                rb.AddForce(knockback, ForceMode2D.Impulse);
            }
        }

        // 플래시
        if (flashOnHit && spriteRenderer != null)
        {
            StartCoroutine(FlashCoroutine());
        }

        // Phase 6: 피드백 시스템 연동
        TriggerFeedback(info);

        // 사망 체크
        if (currentHealth <= 0f && !isDead)
        {
            Die();
        }

        return finalDamage;
    }

    void TriggerFeedback(DamageInfo info)
    {
        // HitStop
        if (enableHitStop)
        {
            HitStop.Request(0.03f);
        }

        // CameraShake
        if (enableCameraShake && CameraShake.Instance != null)
        {
            CameraShake.OnEnemyHit();
        }

        // HitFeedback (이펙트/사운드)
        if (enableHitFeedback)
        {
            HitFeedback.Play(info.HitType, transform.position, info.HitDirection, info.Attacker);
        }
    }

    public void Heal(float amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        currentPoise = maxPoise;
        isStaggered = false;
        staggerTimer = 0f;
        invincibilityTimer = 0f;
        isDead = false;
        gameObject.SetActive(true);
    }

    /// <summary>최대 체력 변경 (업그레이드 등)</summary>
    public void SetMaxHealth(float newMaxHealth)
    {
        maxHealth = Mathf.Max(1f, newMaxHealth);
        currentHealth = Mathf.Min(currentHealth, maxHealth);
    }

    #endregion

    void EnterStagger()
    {
        isStaggered = true;
        staggerTimer = staggerDuration;
        currentPoise = maxPoise; // 경직 후 리셋

        // AI에 경직 알림
        if (enemyAI != null)
        {
            enemyAI.EnterStagger();
        }
    }

    void Die()
    {
        isDead = true;
        currentHealth = 0f;
        OnDied?.Invoke();

        // Phase 6: 사망 피드백
        if (enableHitStop)
            HitStop.Request(0.1f);
        if (enableCameraShake && CameraShake.Instance != null)
            CameraShake.OnEnemyHit(); // 사망 시에도 적당한 흔들림
        if (enableHitFeedback)
            HitFeedback.Play(HitType.InstantKill, transform.position, Vector3.up, null);

        if (enemyAI != null)
        {
            // enemyAI.EnterDead(); // EnemyAI에 구현 필요
        }

        if (disableOnDeath)
        {
            StartCoroutine(DisableAfterDeath());
        }
    }

    System.Collections.IEnumerator DisableAfterDeath()
    {
        yield return new WaitForSeconds(deathDelay);
        gameObject.SetActive(false); // 풀 반환용
    }

    System.Collections.IEnumerator FlashCoroutine()
    {
        if (spriteRenderer == null) yield break;
        Color original = spriteRenderer.color;
        spriteRenderer.color = Color.white;
        yield return new WaitForSeconds(0.05f);
        spriteRenderer.color = original;
    }

    #region Public API

    public float PoiseRatio => maxPoise > 0f ? currentPoise / maxPoise : 1f;
    public bool IsStaggered => isStaggered;

    /// <summary>외부에서 경직 강제 부여 (특수 무기 등)</summary>
    public void ForceStagger(float duration)
    {
        if (isDead) return;
        isStaggered = true;
        staggerTimer = duration;
    }

    #endregion
}