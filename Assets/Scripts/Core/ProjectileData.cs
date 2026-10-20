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
    [SerializeField] private LayerMask groundLayerMask = (1 << 0) | (1 << 3); // 지형 판정용 (0=Default, 3=Ground)

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

    /// <summary>
    /// 수명 종료(명중/지형/사거리/트리거) 시 풀로 반환한다.
    /// Deinitialize()는 상태 정리만 하고, 실제 풀 반환은 이 경로가 담당한다.
    /// isInitialized 가드로 중복 반환을 방지한다 (이미 반환된 투사체는 무시).
    /// </summary>
    void ReturnToPool()
    {
        if (!isInitialized) return; // 이미 반환됨/미초기화 → 중복 반환 방지
        if (ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(this);
        else
            Deinitialize(); // 풀 부재 시 안전망: 최소한 비활성화
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
                    ReturnToPool();
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
                            ReturnToPool();
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
            ReturnToPool();
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
        // Ground 레이어는 serialized LayerMask로 결정 (기본: Default(0) + Ground(3))
        // 계획서: 이동용 Ground와 카메라용 Room Boundary 분리
        return (groundLayerMask.value & (1 << layer)) != 0;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // 보조/안전망 경로 유지: 지상 판정은 groundLayerMask, 타겟 판정은 TryHitTarget의
        // hitTargets 중복 제거를 거치므로 Raycast가 이미 처리한 타격이 여기로 다시 유입돼도
        // 데미지는 항상 1회만 적용된다. (중복 제거를 선택 — 안전망이 생략해도 피격이 누락되지 않음)
        if (!isInitialized) return;
        if (other.gameObject == owner) return;

        if (IsGroundLayer(other.gameObject.layer))
        {
            OnHitWall?.Invoke(transform.position);
            ReturnToPool();
        }
        else
        {
            var damageable = other.GetComponent<IDamageable>();
            if (damageable != null && damageable.Team.IsHostileTo(ownerTeam))
                TryHitTarget(damageable, other.gameObject, transform.position, transform.right);
        }
    }
}