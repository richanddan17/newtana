using UnityEngine;

public static class PlayerBowShot
{
    public const float VfxScale = 4f;
    public const float VfxSpeed = 1.8f;
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

    public static void Fire(PlayerAnimationSet set, Vector3 origin, Vector2 direction, GameObject owner, SpriteRenderer reference)
    {
        if (set == null) return;

        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        SpawnOneShot(ReverseFrames(set.arrowRelease), origin, dir, reference, 10);

        if (!HasFrames(set.energyArrow)) return;

        GameObject boltObject = new GameObject("BowBolt");
        boltObject.transform.localScale = Vector3.one * VfxScale;
        SpriteRenderer boltRenderer = boltObject.AddComponent<SpriteRenderer>();
        boltRenderer.sprite = set.energyArrow.frames[0];
        ApplySorting(boltRenderer, reference, 5);

        float leftOffset = -boltRenderer.sprite.bounds.min.x * Mathf.Abs(boltObject.transform.lossyScale.x);
        boltObject.transform.position = origin + (Vector3)(dir * leftOffset);
        boltObject.transform.right = dir;

        BowProjectile projectile = boltObject.AddComponent<BowProjectile>();
        projectile.Initialize(set.energyArrow, dir, owner, set.arrowImpact, boltRenderer);
    }

    public static void SpawnImpact(PlayerAnimationClip clip, Vector3 position, Vector2 direction, SpriteRenderer reference)
    {
        SpawnOneShot(clip, position, direction, reference, 12);
    }

    static PlayerAnimationClip ReverseFrames(PlayerAnimationClip source)
    {
        if (!HasFrames(source)) return source;

        int count = source.frames.Length;
        Sprite[] frames = new Sprite[count];
        Vector2[] offsets = source.frameOffsets != null && source.frameOffsets.Length == count
            ? new Vector2[count]
            : null;

        for (int i = 0; i < count; i++)
        {
            int sourceIndex = count - 1 - i;
            frames[i] = source.frames[sourceIndex];
            if (offsets != null) offsets[i] = source.frameOffsets[sourceIndex];
        }

        return new PlayerAnimationClip
        {
            id = source.id + "_reverse",
            frames = frames,
            frameOffsets = offsets ?? source.frameOffsets,
            frameInterval = source.frameInterval,
            loop = false,
            footAnchored = source.footAnchored
        };
    }

    public static void SpawnOneShot(PlayerAnimationClip clip, Vector3 position, Vector2 direction, SpriteRenderer reference, int orderOffset)
    {
        if (!HasFrames(clip)) return;

        GameObject effectObject = new GameObject("BowFX");
        effectObject.transform.position = position;
        effectObject.transform.localScale = Vector3.one * VfxScale;
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


