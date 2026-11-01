using UnityEngine;

public static class PlayerBowShot
{
    // VFX 스프라이트는 300ppu, 모션 시트는 1200ppu라 배율은 참조 렌더러의 월드 스케일에서 파생한다.
    public const float VfxScale = 1f;
    public const float VfxSpeed = 1.8f;

    public static float ResolveScale(SpriteRenderer reference, float multiplier = 1f)
    {
        float world = 1f;
        if (reference != null)
        {
            Vector3 scale = reference.transform.lossyScale;
            world = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            if (world <= 0.0001f) world = 1f;
        }

        return VfxScale * world * Mathf.Max(0.0001f, multiplier);
    }
    public static Vector3 GetBowTip(SpriteRenderer body, Vector2 direction, Vector3 fallback)
    {
        if (body == null || body.sprite == null) return fallback;

        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        Bounds bounds = body.bounds;
        Vector2 center = bounds.center;
        Vector2 extents = bounds.extents;
        float distance = float.PositiveInfinity;

        if (Mathf.Abs(dir.x) > 0.0001f) distance = Mathf.Min(distance, extents.x / Mathf.Abs(dir.x));
        if (Mathf.Abs(dir.y) > 0.0001f) distance = Mathf.Min(distance, extents.y / Mathf.Abs(dir.y));

        if (distance < float.PositiveInfinity)
        {
            center += dir * (distance + 0.01f);
        }

        return new Vector3(center.x, center.y, fallback.z);
    }

    public static void Fire(PlayerAnimationSet set, Vector3 origin, Vector2 direction, GameObject owner, SpriteRenderer reference, float damage = 10f)
    {
        if (set == null) return;

        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

        if (!HasFrames(set.energyArrow)) return;

        GameObject boltObject = new GameObject("BowBolt");
        boltObject.transform.localScale = Vector3.one * ResolveScale(reference);
        SpriteRenderer boltRenderer = boltObject.AddComponent<SpriteRenderer>();
        boltRenderer.sprite = set.energyArrow.frames[0];
        ApplySorting(boltRenderer, reference, 5);

        float leftOffset = -boltRenderer.sprite.bounds.min.x * Mathf.Abs(boltObject.transform.lossyScale.x);
        boltObject.transform.position = origin + (Vector3)(dir * leftOffset);
        boltObject.transform.right = dir;

        BowProjectile projectile = boltObject.AddComponent<BowProjectile>();
        projectile.Initialize(set.energyArrow, dir, owner, set.arrowImpact, boltRenderer, damage);
    }

    public static void SpawnImpact(PlayerAnimationClip clip, Vector3 position, Vector2 direction, SpriteRenderer reference)
    {
        SpawnOneShot(clip, position, direction, reference, 12);
    }

    public static void SpawnFootOneShot(PlayerAnimationClip clip, SpriteRenderer body, Vector3 fallbackPosition, int orderOffset)
    {
        if (!HasFrames(clip)) return;

        if (body == null)
        {
            SpawnOneShot(clip, fallbackPosition, Vector2.right, null, orderOffset);
            return;
        }

        float scale = ResolveScale(body);
        Bounds bounds = body.bounds;
        Sprite first = clip.frames[0];
        float halfHeight = first != null ? first.bounds.extents.y * scale : 0f;
        Vector3 position = new Vector3(bounds.center.x, bounds.min.y + halfHeight, bounds.center.z);
        SpawnOneShot(clip, position, Vector2.right, body, orderOffset);
    }

    public static void SpawnOneShot(PlayerAnimationClip clip, Vector3 position, Vector2 direction, SpriteRenderer reference, int orderOffset)
    {
        if (!HasFrames(clip)) return;

        GameObject effectObject = new GameObject("BowFX");
        effectObject.transform.position = position;
        effectObject.transform.localScale = Vector3.one * ResolveScale(reference);
        if (direction.sqrMagnitude > 0.0001f) effectObject.transform.right = direction;

        SpriteRenderer effectRenderer = effectObject.AddComponent<SpriteRenderer>();
        effectRenderer.sprite = clip.frames[0];
        ApplySorting(effectRenderer, reference, orderOffset);

        BowSpriteAnimator animator = effectObject.AddComponent<BowSpriteAnimator>();
        animator.Initialize(clip, effectRenderer);
    }

    static void ApplySorting(SpriteRenderer target, SpriteRenderer reference, int orderOffset)
    {
        if (reference == null) return;
        target.sortingLayerID = reference.sortingLayerID;
        target.sortingOrder = reference.sortingOrder + orderOffset;
    }

    static bool HasFrames(PlayerAnimationClip clip)
    {
        return clip != null && clip.frames != null && clip.frames.Length > 0;
    }
}


