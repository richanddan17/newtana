using UnityEngine;
using System;
using UnityEngine.InputSystem;

/// <summary>
/// 입력 버퍼 통합 관리.
/// 계획서 [6] Jump Buffer, Dash Buffer, Shoot Hold, Shoot Buffer.
/// 계획서 [7] 입력 차단 (Death, 컷신, 부활 연출).
/// </summary>
public class PlayerInputBuffer
{
    // 이동
    public Vector2 MoveInput { get; private set; }
    public Vector2 AimInput { get; private set; }

    // 버튼 상태
    bool jumpHeld;
    bool jumpPressedThisFrame;
    bool jumpReleasedThisFrame;
    float jumpBufferTimer;

    bool dashPressedThisFrame;
    float dashBufferTimer;

    bool shootHeld;
    bool shootPressedThisFrame;
    bool shootReleasedThisFrame;
    float shootBufferTimer;

    bool crouchHeld;

    bool reloadPressedThisFrame;

    // 설정
    readonly float coyoteTime;
    readonly float jumpBufferDuration;
    readonly float dashBufferDuration = 0.1f;

    // Shoot buffer 설정 (Inspector에서 조정 가능하도록 public)
    public float shootBufferDuration = 0.15f;
    public bool shootBufferEnabled = true;
    public bool dashBufferEnabled = false; // 대시 비활성 기본 (에셋 클립 없음)

    // 차단 상태
    bool isBlocked;

    public PlayerInputBuffer(float coyoteTime, float jumpBufferDuration)
    {
        this.coyoteTime = coyoteTime;
        this.jumpBufferDuration = jumpBufferDuration;
    }

    /// <summary>타이머 감소만 수행. 프레임 플래그 정리는 EndFrame()에서.</summary>
    public void Update(float deltaTime)
    {
        if (jumpBufferTimer > 0f) jumpBufferTimer -= deltaTime;
        if (dashBufferTimer > 0f) dashBufferTimer -= deltaTime;
        if (shootBufferTimer > 0f) shootBufferTimer -= deltaTime;
    }

    /// <summary>1회성 프레임 플래그 정리. Input System 콜백(Update 전 발화) 이후, 상태 머신 소비 후 호출.</summary>
    public void EndFrame()
    {
        jumpPressedThisFrame = false;
        jumpReleasedThisFrame = false;
        dashPressedThisFrame = false;
        shootPressedThisFrame = false;
        shootReleasedThisFrame = false;
        reloadPressedThisFrame = false;
    }

    /// <summary>타이머만 초기화. 홀드 상태(shootHeld/crouchHeld/jumpHeld)는 유지.</summary>
    public void ClearTimers()
    {
        jumpBufferTimer = 0f;
        dashBufferTimer = 0f;
        shootBufferTimer = 0f;
    }

    // === 이동 입력 ===
    public void SetMoveInput(Vector2 input) => MoveInput = isBlocked ? Vector2.zero : input;
    public void SetAimInput(Vector2 input) => AimInput = isBlocked ? Vector2.zero : input;

    // === 점프 ===
    public void SetJumpPressed(InputAction.CallbackContext ctx)
    {
        if (isBlocked) return;
        jumpHeld = true;
        jumpPressedThisFrame = true;
        jumpBufferTimer = jumpBufferDuration;
    }

    public void SetJumpReleased(InputAction.CallbackContext ctx)
    {
        jumpHeld = false;
        jumpReleasedThisFrame = true;
    }

    public bool HasJumpBuffer => jumpBufferTimer > 0f && !isBlocked;

    /// <summary>이번 프레임에 점프 버퍼가 유효한가 (Coyote Time과 함께 사용)</summary>
    public bool ConsumeJumpBuffer()
    {
        if (jumpBufferTimer > 0f)
        {
            jumpBufferTimer = 0f;
            return true;
        }
        return false;
    }

    public bool IsJumpHeld => jumpHeld && !isBlocked;
    public bool WasJumpPressedThisFrame => jumpPressedThisFrame && !isBlocked;
    public bool WasJumpReleasedThisFrame => jumpReleasedThisFrame;

    // === 대시 ===
    public void SetDashPressed(InputAction.CallbackContext ctx)
    {
        if (isBlocked) return;
        dashPressedThisFrame = true;
        if (dashBufferEnabled) dashBufferTimer = dashBufferDuration;
    }

    public bool ConsumeDashPressed()
    {
        if (dashBufferTimer > 0f)
        {
            dashBufferTimer = 0f;
            return true;
        }
        return false;
    }

    // === 사격 ===
    public void SetShootPressed(InputAction.CallbackContext ctx)
    {
        if (isBlocked) return;
        shootHeld = true;
        shootPressedThisFrame = true;
        if (shootBufferEnabled) shootBufferTimer = shootBufferDuration;
    }

    public void SetShootReleased(InputAction.CallbackContext ctx)
    {
        shootHeld = false;
        shootReleasedThisFrame = true;
    }

    public bool HasShootBuffer => shootBufferEnabled && shootBufferTimer > 0f && !isBlocked;

    public bool ConsumeShootBuffer()
    {
        if (shootBufferTimer > 0f)
        {
            shootBufferTimer = 0f;
            return true;
        }
        return false;
    }

    public bool IsShootHeld => shootHeld && !isBlocked;
    public bool WasShootPressedThisFrame => shootPressedThisFrame && !isBlocked;
    public bool WasShootReleasedThisFrame => shootReleasedThisFrame;

    // === 앉기 (hold) ===
    public bool IsCrouchHeld => crouchHeld && !isBlocked;

    public void SetCrouchHeld(bool held)
    {
        if (isBlocked && held) return;
        crouchHeld = held;
    }

    // === 재장전 ===
    public void SetReloadPressed(InputAction.CallbackContext ctx)
    {
        if (isBlocked) return;
        reloadPressedThisFrame = true;
    }

    public bool ConsumeReloadPressed()
    {
        if (reloadPressedThisFrame)
        {
            reloadPressedThisFrame = false;
            return true;
        }
        return false;
    }

    // === 차단/초기화 ===
    public void SetBlocked(bool blocked)
    {
        isBlocked = blocked;
        if (blocked)
        {
            // 홀드 상태(shootHeld/crouchHeld/jumpHeld)는 유지, 타이머만 초기화
            ClearTimers();
        }
    }

    public bool IsBlocked => isBlocked;

    public void ClearAll()
    {
        MoveInput = Vector2.zero;
        AimInput = Vector2.zero;
        jumpHeld = false;
        shootHeld = false;
        crouchHeld = false;
        ClearTimers();
        EndFrame();
    }
}
