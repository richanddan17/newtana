using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 적 AI 상태 머신.
/// 계획서 enemy-ai-system.md [1]~[9] 반영.
/// - 상태 기반: Idle/Patrol → Detect → Chase/Reposition → Aim → Shoot → Cooldown → Hurt → Dead
/// - WeaponController 재사용 (플레이어와 같은 발사 구조)
/// - 데이터 드리븐: ScriptableObject로 적 타입별 파라미터 관리
/// </summary>
[DisallowMultipleComponent]
public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] EnemyHealth health;
    [SerializeField] EnemyVision vision;
    [SerializeField] EnemyMovement movement;
    [SerializeField] WeaponController weaponController;
    [SerializeField] TeamComponent teamComponent;

    [Header("AI Data (ScriptableObject 권장)")]
    [SerializeField] EnemyAIData aiData;

    [Header("Runtime Override (인스펙터에서 개별 조정 가능)")]
    [SerializeField] float detectRange = 10f;
    [SerializeField] float attackRange = 8f;
    [SerializeField] float minAttackRange = 2f;
    [SerializeField] float fieldOfView = 110f;
    [SerializeField] float aimTime = 0.5f;
    [SerializeField] float shootCooldown = 1.5f;
    [SerializeField] float burstCooldown = 0.1f;
    [SerializeField] int burstCount = 3;
    [SerializeField] float patrolSpeed = 2f;
    [SerializeField] float chaseSpeed = 4f;
    [SerializeField] float retreatSpeed = 3f;
    [SerializeField] bool canRetreat = true;
    [SerializeField] float loseTargetTime = 3f;
    [SerializeField] bool detectOnDamage = true;

    [Header("Ground/Edge Check")]
    [SerializeField] Transform groundCheckFront;
    [SerializeField] Transform groundCheckBack;
    [SerializeField] float edgeCheckDistance = 0.5f;
    [SerializeField] LayerMask groundLayerMask;

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;
    [SerializeField] bool logStateChanges = false;

    // 상태
    public enum AIState
    {
        Idle,
        Patrol,
        Detect,
        Chase,
        Reposition, // 거리 조절 (접근/후퇴)
        Aim,
        Shoot,
        Cooldown,
        Hurt,
        Stagger,    // 누적 경직
        Dead,
        Search,     // 타겟 놓침 수색
        Charge,     // 근접 돌진 (MeleeCharger)
        Hover,      // 비행 호버 (Flying)
        Pattern     // 보스 패턴 실행 (Boss)
    }

    AIState currentState = AIState.Idle;
    AIState previousState = AIState.Idle;

    // 타입
    EnemyAIData.EnemyType enemyType = EnemyAIData.EnemyType.RangedShooter;

    // 타겟
    Transform playerTransform;
    Vector2 lastKnownPlayerPos;
    float lostTargetTimer;

    // 타이머
    float stateTimer;
    float aimTimer;
    float shootCooldownTimer;
    int burstShotsRemaining;

    // 순찰
    Vector2 patrolDirection = Vector2.right;
    float patrolTimer;
    Vector2 patrolStartPos;

    // 타입별 상태
    float chargeCooldownTimer;
    bool isCharging;
    Vector2 chargeDirection;
    float hoverTimer;

    // 보스 패턴
    int currentPhase = 0;
    float patternCooldownTimer;
    int currentPatternIndex = -1;

    // 컴포넌트 캐시
    Rigidbody2D rb;
    Hurtbox hurtbox;

    // 프로퍼티
    public AIState CurrentState => currentState;
    public bool HasTarget => playerTransform != null;
    public Vector2 TargetDirection => playerTransform != null ? (Vector2)(playerTransform.position - transform.position) : Vector2.zero;
    public float DistanceToTarget => playerTransform != null ? TargetDirection.magnitude : float.MaxValue;

    void Awake()
    {
        if (health == null) health = GetComponent<EnemyHealth>();
        if (vision == null) vision = GetComponent<EnemyVision>();
        if (movement == null) movement = GetComponent<EnemyMovement>();
        if (weaponController == null) weaponController = GetComponent<WeaponController>();
        if (teamComponent == null) teamComponent = GetComponent<TeamComponent>();
        rb = GetComponent<Rigidbody2D>();
        hurtbox = GetComponentInChildren<Hurtbox>();

        // Health 이벤트 구독
        if (health != null)
        {
            health.OnDamaged += OnDamaged;
            health.OnDied += OnDied;
        }

        // AIData가 있으면 값 적용
        if (aiData != null)
            ApplyAIData(aiData);

        // 플레이어 찾기
        FindPlayer();

        // 초기 상태
        ChangeState(aiData != null && aiData.startPatrolling ? AIState.Patrol : AIState.Idle);
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.OnDamaged -= OnDamaged;
            health.OnDied -= OnDied;
        }
    }

    void Update()
    {
        if (health != null && health.IsDead) return;

        // 타겟 유효성 검사
        ValidateTarget();

        // 상태 머신 틱
        TickStateMachine();

        // 타이머 업데이트
        UpdateTimers();

        // 감지 체크 (상태 무관하게)
        CheckDetection();
    }

    void FixedUpdate()
    {
        if (health != null && health.IsDead) return;
        if (currentState == AIState.Dead) return;

        // 상태별 이동 처리
        HandleMovement();
    }

    #region State Machine

    void TickStateMachine()
    {
        // 타입별 상태 머신 분기
        switch (enemyType)
        {
            case EnemyAIData.EnemyType.Turret:
                TickTurretStateMachine();
                break;
            case EnemyAIData.EnemyType.MeleeCharger:
                TickMeleeStateMachine();
                break;
            case EnemyAIData.EnemyType.Flying:
                TickFlyingStateMachine();
                break;
            case EnemyAIData.EnemyType.Boss:
                TickBossStateMachine();
                break;
            default: // RangedShooter
                TickRangedStateMachine();
                break;
        }
    }

    void TickRangedStateMachine()
    {
        switch (currentState)
        {
            case AIState.Idle:
                TickIdle();
                break;
            case AIState.Patrol:
                TickPatrol();
                break;
            case AIState.Detect:
                TickDetect();
                break;
            case AIState.Chase:
                TickChase();
                break;
            case AIState.Reposition:
                TickReposition();
                break;
            case AIState.Aim:
                TickAim();
                break;
            case AIState.Shoot:
                TickShoot();
                break;
            case AIState.Cooldown:
                TickCooldown();
                break;
            case AIState.Hurt:
                TickHurt();
                break;
            case AIState.Stagger:
                TickStagger();
                break;
            case AIState.Search:
                TickSearch();
                break;
        }
    }

    void TickTurretStateMachine()
    {
        // 터렛: 이동 없음, 감지 → 조준 → 발사 → 쿨다운
        switch (currentState)
        {
            case AIState.Idle:
                TickIdle();
                break;
            case AIState.Detect:
                TickTurretDetect();
                break;
            case AIState.Aim:
                TickAim();
                break;
            case AIState.Shoot:
                TickShoot();
                break;
            case AIState.Cooldown:
                TickCooldown();
                break;
            case AIState.Hurt:
                TickHurt();
                break;
            case AIState.Stagger:
                TickStagger();
                break;
            case AIState.Search:
                TickSearch();
                break;
        }
    }

    void TickMeleeStateMachine()
    {
        // 근접 돌진: 감지 → 추적 → 돌진(Charge) → 쿨다운
        switch (currentState)
        {
            case AIState.Idle:
                TickIdle();
                break;
            case AIState.Patrol:
                TickPatrol();
                break;
            case AIState.Detect:
                TickMeleeDetect();
                break;
            case AIState.Chase:
                TickMeleeChase();
                break;
            case AIState.Charge:
                TickCharge();
                break;
            case AIState.Cooldown:
                TickMeleeCooldown();
                break;
            case AIState.Hurt:
                TickHurt();
                break;
            case AIState.Stagger:
                TickStagger();
                break;
            case AIState.Search:
                TickSearch();
                break;
        }
    }

    void TickFlyingStateMachine()
    {
        // 비행: 중력 무시, 호버링하며 사격
        switch (currentState)
        {
            case AIState.Idle:
                TickIdle();
                break;
            case AIState.Hover:
                TickHover();
                break;
            case AIState.Detect:
                TickFlyingDetect();
                break;
            case AIState.Chase:
                TickFlyingChase();
                break;
            case AIState.Aim:
                TickAim();
                break;
            case AIState.Shoot:
                TickShoot();
                break;
            case AIState.Cooldown:
                TickCooldown();
                break;
            case AIState.Hurt:
                TickHurt();
                break;
            case AIState.Stagger:
                TickStagger();
                break;
            case AIState.Search:
                TickSearch();
                break;
        }
    }

    void TickBossStateMachine()
    {
        // 보스: 페이즈별 패턴 실행
        switch (currentState)
        {
            case AIState.Idle:
                TickIdle();
                break;
            case AIState.Pattern:
                TickPattern();
                break;
            case AIState.Chase:
                TickBossChase();
                break;
            case AIState.Aim:
                TickAim();
                break;
            case AIState.Shoot:
                TickShoot();
                break;
            case AIState.Cooldown:
                TickCooldown();
                break;
            case AIState.Hurt:
                TickHurt();
                break;
            case AIState.Stagger:
                TickStagger();
                break;
            case AIState.Dead:
                // 보스 사망 처리
                break;
        }
    }

    void ChangeState(AIState newState)
    {
        if (currentState == newState) return;

        previousState = currentState;
        currentState = newState;
        stateTimer = 0f;

        if (logStateChanges)
            Debug.Log($"[EnemyAI] {name}: {previousState} -> {currentState}");

        OnStateEnter(newState);
    }

    void OnStateEnter(AIState state)
    {
        switch (state)
        {
            case AIState.Idle:
                movement?.SetMoveSpeed(0f);
                break;
            case AIState.Patrol:
                patrolStartPos = transform.position;
                patrolDirection = Random.value > 0.5f ? Vector2.right : Vector2.left;
                patrolTimer = Random.Range(2f, 5f);
                movement?.SetMoveSpeed(patrolSpeed);
                break;
            case AIState.Detect:
                movement?.SetMoveSpeed(0f);
                break;
            case AIState.Chase:
                movement?.SetMoveSpeed(chaseSpeed);
                break;
            case AIState.Reposition:
                movement?.SetMoveSpeed(chaseSpeed);
                break;
            case AIState.Aim:
                movement?.SetMoveSpeed(0f);
                aimTimer = GetAimTime();
                // 조준 시작 이벤트 (예고용)
                OnAimStart?.Invoke();
                break;
            case AIState.Shoot:
                // 발사 시작
                burstShotsRemaining = burstCount;
                shootCooldownTimer = 0f;
                break;
            case AIState.Cooldown:
                shootCooldownTimer = shootCooldown;
                break;
            case AIState.Hurt:
                movement?.SetMoveSpeed(0f);
                break;
            case AIState.Stagger:
                movement?.SetMoveSpeed(0f);
                break;
            case AIState.Search:
                movement?.SetMoveSpeed(patrolSpeed * 0.5f);
                lostTargetTimer = loseTargetTime;
                break;
            case AIState.Dead:
                movement?.SetMoveSpeed(0f);
                if (hurtbox != null) hurtbox.SetActive(false);
                break;
            case AIState.Charge:
                isCharging = true;
                chargeDirection = TargetDirection.normalized;
                movement?.SetMoveSpeed(aiData != null ? aiData.chargeSpeed : chaseSpeed * 2f);
                break;
            case AIState.Hover:
                // 비행형: 중력 무시, 호버 속도
                if (movement != null) movement.rb.gravityScale = 0f;
                movement?.SetMoveSpeed(aiData != null ? aiData.hoverSpeed : chaseSpeed);
                break;
            case AIState.Pattern:
                // 보스 패턴 시작
                patternCooldownTimer = 0f;
                break;
        }
    }

    #endregion

    #region State Ticks

    void TickIdle()
    {
        // 아무것도 안 함, 감지만 대기
    }

    void TickPatrol()
    {
        patrolTimer -= Time.deltaTime;

        // 벽/낭떠러지 체크
        if (IsEdgeAhead() || IsWallAhead())
        {
            patrolDirection = -patrolDirection; // 방향 반전
        }

        // 순찰 시간 끝나면 Idle 또는 계속
        if (patrolTimer <= 0f)
        {
            if (Random.value > 0.5f)
                ChangeState(AIState.Idle);
            else
            {
                patrolTimer = Random.Range(2f, 5f);
                patrolDirection = Random.value > 0.5f ? Vector2.right : Vector2.left;
            }
        }

        // 감지되면 Detect로
        if (vision != null && vision.CanSeePlayer)
        {
            ChangeState(AIState.Detect);
        }
    }

    void TickDetect()
    {
        // 감지 확인 후 바로 Chase/Reposition 결정
        if (HasTarget)
        {
            float dist = DistanceToTarget;
            if (dist > attackRange)
                ChangeState(AIState.Chase);
            else if (dist < minAttackRange && canRetreat)
                ChangeState(AIState.Reposition);
            else
                ChangeState(AIState.Aim);
        }
        else
        {
            ChangeState(AIState.Search);
        }
    }

    void TickChase()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Search);
            return;
        }

        float dist = DistanceToTarget;
        
        // 공격 범위 진입
        if (dist <= attackRange)
        {
            if (dist < minAttackRange && canRetreat)
                ChangeState(AIState.Reposition);
            else
                ChangeState(AIState.Aim);
        }

        // 시야 잃음
        if (vision != null && !vision.CanSeePlayer)
        {
            lostTargetTimer += Time.deltaTime;
            if (lostTargetTimer >= loseTargetTime)
                ChangeState(AIState.Search);
        }
        else
        {
            lostTargetTimer = 0f;
        }
    }

    void TickReposition()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Search);
            return;
        }

        float dist = DistanceToTarget;

        // 적정 거리 회복
        if (dist >= minAttackRange * 1.5f && dist <= attackRange)
        {
            ChangeState(AIState.Aim);
        }
        // 너무 멀어지면 Chase
        else if (dist > attackRange)
        {
            ChangeState(AIState.Chase);
        }
    }

    void TickAim()
    {
        aimTimer -= Time.deltaTime;

        if (!HasTarget)
        {
            ChangeState(AIState.Search);
            return;
        }

        // 조준 완료
        if (aimTimer <= 0f)
        {
            ChangeState(AIState.Shoot);
        }
    }

    void TickShoot()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Cooldown);
            return;
        }

        // 발사 실행
        if (weaponController != null && weaponController.CanFire)
        {
            // 타겟 방향으로 조준
            Vector2 aimDir = TargetDirection.normalized;
            weaponController.TryFire(); // WeaponController가 PlayerAnimation 참조하므로 적용 필요
        }

        // 버스트/단발 처리
        if (burstShotsRemaining > 0)
        {
            shootCooldownTimer += Time.deltaTime;
            if (shootCooldownTimer >= burstCooldown)
            {
                burstShotsRemaining--;
                shootCooldownTimer = 0f;
            }
        }

        // 발사 완료
        if (burstShotsRemaining <= 0)
        {
            ChangeState(AIState.Cooldown);
        }
    }

    void TickCooldown()
    {
        shootCooldownTimer -= Time.deltaTime;
        if (shootCooldownTimer <= 0f)
        {
            if (!HasTarget)
                ChangeState(AIState.Search);
            else
            {
                float dist = DistanceToTarget;
                if (dist > attackRange)
                    ChangeState(AIState.Chase);
                else if (dist < minAttackRange && canRetreat)
                    ChangeState(AIState.Reposition);
                else
                    ChangeState(AIState.Aim);
            }
        }
    }

    void TickHurt()
    {
        // Health에서 상태 복구 시 ExitHurt 호출됨
    }

    void TickStagger()
    {
        // Health에서 경직 끝 시 ExitStagger 호출됨
    }

    void TickSearch()
    {
        lostTargetTimer -= Time.deltaTime;

        // 마지막 알려진 위치로 이동
        if (Vector2.Distance(transform.position, lastKnownPlayerPos) < 1f)
        {
            // 도착함
            if (lostTargetTimer <= 0f)
            {
                ChangeState(aiData != null && aiData.startPatrolling ? AIState.Patrol : AIState.Idle);
            }
        }
        else
        {
            // 마지막 위치로 이동
            Vector2 dir = (lastKnownPlayerPos - (Vector2)transform.position).normalized;
            movement?.MoveTowards(dir);
        }

        // 재감지
        if (vision != null && vision.CanSeePlayer)
        {
            ChangeState(AIState.Detect);
        }
    }

    // === Turret ===
    void TickTurretDetect()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Search);
            return;
        }

        float dist = DistanceToTarget;
        if (dist <= attackRange)
        {
            ChangeState(AIState.Aim);
        }
        else
        {
            ChangeState(AIState.Search);
        }
    }

    // === Melee ===
    void TickMeleeDetect()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Search);
            return;
        }

        float dist = DistanceToTarget;
        if (dist <= attackRange)
        {
            if (chargeCooldownTimer <= 0f)
            {
                ChangeState(AIState.Charge);
            }
            else
            {
                ChangeState(AIState.Chase);
            }
        }
        else if (dist > attackRange * 1.5f)
        {
            ChangeState(AIState.Chase);
        }
    }

    void TickMeleeChase()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Search);
            return;
        }

        float dist = DistanceToTarget;
        
        // 돌진 범위 진입
        if (dist <= attackRange && chargeCooldownTimer <= 0f)
        {
            ChangeState(AIState.Charge);
        }
        // 너무 멀어짐
        else if (dist > attackRange * 2f)
        {
            ChangeState(AIState.Search);
        }

        // 시야 잃음
        if (vision != null && !vision.CanSeePlayer)
        {
            lostTargetTimer += Time.deltaTime;
            if (lostTargetTimer >= loseTargetTime)
                ChangeState(AIState.Search);
        }
        else
        {
            lostTargetTimer = 0f;
        }
    }

    void TickCharge()
    {
        if (!HasTarget)
        {
            EndCharge();
            return;
        }

        // 돌진 이동 (EnemyMovement에서 처리)
        chargeCooldownTimer -= Time.deltaTime;

        // 돌진 완료 조건: 타겟 도달 또는 시간 초과
        float dist = DistanceToTarget;
        if (dist <= aiData.meleeRange)
        {
            // 근접 공격 실행
            ExecuteMeleeAttack();
            EndCharge();
        }
        else if (stateTimer > 2f) // 최대 2초 돌진
        {
            EndCharge();
        }
    }

    void ExecuteMeleeAttack()
    {
        if (!HasTarget) return;

        var damageable = playerTransform.GetComponent<IDamageable>();
        if (damageable != null)
        {
            var info = new DamageInfo(aiData.meleeDamage, gameObject, TargetDirection.normalized, 5f, HitType.Melee);
            damageable.TakeDamage(info);
        }
    }

    void EndCharge()
    {
        isCharging = false;
        chargeCooldownTimer = aiData.chargeCooldown;
        ChangeState(AIState.Cooldown);
    }

    void TickMeleeCooldown()
    {
        chargeCooldownTimer -= Time.deltaTime;
        if (chargeCooldownTimer <= 0f)
        {
            if (!HasTarget)
                ChangeState(AIState.Search);
            else
            {
                float dist = DistanceToTarget;
                if (dist > attackRange)
                    ChangeState(AIState.Chase);
                else
                    ChangeState(AIState.Charge);
            }
        }
    }

    // === Flying ===
    void TickHover()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Search);
            return;
        }

        // 호버 높이 유지
        float targetY = playerTransform.position.y + aiData.hoverHeight;
        float currentY = transform.position.y;
        float yDiff = targetY - currentY;

        // 수직 이동
        if (Mathf.Abs(yDiff) > 0.5f)
        {
            Vector2 moveDir = new Vector2(0f, yDiff > 0 ? 1f : -1f);
            movement?.MoveTowards(moveDir);
        }

        // 수평 거리 유지
        float dist = DistanceToTarget;
        if (dist > attackRange)
        {
            ChangeState(AIState.Chase);
        }
        else if (dist < minAttackRange)
        {
            ChangeState(AIState.Reposition);
        }
        else
        {
            ChangeState(AIState.Aim);
        }
    }

    void TickFlyingDetect()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Search);
            return;
        }

        float dist = DistanceToTarget;
        if (dist <= attackRange)
        {
            ChangeState(AIState.Aim);
        }
        else
        {
            ChangeState(AIState.Chase);
        }
    }

    void TickFlyingChase()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Search);
            return;
        }

        // 비행 추적: 수직/수평 모두 이동
        Vector2 dir = TargetDirection.normalized;
        movement?.MoveTowards(dir);

        float dist = DistanceToTarget;
        if (dist <= attackRange)
        {
            ChangeState(AIState.Aim);
        }

        // 시야 잃음
        if (vision != null && !vision.CanSeePlayer)
        {
            lostTargetTimer += Time.deltaTime;
            if (lostTargetTimer >= loseTargetTime)
                ChangeState(AIState.Search);
        }
        else
        {
            lostTargetTimer = 0f;
        }
    }

    // === Boss ===
    void TickPattern()
    {
        if (aiData == null || aiData.bossPatterns == null) return;

        var phaseData = GetCurrentPhaseData();
        if (phaseData == null || phaseData.Patterns.Length == 0) return;

        patternCooldownTimer -= Time.deltaTime;

        if (currentPatternIndex == -1 || patternCooldownTimer <= 0f)
        {
            SelectNextPattern(phaseData);
        }

        // 패턴 실행 중
        if (currentPatternIndex >= 0)
        {
            ExecuteCurrentPattern(phaseData);
        }
    }

    BossPhaseData GetCurrentPhaseData()
    {
        if (aiData.bossPatterns == null) return null;

        // 현재 체력 비율로 페이즈 결정
        float healthRatio = health.CurrentHealth / health.MaxHealth;
        
        // 간단한 2페이즈: 50% 미만이면 2페이즈
        if (healthRatio < aiData.phaseTransitionHealth)
            currentPhase = 1;

        // 페이즈에 맞는 패턴 필터링 (실제로는 BossPhaseData 배열 사용 권장)
        return new BossPhaseData 
        { 
            PhaseName = currentPhase == 0 ? "Phase 1" : "Phase 2",
            Patterns = aiData.bossPatterns,
            HealthThreshold = currentPhase == 0 ? 1f : aiData.phaseTransitionHealth
        };
    }

    void SelectNextPattern(BossPhaseData phaseData)
    {
        var validPatterns = phaseData.Patterns.Where(p => 
            p.Phase == (currentPhase == 0 ? BossPattern.BossPhase.Phase1 : BossPattern.BossPhase.Phase2) ||
            p.Phase == BossPattern.BossPhase.AllPhases
        ).ToArray();

        if (validPatterns.Length == 0) return;

        currentPatternIndex = Random.Range(0, validPatterns.Length);
        patternCooldownTimer = validPatterns[currentPatternIndex].Cooldown;
        stateTimer = 0f;
    }

    void ExecuteCurrentPattern(BossPhaseData phaseData)
    {
        var pattern = phaseData.Patterns[currentPatternIndex];
        stateTimer += Time.deltaTime;

        switch (pattern.Type)
        {
            case BossPattern.PatternType.ShootBurst:
                if (weaponController != null && weaponController.CanFire)
                {
                    weaponController.TryFire();
                }
                if (stateTimer >= pattern.Duration)
                {
                    currentPatternIndex = -1;
                }
                break;

            case BossPattern.PatternType.ShootSpread:
                // 산탄 발사 (WeaponController의 Shotgun 모드 활용)
                if (weaponController != null && weaponController.CanFire)
                {
                    weaponController.TryFire();
                }
                if (stateTimer >= pattern.Duration)
                {
                    currentPatternIndex = -1;
                }
                break;

            case BossPattern.PatternType.Charge:
                if (enemyType == EnemyAIData.EnemyType.MeleeCharger)
                {
                    ChangeState(AIState.Charge);
                    currentPatternIndex = -1;
                }
                break;

            case BossPattern.PatternType.SpawnMinions:
                // 소환 로직 (별도 구현 필요)
                if (stateTimer >= pattern.Duration)
                {
                    currentPatternIndex = -1;
                }
                break;

            case BossPattern.PatternType.Dash:
                // 대시 이동
                if (stateTimer >= pattern.Duration)
                {
                    currentPatternIndex = -1;
                }
                break;

            case BossPattern.PatternType.Invulnerable:
                // 무적 상태 (Health에서 처리)
                if (stateTimer >= pattern.Duration)
                {
                    currentPatternIndex = -1;
                }
                break;
        }
    }

    void TickBossChase()
    {
        if (!HasTarget)
        {
            ChangeState(AIState.Idle);
            return;
        }

        // 보스는 패턴 쿨다운 중에는 추적
        float dist = DistanceToTarget;
        if (dist > attackRange)
        {
            movement?.MoveTowards(TargetDirection.normalized);
        }
        else
        {
            // 패턴 선택 쿨다운 확인
            if (patternCooldownTimer <= 0f)
            {
                ChangeState(AIState.Pattern);
            }
        }
    }

    #endregion

    #region Movement Handling

    void HandleMovement()
    {
        if (movement == null) return;

        switch (currentState)
        {
            case AIState.Patrol:
                movement.MoveTowards(patrolDirection);
                break;
            case AIState.Chase:
                if (HasTarget)
                    movement.MoveTowards(TargetDirection.normalized);
                break;
            case AIState.Reposition:
                if (HasTarget)
                {
                    float dist = DistanceToTarget;
                    Vector2 dir = TargetDirection.normalized;
                    // 너무 가까우면 후퇴, 멀면 접근
                    if (dist < minAttackRange)
                        movement.MoveTowards(-dir);
                    else
                        movement.MoveTowards(dir);
                }
                break;
            case AIState.Charge:
                // 돌진: chargeDirection으로 고속 이동
                if (isCharging && HasTarget)
                    movement.MoveTowards(chargeDirection);
                break;
            case AIState.Hover:
                // 비행 호버: 수직/수평 모두 이동 (Update에서 처리)
                break;
            case AIState.Pattern:
                // 보스 패턴 중 이동 (패턴별 처리)
                break;
            case AIState.Search:
                if (HasTarget) break; // Search에서는 lastKnownPlayerPos로 이동 (Update에서 처리)
                break;
        }
    }

    #endregion

    #region Vision & Detection

    void FindPlayer()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    void ValidateTarget()
    {
        if (playerTransform == null)
        {
            FindPlayer();
        }
    }

    void CheckDetection()
    {
        if (vision == null) return;

        bool canSee = vision.CanSeePlayer;
        
        // 피격 시 강제 감지
        if (detectOnDamage && health != null && health.IsInvincibleNow())
        {
            // 최근 피격 시 플레이어 위치 기억
            if (playerTransform != null)
                lastKnownPlayerPos = playerTransform.position;
        }
    }

    #endregion

    #region Edge/Wall Check

    bool IsEdgeAhead()
    {
        if (groundCheckFront == null) return false;
        Vector2 checkPos = groundCheckFront.position;
        Vector2 dir = patrolDirection.x > 0 ? Vector2.right : Vector2.left;
        var hit = Physics2D.Raycast(checkPos, Vector2.down, edgeCheckDistance, groundLayerMask);
        return hit.collider == null;
    }

    bool IsWallAhead()
    {
        if (groundCheckFront == null) return false;
        Vector2 checkPos = groundCheckFront.position;
        Vector2 dir = patrolDirection.x > 0 ? Vector2.right : Vector2.left;
        var hit = Physics2D.Raycast(checkPos, dir, 0.5f, groundLayerMask);
        return hit.collider != null;
    }

    #endregion

    #region Timers

    void UpdateTimers()
    {
        stateTimer += Time.deltaTime;
    }

    float GetAimTime()
    {
        // 거리 기반 조준 시간 조정 (가까우면 짧게)
        if (!HasTarget) return aimTime;
        float dist = DistanceToTarget;
        return Mathf.Lerp(aimTime * 0.5f, aimTime * 1.5f, dist / attackRange);
    }

    #endregion

    #region Health Events

    void OnDamaged(DamageInfo info)
    {
        if (currentState == AIState.Dead) return;

        // 피격 시 타겟 갱신
        if (info.Attacker != null && info.Attacker.CompareTag("Player"))
        {
            playerTransform = info.Attacker.transform;
            lastKnownPlayerPos = playerTransform.position;
        }

        // 경직 상태면 Stagger, 아니면 Hurt
        if (health.IsStaggered)
            ChangeState(AIState.Stagger);
        else
            ChangeState(AIState.Hurt);
    }

    void OnDied()
    {
        ChangeState(AIState.Dead);
    }

    #endregion

    #region Public API (Health/외부에서 호출)

    /// <summary>피격 회복 - Hurt 상태에서 복귀</summary>
    public void ExitHurt()
    {
        if (currentState == AIState.Hurt)
        {
            if (HasTarget)
            {
                float dist = DistanceToTarget;

                // 타입별 복귀 상태
                switch (enemyType)
                {
                    case EnemyAIData.EnemyType.Turret:
                        if (dist <= attackRange)
                            ChangeState(AIState.Aim);
                        else
                            ChangeState(AIState.Search);
                        break;
                    case EnemyAIData.EnemyType.MeleeCharger:
                        if (dist <= attackRange && chargeCooldownTimer <= 0f)
                            ChangeState(AIState.Charge);
                        else if (dist > attackRange)
                            ChangeState(AIState.Chase);
                        else
                            ChangeState(AIState.Cooldown);
                        break;
                    case EnemyAIData.EnemyType.Flying:
                        if (dist <= attackRange)
                            ChangeState(AIState.Aim);
                        else if (dist < minAttackRange)
                            ChangeState(AIState.Reposition);
                        else
                            ChangeState(AIState.Chase);
                        break;
                    case EnemyAIData.EnemyType.Boss:
                        ChangeState(AIState.Pattern);
                        break;
                    default: // RangedShooter
                        if (dist > attackRange)
                            ChangeState(AIState.Chase);
                        else if (dist < minAttackRange && canRetreat)
                            ChangeState(AIState.Reposition);
                        else
                            ChangeState(AIState.Aim);
                        break;
                }
            }
            else
            {
                ChangeState(AIState.Search);
            }
        }
    }

    /// <summary>경직 회복</summary>
    public void ExitStagger()
    {
        if (currentState == AIState.Stagger)
        {
            ExitHurt(); // 같은 로직
        }
    }

    /// <summary>AI 데이터 적용 (스크립터블 오브젝트에서)</summary>
    public void ApplyAIData(EnemyAIData data)
    {
        aiData = data;
        enemyType = data.EnemyType;
        detectRange = data.detectRange;
        attackRange = data.attackRange;
        minAttackRange = data.minAttackRange;
        fieldOfView = data.fieldOfView;
        aimTime = data.aimTime;
        shootCooldown = data.shootCooldown;
        burstCooldown = data.burstCooldown;
        burstCount = data.burstCount;
        patrolSpeed = data.patrolSpeed;
        chaseSpeed = data.chaseSpeed;
        retreatSpeed = data.retreatSpeed;
        canRetreat = data.canRetreat;
        loseTargetTime = data.loseTargetTime;
        detectOnDamage = data.detectOnDamage;

        // 타입별 파라미터
        if (movement != null)
        {
            movement.rb.gravityScale = data.ignoreGravity ? 0f : 1f;
        }

        // 보스 패턴 초기화
        if (enemyType == EnemyAIData.EnemyType.Boss && data.bossPatterns != null)
        {
            currentPhase = 0;
            currentPatternIndex = -1;
            patternCooldownTimer = 0f;
        }
    }

    #endregion

    #region Events

    public event System.Action OnAimStart; // 조준 예고 (플레이어 반응용)

    #endregion

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // 감지 범위
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectRange);

        // 공격 범위
        Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // 최소 공격 범위
        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, minAttackRange);

        // 시야각
        if (playerTransform != null)
        {
            Gizmos.color = vision != null && vision.CanSeePlayer ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, playerTransform.position);
        }

        // 순찰 방향
        if (currentState == AIState.Patrol)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, patrolDirection * 2f);
        }

        // 엣지 체크
        if (groundCheckFront != null)
        {
            Gizmos.color = IsEdgeAhead() ? Color.red : Color.green;
            Gizmos.DrawRay(groundCheckFront.position, Vector2.down * edgeCheckDistance);
        }
        if (groundCheckBack != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(groundCheckBack.position, Vector2.down * edgeCheckDistance);
        }

        // 상태 표시
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, $"AI: {currentState}");
    }
}