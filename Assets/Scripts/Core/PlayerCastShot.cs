using UnityEngine;

public enum CastShotType
{
    Basic,
    Rapid,
    Big
}

public static class PlayerCastShot
{
    public static void Fire(PlayerAnimationSet set, CastShotType type, Vector3 origin,
        Vector2 direction, GameObject owner, SpriteRenderer reference)
    {
        if (set == null) return;

        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

        PlayerAnimationClip muzzle = type == CastShotType.Big ? set.muzzleBig
            : type == CastShotType.Rapid ? set.muzzleRapid : set.muzzleBasic;
        PlayerAnimationClip bolt = type == CastShotType.Big ? set.bigOrb
            : type == CastShotType.Rapid ? set.rapidBolt : set.basicBolt;
        PlayerAnimationClip impact = type == CastShotType.Big ? set.impactBig
            : type == CastShotType.Rapid ? set.impactRapid : set.impactBasic;

        float speed = type == CastShotType.Big ? 10f
            : type == CastShotType.Rapid ? 34f : 16f;
        float maxDistance = type == CastShotType.Big ? 18f : 24f;

        PlayerBowShot.SpawnOneShot(muzzle, origin, dir, reference, 10);

        if (!HasFrames(bolt)) return;

        Sprite boltSprite = bolt.frames[0];
        if (boltSprite == null) return;

        GameObject boltObject = new GameObject(type == CastShotType.Big ? "BigOrb" : "CastBolt");
        boltObject.transform.localScale = Vector3.one * PlayerBowShot.VfxScale;
        SpriteRenderer boltRenderer = boltObject.AddComponent<SpriteRenderer>();
        boltRenderer.sprite = boltSprite;
        ApplySorting(boltRenderer, reference, 5);

        // Firepoint sits just past the muzzle flash's leading edge so the bolt
        // appears to leave the muzzle the instant the flash fires.
        float muzzleEdge = GetForwardExtent(muzzle) * PlayerBowShot.VfxScale;
        Vector3 firePoint = origin + (Vector3)(dir * Mathf.Max(0f, muzzleEdge + 0.03f));

        float leftOffset = -boltSprite.bounds.min.x * Mathf.Abs(boltObject.transform.lossyScale.x);
        boltObject.transform.position = firePoint + (Vector3)(dir * leftOffset);
        boltObject.transform.right = dir;

        BowProjectile projectile = boltObject.AddComponent<BowProjectile>();
        projectile.Initialize(bolt, dir, owner, impact, boltRenderer, speed, maxDistance);
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

    static float GetForwardExtent(PlayerAnimationClip clip)
    {
        if (!HasFrames(clip)) return 0f;

        float extent = 0f;
        for (int i = 0; i < clip.frames.Length; i++)
        {
            Sprite sprite = clip.frames[i];
            if (sprite != null) extent = Mathf.Max(extent, sprite.bounds.max.x);
        }
        return extent;
    }
}
