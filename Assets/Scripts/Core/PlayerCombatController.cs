using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerCombatController : MonoBehaviour
{
    const string Attack1Clip = "attack1";
    const string Attack2Clip = "attack2";
    const string Attack3Clip = "attack3";
    const string HeavyReleaseClip = "heavyRelease";
    const int Attack1HitFrame = 3;
    const int Attack2HitFrame = 1;
    const int Attack3HitFrame = 5;
    const int HeavyReleaseHitFrame = 1;

    [Header("References")]
    [SerializeField] PlayerAnimation playerAnimation;
    [SerializeField] PlayerMovement playerMovement;
    [SerializeField] TeamComponent teamComponent;
    [SerializeField] Rigidbody2D rb;

    [Header("Attack 1 Dash")]
    [SerializeField] bool enableAttack1Dash = true;
    [SerializeField] float attack1DashRange = 7.2f;
    [SerializeField] float attack1DashStopDistance = 0.8f;
    [SerializeField] float attack1DashDuration = 0.15f;
    [SerializeField] LayerMask enemyLayers = 1 << 7;

    [Header("Attack 3 Teleport")]
    [SerializeField] bool enableAttack3Teleport = true;
    [SerializeField] float attack3TeleportRange = 7.2f;
    [SerializeField] float attack3TeleportBehindOffset = 1.6f;
    [SerializeField] int attack3TeleportFrame = 4;

    [Header("Damage")]
    [SerializeField] float attack1Damage = 12f;
    [SerializeField] float attack2Damage = 16f;
    [SerializeField] float attack3Damage = 24f;
    [SerializeField] float heavyDamage = 40f;

    [Header("Hit Shape")]
    [SerializeField] float attack1Radius = 1.1f;
    [SerializeField] float attack2Radius = 1.1f;
    [SerializeField] float attack3Radius = 1.1f;
    [SerializeField] float heavyRadius = 1.6f;

    [Header("Feedback")]
    [SerializeField] float attack1HitStop = 0.06f;
    [SerializeField] float attack2HitStop = 0.06f;
    [SerializeField] float attack3HitStop = 0.08f;
    [SerializeField] float heavyHitStop = 0.12f;
    [SerializeField] float attack1Knockback = 6f;
    [SerializeField] float attack2Knockback = 4f;
    [SerializeField] float attack3Knockback = 6f;
    [SerializeField] float heavyKnockback = 32f;
    [SerializeField] float attack3ShakeAmplitude = 0.08f;
    [SerializeField] float attack3ShakeDuration = 0.26f;
    [SerializeField] float heavyShakeAmplitude = 0.16f;
    [SerializeField] float heavyShakeDuration = 0.42f;
    [SerializeField] bool enableCameraShake = true;

    readonly HashSet<int> hitTargets = new();
    PlayerAnimationClip activeClip;
    bool hitResolved;
    bool attack1DashStarted;
    bool attack3TeleportDone;

    Team SourceTeam => teamComponent != null ? teamComponent.Team : Team.Player;
    int Facing => playerMovement != null
        ? playerMovement.FacingDirection
        : (transform.localScale.x < 0f ? -1 : 1);

    void Awake()
    {
        if (playerAnimation == null) playerAnimation = GetComponent<PlayerAnimation>();
        if (playerMovement == null) playerMovement = GetComponent<PlayerMovement>();
        if (teamComponent == null) teamComponent = GetComponent<TeamComponent>();
        if (rb == null) rb = GetComponent<Rigidbody2D>();
    }

    void OnEnable()
    {
        if (playerAnimation == null) return;
        playerAnimation.OnDebugClipFrame += OnDebugClipFrame;
        playerAnimation.OnDebugClipFinished += OnDebugClipFinished;
    }

    void OnDisable()
    {
        if (playerAnimation != null)
        {
            playerAnimation.OnDebugClipFrame -= OnDebugClipFrame;
            playerAnimation.OnDebugClipFinished -= OnDebugClipFinished;
        }

        activeClip = null;
        hitTargets.Clear();
        hitResolved = false;
        attack1DashStarted = false;
        attack3TeleportDone = false;
    }

    void OnDebugClipFinished(PlayerAnimationClip clip)
    {
        if (ReferenceEquals(activeClip, clip)) activeClip = null;
    }

    void OnDebugClipFrame(int frame)
    {
        PlayerAnimationClip clip = playerAnimation != null ? playerAnimation.CurrentDebugClip : null;
        if (clip == null) return;

        if (frame == 0 || !ReferenceEquals(activeClip, clip)) BeginClip(clip);

        switch (clip.id)
        {
            case Attack1Clip:
                if (frame == 0) TryAttack1Dash();
                if (frame == Attack1HitFrame) ApplyAttack(attack1Damage, attack1Radius, attack1Knockback, attack1HitStop, 0f, 0f);
                break;

            case Attack2Clip:
                if (frame == Attack2HitFrame) ApplyAttack(attack2Damage, attack2Radius, attack2Knockback, attack2HitStop, 0f, 0f);
                break;

            case Attack3Clip:
                if (frame == attack3TeleportFrame) TryAttack3Teleport();
                if (frame == Attack3HitFrame) ApplyAttack(attack3Damage, attack3Radius, attack3Knockback, attack3HitStop, attack3ShakeAmplitude, attack3ShakeDuration);
                break;

            case HeavyReleaseClip:
                if (frame == HeavyReleaseHitFrame) ApplyAttack(heavyDamage, heavyRadius, heavyKnockback, heavyHitStop, heavyShakeAmplitude, heavyShakeDuration);
                break;
        }
    }

    void BeginClip(PlayerAnimationClip clip)
    {
        activeClip = clip;
        hitTargets.Clear();
        hitResolved = false;
        attack1DashStarted = false;
        attack3TeleportDone = false;
    }

    void TryAttack1Dash()
    {
        if (!enableAttack1Dash || attack1DashStarted) return;
        attack1DashStarted = true;
        if (playerMovement == null) return;

        Hurtbox target = FindNearestEnemy(attack1DashRange);
        if (target == null) return;

        Collider2D targetCollider = target.GetComponent<Collider2D>();
        float targetX = targetCollider != null
            ? targetCollider.bounds.center.x
            : target.transform.position.x;
        float dx = targetX - transform.position.x;
        float distance = Mathf.Abs(dx) - attack1DashStopDistance;
        if (distance <= 0f) return;

        int facing = dx >= 0f ? 1 : -1;
        playerMovement.FaceDirection(facing);
        playerMovement.ApplyLunge(new Vector2(facing, 0f), distance, attack1DashDuration);
    }

    void TryAttack3Teleport()
    {
        if (!enableAttack3Teleport || attack3TeleportDone) return;
        attack3TeleportDone = true;

        Hurtbox target = FindNearestEnemy(attack3TeleportRange);
        if (target == null) return;

        Collider2D targetCollider = target.GetComponent<Collider2D>();
        float targetX = targetCollider != null
            ? targetCollider.bounds.center.x
            : target.transform.position.x;
        int towardTarget = targetX >= transform.position.x ? 1 : -1;
        targetX += towardTarget * attack3TeleportBehindOffset;

        Vector3 position = transform.position;
        position.x = targetX;
        transform.position = position;

        if (rb != null) rb.position = new Vector2(targetX, rb.position.y);
        if (playerMovement != null) playerMovement.FaceDirection(-towardTarget);
    }

    void ApplyAttack(float damage, float radius, float knockback, float hitStop, float shakeAmplitude, float shakeDuration)
    {
        if (hitResolved || radius <= 0f) return;

        int facing = Facing;
        Vector2 center = GetHitCenter(radius, facing);
        Collider2D[] overlaps = Physics2D.OverlapCircleAll(center, radius, enemyLayers);
        bool confirmed = false;

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider2D overlap = overlaps[i];
            if (overlap == null) continue;

            Hurtbox hurtbox = overlap.GetComponentInParent<Hurtbox>();
            IDamageable damageable = overlap.GetComponentInParent<IDamageable>();
            if (hurtbox == null || damageable == null || damageable.IsDead) continue;

            Team targetTeam = hurtbox.GetTeam();
            if (targetTeam == Team.None) targetTeam = damageable.Team;
            if (!targetTeam.IsHostileTo(SourceTeam)) continue;
            if (!hitTargets.Add(hurtbox.GetInstanceID())) continue;

            DamageInfo info = new DamageInfo(damage, gameObject, new Vector2(facing, 0f), knockback, HitType.Melee);
            if (damageable.TakeDamage(info) > 0f) confirmed = true;
        }

        if (!confirmed) return;

        hitResolved = true;
        if (hitStop > 0f) HitStop.Request(hitStop);
        if (enableCameraShake && shakeAmplitude > 0f && shakeDuration > 0f)
            CameraShake.Shake(new Vector2(facing, 0f), shakeAmplitude, shakeDuration);
    }

    Vector2 GetHitCenter(float radius, int facing)
    {
        SpriteRenderer body = playerAnimation != null ? playerAnimation.BodyRenderer : null;
        Vector3 origin = body != null ? body.bounds.center : transform.position;
        origin.x += facing * radius * 0.35f;
        return origin;
    }

    Hurtbox FindNearestEnemy(float range)
    {
        if (range <= 0f) return null;

        Collider2D[] overlaps = Physics2D.OverlapCircleAll(transform.position, range, enemyLayers);
        Hurtbox best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider2D overlap = overlaps[i];
            if (overlap == null) continue;

            Hurtbox hurtbox = overlap.GetComponentInParent<Hurtbox>();
            IDamageable damageable = overlap.GetComponentInParent<IDamageable>();
            if (hurtbox == null || damageable == null || damageable.IsDead) continue;

            Team targetTeam = hurtbox.GetTeam();
            if (targetTeam == Team.None) targetTeam = damageable.Team;
            if (!targetTeam.IsHostileTo(SourceTeam)) continue;

            float distance = ((Vector2)overlap.bounds.center - (Vector2)transform.position).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = hurtbox;
        }

        return best;
    }
}
