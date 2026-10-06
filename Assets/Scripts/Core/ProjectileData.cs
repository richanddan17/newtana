using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 투사체 프리팹에 부착. 계획서 weapon-projectile-system.md [4], [5] 반영.
/// 풀링용 초기화/정리, 터널링 방지, 중복 타격 방지 포함.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class ProjectileData : MonoBehaviour
{
    [Header("Runtime (자동 설정)")]
    [SerializeField] Team ownerTeam = Team.None;
    [SerializeField] float damage = 10f;
    [SerializeField] float speed = 20f;
    [SerializeField] float maxRange = 15f;
    [SerializeField] float knockbackForce = 3f;
    [SerializeField] HitType hitType = HitType.Bullet;
    [SerializeField] int penetrationMax = 0;
    [SerializeField] GameObject owner;

    // 런타임 상태
    Rigidbody2D rb;
    Collider2D col;
    float traveledDistance;
    readonly HashSet<GameObject> hitTargets = new(); // 관통 시 중복 타격 방지
    bool isInitialized;

    // 이벤트 (피드백 연결용)
    public event Action<DamageInfo, GameObject> OnHitTarget;     // info, target
    public event Action<Vector2> OnHitWall;                      // hit point
    public event Action OnExpired;                               // 수명/사거리 끝

    public Team OwnerTeam => ownerTeam;
    public GameObject Owner => owner;
    public float Damage => damage;
    public float TraveledDistance => traveledDistance;
    public float KnockbackForce => knockbackForce;
    public HitType HitType => hitType;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        // 투사체는 Trigger로 동작 (물리 충돌은 레이어 매트릭스로 차단)
        col.isTrigger = true;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // 터널링 방지 보조
    }

    /// <summary>풀에서 꺼낼 때 호출. 모든 상태 완전 초기화.</summary>
    public void Initialize(ProjectileRuntimeData data)
    {
        ownerTeam = data.OwnerTeam;
        damage = data.Damage;
        speed = data.Speed;
        maxRange = data.MaxRange;
        knockbackForce = data.KnockbackForce;
        hitType = data.HitType;
        penetrationMax = data.PenetrationMax;
        owner = data.Owner;

        traveledDistance = 0f;
        hitTargets.Clear();
        isInitialized = true;
        gameObject.SetActive(true);
    }

    /// <summary>풀로 반환 시 정리</summary>
    public void Deinitialize()
    {
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        traveledDistance = 0f;
        hitTargets.Clear();
        isInitialized = false;
        gameObject.SetActive(false);
    }

    void FixedUpdate()
    {
        if (!isInitialized) return;

        Vector2 moveDelta = (Vector2)transform.right * speed * Time.fixedDeltaTime;
        float stepDist = moveDelta.magnitude;

        // 터널링 방지: 이동 거리만큼 Raycast로 선제 판정
        if (stepDist > 0.001f)
        {
            var hits = Physics2D.RaycastAll(rb.position, moveDelta.normalized, stepDist, ~0, -Mathf.Infinity, Mathf.Infinity);
            foreach (var hit in hits)
            {
                if (hit.collider == col) continue; // 자기 자신 무시
                if (hit.collider.gameObject == owner) continue; // 발사자 무시

                // 지형 충돌
                if (IsGroundLayer(hit.collider.gameObject.layer))
                {
                    OnHitWall?.Invoke(hit.point);
                    Deinitialize();
                    return;
                }

                // Hurtbox 충돌
                var damageable = hit.collider.GetComponent<IDamageable>();
                if (damageable != null && damageable.Team.IsHostileTo(ownerTeam))
                {
                    if (TryHitTarget(damageable, hit.collider.gameObject, hit.point, moveDelta.normalized))
                    {
                        if (penetrationMax <= 0 || hitTargets.Count >= penetrationMax)
                        {
                            Deinitialize();
                            return;
                        }
                    }
                }
            }
        }

        // 실제 이동
        rb.MovePosition(rb.position + moveDelta);
        traveledDistance += stepDist;

        // 사거리 체크
        if (traveledDistance >= maxRange)
        {
            OnExpired?.Invoke();
            Deinitialize();
        }
    }

    bool TryHitTarget(IDamageable target, GameObject targetObj, Vector2 hitPoint, Vector2 hitDir)
    {
        // 같은 프레임/같은 총알에 중복 타격 방지
        if (hitTargets.Contains(targetObj)) return false;
        hitTargets.Add(targetObj);

        // DamageInfo 구성
        var info = new DamageInfo(damage, owner, hitDir, knockbackForce, hitType);
        float applied = target.TakeDamage(info);

        if (applied > 0f)
        {
            OnHitTarget?.Invoke(info, targetObj);
            return true;
        }
        return false;
    }

    bool IsGroundLayer(int layer)
    {
        // Ground 레이어는 프로젝트별로 다름. 기본: Default(0) + Ground(3) 등
        // 계획서: 이동용 Ground와 카메라용 Room Boundary 분리
        return layer == 0 || layer == 3; // Default, Ground (프로젝트 설정 맞게 수정)
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // Continuous 모드에서 Raycast로 선처리했으므로 여기는 보조/안전망
        if (!isInitialized) return;
        if (other.gameObject == owner) return;

        if (IsGroundLayer(other.gameObject.layer))
        {
            OnHitWall?.Invoke(transform.position);
            Deinitialize();
        }
        else
        {
            var damageable = other.GetComponent<IDamageable>();
            if (damageable != null && damageable.Team.IsHostileTo(ownerTeam))
                TryHitTarget(damageable, other.gameObject, transform.position, transform.right);
        }
    }
}