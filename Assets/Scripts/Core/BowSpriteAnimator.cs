using UnityEngine;

public class BowSpriteAnimator : MonoBehaviour
{
    SpriteRenderer spriteRenderer;
    PlayerAnimationClip clip;
    int frame;
    float timer;

    public void Initialize(PlayerAnimationClip source, SpriteRenderer target)
    {
        clip = source;
        spriteRenderer = target;
        frame = 0;
        timer = 0f;
        if (spriteRenderer != null && clip != null && clip.frames.Length > 0)
        {
            spriteRenderer.sprite = clip.frames[0];
        }
    }

    void Update()
    {
        if (clip == null || spriteRenderer == null || clip.frames == null || clip.frames.Length == 0) return;

        float interval = (clip.frameInterval > 0f ? clip.frameInterval : 0.04f) / PlayerBowShot.VfxSpeed;
        timer += Time.deltaTime;
        while (timer >= interval)
        {
            timer -= interval;
            frame++;
            if (frame >= clip.frames.Length)
            {
                if (clip.loop)
                {
                    frame = 0;
                }
                else
                {
                    Destroy(gameObject);
                    return;
                }
            }
        }

        spriteRenderer.sprite = clip.frames[frame];
    }
}
