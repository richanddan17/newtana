using UnityEngine;
using System;

/// <summary>
/// 플레이어 상태 머신 컨트롤러.
/// 계획서 player-state-input-system.md [1]~[4], [7], [8] 반영.
/// PlayerState.cs의 정적 규칙(PriorityOrder, Permissions, Events)을 단일 출처로 사용.
/// </summary>
[DisallowMultipleComponent]
public class PlayerStateController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerInputHandler inputHandler;
    [SerializeField] Rigidbody2D rb;
    [SerializeField] Health health;

    [Header("Ground Check")]
    [SerializeField] Transform groundCheckPoint;
    [SerializeField] float groundCheckRadius = 0.2f;
    [SerializeField] LayerMask groundLayerMask;

    [Header("Coyote & Jump Buffer (시간)")]
    [SerializeField] float coyoteTime = 0.1f;
    [SerializeField] float jumpBufferTime = 0.15f;

    [Header("Crouch")]
    [SerializeField] KeyCode crouchKey = KeyCode.LeftControl; // 또는 S/Down
    [SerializeField] bool holdToCrouch = true; // 누르고 있는 동안만 앉기

    [Header("Shoot Buffer")]
    [SerializeField] float shootBufferDuration = 0.15f;
    [SerializeField] bool shootBufferEnabled = true;
    [SerializeField] bool dashBufferEnabled = false;

    [Header("Dash Settings (비활성 - 에셋에 클립 없음)")]
    [SerializeField] bool enableDash = false; // 기본 비활성
    [SerializeField] float dashDistance = 5.5f;
    [SerializeField] float dashDuration = 0.2f;
    [SerializeField] float dashCooldown = 1f;
    [SerializeField] bool dashInvincibility = true;
    [SerializeField] float dashInvincibilityDuration = 0.15f;

    const string DashSourceId = "Dash";

    [Header("State Debug")]
    [SerializeField] bool logStateChanges = false;

    [Header("Movement Lock")]
    [SerializeField] bool movementLocked = false;

    // 현재 상태
    PlayerState currentMoveState = PlayerState.Idle;
    PlayerState currentOverlayState = PlayerState.Idle; // Idle = 오버레이 없음
    bool hasOverlay => currentOverlayState != PlayerState.Idle;

    // 타이머
    float coyoteTimer;
    float dashCooldownTimer;
    float dashTimer;
    bool isDashing;

    // 외부 속도 (대시, 넉백)
    Vector2 externalVelocity;
    float externalVelocityTimer;

    // 입력 버퍼 참조
    PlayerInputBuffer inputBuffer;

    // 이벤트 구독 해제용
    Action<PlayerState, PlayerState> moveStateChangedHandler;
    Action<PlayerState, bool> overlayChangedHandler;

    // 프로퍼티
    public PlayerState CurrentMoveState => currentMoveState;
    public PlayerState CurrentOverlayState => currentOverlayState;
    public bool IsGrounded { get; private set; }
    public Vector2 Velocity => rb ? rb.linearVelocity : Vector2.zero;
    public Vector2 InputDirection => inputBuffer?.MoveInput ?? Vector2.zero;
    public bool IsDashing => isDashing;
    public bool MovementLocked
    {
        get => movementLocked;
        set => movementLocked = value;
    }
    public bool CanMove => !movementLocked && GetPermissions(currentMoveState).CanMove;
    public bool CanJump => !movementLocked && GetPermissions(currentMoveState).CanJump;
    public bool CanShoot => GetPermissions(currentMoveState).CanShoot && CanActivateCurrentOverlay();
    public bool CanDash => !movementLocked && enableDash && GetPermissions(currentMoveState).CanDash && dashCooldownTimer <= 0f;
    public bool CanBeHit => GetPermissions(currentMoveState).CanBeHit;
    public bool IsCrouching => currentMoveState == PlayerState.Crouch;
    public float CoyoteTime => coyoteTime;
    public bool WasJumpPressedThisFrame => inputBuffer?.WasJumpPressedThisFrame ?? false;
    public bool WasJumpReleasedThisFrame => inputBuffer?.WasJumpReleasedThisFrame ?? false;
    public bool IsJumpHeld => inputBuffer?.IsJumpHeld ?? false;

    // === 공개 API (조준/버퍼/코요테) ===
    /// <summary>스냅된 조준 방향 (정면/위45/위90/아래45/아래90)</summary>
    public Vector2 AimInput { get; private set; }
    // 조준 방향 인덱스 (애니메이터 AimDir 파라미터와 동일한 값, 계획서 [6] 표 기준)
    public const int AimDirFront = 0;   // 정면 (±22.5°)
    public const int AimDirUp45 = 1;    // 위 45°
    public const int AimDirUp90 = 2;    // 위 90°
    public const int AimDirDown45 = 3;  // 아래 45° (공중만)
    public const int AimDirDown90 = 4;  // 아래 90° (공중만)

    // 스냅 벡터 → 인덱스 판별 임계값 (정면 y=0, 대각 y=±0.707, 수직 y=±1)
    const float AimSideThreshold = 0.5f;
    const float AimVerticalThreshold = 0.95f;

    /// <summary>조준 방향 인덱스 (0~4: 정면/위45/위90/아래45/아래90)</summary>
    public int AimDirectionIndex { get; private set; }
    public bool HasJumpBuffer => inputBuffer?.HasJumpBuffer ?? false;
    public bool ConsumeJumpBuffer() => inputBuffer != null && inputBuffer.ConsumeJumpBuffer();
    public bool HasShootBuffer => inputBuffer?.HasShootBuffer ?? false;
    public bool ConsumeShootBuffer() => inputBuffer != null && inputBuffer.ConsumeShootBuffer();
    public bool IsCrouchHeld => inputBuffer?.IsCrouchHeld ?? false;
    public bool CanUseCoyoteTime => IsGrounded || coyoteTimer > 0f;
    public void NotifyJumpExecuted() => coyoteTimer = 0f;

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();

        inputBuffer = new PlayerInputBuffer(coyoteTime, jumpBufferTime);
        inputBuffer.shootBufferDuration = shootBufferDuration;
        inputBuffer.shootBufferEnabled = shootBufferEnabled;
        inputBuffer.dashBufferEnabled = dashBufferEnabled;

        // Input System 액션 연결
        if (inputHandler != null)
        {
            inputHandler.OnMoveInput += inputBuffer.SetMoveInput;
            inputHandler.OnJumpPressed += inputBuffer.SetJumpPressed;
            inputHandler.OnJumpReleased += inputBuffer.SetJumpReleased;
            inputHandler.OnDashPressed += inputBuffer.SetDashPressed;
            inputHandler.OnShootPressed += inputBuffer.SetShootPressed;
            inputHandler.OnShootReleased += inputBuffer.SetShootReleased;
            // OnReloadPressed: 비활성 (에셋에 재장전 클립 없음)
            inputHandler.OnAimInput += inputBuffer.SetAimInput;
            inputHandler.OnCrouchPressed += inputBuffer.SetCrouchHeld;
        }

        // 이벤트 핸들러 캐시
        moveStateChangedHandler = OnMoveStateChanged;
        overlayChangedHandler = OnOverlayChanged;
        PlayerStateEvents.OnMoveStateChanged += moveStateChangedHandler;
        PlayerStateEvents.OnOverlayChanged += overlayChangedHandler;
    }

    void OnDestroy()
    {
        PlayerStateEvents.OnMoveStateChanged -= moveStateChangedHandler;
        PlayerStateEvents.OnOverlayChanged -= overlayChangedHandler;
    }

    void Update()
    {
        // 타이머 업데이트
        UpdateTimers();

        // 입력 버퍼 업데이트
        inputBuffer.Update(Time.deltaTime);

        // 지상 판정
        CheckGrounded();

        // 앉기 입력 처리 (Old Input System 폴백)
        HandleCrouchInput();

        // 상태 머신 틱
        TickStateMachine();

        // 외부 속도 적용
        ApplyExternalVelocity();

        // 조준 방향 5구간 스냅 (정면 ±22.5° / 위45 / 위90 / 아래45 / 아래90)
        if (inputHandler != null)
        {
            AimInput = PlayerInputHandler.SnapTo5Directions(inputHandler.AimInput, IsGrounded);

            // 스냅된 벡터 → 인덱스 (임계값으로 45°/90° 구분)
            if (AimInput.y > AimSideThreshold)
                AimDirectionIndex = AimInput.y >= AimVerticalThreshold ? AimDirUp90 : AimDirUp45;
            else if (AimInput.y < -AimSideThreshold)
                AimDirectionIndex = AimInput.y <= -AimVerticalThreshold ? AimDirDown90 : AimDirDown45;
            else
                AimDirectionIndex = AimDirFront;
        }

        // 1회성 프레임 플래그 정리 (Input System 콜백은 Update 전에 발화하므로 소비 후 정리)
        inputBuffer.EndFrame();
    }

    void HandleCrouchInput()
    {
        if (inputBuffer != null && inputBuffer.IsBlocked) return; // 입력 차단(대화/컷신) 중 Old Input 폴백 우회 차단
        if (!IsGrounded) return; // 공중에서는 앉기 불가
        if (currentMoveState == PlayerState.Hurt || currentMoveState == PlayerState.Death) return;

        bool crouchHeld = inputBuffer != null && inputBuffer.IsCrouchHeld;

        // Old Input System 폴백 (Crouch 액션이 없는 경우)
        // 레거시 Input Manager 비활성화 시 Input 클래스가 런타임 예외를 던지므로 가드
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!crouchHeld)
        {
            if (holdToCrouch)
                crouchHeld = Input.GetKey(crouchKey);
            else
                crouchHeld = Input.GetKeyDown(crouchKey);
        }
#endif

        bool wantsCrouch = crouchHeld && InputDirection.sqrMagnitude < 0.01f; // 이동 중이면 앉기 안 함

        if (wantsCrouch && currentMoveState != PlayerState.Crouch)
        {
            if (PlayerStateRules.CanTransitionTo(currentMoveState, PlayerState.Crouch))
                SetMoveState(PlayerState.Crouch);
        }
        else if (!wantsCrouch && currentMoveState == PlayerState.Crouch)
        {
            // 앉기 해제 → 실제 조건으로 상태 결정
            SetMoveState(DetermineDesiredMoveState());
        }
    }

    void UpdateTimers()
    {
        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.deltaTime;
        if (dashTimer > 0f) dashTimer -= Time.deltaTime;
        if (externalVelocityTimer > 0f) externalVelocityTimer -= Time.deltaTime;
    }

    void CheckGrounded()
    {
        bool wasGrounded = IsGrounded;
        IsGrounded = groundCheckPoint != null && 
            Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayerMask);

        // Coyote timer: 착지 시 리셋, 공중 진입시 시작
        if (IsGrounded && !wasGrounded)
        {
            coyoteTimer = coyoteTime;
        }
        else if (!IsGrounded)
        {
            coyoteTimer -= Time.deltaTime;
        }
    }

    void TickStateMachine()
    {
        // 1. 이동 상태 결정 (우선순위 기반)
        PlayerState desiredMoveState = DetermineDesiredMoveState();
        
        // 2. 현재 상태에서 원하는 상태로 전환 가능한지 확인
        if (PlayerStateRules.CanTransitionTo(currentMoveState, desiredMoveState))
        {
            SetMoveState(desiredMoveState);
        }
        // Death/Hurt는 자동 전환 안 함 (외부에서만 변경)
        else if (currentMoveState == PlayerState.Death || currentMoveState == PlayerState.Hurt)
        {
            // 유지
        }

        // 3. 오버레이 상태 처리 (사격/재장전)
        UpdateOverlayState();

        // 4. 대시 처리
        UpdateDash();
    }

    PlayerState DetermineDesiredMoveState()
    {
        // 외부 속도(대시/넉백) 중이면 이동 상태 강제 고정
        if (isDashing) return PlayerState.Dash;
        if (externalVelocityTimer > 0f && externalVelocity.sqrMagnitude > 0.01f) return PlayerState.Dash; // 넉백도 Dash 상태로 처리

        // Hurt/Death는 입력 무시
        if (currentMoveState == PlayerState.Hurt || currentMoveState == PlayerState.Death)
            return currentMoveState;

        // Crouch 포함 모든 상태에서 실제 조건으로 직접 판단 (Crouch 고정 반환 버그 수정)
        return PlayerStateRules.DetermineMoveState(IsGrounded, Velocity, InputDirection, isDashing, false);
    }

    void SetMoveState(PlayerState newState)
    {
        if (newState == currentMoveState) return;

        PlayerState oldState = currentMoveState;
        currentMoveState = newState;

        if (logStateChanges)
            Debug.Log($"[State] Move: {oldState} -> {newState}");

        PlayerStateEvents.RaiseMoveStateChanged(oldState, newState);

        // 상태 진입 시 처리
        OnMoveStateEnter(newState, oldState);
    }

    void OnMoveStateEnter(PlayerState newState, PlayerState oldState)
    {
        switch (newState)
        {
            case PlayerState.Dash:
                StartDash();
                break;
            case PlayerState.Crouch:
                // 앉기 진입: 이동 정지, 사격 오버레이는 유지 가능 (정면만)
                break;
            case PlayerState.Hurt:
                // Hurt 진입 시 사격 오버레이 강제 해제
                if (hasOverlay && currentOverlayState == PlayerState.Shooting)
                    SetOverlayState(PlayerState.Idle, false);
                break;
            case PlayerState.Death:
                // Death 진입 시 모든 오버레이 해제
                if (hasOverlay) SetOverlayState(PlayerState.Idle, false);
                break;
        }

        // 상태 이탈 시 처리
        OnMoveStateExit(oldState, newState);
    }

    void OnMoveStateExit(PlayerState oldState, PlayerState newState)
    {
        if (oldState == PlayerState.Dash)
        {
            EndDash();
        }
    }

    void UpdateOverlayState()
    {
        // Hurt/Death 중에는 오버레이 불가
        if (currentMoveState == PlayerState.Hurt || currentMoveState == PlayerState.Death)
        {
            if (hasOverlay) SetOverlayState(PlayerState.Idle, false);
            return;
        }

        // 앉기 중에는 위 조준 불가 (정면만) - 애니메이션에서 처리
        // 사격 입력 체크
        bool shootHeld = inputBuffer.IsShootHeld;
        bool reloadPressed = inputBuffer.ConsumeReloadPressed();

        // 재장전 우선 (사격 중 재장전 입력 시 사격 해제) - UseAmmo=true일 때만 동작
        if (reloadPressed && PlayerStateRules.CanActivateOverlay(currentMoveState, PlayerState.Reloading))
        {
            SetOverlayState(PlayerState.Reloading, true);
            return;
        }

        // 사격
        if (shootHeld && PlayerStateRules.CanActivateOverlay(currentMoveState, PlayerState.Shooting))
        {
            if (!hasOverlay || currentOverlayState != PlayerState.Shooting)
                SetOverlayState(PlayerState.Shooting, true);
        }
        else if (hasOverlay && currentOverlayState == PlayerState.Shooting)
        {
            SetOverlayState(PlayerState.Idle, false);
        }
    }

    void SetOverlayState(PlayerState overlay, bool active)
    {
        if (active)
        {
            if (overlay == PlayerState.Idle) return;
            if (hasOverlay && currentOverlayState == overlay) return;

            PlayerState oldOverlay = currentOverlayState;
            currentOverlayState = overlay;

            if (logStateChanges)
                Debug.Log($"[State] Overlay: {oldOverlay} -> {overlay} (active)");

            PlayerStateEvents.RaiseOverlayChanged(overlay, true);
        }
        else
        {
            if (!hasOverlay) return;

            PlayerState oldOverlay = currentOverlayState;
            currentOverlayState = PlayerState.Idle;

            if (logStateChanges)
                Debug.Log($"[State] Overlay: {oldOverlay} -> None (inactive)");

            PlayerStateEvents.RaiseOverlayChanged(oldOverlay, false);
        }
    }

    bool CanActivateCurrentOverlay()
    {
        if (!hasOverlay) return true;
        return PlayerStateRules.CanActivateOverlay(currentMoveState, currentOverlayState);
    }

    void UpdateDash()
    {
        if (!enableDash) return;

        if (dashTimer > 0f)
        {
            isDashing = true;
            return;
        }

        if (isDashing) EndDash();

        if (inputBuffer.ConsumeDashPressed() && CanDash)
        {
            TryDash();
        }
    }

    public bool TryDash()
    {
        if (!enableDash || !CanDash) return false;

        float facing = Mathf.Sign(transform.localScale.x);
        if (facing == 0f) facing = 1f;

        Vector2 dashDir = InputDirection.sqrMagnitude > 0.01f
            ? InputDirection.normalized
            : new Vector2(facing, 0f);

        RequestDash(dashDir);
        return true;
    }

    void StartDash()
    {
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
        isDashing = true;

        // 대시 방향 결정 (이미 RequestDash에서 설정됨)
        // 대시 무적: Health에 무적 요청, EndDash에서 해제
        if (dashInvincibility)
        {
            health?.AddInvincibility(DashSourceId, dashInvincibilityDuration, false);
        }
    }

    void EndDash()
    {
        dashTimer = 0f;
        isDashing = false;
        externalVelocity = Vector2.zero;
        externalVelocityTimer = 0f;
        health?.RemoveInvincibility(DashSourceId);
    }

    void RequestDash(Vector2 direction)
    {
        externalVelocity = direction * (dashDistance / dashDuration);
        externalVelocityTimer = dashDuration;
        // 상태 머신이 다음 틱에서 Dash 상태로 전환
    }

    /// <summary>외부에서 넉백 등 속도 적용 요청</summary>
    public void ApplyKnockback(Vector2 velocity, float duration)
    {
        externalVelocity = velocity;
        externalVelocityTimer = duration;
        // 넉백 중에도 Dash 상태로 취급되어 이동 입력 차단됨
    }

    void ApplyExternalVelocity()
    {
        if (externalVelocityTimer > 0f && externalVelocity.sqrMagnitude > 0.001f && rb != null)
        {
            rb.linearVelocity = new Vector2(externalVelocity.x, rb.linearVelocity.y);
        }
    }

    // === 공개 API (다른 시스템에서 호출) ===

    /// <summary>피격 시 호출 - Hurt 상태 강제 진입</summary>
    public void EnterHurtState()
    {
        if (currentMoveState == PlayerState.Death) return;
        SetMoveState(PlayerState.Hurt);
    }

    /// <summary>회복/무적 끝 시 호출 - 실제 조건으로 상태 복구</summary>
    public void ExitHurtState()
    {
        if (currentMoveState != PlayerState.Hurt) return;
        // DetermineDesiredMoveState가 실제 조건(지상/공중, 입력)으로 판단
        SetMoveState(DetermineDesiredMoveState());
    }

    /// <summary>사망 시 호출</summary>
    public void EnterDeathState()
    {
        SetMoveState(PlayerState.Death);
    }

    /// <summary>부활 시 호출 - 전체 초기화</summary>
    public void ResetState()
    {
        currentMoveState = PlayerState.Idle;
        currentOverlayState = PlayerState.Idle;
        isDashing = false;
        dashTimer = 0f;
        dashCooldownTimer = 0f;
        externalVelocity = Vector2.zero;
        externalVelocityTimer = 0f;
        coyoteTimer = 0f;
        movementLocked = false;
        health?.RemoveInvincibility(DashSourceId); // 대시 강제 종료 시 무적 해제
        inputBuffer.ClearAll();

        if (logStateChanges)
            Debug.Log("[State] Reset to Idle");
    }

    /// <summary>입력 차단 (컷신, 일시정지, 부활 연출 등)</summary>
    public void SetInputBlocked(bool blocked)
    {
        inputBuffer.SetBlocked(blocked);
    }

    StatePermissions GetPermissions(PlayerState state) => StatePermissions.Default(state);

    // === 이벤트 핸들러 (애니메이션 등 구독용) ===
    void OnMoveStateChanged(PlayerState from, PlayerState to) { }
    void OnOverlayChanged(PlayerState overlay, bool active) { }

    void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);
        }
    }
}