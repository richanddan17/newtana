using UnityEngine;

/// <summary>
/// 피격 판정(Hurtbox) 컴포넌트.
/// 계획서 hitbox-hurtbox-system.md [2], [3], [5], [6] 반영.
/// - 이동용 Collider와 분리 (Trigger 전용)
/// - Team 필터로 아군 사격 방지
/// - IDamageable 연결로 데미지 전달
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class Hurtbox : MonoBehaviour
{
    [Header("References")]
    [SerializeField] IDamageable damageable; // 같은 오브젝트 또는 부모에서 찾기
    [SerializeField] TeamComponent teamComponent;

    [Header("Settings")]
    [SerializeField] bool isActive = true;
    [SerializeField] LayerMask projectileLayers; // 감지할 투사체 레이어

    [Header("Hit Filter")]
    [SerializeField] bool useTeamFilter = true; // Team 기반 필터링
    [SerializeField] bool ignoreOwnerProjectiles = true; // 자기 발사 투사체 무시

    // 상태
    Collider2D hurtboxCollider;

    // 이벤트 (피드백용)
    public event System.Action<DamageInfo, GameObject> OnHit; // info, attacker

    void Awake()
    {
        hurtboxCollider = GetComponent<Collider2D>();
        hurtboxCollider.isTrigger = true; // 반드시 Trigger

        if (damageable == null) damageable = GetComponentInParent<IDamageable>();
        if (teamComponent == null) teamComponent = GetComponentInParent<TeamComponent>();

        // 레이어 자동 설정 (TeamComponent가 있으면)
        if (teamComponent != null)
        {
            gameObject.layer = teamComponent.Team.LayerIndex();
        }
    }

    void OnValidate()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!isActive) return;
        if (damageable == null || damageable.IsDead) return;

        // 투사체 레이어 확인
        if (projectileLayers != 0 && (projectileLayers.value & (1 << other.gameObject.layer)) == 0)
            return;

        // ProjectileData 컴포넌트 확인
        var projectile = other.GetComponent<ProjectileData>();
        if (projectile == null) return;

        // 팀 필터: 아군 투사체 무시
        if (useTeamFilter && teamComponent != null)
        {
            if (!projectile.OwnerTeam.IsHostileTo(teamComponent.Team))
                return;
        }

        // 자기 발사 투사체 무시
        if (ignoreOwnerProjectiles && projectile.Owner != null && projectile.Owner == damageable as MonoBehaviour?.gameObject)
            return;

        // 데미지 적용 (IDamageable 통해)
        float applied = damageable.TakeDamage(new DamageInfo(
            projectile.Damage,
            projectile.Owner,
            projectile.transform.right, // 발사 방향 = 히트 방향
            projectile.GetComponent<ProjectileData>().KnockbackForce,
            projectile.GetComponent<ProjectileData>().HitType
        ));

        if (applied > 0f)
        {
            OnHit?.Invoke(new DamageInfo(projectile.Damage, projectile.Owner, projectile.transform.right, projectile.GetComponent<ProjectileData>().KnockbackForce, projectile.GetComponent<ProjectileData>().HitType), projectile.Owner);
            
            // 투사체에 명중 알림 (관통 처리 위해)
            // projectile.OnHitTarget 이벤트는 ProjectileData 내부에서 처리됨
        }
    }

    /// <summary>활성화/비활성화 (상태 이상, 무적 등)</summary>
    public void SetActive(bool active)
    {
        isActive = active;
        if (hurtboxCollider != null) hurtboxCollider.enabled = active;
    }

    /// <summary>현재 팀 반환</summary>
    public Team GetTeam() => teamComponent?.Team ?? Team.None;
}