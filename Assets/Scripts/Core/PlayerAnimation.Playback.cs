using UnityEngine;

/// <summary>
/// PlayerAnimation 프레임 재생부 (partial).
/// 클립 선택은 PlayerAnimation.ResolveClip, 프레임 진행은 이 파일이 담당한다.
/// 1회 클립은 마지막 프레임에서 정지하고, 루프 클립은 0으로 순환한다.
/// </summary>
public partial class PlayerAnimation
{
    PlayerAnimationClip activeClip;
    int activeFrame;
    float activeTimer;

    void UpdateSprite()
    {
        if (spriteRenderer == null) return;

        PlayerAnimationClip clip = ResolveClip();
        if (!HasFrames(clip)) return;

        if (!ReferenceEquals(clip, activeClip))
        {
            activeClip = clip;
            activeFrame = 0;
            activeTimer = 0f;
        }
        else
        {
            AdvanceFrame(clip);
        }

        Sprite next = clip.frames[activeFrame];
        if (next != null && spriteRenderer.sprite != next) spriteRenderer.sprite = next;
    }

    void AdvanceFrame(PlayerAnimationClip clip)
    {
        if (clip.frames.Length <= 1) return;

        float interval = clip.frameInterval;
        if (interval <= 0f) return;

        activeTimer += Time.deltaTime;
        while (activeTimer >= interval)
        {
            activeTimer -= interval;
            if (activeFrame + 1 < clip.frames.Length)
            {
                activeFrame++;
            }
            else if (clip.loop)
            {
                activeFrame = 0;
            }
            else
            {
                activeTimer = 0f;
                break;
            }
        }
    }

    #region MuzzleFX

    PlayerAnimationClip muzzleFxClip;
    int muzzleFxFrame;
    float muzzleFxTimer;
    bool muzzleFxPlaying;

    void OnWeaponFired() => StartMuzzleFx();

    /// <summary>발사 순간 총구 플래시를 0프레임부터 재생. Death/부활 중에는 재개하지 않는다.</summary>
    void StartMuzzleFx()
    {
        if (muzzleFxRenderer == null || animationSet == null) return;
        if (IsWakePlaying) return;
        if (stateController != null && stateController.CurrentMoveState == PlayerState.Death) return;

        PlayerAnimationClip clip = animationSet.muzzleBasic;
        if (!HasFrames(clip)) return;

        muzzleFxClip = clip;
        muzzleFxFrame = 0;
        muzzleFxTimer = 0f;
        muzzleFxPlaying = true;
        muzzleFxRenderer.sprite = clip.frames[0];
    }

    void UpdateMuzzleFx()
    {
        if (!muzzleFxPlaying || muzzleFxRenderer == null || !HasFrames(muzzleFxClip)) return;

        float interval = muzzleFxClip.frameInterval;
        if (interval > 0f)
        {
            muzzleFxTimer += Time.deltaTime;
            while (muzzleFxTimer >= interval)
            {
                muzzleFxTimer -= interval;
                muzzleFxFrame++;
                if (muzzleFxFrame >= muzzleFxClip.frames.Length)
                {
                    muzzleFxPlaying = false;
                    muzzleFxRenderer.sprite = null;
                    return;
                }
            }
        }

        Sprite next = muzzleFxClip.frames[muzzleFxFrame];
        if (next != null && muzzleFxRenderer.sprite != next) muzzleFxRenderer.sprite = next;
    }

    void StopMuzzleFx()
    {
        muzzleFxPlaying = false;
        muzzleFxClip = null;
        if (muzzleFxRenderer != null) muzzleFxRenderer.sprite = null;
    }

    #endregion
}
