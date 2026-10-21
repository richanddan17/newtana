using UnityEngine;

/// <summary>
/// 캐스트 연출 재생 (v5 player_1 [6]).
/// 몸 시트 19_cast 와 별개로, 이펙트 시트를 단계별로 얹는다:
///   gather → chargeLoop → release   (일반 캐스트)
///   bigLoop → bigRelease             (강 캐스트)
///   aura                              (주변 아우라, 루프)
/// 게임플레이 트리거는 아직 없으므로 외부에서 API로 호출한다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerCastFx : MonoBehaviour
{
    [SerializeField] PlayerAnimationSet animationSet;
    [SerializeField] SpriteRenderer castFxRenderer; // CastFX
    [SerializeField] SpriteRenderer auraFxRenderer; // AuraFX
    [SerializeField] Transform castPoint;          // 이펙트가 붙을 기준 위치

    PlayerAnimationClip playingClip;
    int playingFrame;
    float playingTimer;
    bool playingActive;

    PlayerAnimationClip auraClip;
    int auraFrame;
    float auraTimer;
    bool auraActive;

    public bool IsPlaying => playingActive;

    void Awake()
    {
        if (animationSet == null)
        {
            var anim = GetComponent<PlayerAnimation>();
            if (anim != null) animationSet = anim.AnimationSet;
        }
        if (castFxRenderer == null) castFxRenderer = FindRenderer("CastFX");
        if (auraFxRenderer == null) auraFxRenderer = FindRenderer("AuraFX");
        if (castPoint == null) castPoint = transform.Find("CastPoint");
    }

    SpriteRenderer FindRenderer(string childName)
    {
        var child = transform.Find(childName);
        return child != null ? child.GetComponent<SpriteRenderer>() : null;
    }

    void Update()
    {
        UpdatePlaying();
        UpdateAura();
    }

    #region 공개 API

    /// <summary>입력 모으기 단계 (gather).</summary>
    public void PlayGather() => PlayOneShot(animationSet != null ? animationSet.castGather : null);

    /// <summary>-charge 루프 (chargeLoop).</summary>
    public void PlayChargeLoop() => PlayLoop(animationSet != null ? animationSet.castChargeLoop : null);

    /// <summary>해제 단계 (release).</summary>
    public void PlayRelease() => PlayOneShot(animationSet != null ? animationSet.castRelease : null);

    /// <summary>강 캐스트 루프 (bigLoop).</summary>
    public void PlayBigLoop() => PlayLoop(animationSet != null ? animationSet.castBigLoop : null);

    /// <summary>강 캐스트 해제 (bigRelease).</summary>
    public void PlayBigRelease() => PlayOneShot(animationSet != null ? animationSet.castBigRelease : null);

    /// <summary>아우라 표시 (루프).</summary>
    public void ShowAura() => PlayAura(animationSet != null ? animationSet.castAura : null);

    public void HideAura()
    {
        auraActive = false;
        auraClip = null;
        if (auraFxRenderer != null) auraFxRenderer.sprite = null;
    }

    /// <summary>캐스트 연출 전체 정지 (사망/부활 시 호출).</summary>
    public void StopAll()
    {
        playingActive = false;
        playingClip = null;
        if (castFxRenderer != null) castFxRenderer.sprite = null;
        HideAura();
    }

    #endregion

    void PlayOneShot(PlayerAnimationClip clip)
    {
        if (!HasFrames(clip)) return;
        playingClip = clip;
        playingFrame = 0;
        playingTimer = 0f;
        playingActive = true;
        if (castFxRenderer != null) castFxRenderer.sprite = clip.frames[0];
    }

    void PlayLoop(PlayerAnimationClip clip)
    {
        if (!HasFrames(clip)) return;
        playingClip = clip;
        playingFrame = 0;
        playingTimer = 0f;
        playingActive = true;
        if (castFxRenderer != null) castFxRenderer.sprite = clip.frames[0];
    }

    void PlayAura(PlayerAnimationClip clip)
    {
        if (!HasFrames(clip)) return;
        auraClip = clip;
        auraFrame = 0;
        auraTimer = 0f;
        auraActive = true;
        if (auraFxRenderer != null) auraFxRenderer.sprite = clip.frames[0];
    }

    void UpdatePlaying()
    {
        if (!playingActive || !HasFrames(playingClip)) return;
        if (castFxRenderer == null) return;

        float interval = playingClip.frameInterval;
        if (interval > 0f)
        {
            playingTimer += Time.deltaTime;
            while (playingTimer >= interval)
            {
                playingTimer -= interval;
                playingFrame++;
                if (playingFrame >= playingClip.frames.Length)
                {
                    if (playingClip.loop) playingFrame = 0;
                    else
                    {
                        playingActive = false;
                        playingClip = null;
                        castFxRenderer.sprite = null;
                        return;
                    }
                }
            }
        }

        Sprite next = playingClip.frames[playingFrame];
        if (next != null && castFxRenderer.sprite != next) castFxRenderer.sprite = next;
    }

    void UpdateAura()
    {
        if (!auraActive || !HasFrames(auraClip)) return;
        if (auraFxRenderer == null) return;

        float interval = auraClip.frameInterval;
        if (interval > 0f)
        {
            auraTimer += Time.deltaTime;
            while (auraTimer >= interval)
            {
                auraTimer -= interval;
                auraFrame++;
                if (auraFrame >= auraClip.frames.Length) auraFrame = 0; // 아우라는 항상 루프
            }
        }

        Sprite next = auraClip.frames[auraFrame];
        if (next != null && auraFxRenderer.sprite != next) auraFxRenderer.sprite = next;
    }

    static bool HasFrames(PlayerAnimationClip clip)
    {
        return clip != null && clip.frames != null && clip.frames.Length > 0;
    }
}
