using UnityEngine;
using System.Collections.Generic;

// Uses the project's EnemyHealth / IDamageable / DamageInfo. No Animator required.
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth), typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class DemonSlimeBoss : EnemyAI
{
    public enum SlimeState { Idle, Chase, Windup, Attack, Recovery, Stagger, Dead }
    [Header("Target / arena")]
    public Transform target;
    public LayerMask groundMask;
    public float detectionRange = 16f;
    public float arenaHalfWidth = 12f;
    [Header("Combat")]
    public float moveSpeed = 2.2f;
    public float dashSpeed = 8f;
    public float attackDistance = 2.6f;
    public float damage = 18f;
    public float attackReach = 3.1f;
    public Vector2 attackSize = new Vector2(3.2f, 2.1f);
    public float telegraphTime = 0.65f;
    public float recoveryTime = 1.15f;
    public float hurtTime = 0.18f;
    [Header("Animation (original PNG frames)")]
    public SpriteRenderer body;
    public Sprite[] idleFrames, walkFrames, cleaveFrames, hitFrames, deathFrames;
    public float framesPerSecond = 12f;
    public int firstActiveFrame = 7;
    public int lastActiveFrame = 10;
    // Uploaded artwork attacks LEFT by default.
    public bool artworkFacesRight = false;
    [Header("Feedback")]
    public Color normalTint = Color.white;
    public Color warningTint = new Color(1f, 0.35f, 0.35f);
    public bool showBossBar = true;

    public SlimeState State { get; private set; }
    public int Phase { get; private set; } = 1;
    public event System.Action<int> PhaseChanged;
    Rigidbody2D physicsBody;
    BoxCollider2D solid;
    Vector2 home;
    float elapsed, animationTime, desiredVelocity, targetSearchTimer, hitTimer;
    int facing = 1, patternCounter, comboRemaining;
    bool engaged, dashAttack;
    readonly HashSet<IDamageable> damagedThisSwing = new HashSet<IDamageable>();
    Sprite[] frames;
    Color tint;
    float Rate => framesPerSecond * (Phase == 3 ? 1.2f : 1f);
    float AttackDuration => cleaveFrames != null && cleaveFrames.Length > 0 ? cleaveFrames.Length / Rate : 1.25f;

    protected override void Awake()
    {
        base.Awake();
        physicsBody = GetComponent<Rigidbody2D>();
        solid = GetComponent<BoxCollider2D>();
        if (body == null) body = GetComponentInChildren<SpriteRenderer>();
        // This boss owns its Rigidbody. Do not let the generic movement drive it too.
        if (movement != null) { movement.Stop(); movement.enabled = false; }
        if (weaponController != null) weaponController.enabled = false;
        health.OnDamaged += HandleDamage;
        home = transform.position;
        enemyType = EnemyAIData.EnemyType.Boss;
        ResetBoss();
    }

    void OnEnable() { if (physicsBody != null) ResetBoss(); }
    void OnDisable() { desiredVelocity = 0f; if (physicsBody != null) SetVelocity(Vector2.zero); }

    void ResetBoss()
    {
        Phase = 1; patternCounter = 0; engaged = false; comboRemaining = 0;
        desiredVelocity = 0f; hitTimer = 0f; facing = 1; targetSearchTimer = 0f;
        damagedThisSwing.Clear(); tint = normalTint;
        SetState(SlimeState.Idle);
    }

    public override void ResetState()
    {
        base.ResetState();
        transform.position = home;
        enemyType = EnemyAIData.EnemyType.Boss;
        ResetBoss();
    }

    protected override void Update()
    {
        // Intentionally do not tick EnemyAI's ranged/boss-pattern state machine.
        if (health == null) return;
        if (health.IsDead)
        {
            if (State != SlimeState.Dead) SetState(SlimeState.Dead);
            elapsed += Time.deltaTime; animationTime += Time.deltaTime;
            DrawFrame(); return;
        }
        elapsed += Time.deltaTime; animationTime += Time.deltaTime;
        hitTimer = Mathf.Max(0f, hitTimer - Time.deltaTime);
        int nextPhase = health.CurrentHealth / Mathf.Max(1f, health.MaxHealth) <= 0.33f ? 3 :
            health.CurrentHealth / Mathf.Max(1f, health.MaxHealth) <= 0.66f ? 2 : 1;
        if (nextPhase > Phase) { Phase = nextPhase; PhaseChanged?.Invoke(Phase); }
        targetSearchTimer -= Time.deltaTime;
        if ((target == null || !target.gameObject.activeInHierarchy) && targetSearchTimer <= 0f)
        {
            targetSearchTimer = 0.5f;
            var player = GameObject.FindGameObjectWithTag("Player");
            target = player != null ? player.transform : null;
        }
        if (health.IsStaggered)
        {
            if (State != SlimeState.Stagger) SetState(SlimeState.Stagger);
            DrawFrame(); return;
        }
        if (State == SlimeState.Stagger) SetState(SlimeState.Recovery);
        bool validTarget = target != null && target.gameObject.activeInHierarchy;
        if (!validTarget)
        {
            desiredVelocity = 0f;
            if (State != SlimeState.Idle) SetState(SlimeState.Idle);
            DrawFrame(); return;
        }
        // Track the target in windup, ordinary swings and recovery.
        // Once a dash starts, keep its committed movement direction.
        if (!(State == SlimeState.Attack && dashAttack)) FaceTarget();
        float distance = Mathf.Abs(target.position.x - transform.position.x);
        if (!engaged && Vector2.Distance(target.position, transform.position) <= detectionRange) engaged = true;
        switch (State)
        {
            case SlimeState.Idle:
                if (engaged) SetState(SlimeState.Chase);
                break;
            case SlimeState.Chase:
                FaceTarget();
                desiredVelocity = facing * moveSpeed * (1f + (Phase - 1) * 0.18f);
                if (distance <= attackDistance || (Phase >= 2 && distance < 7f && patternCounter % 3 == 2))
                {
                    dashAttack = Phase >= 2 && distance > attackDistance;
                    comboRemaining = Phase == 3 && !dashAttack ? 2 : 1;
                    patternCounter++;
                    SetState(SlimeState.Windup);
                }
                break;
            case SlimeState.Windup:
                // Facing is updated above until a committed dash starts.
                if (elapsed >= telegraphTime / (1f + (Phase - 1) * 0.12f)) SetState(SlimeState.Attack);
                break;
            case SlimeState.Attack:
                int activeFrame = Mathf.FloorToInt(animationTime * Rate);
                desiredVelocity = dashAttack && activeFrame >= 3 && activeFrame <= lastActiveFrame ? facing * dashSpeed : 0f;
                if (activeFrame >= firstActiveFrame && activeFrame <= lastActiveFrame) DealDamage();
                if (elapsed >= AttackDuration)
                {
                    comboRemaining--;
                    if (comboRemaining > 0) { FaceTarget(); dashAttack = false; SetState(SlimeState.Windup); }
                    else SetState(SlimeState.Recovery);
                }
                break;
            case SlimeState.Recovery:
                if (elapsed >= recoveryTime / (1f + (Phase - 1) * 0.2f)) SetState(SlimeState.Chase);
                break;
        }
        DrawFrame();
    }

    protected override void FixedUpdate()
    {
        if (physicsBody == null) return;
        if (health == null || health.IsDead) { SetVelocity(Vector2.zero); return; }
        float vx = health.IsStaggered ? 0f : desiredVelocity;
        if (Mathf.Abs(transform.position.x + vx * Time.fixedDeltaTime - home.x) > arenaHalfWidth) vx = 0f;
        // Use only level geometry in groundMask, NOT Player or Enemy layers.
        if (Mathf.Abs(vx) > 0.01f && groundMask.value != 0)
        {
            Bounds b = solid.bounds;
            float dir = Mathf.Sign(vx);
            Vector2 foot = new Vector2(b.center.x + dir * (b.extents.x + 0.15f), b.min.y + 0.2f);
            bool floor = Physics2D.Raycast(foot, Vector2.down, 0.65f, groundMask);
            bool wall = Physics2D.Raycast(b.center, Vector2.right * dir, b.extents.x + Mathf.Abs(vx) * Time.fixedDeltaTime + 0.08f, groundMask);
            if (!floor || wall) vx = 0f;
        }
        SetVelocity(new Vector2(vx, GetVelocity().y));
    }

    void SetState(SlimeState state)
    {
        State = state; elapsed = animationTime = 0f; desiredVelocity = 0f;
        tint = state == SlimeState.Windup ? warningTint : normalTint;
        if (state == SlimeState.Attack) damagedThisSwing.Clear();
        frames = state == SlimeState.Chase ? walkFrames : state == SlimeState.Attack ? cleaveFrames :
            state == SlimeState.Stagger ? hitFrames : state == SlimeState.Dead ? deathFrames : idleFrames;
    }

    void FaceTarget()
    {
        if (target != null && Mathf.Abs(target.position.x - transform.position.x) > 0.05f)
            facing = target.position.x >= transform.position.x ? 1 : -1;
    }

    Vector2 AttackCenter => (Vector2)transform.position + new Vector2(facing * attackReach * 0.5f, 1f);
    void DealDamage()
    {
        foreach (var col in Physics2D.OverlapBoxAll(AttackCenter, attackSize, 0f))
        {
            // Restrict hits to the assigned player's hierarchy; never damage other enemies.
            if (target == null || !(col.transform == target || col.transform.IsChildOf(target) || target.IsChildOf(col.transform))) continue;
            IDamageable victim = col.GetComponentInParent<IDamageable>();
            if (victim == null || victim == (IDamageable)health || !damagedThisSwing.Add(victim)) continue;
            victim.TakeDamage(new DamageInfo(damage * (1f + (Phase - 1) * 0.25f), gameObject,
                new Vector2(facing, 0.2f).normalized, dashAttack ? 7f : 4f, HitType.Melee));
        }
    }

    void DrawFrame()
    {
        if (body == null) return;
        body.flipX = artworkFacesRight ? facing < 0 : facing > 0;
        body.color = hitTimer > 0f ? new Color(1f, 0.65f, 0.65f) : tint;
        Sprite[] selected = frames;
        float clock = animationTime;
        if (hitTimer > 0f && State != SlimeState.Attack && State != SlimeState.Dead && hitFrames != null && hitFrames.Length > 0)
        { selected = hitFrames; clock = hurtTime - hitTimer; }
        if (selected == null || selected.Length == 0) return;
        int index = Mathf.FloorToInt(clock * Rate);
        bool loop = State == SlimeState.Idle || State == SlimeState.Chase || State == SlimeState.Recovery || State == SlimeState.Windup;
        body.sprite = selected[loop && selected == frames ? index % selected.Length : Mathf.Min(index, selected.Length - 1)];
    }

    protected override void OnDied()
    {
        base.OnDied(); SetState(SlimeState.Dead);
        if (physicsBody != null) SetVelocity(Vector2.zero);
    }

    void OnGUI()
    {
        if (!showBossBar || !engaged || health == null || health.IsDead) return;
        float width = Mathf.Min(520f, Screen.width * 0.7f), x = (Screen.width - width) / 2f;
        GUI.Box(new Rect(x, 22f, width, 52f), "DEMON SLIME  |  PHASE " + Phase);
        Color old = GUI.color; GUI.color = new Color(0.7f, 0.08f, 0.14f);
        GUI.DrawTexture(new Rect(x + 10f, 53f, (width - 20f) * Mathf.Clamp01(health.CurrentHealth / health.MaxHealth), 12f), Texture2D.whiteTexture);
        GUI.color = old;
    }

    protected override void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red; Gizmos.DrawWireCube(AttackCenter, attackSize);
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, detectionRange);
    }

    Vector2 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return physicsBody.linearVelocity;
#else
        return physicsBody.velocity;
#endif
    }
    void SetVelocity(Vector2 v)
    {
#if UNITY_6000_0_OR_NEWER
        physicsBody.linearVelocity = v;
#else
        physicsBody.velocity = v;
#endif
    }
    void HandleDamage(DamageInfo info) { engaged = true; hitTimer = hurtTime; }
    public void ShowHit() { hitTimer = hurtTime; }
}
