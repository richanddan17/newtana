using UnityEngine;

/// <summary>
/// 모든 공격/피해 전달에 사용하는 통합 데이터 구조.
/// 계획서 [공통 규약 1, 2] 준수: 투사체/함정/환경 모두 동일 구조 사용.
/// </summary>
public readonly struct DamageInfo
{
    public readonly float Damage;
    public readonly GameObject Attacker;
    public readonly Vector2 HitDirection; // 정규화된 방향 (넉백용)
    public readonly float KnockbackForce;
    public readonly HitType HitType;

    public DamageInfo(float damage, GameObject attacker, Vector2 hitDirection, float knockbackForce, HitType hitType = HitType.Bullet)
    {
        Damage = damage;
        Attacker = attacker;
        HitDirection = hitDirection.sqrMagnitude > 0f ? hitDirection.normalized : Vector2.right;
        KnockbackForce = knockbackForce;
        HitType = hitType;
    }

    /// <summary>편의 생성자: 방향을 attacker→target으로 자동 계산</summary>
    public static DamageInfo Create(float damage, GameObject attacker, GameObject target, float knockbackForce, HitType hitType = HitType.Bullet)
    {
        Vector2 dir = target != null && attacker != null
            ? (Vector2)(target.transform.position - attacker.transform.position)
            : Vector2.right;
        return new DamageInfo(damage, attacker, dir, knockbackForce, hitType);
    }
}

/// <summary>피격 타입 분류 (확장 가능)</summary>
public enum HitType
{
    Bullet,     // 일반 총알
    Explosion,  // 폭발/범위
    Melee,      // 근접 (향후 확장)
    Trap,       // 함정/환경
    FallDamage, // 낙사
    InstantKill // 즉사
}