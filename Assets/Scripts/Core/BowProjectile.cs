using System;
using UnityEngine;

public class BowProjectile : MonoBehaviour
{
    const float Speed = 18f;
    const float MaxDistance = 24f;

    SpriteRenderer spriteRenderer;
    PlayerAnimationClip arrowClip;
    PlayerAnimationClip impactClip;
    GameObject owner;
    Vector2 direction;
    int frame;
    float frameTimer;
    float traveledDistance;
    float speed = Speed;
    float maxDistance = MaxDistance;
    float damage = 10f;

    public void Initialize(PlayerAnimationClip arrow, Vector2 travelDirection, GameObject shotOwner,
        PlayerAnimationClip impact, SpriteRenderer renderer, float travelSpeed = Speed,
        float travelDistance = MaxDistance, float damage = 10f)
    {
        arrowClip = arrow;
        direction = travelDirection.sqrMagnitude > 0.0001f ? travelDirection.normalized : Vector2.right;
        owner = shotOwner;
        impactClip = impact;
        spriteRenderer = renderer;
        speed = travelSpeed > 0f ? travelSpeed : Speed;
        maxDistance = travelDistance > 0f ? travelDistance : MaxDistance;
        this.damage = damage;
        frame = 0;
        frameTimer = 0f;
        traveledDistance = 0f;
    }

    void Update()
    {
        AnimateArrow();

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return;

        Vector2 start = transform.position;
        Vector2 end = start + direction * speed * deltaTime;
        float stepDistance = Vector2.Distance(start, end);

        RaycastHit2D[] hits = Physics2D.RaycastAll(start, direction, stepDistance);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hitCollider = hits[i].collider;
            if (hitCollider == null) continue;
            if (owner != null && hitCollider.transform.IsChildOf(owner.transform)) continue;

            IDamageable damageable = hitCollider.GetComponentInParent<IDamageable>();
            if (damageable == null && hitCollider.isTrigger) continue;

            Hurtbox hurtbox = hitCollider.GetComponentInParent<Hurtbox>();
            if (hurtbox == null || damageable == null || damageable.IsDead) continue;

            Team targetTeam = hurtbox.GetTeam();
            if (targetTeam == Team.None) targetTeam = damageable.Team;
            if (!targetTeam.IsHostileTo(owner != null ? owner.GetComponent<TeamComponent>()?.Team ?? Team.Player : Team.Player)) continue;

            Hit(hits[i].point, damageable);
            return;
        }

        transform.position = end;
        traveledDistance += stepDistance;
        if (traveledDistance >= maxDistance) Destroy(gameObject);
    }

    void AnimateArrow()
    {
        if (spriteRenderer == null || arrowClip == null || arrowClip.frames == null || arrowClip.frames.Length <= 1) return;

        float interval = arrowClip.frameInterval > 0f ? arrowClip.frameInterval : 0.04f;
        frameTimer += Time.deltaTime;
        while (frameTimer >= interval)
        {
            frameTimer -= interval;
            frame = (frame + 1) % arrowClip.frames.Length;
        }

        spriteRenderer.sprite = arrowClip.frames[frame];
    }

    void Hit(Vector2 point, IDamageable damageable)
    {
        var info = new DamageInfo(damage, owner, point - (Vector2)owner.transform.position, 1f, HitType.Bullet);
        damageable.TakeDamage(info);
        PlayerBowShot.SpawnImpact(impactClip, point, direction, spriteRenderer);
        Destroy(gameObject);
    }
}
