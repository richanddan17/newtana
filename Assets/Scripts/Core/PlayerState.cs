using System;
using UnityEngine;

/// <summary>
/// 플레이어 상태 머신 - 계획서 [공통 규약 4] "단일 출처"
/// 이동/애니메이션/무기/체력 문서는 여기만 참조하고 별도 정의 금지.
/// </summary>
public enum PlayerState
{
    // 이동 계열 (동시 1개만 활성)
    Idle = 0,
    Run = 1,
    Jump = 2,       // 상승
    Fall = 3,       // 하강
    Crouch = 4,     // 앉기 (에셋에 있음)
    Hurt = 5,       // 피격 경직/넉백
    Death = 6,      // 사망

    // 오버레이 (이동 상태 위에 겹쳐 활성화될 수 있음)
    Shooting = 100, // 사격 중
    Reloading = 101,// 재장전 (UseAmmo=true일 때만 사용, 기본 비활성)
    Dash = 102,     // 대시 (에셋에 클립 없음 → 기본 비활성, enableDash=true로 활성화)
}

/// <summary>
/// 상태 우선순위 및 허용 규칙 정적 데이터.
/// 계획서 player-state-input-system.md [2], [3] 반영.
/// </summary>
public static class PlayerStateRules
{
    /// <summary>우선순위 높을수록 하위 상태 진입 차단/덮어씀</summary>
    public static readonly PlayerState[] PriorityOrder = new[]
    {
        PlayerState.Death,
        PlayerState.Hurt,
        PlayerState.Crouch,   // 앉기는 Hurt 아래, 공중 위
        PlayerState.Jump,
        PlayerState.Fall,
        PlayerState.Run,
        PlayerState.Idle,
    };

    /// <summary>오버레이 상태들 (사격 + 재장전(UseAmmo=true일 때) + 대시(enableDash=true일 때))</summary>
    public static readonly PlayerState[] OverlayStates = new[]
    {
        PlayerState.Shooting,
        PlayerState.Reloading,
        PlayerState.Dash,
    };

    /// <summary>상태별 허용 규칙 (Inspector에서 데이터로 관리 권장)</summary>
    public static readonly StatePermissions[] Permissions = new[]
    {
        // Idle
        new StatePermissions(PlayerState.Idle,
            canMove: true, canJump: true, canShoot: true, canDash: false, canBeHit: true),
        // Run
        new StatePermissions(PlayerState.Run,
            canMove: true, canJump: true, canShoot: true, canDash: false, canBeHit: true),
        // Jump
        new StatePermissions(PlayerState.Jump,
            canMove: true, canJump: false, canShoot: true, canDash: false, canBeHit: true),
        // Fall
        new StatePermissions(PlayerState.Fall,
            canMove: true, canJump: false, canShoot: true, canDash: false, canBeHit: true),
        // Crouch (앉기: 이동 불가, 사격만 정면 가능)
        new StatePermissions(PlayerState.Crouch,
            canMove: false, canJump: true, canShoot: true, canDash: false, canBeHit: true),
        // Hurt
        new StatePermissions(PlayerState.Hurt,
            canMove: false, canJump: false, canShoot: false, canDash: false, canBeHit: false), // 무적 시간 중
        // Death
        new StatePermissions(PlayerState.Death,
            canMove: false, canJump: false, canShoot: false, canDash: false, canBeHit: false),
    };

    /// <summary>오버레이 허용 규칙 (기본값, Inspector에서 오버라이드 가능)</summary>
    public static readonly OverlayPermissions[] OverlayPermissions = new[]
    {
        new OverlayPermissions(PlayerState.Shooting,
            allowedOn: new[] { PlayerState.Idle, PlayerState.Run, PlayerState.Jump, PlayerState.Fall, PlayerState.Crouch },
            blockedBy: new[] { PlayerState.Hurt, PlayerState.Death }),
        new OverlayPermissions(PlayerState.Reloading,
            allowedOn: new[] { PlayerState.Idle, PlayerState.Run, PlayerState.Jump, PlayerState.Fall, PlayerState.Crouch },
            blockedBy: new[] { PlayerState.Hurt, PlayerState.Death, PlayerState.Shooting }),
        new OverlayPermissions(PlayerState.Dash,
            allowedOn: new[] { PlayerState.Idle, PlayerState.Run },
            blockedBy: new[] { PlayerState.Hurt, PlayerState.Death, PlayerState.Shooting, PlayerState.Reloading, PlayerState.Crouch }),
    };

    /// <summary>현재 이동 상태가 오버레이를 허용하는지 확인</summary>
    public static bool CanActivateOverlay(PlayerState currentMoveState, PlayerState overlay)
    {
        var perm = Array.Find(OverlayPermissions, p => p.Overlay == overlay);
        if (perm.Equals(default(OverlayPermissions))) return false;
        return Array.Exists(perm.AllowedOn, s => s == currentMoveState) &&
               !Array.Exists(perm.BlockedBy, s => s == currentMoveState);
    }

    /// <summary>이동 상태 간 전환 가능 여부 (우우선순위 기반)</summary>
    public static bool CanTransitionTo(PlayerState from, PlayerState to)
    {
        if (from == to) return false;
        if (from == PlayerState.Death) return false; // Death에서 자동 전환 없음
        if (IsOverlay(from) || IsOverlay(to)) return true; // 오버레이는 별도 관리

        int fromIdx = Array.IndexOf(PriorityOrder, from);
        int toIdx = Array.IndexOf(PriorityOrder, to);
        return toIdx <= fromIdx; // 같거나 높은 우선순위로만 전환 허용
    }

    public static bool IsOverlay(PlayerState state) => Array.Exists(OverlayStates, s => s == state);

    public static bool IsMoveState(PlayerState state) => !IsOverlay(state);

    /// <summary>실제 조건(지상/공중, 입력, 속도)으로 이동 상태 결정</summary>
    public static PlayerState DetermineMoveState(bool isGrounded, Vector2 velocity, Vector2 inputDir, bool isDashing, bool isCrouching = false)
    {
        if (isDashing) return PlayerState.Dash;
        if (!isGrounded) return velocity.y > 0.01f ? PlayerState.Jump : PlayerState.Fall;
        if (isCrouching) return PlayerState.Crouch;
        return inputDir.sqrMagnitude > 0.01f ? PlayerState.Run : PlayerState.Idle;
    }
}

[Serializable]
public struct StatePermissions
{
    public PlayerState State;
    public bool CanMove;
    public bool CanJump;
    public bool CanShoot;
    public bool CanDash;
    public bool CanBeHit;

    public StatePermissions(PlayerState state, bool canMove, bool canJump, bool canShoot, bool canDash, bool canBeHit)
    {
        State = state; CanMove = canMove; CanJump = canJump; CanShoot = canShoot; CanDash = canDash; CanBeHit = canBeHit;
    }

    public static StatePermissions Default(PlayerState state) => Array.Find(PlayerStateRules.Permissions, p => p.State == state);
}

[Serializable]
public struct OverlayPermissions
{
    public PlayerState Overlay;
    public PlayerState[] AllowedOn;
    public PlayerState[] BlockedBy;

    public OverlayPermissions(PlayerState overlay, PlayerState[] allowedOn, PlayerState[] blockedBy)
    {
        Overlay = overlay; AllowedOn = allowedOn; BlockedBy = blockedBy;
    }
}

/// <summary>
/// 상태 변경 이벤트 (애니메이션/이펙트/사운드가 구독)
/// 계획서 [4]: 상태 시스템이 애니메이션을 직접 호출하지 않음
/// </summary>
public static class PlayerStateEvents
{
    public static event Action<PlayerState, PlayerState> OnMoveStateChanged; // from, to
    public static event Action<PlayerState, bool> OnOverlayChanged;          // overlay, active

    public static void RaiseMoveStateChanged(PlayerState from, PlayerState to) => OnMoveStateChanged?.Invoke(from, to);
    public static void RaiseOverlayChanged(PlayerState overlay, bool active) => OnOverlayChanged?.Invoke(overlay, active);
}