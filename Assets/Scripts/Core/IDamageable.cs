using UnityEngine;

/// <summary>
/// 피격 대상 인터페이스. 계획서 [공통 규약 2] 준수.
/// 투사체/함정은 Health를 직접 건드리지 않고 이 인터페이스를 통해 DamageInfo 전달만 함.
/// </summary>
public interface IDamageable
{
    /// <summary>데미지 적용. 반환값: 실제 적용된 데미지 (무적/방어 등으로 0일 수 있음)</summary>
    float TakeDamage(DamageInfo info);

    /// <summary>현재 체력</summary>
    float CurrentHealth { get; }

    /// <summary>최대 체력</summary>
    float MaxHealth { get; }

    /// <summary>사망 여부</summary>
    bool IsDead { get; }

    /// <summary>팀 소속 (필터링용)</summary>
    Team Team { get; }

    /// <summary>피격 이벤트 (UI, 이펙트, 애니메이션 구독용)</summary>
    event System.Action<DamageInfo> OnDamaged;

    /// <summary>사망 이벤트 (체크포인트, 적 리셋 등 구독용)</summary>
    event System.Action OnDied;

    /// <summary>체력 회복 (최대 체력 초과 불가)</summary>
    void Heal(float amount);

    /// <summary>부활/리셋 시 전체 상태 초기화</summary>
    void ResetHealth();
}

/// <summary>
/// 환경 데미지(가시, 불, 낙사 구역 등)도 같은 인터페이스 사용.
/// Collider에 부착하고 IsTrigger=true로 사용.
/// </summary>
public interface IEnvironmentDamage : IDamageable
{
    /// <summary>지속 데미지 초당량 (즉사면 float.MaxValue)</summary>
    float DamagePerSecond { get; }

    /// <summary>무적 시간 무시 여부</summary>
    bool IgnoreInvincibility { get; }
}