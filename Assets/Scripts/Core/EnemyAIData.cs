using UnityEngine;
using System;

// Force recompile to clear stale cache

/// <summary>
/// 적 AI 데이터 - ScriptableObject로 관리하여 프리팹별/타입별 설정 용이.
/// 계획서 enemy-ai-system.md [7], [8], Phase 8 확장 반영.
/// 같은 AI 코드, 데이터만 바꿔 여러 적/보스 생성 가능.
/// </summary>
[CreateAssetMenu(menuName = "RunGun/Enemy AI Data", fileName = "EnemyAIData_")]
public class EnemyAIData : ScriptableObject
{
    [Header("Identity")]
    public string EnemyName;
    public GameObject EnemyPrefab; // 프리팹 참조 (풀링용)
    public EnemyType Type = EnemyType.RangedShooter; // 타입별 로직 분기

    public enum EnemyType
    {
        RangedShooter,   // 기본 원거리 사격형
        Turret,          // 고정 포대 (이동 없음)
        MeleeCharger,    // 근접 돌진형
        Flying,          // 비행형 (중력 무시)
        Boss,            // 보스 (패턴 기반)
        AR,              // 소총병 (3방향 사격)
        RPG,             // 로켓병 (정면 전용)
        Sniper           // 저격수 (정면 전용)
    }

    [Header("Detection")]
    public float detectRange = 10f;
    public float fieldOfView = 110f;
    public float loseTargetTime = 3f;
    public bool detectOnDamage = true;

    [Header("Combat Ranges")]
    public float attackRange = 8f;
    public float minAttackRange = 2f;
    public bool canRetreat = true;

    [Header("Combat Timing")]
    public float aimTime = 0.5f;
    public float shootCooldown = 1.5f;
    public float burstCooldown = 0.1f;
    public int burstCount = 3;

    [Header("Movement")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 4f;
    public float retreatSpeed = 3f;
    public bool startPatrolling = true;

    // 타입별 이동 특성
    [Header("Type-Specific Movement")]
    public bool ignoreGravity = false;        // Flying: 중력 무시
    public float hoverHeight = 3f;            // Flying: 호버 높이
    public float hoverSpeed = 5f;             // Flying: 호버 속도
    public float chargeSpeed = 8f;            // Melee: 돌진 속도
    public float chargeCooldown = 2f;         // Melee: 돌진 쿨다운
    public float meleeRange = 1.5f;           // Melee: 근접 공격 범위
    public int meleeDamage = 20;              // Melee: 근접 데미지

    [Header("Weapon")]
    public WeaponData weaponData;             // 원거리 무기 (Ranged/Turret/Boss)
    public bool useMeleeAttack = false;       // 근접 공격 사용 여부
    public int aimDirectionCount = 3;         // 조준 방향 수 (AR=3, RPG/Sniper=1)

    [Header("Health/Poise")]
    public float maxHealth = 50f;
    public float maxPoise = 10f;
    public float poiseRecoveryRate = 2f;
    public float staggerDuration = 0.5f;
    public float hitInvincibilityDuration = 0.3f;

    [Header("Boss Patterns (EnemyType.Boss일 때만 사용)")]
    public BossPattern[] bossPatterns;        // 페이즈별 패턴
    public float phaseTransitionHealth = 0.5f; // 2페이즈 전환 체력 비율

    /// <summary>기본값으로 초기화 (에디터에서)</summary>
    void Reset()
    {
        EnemyName = "New Enemy";
        Type = EnemyType.RangedShooter;
        detectRange = 10f;
        fieldOfView = 110f;
        attackRange = 8f;
        minAttackRange = 2f;
        aimTime = 0.5f;
        shootCooldown = 1.5f;
        burstCount = 3;
        patrolSpeed = 2f;
        chaseSpeed = 4f;
        maxHealth = 50f;
        maxPoise = 10f;
    }
}

/// <summary>
/// 보스 패턴 정의 (데이터 드리븐)
/// </summary>
[System.Serializable]
public class BossPattern
{
    public string PatternName;
    public BossPhase Phase = BossPhase.Phase1; // 어느 페이즈에서 사용

    public enum BossPhase
    {
        Phase1,
        Phase2,
        Phase3,
        AllPhases
    }

    [Header("Pattern Behavior")]
    public PatternType Type = PatternType.ShootBurst;
    public float Duration = 3f;           // 패턴 지속 시간
    public float Cooldown = 5f;           // 패턴 후 쿨다운
    public int RepeatCount = 1;           // 반복 횟수

    public enum PatternType
    {
        ShootBurst,       // 연사/버스트 사격
        ShootSpread,      // 산탄/확산 사격
        ShootAimed,       // 플레이어 조준 사격
        Charge,           // 돌진 (Melee)
        SpawnMinions,     // 소환
        Dash,             // 대시 이동
        Teleport,         // 순간이동
        Invulnerable,     // 무적 상태
        Custom            // 커스텀 (스크립트에서 처리)
    }

    [Header("Pattern Parameters (Type별 해석 달라짐)")]
    public float Param1; // 예: 발사 간격, 돌진 속도, 소환 개수 등
    public float Param2; // 예: 탄 퍼짐, 돌진 거리 등
    public float Param3;
    public int IntParam1;
    public int IntParam2;

    [Header("Visual/Sound")]
    public GameObject WarningEffectPrefab; // 예고 이펙트
    public AudioClip WarningSound;
    public AudioClip ExecuteSound;
}

/// <summary>
/// 보스 페이즈 데이터
/// </summary>
[System.Serializable]
public class BossPhaseData
{
    public string PhaseName = "Phase 1";
    public float HealthThreshold = 1f; // 이 체력 비율 미만일 때 진입
    public BossPattern[] Patterns;     // 이 페이즈에서 사용할 패턴들
    public float PatternSelectCooldown = 1f; // 패턴 선택 쿨다운
}