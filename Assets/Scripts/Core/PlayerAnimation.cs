using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>클립별 재생 속도 설정. Inspector에서 애니메이션마다 따로 조정한다.</summary>
[Serializable]
public class ClipPlaybackSpeed
{
    public PlayerAnimationClipId clip;

    [Tooltip("1 = 원본, 작을수록 느려진다.")]
    [Range(0.1f, 3f)] public float speed = 0.75f;
}

/// <summary>
/// 플레이어 애니메이션 (v5 player_1).
/// - 몸통은 Body 자식의 SpriteRenderer 한 장이 담당한다 (공격·캐스트 몸 동작 포함).
/// - 이펙트는 CastFX / AuraFX 자식이 담당한다.
/// - 좌우 반전은 루트 scale.x 가 담당한다 (PlayerMovement가 기록). SpriteRenderer.flipX 는 쓰지 않는다.
/// - 시트·프레임·타이밍은 PlayerAnimationSet 에셋이 보유한다.
/// </summary>
[DisallowMultipleComponent]
public partial class PlayerAnimation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerAnimationSet animationSet;
    [SerializeField] PlayerStateController stateController;
    [SerializeField] PlayerMovement movement;
    [SerializeField] SpriteRenderer spriteRenderer; // Body
    [SerializeField] SpriteRenderer castFxRenderer; // CastFX
    [SerializeField] SpriteRenderer auraFxRenderer; // AuraFX
    [SerializeField] WeaponController weapon;
    [SerializeField] Transform muzzleTransform;
    [SerializeField] SpriteRenderer muzzleFxRenderer;

    [Header("Muzzle (v5: 몸 시트에 방향이 없으므로 단일 오프셋)")]
    [SerializeField] Vector2 muzzleOffset = new Vector2(0.08f, 0.04f);

    [Header("Options")]
    [SerializeField] bool useAimDirectionForFlip = true;

    [Header("Playback Speed (per clip)")]
    [Tooltip("목록에 없는 클립에 적용되는 기본 속도. 1 = 원본, 작을수록 느려진다.")]
    [SerializeField, Range(0.1f, 3f)] float defaultPlaybackSpeed = 0.75f;
    [Tooltip("클립별 재생 속도. 목록에 있는 클립은 기본 속도 대신 이 값을 쓴다.")]
    [SerializeField] ClipPlaybackSpeed[] clipPlaybackSpeeds = new ClipPlaybackSpeed[0];

    public event Action<int> OnAimDirectionChanged;
    public event Action<PlayerAnimationClip> OnDebugClipFinished;
    public event Action<bool> OnShootingChanged;
    public event Action OnHurtAnimationStart;
    public event Action OnDeathAnimationStart;

    /// <summary>부활 연출 중 여부. RespawnSystem이 설정하고, wake 클립이 끝나면 자동 해제.</summary>
    public bool IsWakePlaying { get; set; }

    /// <summary>공격/차지 중 좌우 방향 고정. PlayerMovement 가 이 값을 보고 flip 을 멈춘다.</summary>
    public bool FreezeFacing { get; set; }

    /// <summary>현재 재생 중인 클립의 재생 속도.</summary>
    public float PlaybackSpeed => GetPlaybackSpeed(activeClip);

    /// <summary>지정한 클립의 재생 속도. 클립별 설정 → 기본값 순으로 찾는다.</summary>
    public float GetPlaybackSpeed(PlayerAnimationClip clip)
    {
        if (clip != null && ReferenceEquals(clip, debugClipOverride) && debugClipPlaybackSpeed > 0f)
            return Mathf.Max(0.01f, debugClipPlaybackSpeed);

        if (clip != null && clipPlaybackSpeeds != null && animationSet != null)
        {
            for (int i = 0; i < clipPlaybackSpeeds.Length; i++)
            {
                ClipPlaybackSpeed entry = clipPlaybackSpeeds[i];
                if (entry == null) continue;
                if (ReferenceEquals(animationSet.Get(entry.clip), clip))
                    return Mathf.Max(0.01f, entry.speed);
            }
        }

        return Mathf.Max(0.01f, ResolveUnlistedPlaybackSpeed(clip));
    }

    float ResolveUnlistedPlaybackSpeed(PlayerAnimationClip clip)
    {
        return IsSwordAttackClip(clip) ? SwordAttackPlaybackSpeed : defaultPlaybackSpeed;
    }

    bool IsSwordAttackClip(PlayerAnimationClip clip)
    {
        if (clip == null || animationSet == null) return false;

        return ReferenceEquals(animationSet.Get(PlayerAnimationClipId.Attack1), clip)
            || ReferenceEquals(animationSet.Get(PlayerAnimationClipId.Attack2), clip)
            || ReferenceEquals(animationSet.Get(PlayerAnimationClipId.Attack3), clip)
            || ReferenceEquals(animationSet.Get(PlayerAnimationClipId.JumpAttack), clip)
            || ReferenceEquals(animationSet.Get(PlayerAnimationClipId.UpAttack), clip)
            || ReferenceEquals(animationSet.Get(PlayerAnimationClipId.DownAttack), clip)
            || ReferenceEquals(animationSet.Get(PlayerAnimationClipId.HeavyAttack), clip);
    }

    public PlayerAnimationSet AnimationSet => animationSet;

    int currentAimDirIndex = PlayerStateController.AimDirFront;
    PlayerAnimationClip hurtClipOverride;
    bool hurtAnimationDone;
    PlayerAnimationClip debugClipOverride;

    void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = FindChildRenderer("Body") ?? GetComponentInChildren<SpriteRenderer>();
        if (castFxRenderer == null) castFxRenderer = FindChildRenderer("CastFX");
        if (auraFxRenderer == null) auraFxRenderer = FindChildRenderer("AuraFX");
        if (stateController == null) stateController = GetComponent<PlayerStateController>();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (muzzleFxRenderer == null) muzzleFxRenderer = FindChildRenderer("MuzzleFX");

        if (stateController != null)
        {
            PlayerStateEvents.OnMoveStateChanged += OnMoveStateChanged;
            PlayerStateEvents.OnOverlayChanged += OnOverlayChanged;
        }
        if (weapon != null) weapon.OnFire += OnWeaponFired;
    }

    void OnDestroy()
    {
        if (stateController != null)
        {
            PlayerStateEvents.OnMoveStateChanged -= OnMoveStateChanged;
            PlayerStateEvents.OnOverlayChanged -= OnOverlayChanged;
        }
        if (weapon != null) weapon.OnFire -= OnWeaponFired;
    }

    void LateUpdate()
    {
        UpdateAimDirection();
        UpdateSprite();
        UpdateMuzzlePosition();
        UpdateMuzzleFx();
    }

    /// <summary>현재 상태에서 표시할 클립을 결정한다. 우선순위: Death → Wake → Hurt → Dash/Shooting 오버레이 → 이동 상태.</summary>
    PlayerAnimationClip ResolveClip()
    {
        if (animationSet == null) return null;

        if (HasFrames(debugClipOverride)) return debugClipOverride;

        if (stateController == null) return null;

        PlayerState moveState = stateController.CurrentMoveState;
        PlayerState overlay = stateController.CurrentOverlayState;

        if (moveState == PlayerState.Death) return WithFallback(animationSet.death, animationSet.idle);

        if (IsWakePlaying && HasFrames(animationSet.wake)) return animationSet.wake;

        if (moveState == PlayerState.Hurt)
        {
            if (hurtAnimationDone) return WithFallback(animationSet.idle, null);
            if (hurtClipOverride != null && HasFrames(hurtClipOverride)) return hurtClipOverride;
            return WithFallback(animationSet.hurt, animationSet.idle);
        }

        if (overlay == PlayerState.Dash) return WithFallback(animationSet.dash, animationSet.idle);
        if (overlay == PlayerState.Shooting) return WithFallback(animationSet.attack1, animationSet.idle);

        switch (moveState)
        {
            case PlayerState.Idle: return WithFallback(animationSet.idle, null);
            case PlayerState.Run: return WithFallback(animationSet.run, animationSet.idle);
            case PlayerState.Jump: return WithFallback(animationSet.jump, animationSet.idle);
            case PlayerState.Fall: return WithFallback(animationSet.fall, animationSet.idle);
            case PlayerState.Crouch: return WithFallback(animationSet.crouch, animationSet.idle);
            case PlayerState.Dash: return WithFallback(animationSet.dash, animationSet.idle);
            default: return animationSet.idle;
        }
    }

    static PlayerAnimationClip WithFallback(PlayerAnimationClip primary, PlayerAnimationClip fallback)
    {
        if (HasFrames(primary)) return primary;
        return HasFrames(fallback) ? fallback : primary;
    }

    static bool HasFrames(PlayerAnimationClip clip)
    {
        return clip != null && clip.frames != null && clip.frames.Length > 0;
    }

    void UpdateAimDirection()
    {
        if (!useAimDirectionForFlip || stateController == null) return;

        int newAimDir = stateController.AimDirectionIndex;
        if (newAimDir != currentAimDirIndex)
        {
            currentAimDirIndex = newAimDir;
            OnAimDirectionChanged?.Invoke(currentAimDirIndex);
        }
    }

    void UpdateMuzzlePosition()
    {
        if (muzzleTransform == null) return;
        muzzleTransform.localPosition = new Vector3(muzzleOffset.x, muzzleOffset.y, 0f);
    }

    #region State Event Handlers

    void OnMoveStateChanged(PlayerState from, PlayerState to)
    {
        if (to == PlayerState.Death)
        {
            OnDeathAnimationStart?.Invoke();
        }
        else if (to == PlayerState.Hurt)
        {
            OnHurtAnimationStart?.Invoke();
            ResolveHurtVariant();
        }
        else if (to == PlayerState.Dash)
        {
            SpawnDashDust();
        }
    }

    void SpawnDashDust()
    {
        if (animationSet == null) return;
        PlayerBowShot.SpawnFootOneShot(animationSet.dashDust, spriteRenderer, transform.position, 8);
        PlayerBowShot.SpawnFootOneShot(animationSet.dashFx, spriteRenderer, transform.position, 6);
    }

    void OnOverlayChanged(PlayerState overlay, bool active)
    {
        if (overlay == PlayerState.Shooting) OnShootingChanged?.Invoke(active);
    }

    /// <summary>피격 진입 순간 넉백 여부를 한 번만 판정해 고정한다 (속도 감쇠에 따라 클립이 갈라지지 않도록).</summary>
    void ResolveHurtVariant()
    {
        hurtClipOverride = null;
        hurtAnimationDone = false;
        if (animationSet == null) return;

        if (HasFrames(animationSet.hurt)) hurtClipOverride = animationSet.hurt;
    }

    #endregion

    #region Public API

    public void TriggerHurtFlash() => OnHurtAnimationStart?.Invoke();

    public void TriggerDeath() => OnDeathAnimationStart?.Invoke();

    /// <summary>부활 시 애니메이션 리셋 — 잔여 연출 정리 + 다음 사망에서 death 클립이 다시 재생되도록.</summary>
    public void ResetAnimation()
    {
        IsWakePlaying = false;
        FreezeFacing = false;
        hurtClipOverride = null;
        debugClipOverride = null;
        debugClipPlaybackSpeed = 0f;
        debugLoopStartFrame = 0;
        debugLoopEndFrame = -1;
        lastDebugFrame = -1;
        activeClip = null;
        activeFrame = 0;
        activeTimer = 0f;
        StopMuzzleFx();
    }

    public int GetCurrentAimDirectionIndex() => currentAimDirIndex;

    public SpriteRenderer BodyRenderer => spriteRenderer;

    /// <summary>현재 재생 중인 디버그/전투 클립. OnDebugClipFrame 구독자가 프레임 이벤트를 해석할 때 사용한다.</summary>
    public PlayerAnimationClip CurrentDebugClip => debugClipOverride;

    public float CurrentFrameInterval =>
        activeClip != null
            ? activeClip.GetFrameDuration(activeFrame) / GetPlaybackSpeed(activeClip)
            : 0.05f / GetPlaybackSpeed(null);

    public void PlayDebugClip(PlayerAnimationClip clip)
    {
        PlayDebugClip(clip, 0, -1, 0f);
    }

    public void PlayDebugClip(PlayerAnimationClip clip, int loopStartFrame)
    {
        PlayDebugClip(clip, loopStartFrame, -1, 0f);
    }

    public void PlayDebugClip(PlayerAnimationClip clip, int loopStartFrame, int loopEndFrame)
    {
        PlayDebugClip(clip, loopStartFrame, loopEndFrame, 0f);
    }

    public void PlayDebugClip(PlayerAnimationClip clip, int loopStartFrame, int loopEndFrame, float playbackSpeed)
    {
        debugClipOverride = clip;
        debugClipPlaybackSpeed = playbackSpeed;
        debugLoopStartFrame = Mathf.Max(0, loopStartFrame);
        debugLoopEndFrame = loopEndFrame;
        lastDebugFrame = -1;
        activeClip = null;
        activeFrame = 0;
        activeTimer = 0f;
    }

    public void ClearDebugClip()
    {
        debugClipOverride = null;
        debugClipPlaybackSpeed = 0f;
        debugLoopStartFrame = 0;
        debugLoopEndFrame = -1;
        lastDebugFrame = -1;
        activeClip = null;
        activeFrame = 0;
        activeTimer = 0f;
    }

    /// <summary>현재 조준 방향 벡터 (우측 기준). 좌우 반전은 루트 scale.x 가 담당.</summary>
    public Vector2 GetCurrentAimDirectionVector()
    {
        float facing = transform.localScale.x > 0f ? 1f : -1f;
        const float kDiagonal = 0.7071068f;

        switch (currentAimDirIndex)
        {
            case PlayerStateController.AimDirUp45:
                return new Vector2(kDiagonal * facing, kDiagonal);
            case PlayerStateController.AimDirUp90:
                return Vector2.up;
            case PlayerStateController.AimDirDown45:
                return new Vector2(kDiagonal * facing, -kDiagonal);
            case PlayerStateController.AimDirDown90:
                return Vector2.down;
            default:
                return new Vector2(facing, 0f);
        }
    }

    public Vector3 GetMuzzleWorldPosition()
    {
        return muzzleTransform != null ? muzzleTransform.position : transform.position;
    }

    #endregion

    SpriteRenderer FindChildRenderer(string childName)
    {
        var child = transform.Find(childName);
        return child != null ? child.GetComponent<SpriteRenderer>() : null;
    }

    void Reset()
    {
        EnsureClipSpeedEntries();
    }

    void OnValidate()
    {
        EnsureClipSpeedEntries();
    }

    void EnsureClipSpeedEntries()
    {
        PlayerAnimationClipId[] ids = (PlayerAnimationClipId[])Enum.GetValues(typeof(PlayerAnimationClipId));
        var entries = new List<ClipPlaybackSpeed>(clipPlaybackSpeeds ?? new ClipPlaybackSpeed[0]);
        bool added = false;

        for (int i = 0; i < ids.Length; i++)
        {
            bool exists = false;
            for (int j = 0; j < entries.Count; j++)
            {
                if (entries[j] != null && entries[j].clip == ids[i])
                {
                    exists = true;
                    break;
                }
            }
            if (exists) continue;

            entries.Add(new ClipPlaybackSpeed
            {
                clip = ids[i],
                speed = IsSwordAttack(ids[i]) ? SwordAttackPlaybackSpeed : defaultPlaybackSpeed
            });
            added = true;
        }

        if (added) clipPlaybackSpeeds = entries.ToArray();
    }

    const float SwordAttackPlaybackSpeed = 1.1f;

    static bool IsSwordAttack(PlayerAnimationClipId clipId)
    {
        switch (clipId)
        {
            case PlayerAnimationClipId.Attack1:
            case PlayerAnimationClipId.Attack2:
            case PlayerAnimationClipId.Attack3:
            case PlayerAnimationClipId.JumpAttack:
            case PlayerAnimationClipId.UpAttack:
            case PlayerAnimationClipId.DownAttack:
            case PlayerAnimationClipId.HeavyAttack:
                return true;
            default:
                return false;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (muzzleTransform == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(muzzleTransform.position, 0.05f);
    }
}
