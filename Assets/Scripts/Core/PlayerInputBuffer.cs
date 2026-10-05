using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 입력 버퍼 통합 관리.
/// 계획서 [6] Jump Buffer, Dash Buffer, Shoot Hold.
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

    bool reloadPressedThisFrame;

    // 설정
    readonly float coyoteTime;
    readonly float jumpBufferDuration;
    readonly float dashBufferDuration = 0.1f;

    // 차단 상태
    bool isBlocked;

    public PlayerInputBuffer(float coyoteTime, float jumpBufferDuration)
    {
        this.coyoteTime = coyoteTime;
        this.jumpBufferDuration = jumpBufferDuration;
    }

    public void Update(float deltaTime)
    {
        if (isBlocked)
        {
            ClearFrameInputs();
            return;
        }

        // 타이머 감소
        if (jumpBufferTimer > 0f) jumpBufferTimer -= deltaTime;
        if (dashBufferTimer > 0f) dashBufferTimer -= deltaTime;

        // 프레임 단위 플래그 리셋
        ClearFrameInputs();
    }

    void ClearFrameInputs()
    {
        jumpPressedThisFrame = false;
        jumpReleasedThisFrame = false;
        dashPressedThisFrame = false;
        shootPressedThisFrame = false;
        shootReleasedThisFrame = false;
        reloadPressedThisFrame = false;
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
        dashBufferTimer = dashBufferDuration;
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
    }

    public void SetShootReleased(InputAction.CallbackContext ctx)
    {
        shootHeld = false;
        shootReleasedThisFrame = true;
    }

    public bool IsShootHeld => shootHeld && !isBlocked;
    public bool WasShootPressedThisFrame => shootPressedThisFrame && !isBlocked;
    public bool WasShootReleasedThisFrame => shootReleasedThisFrame;

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
    public void SetBlocked(bool blocked) => isBlocked = blocked;
    public bool IsBlocked => isBlocked;

    public void ClearAll()
    {
        MoveInput = Vector2.zero;
        AimInput = Vector2.zero;
        jumpHeld = false;
        jumpBufferTimer = 0f;
        dashBufferTimer = 0f;
        shootHeld = false;
        ClearFrameInputs();
    }
}