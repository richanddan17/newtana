using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerMeleeVfx : MonoBehaviour
{
    [SerializeField] LayerMask hitLayers = 1 << 7;
    [SerializeField] float defaultHitRadius = 1.1f;

    PlayerAnimation playerAnimation;
    TeamComponent teamComponent;
    SpriteRenderer body;
    SpriteRenderer effect;
    PlayerAnimationClip clip;
    int frameIndex;
    float lifetime;
    float slashZRot;
    Vector2 slashOffset;
    int sortingOffset = 1;
    bool sequenceActive;
    int sequenceCount;
    float sequenceTimer;
    float sequenceInterval;
    readonly HashSet<int> hitTargets = new();

    void Awake()
    {
        playerAnimation = GetComponent<PlayerAnimation>();
        teamComponent = GetComponent<TeamComponent>();

        GameObject effectObject = new GameObject("MeleeFX");
        effectObject.transform.SetParent(transform, false);
        effect = effectObject.AddComponent<SpriteRenderer>();
        effect.enabled = false;
    }

    public void SetSlashRotation(float z)
    {
        slashZRot = z;
        if (effect != null) effect.transform.localRotation = Quaternion.Euler(0f, 0f, z);
    }

    public void SetAnchorOffset(Vector2 offset)
    {
        slashOffset = offset;
    }

    public void BeginSwing()
    {
        hitTargets.Clear();
    }

    public int PlayHit(PlayerAnimationClip hitClip, float radius, SpriteRenderer reference)
    {
        if (hitClip == null || effect == null || !effect.enabled) return 0;

        Team sourceTeam = teamComponent != null ? teamComponent.Team : Team.Player;
        Vector2 center = effect.transform.position;
        float facing = transform.lossyScale.x < 0f ? -1f : 1f;
        Vector2 hitDirection = Quaternion.Euler(0f, 0f, slashZRot) * new Vector2(facing, 0f);
        float queryRadius = radius > 0f ? radius : defaultHitRadius;
        Collider2D[] overlaps = Physics2D.OverlapCircleAll(center, queryRadius, hitLayers);
        SpriteRenderer sortReference = reference != null ? reference : body;
        int spawned = 0;

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider2D overlap = overlaps[i];
            if (overlap == null) continue;

            Hurtbox hurtbox = overlap.GetComponentInParent<Hurtbox>();
            if (hurtbox == null) continue;

            IDamageable damageable = overlap.GetComponentInParent<IDamageable>();
            Team targetTeam = hurtbox.GetTeam();
            if (targetTeam == Team.None && damageable != null) targetTeam = damageable.Team;
            if (!targetTeam.IsHostileTo(sourceTeam)) continue;
            if (damageable != null && damageable.IsDead) continue;
            if (!hitTargets.Add(hurtbox.GetInstanceID())) continue;

            Vector2 point = overlap.ClosestPoint(center);
            PlayerBowShot.SpawnImpact(hitClip, point, hitDirection, sortReference);
            spawned++;
        }

        return spawned;
    }

    public void Show(PlayerAnimationClip source, int sourceFrame, SpriteRenderer reference, float duration)
    {
        if (source == null || source.frames == null || source.frames.Length == 0)
        {
            Hide();
            return;
        }

        if (sourceFrame < 0 || sourceFrame >= source.frames.Length)
        {
            Hide();
            return;
        }

        clip = source;
        frameIndex = sourceFrame;
        body = reference != null ? reference : body;
        lifetime = Mathf.Max(0.01f, duration);
        sortingOffset = 1;

        effect.sprite = clip.frames[frameIndex];
        effect.enabled = true;
        UpdateTransform();
    }

    public void PlaySequence(PlayerAnimationClip source, int count, float frameInterval, SpriteRenderer reference, bool applyVfxSpeed = true, int orderOffset = 1)
    {
        if (source == null || source.frames == null || source.frames.Length == 0)
        {
            Hide();
            return;
        }

        clip = source;
        frameIndex = 0;
        sequenceCount = Mathf.Clamp(count <= 0 ? source.frames.Length : count, 1, source.frames.Length);
        float interval = frameInterval > 0f ? frameInterval : 0.05f;
        if (applyVfxSpeed) interval /= PlayerBowShot.VfxSpeed;
        sequenceInterval = Mathf.Max(0.005f, interval);
        sequenceTimer = 0f;
        sequenceActive = true;
        body = reference != null ? reference : body;
        sortingOffset = orderOffset;

        effect.sprite = clip.frames[0];
        effect.enabled = true;
        UpdateTransform();
    }

    public void Hide()
    {
        sequenceActive = false;
        clip = null;
        if (effect != null) effect.enabled = false;
    }

    void LateUpdate()
    {
        if (clip == null || effect == null || !effect.enabled) return;

        if (sequenceActive)
        {
            sequenceTimer += Time.deltaTime;
            while (sequenceTimer >= sequenceInterval)
            {
                sequenceTimer -= sequenceInterval;
                frameIndex++;
                if (frameIndex >= sequenceCount)
                {
                    Hide();
                    return;
                }
            }

            Sprite next = clip.frames[frameIndex];
            if (next != null && effect.sprite != next) effect.sprite = next;
        }
        else
        {
            lifetime -= Time.deltaTime;
            if (lifetime <= 0f)
            {
                Hide();
                return;
            }
        }

        UpdateTransform();
    }

    void UpdateTransform()
    {
        if (body == null && playerAnimation != null) body = playerAnimation.BodyRenderer;
        if (body == null || clip == null || effect == null) return;
        if (clip.frameOffsets == null || frameIndex >= clip.frameOffsets.Length) return;

        Bounds bounds = body.bounds;
        float facing = body.transform.lossyScale.x < 0f ? -1f : 1f;
        float parentFacing = transform.lossyScale.x < 0f ? -1f : 1f;
        Vector2 offset = clip.frameOffsets[frameIndex] + slashOffset;
        float baseY = clip.footAnchored ? bounds.min.y : bounds.center.y;

        if (clip.footAnchored && effect.sprite != null)
        {
            Vector3 half = effect.sprite.bounds.extents;
            offset += new Vector2(half.x, half.y);
        }

        Vector3 position = new Vector3(
            bounds.center.x + offset.x * facing,
            baseY + offset.y,
            bounds.center.z);

        effect.transform.position = position;
        effect.transform.localRotation = Quaternion.Euler(0f, 0f, slashZRot);
        effect.transform.localScale = new Vector3(facing * parentFacing, 1f, 1f);
        effect.sortingLayerID = body.sortingLayerID;
        effect.sortingOrder = body.sortingOrder + sortingOffset;
    }

    void OnDisable()
    {
        Hide();
    }
}
