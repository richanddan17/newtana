using System;
using UnityEngine;

/// <summary>
/// 시트 1개 = 프레임 배열 + 재생 설정.
/// v5 player_1 기준: 모든 상태가 몸통 한 장(몸 시트) 또는 이펙트 시트.
/// </summary>
[Serializable]
public class PlayerAnimationClip
{
    public string id;
    public Sprite[] frames = new Sprite[0];

    [Tooltip("프레임 간격(초). 0 이하면 정지")]
    public float frameInterval = 0.05f;

    [Tooltip("true=루프, false=1회 재생 후 마지막 프레임 유지")]
    public bool loop;
}

/// <summary>
/// 플레이어 애니메이션 세트 — 모든 시트/프레임/타이밍을 담은 ScriptableObject.
/// 런타임( PlayerAnimation )은 이 에셋 하나만 참조한다.
/// 에셋 생성/갱신: Tools/PlayerAnimation/Import Sheets From Manifest (에디터)
/// </summary>
[CreateAssetMenu(fileName = "PlayerAnimationSet", menuName = "RunGun/Player Animation Set")]
public class PlayerAnimationSet : ScriptableObject
{
    [Header("Body — 이동 계열")]
    public PlayerAnimationClip idle;
    public PlayerAnimationClip run;
    public PlayerAnimationClip jump;
    public PlayerAnimationClip fall;
    public PlayerAnimationClip crouch;
    public PlayerAnimationClip dash;

    [Header("Body — 공격 계열")]
    public PlayerAnimationClip attack1;
    public PlayerAnimationClip attack2;
    public PlayerAnimationClip attack3;
    public PlayerAnimationClip jumpAttack;
    public PlayerAnimationClip upAttack;
    public PlayerAnimationClip downAttack;
    public PlayerAnimationClip heavyAttack;
    public PlayerAnimationClip bow;

    [Header("Body — 특수/피격 계열")]
    public PlayerAnimationClip doubleJump;
    public PlayerAnimationClip wallSlide;
    public PlayerAnimationClip climb;
    public PlayerAnimationClip enterDoor;
    public PlayerAnimationClip grapple;
    public PlayerAnimationClip cast;
    public PlayerAnimationClip hurt;
    public PlayerAnimationClip hurtKnockback;
    public PlayerAnimationClip death;

    [Header("Body — 부활/대기")]
    public PlayerAnimationClip wake;
    public PlayerAnimationClip dormant;

    [Header("Muzzle FX (총구)")]
    public PlayerAnimationClip muzzleBasic;
    public PlayerAnimationClip muzzleRapid;
    public PlayerAnimationClip muzzleBig;

    [Header("Impact FX (피격)")]
    public PlayerAnimationClip impactBasic;
    public PlayerAnimationClip impactRapid;
    public PlayerAnimationClip impactBig;

    [Header("Cast FX (캐스트 단계)")]
    public PlayerAnimationClip castGather;
    public PlayerAnimationClip castChargeLoop;
    public PlayerAnimationClip castRelease;
    public PlayerAnimationClip castBigLoop;
    public PlayerAnimationClip castBigRelease;
    public PlayerAnimationClip castAura;

    [Header("Dust FX (착지/점프/대시)")]
    public PlayerAnimationClip jumpDust;
    public PlayerAnimationClip landDust;
    public PlayerAnimationClip dashDust;
    public PlayerAnimationClip airJumpRing;

    public PlayerAnimationClip Get(PlayerAnimationClipId clipId)
    {
        switch (clipId)
        {
            case PlayerAnimationClipId.Idle: return idle;
            case PlayerAnimationClipId.Run: return run;
            case PlayerAnimationClipId.Jump: return jump;
            case PlayerAnimationClipId.Fall: return fall;
            case PlayerAnimationClipId.Crouch: return crouch;
            case PlayerAnimationClipId.Dash: return dash;
            case PlayerAnimationClipId.Attack1: return attack1;
            case PlayerAnimationClipId.Attack2: return attack2;
            case PlayerAnimationClipId.Attack3: return attack3;
            case PlayerAnimationClipId.JumpAttack: return jumpAttack;
            case PlayerAnimationClipId.UpAttack: return upAttack;
            case PlayerAnimationClipId.DownAttack: return downAttack;
            case PlayerAnimationClipId.HeavyAttack: return heavyAttack;
            case PlayerAnimationClipId.Bow: return bow;
            case PlayerAnimationClipId.DoubleJump: return doubleJump;
            case PlayerAnimationClipId.WallSlide: return wallSlide;
            case PlayerAnimationClipId.Climb: return climb;
            case PlayerAnimationClipId.EnterDoor: return enterDoor;
            case PlayerAnimationClipId.Grapple: return grapple;
            case PlayerAnimationClipId.Cast: return cast;
            case PlayerAnimationClipId.Hurt: return hurt;
            case PlayerAnimationClipId.HurtKnockback: return hurtKnockback;
            case PlayerAnimationClipId.Death: return death;
            case PlayerAnimationClipId.Wake: return wake;
            case PlayerAnimationClipId.Dormant: return dormant;
            default: return null;
        }
    }
}

/// <summary>세트에 담긴 클립의 종류. 상태→클립 매핑과 에디터 임포트 매핑에 함께 사용.</summary>
public enum PlayerAnimationClipId
{
    Idle, Run, Jump, Fall, Crouch, Dash,
    Attack1, Attack2, Attack3, JumpAttack, UpAttack, DownAttack, HeavyAttack, Bow,
    DoubleJump, WallSlide, Climb, EnterDoor, Grapple, Cast,
    Hurt, HurtKnockback, Death, Wake, Dormant
}
