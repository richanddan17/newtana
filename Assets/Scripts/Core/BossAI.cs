using UnityEngine;

/// <summary>
/// 보스 전용 AI - 패턴 기반, 페이즈 전환, 특수 능력.
/// EnemyAI를 상속받아 보스 전용 로직 추가.
/// 계획서 Phase 8, enemy-ai-system.md [8] 확장.
/// </summary>
[DisallowMultipleComponent]
public class BossAI : EnemyAI
{
    [Header("Boss Specific")]
    [SerializeField] int maxPhases = 3;
    [SerializeField] float[] phaseHealthThresholds = { 0.66f, 0.33f }; // 체력 비율로 페이즈 전환
    [SerializeField] bool enrageAtLowHealth = true;
    [SerializeField] float enrageSpeedMultiplier = 1.5f;
    [SerializeField] float enrageDamageMultiplier = 1.5f;

    [Header("Special Abilities")]
    [SerializeField] bool hasShield = false;
    [SerializeField] float shieldRechargeTime = 10f;
    [SerializeField] float maxShield = 50f;

    [Header("Arena")]
    [SerializeField] Transform[] spawnPoints; // 소환 위치
    [SerializeField] GameObject minionPrefab; // 소환할 미니언

    // 상태
    int currentPhaseIndex = 0;
    float currentShield;
    bool isEnraged = false;
    float shieldRechargeTimer;

    // 이벤트
    public event System.Action<int> OnPhaseChanged; // newPhaseIndex
    public event System.Action OnEnrageActivated;
    public event System.Action<float> OnShieldChanged; // current, max

    protected override void Awake()
    {
        base.Awake();
        enemyType = EnemyAIData.EnemyType.Boss;
        currentShield = maxShield;
        shieldRechargeTimer = shieldRechargeTime;
    }

    protected override void Update()
    {
        base.Update();

        if (health == null || health.IsDead) return;

        // 쉴드 재충전
        UpdateShield();

        // 페이즈 전환 체크
        CheckPhaseTransition();

        // 분노 모드 체크
        CheckEnrage();
    }

    void UpdateShield()
    {
        if (!hasShield || currentShield >= maxShield) return;

        shieldRechargeTimer -= Time.deltaTime;
        if (shieldRechargeTimer <= 0f)
        {
            currentShield = Mathf.Min(maxShield, currentShield + maxShield * 0.1f * Time.deltaTime);
            OnShieldChanged?.Invoke(currentShield, maxShield);
        }
    }

    void CheckPhaseTransition()
    {
        if (currentPhaseIndex >= phaseHealthThresholds.Length) return;

        float healthRatio = health.CurrentHealth / health.MaxHealth;
        if (healthRatio <= phaseHealthThresholds[currentPhaseIndex])
        {
            AdvancePhase();
        }
    }

    void AdvancePhase()
    {
        currentPhaseIndex++;
        currentPhase = currentPhaseIndex; // EnemyAI의 currentPhase 동기화

        // 페이즈 전환 효과
        OnPhaseChanged?.Invoke(currentPhaseIndex);

        // 페이즈별 버프 적용
        ApplyPhaseBuffs(currentPhaseIndex);

        // 패턴 쿨다운 리셋으로 즉시 새 패턴 선택
        patternCooldownTimer = 0f;
        currentPatternIndex = -1;

        Debug.Log($"[BossAI] Phase {currentPhaseIndex + 1} started!");
    }

    void ApplyPhaseBuffs(int phase)
    {
        // 페이즈별 능력치 증가
        float healthMultiplier = 1f + phase * 0.2f;
        float speedMultiplier = 1f + phase * 0.15f;
        float damageMultiplier = 1f + phase * 0.25f;

        if (health != null)
        {
            health.SetMaxHealth(health.MaxHealth * healthMultiplier);
        }
        if (movement != null)
        {
            movement.SetMoveSpeed(movement.GetType().GetField("chaseSpeed") != null ? 
                (float)movement.GetType().GetField("chaseSpeed").GetValue(movement) * speedMultiplier : 0f);
        }
        if (weaponController != null && weaponController.CurrentWeapon != null)
        {
            // 무기 데미지 배율 적용 (WeaponData 수정 필요)
        }
    }

    void CheckEnrage()
    {
        if (isEnraged || !enrageAtLowHealth) return;

        float healthRatio = health.CurrentHealth / health.MaxHealth;
        if (healthRatio <= 0.2f) // 20% 미만
        {
            ActivateEnrage();
        }
    }

    void ActivateEnrage()
    {
        isEnraged = true;
        OnEnrageActivated?.Invoke();

        // 분노 모드: 속도/데미지 증가
        if (movement != null)
        {
            // 속도 증가 로직
        }
        // 데미지 배율 적용
        Debug.Log("[BossAI] ENRAGE ACTIVATED!");
    }

    // 쉴드 데미지 처리 (Health와 별도)
    public bool TakeShieldDamage(float damage)
    {
        if (!hasShield || currentShield <= 0f) return false;

        currentShield = Mathf.Max(0f, currentShield - damage);
        shieldRechargeTimer = shieldRechargeTime;
        OnShieldChanged?.Invoke(currentShield, maxShield);

        if (currentShield <= 0f)
        {
            // 쉴드 파괴 시 잠시 스턴
            if (health != null) health.ForceStagger(1f);
        }

        return true;
    }

    // Health의 TakeDamage 오버라이드 (쉴드 우선 처리)
    // EnemyHealth에서 호출되도록 연결 필요

    public int CurrentPhase => currentPhaseIndex + 1;
    public float ShieldRatio => maxShield > 0f ? currentShield / maxShield : 0f;
    public bool IsEnraged => isEnraged;

    // 소환 능력
    public void SpawnMinions(int count)
    {
        if (minionPrefab == null || spawnPoints == null || spawnPoints.Length == 0) return;

        for (int i = 0; i < count; i++)
        {
            var spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
            Instantiate(minionPrefab, spawnPoint.position, spawnPoint.rotation);
        }
    }

    protected override void OnDied()
    {
        base.OnDied();
        // 보스 사망 이펙트, 드롭 등
    }

    void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // 페이즈 전환 체력 표시
        if (phaseHealthThresholds != null)
        {
            for (int i = 0; i < phaseHealthThresholds.Length; i++)
            {
                UnityEditor.Handles.Label(
                    transform.position + Vector3.up * (3f + i * 0.5f),
                    $"Phase {i + 2} at {phaseHealthThresholds[i] * 100:F0}% HP"
                );
            }
        }

        if (hasShield)
        {
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 4f,
                $"Shield: {currentShield:F0}/{maxShield:F0}"
            );
        }
    }
}