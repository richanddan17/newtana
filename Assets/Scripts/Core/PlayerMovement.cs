using UnityEngine;

/// <summary>
/// 플레이어 이동 시스템.
/// 계획서 player-movement-system.md [1]~[9] 반영.
/// PlayerStateController의 상태/허용 규칙을 참조하여 이동 허용 여부 결정.
/// GroundCheck 컴포넌트로 지상 판정 분리 (이동용 Ground와 카메라용 Room Boundary 분리).
/// 상태 시스템이 애니메이션을 직접 호출하지 않고, 이동 결과값만 읽도록 분리.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerStateController stateController;
    [SerializeField] GroundCheck groundCheck;
    [SerializeField] Rigidbody2D rb;

    [Header("Move Settings")]
    [SerializeField] float maxMoveSpeed = 6f;
    [SerializeField] float acceleration = 50f;
    [SerializeField] float deceleration = 60f;
    [SerializeField] float airAcceleration = 30f;
    [SerializeField] float airDeceleration = 15f;
    [SerializeField] float maxAirMoveSpeed = 5f;

    [Header("Shoot Move Modifier")]
    [SerializeField] bool shootMoveEnabled = true;
    [SerializeField, Range(0f, 1f)] float shootMoveSpeedMultiplier = 1f; // 1.0 = 감속 없음
    [SerializeField] bool aimLockWhileShooting = false; // 조준 고정 옵션

    [Header("Jump Settings")]
    [SerializeField] float jumpForce = 14f;
    [SerializeField] float maxJumpHeight = 4f;
    [SerializeField] bool enableJumpCut = true; // 키 일찍 떼면 낮은 점프
    [SerializeField] float jumpCutGravityScale = 2.5f;
    [SerializeField] float maxFallSpeed = 25f;
    [SerializeField] float fallGravityScale = 1.5f;

    [Header("Dash/Knockback (외부 속도 연동)")]
    [SerializeField] bool overrideExternalVelocity = true;

    [Header("Slope Handling")]
    [SerializeField] bool handleSlopes = true;
    [SerializeField] float maxSlopeAngle = 45f;

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    // 내부 상태
    float coyoteTimer;
    bool wasGrounded;

    // 프로퍼티 (애니메이션/다른 시스템이 읽기만 함)
    public bool IsGrounded => groundCheck != null && groundCheck.IsGrounded;
    public Vector2 Velocity => rb ? rb.linearVelocity : Vector2.zero;
    public float HorizontalSpeed => Mathf.Abs(Velocity.x);
    public float VerticalSpeed => Velocity.y;
    public Vector2 MoveInput => stateController?.InputDirection ?? Vector2.zero;
    public bool IsMoving => HorizontalSpeed > 0.1f;
    public int FacingDirection => transform.localScale.x > 0 ? 1 : -1;
    public Vector2 GroundNormal => groundCheck != null ? groundCheck.GroundNormal : Vector2.up;
    public float SlopeAngle => groundCheck != null ? groundCheck.GetSlopeAngle() : 0f;

    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (stateController == null) stateController = GetComponent<PlayerStateController>();
        if (groundCheck == null) groundCheck = GetComponent<GroundCheck>();

        // Rigidbody2D 설정 확인
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.freezeRotation = true;
        rb.gravityScale = 1f;

        // GroundCheck 이벤트 구독
        if (groundCheck != null)
        {
            groundCheck.OnGroundedChanged += OnGroundedChanged;
        }

        // StateController 이벤트 구독
        if (stateController != null)
        {
            PlayerStateEvents.OnMoveStateChanged += OnMoveStateChanged;
        }
    }

    void OnDestroy()
    {
        if (groundCheck != null) groundCheck.OnGroundedChanged -= OnGroundedChanged;
        PlayerStateEvents.OnMoveStateChanged -= OnMoveStateChanged;
    }

    void OnGroundedChanged(bool grounded)
    {
        if (grounded)
        {
            coyoteTimer = stateController != null ? stateController.CoyoteTime : 0.1f;
        }
    }

    void Update()
    {
        // Coyote timer (FixedUpdate에서 감소하지만 Update에서도 동기화)
        if (!IsGrounded)
        {
            coyoteTimer -= Time.deltaTime;
        }

        // 방향 전환 (StateController가 관리하지만 여기에서도 동기화)
        UpdateFacingDirection();
    }

    void FixedUpdate()
    {
        if (stateController == null) return;

        // 상태별 이동 허용 확인
        bool canMove = stateController.CanMove;
        bool isDashing = stateController.IsDashing;

        // 외부 속도(대시/넉백) 처리
        if (isDashing || (overrideExternalVelocity && HasExternalVelocity()))
        {
            ApplyGravityOnly();
            return;
        }

        if (!canMove)
        {
            Decelerate();
            ApplyGravity();
            return;
        }

        // 이동 처리
        HandleMovement();

        // 점프 처리 (StateController의 버퍼와 연동)
        HandleJump();

        // 중력 적용
        ApplyGravity();
    }

    #region Movement

    void HandleMovement()
    {
        Vector2 inputDir = MoveInput;
        float targetSpeed = 0f;
        float currentAccel = 0f;
        float currentDecel = 0f;

        // 사격 중 이동 속도 배율
        float speedMultiplier = 1f;
        if (stateController.CurrentOverlayState == PlayerState.Shooting)
        {
            if (!shootMoveEnabled)
            {
                inputDir = Vector2.zero; // 조준 고정 시 이동 입력 무시
            }
            else
            {
                speedMultiplier = shootMoveSpeedMultiplier;
            }
        }

        // 경사면 처리: 목표 속도를 경사면 방향으로 투영
        if (handleSlopes && IsGrounded && SlopeAngle > 0.1f && SlopeAngle <= maxSlopeAngle)
        {
            Vector2 slopeDir = groundCheck.GetSlopeDirection();
            // 입력 방향을 경사면 방향으로 투영
            float inputAlongSlope = Vector2.Dot(inputDir, slopeDir);
            inputDir = slopeDir * inputAlongSlope;
        }

        // 목표 속도 계산
        if (inputDir.x != 0f)
        {
            targetSpeed = inputDir.x * maxMoveSpeed * speedMultiplier;
            currentAccel = IsGrounded ? acceleration : airAcceleration;
        }
        else
        {
            currentDecel = IsGrounded ? deceleration : airDeceleration;
        }

        // 현재 속도
        float currentSpeed = rb.linearVelocity.x;
        
        // 가속/감속
        float speedDiff = targetSpeed - currentSpeed;
        float accelRate = (targetSpeed == 0f) ? currentDecel : currentAccel;
        float movement = speedDiff * accelRate * Time.fixedDeltaTime;

        // 공중 최대 속도 제한
        if (!IsGrounded && Mathf.Abs(currentSpeed + movement) > maxAirMoveSpeed * speedMultiplier)
        {
            movement = Mathf.Clamp(currentSpeed + movement, -maxAirMoveSpeed * speedMultiplier, maxAirMoveSpeed * speedMultiplier) - currentSpeed;
        }

        // 속도 적용
        rb.linearVelocity = new Vector2(currentSpeed + movement, rb.linearVelocity.y);
    }

    void Decelerate()
    {
        float currentSpeed = rb.linearVelocity.x;
        float decel = IsGrounded ? deceleration : airDeceleration;
        float newSpeed = Mathf.MoveTowards(currentSpeed, 0f, decel * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newSpeed, rb.linearVelocity.y);
    }

    #endregion

    #region Jump

    void HandleJump()
    {
        // StateController의 WasJumpPressedThisFrame + 자체 Coyote 확인
        bool jumpInput = stateController.WasJumpPressedThisFrame;
        bool canJump = IsGrounded || coyoteTimer > 0f;

        if (jumpInput && canJump && stateController.CanJump)
        {
            ExecuteJump();
        }

        // 점프 컷 (키 일찍 떼면 상승 중지)
        if (enableJumpCut && stateController.WasJumpReleasedThisFrame && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * 0.5f);
        }
    }

    void ExecuteJump()
    {
        coyoteTimer = 0f;
        
        // StateController의 점프 버퍼도 소비 (이벤트 또는 직접 호출 필요)
        // 현재는 StateController 내부에서 관리하므로 여기선 로컬만 리셋
        
        // 점프 힘 계산 (최대 높이 기반 보정)
        float gravity = Physics2D.gravity.y * rb.gravityScale;
        float requiredVelocity = Mathf.Sqrt(-2f * gravity * maxJumpHeight);
        float jumpVel = Mathf.Max(jumpForce, requiredVelocity);

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVel);
    }

    #endregion

    #region Gravity

    void ApplyGravity()
    {
        float gravityScale = 1f;

        if (rb.linearVelocity.y < 0f)
        {
            // 낙하 중: 더 빠른 낙하
            gravityScale = fallGravityScale;
        }
        else if (rb.linearVelocity.y > 0f && !stateController.IsJumpHeld)
        {
            // 상승 중인데 점프 키 떼짐: 점프 컷 중력
            gravityScale = jumpCutGravityScale;
        }

        rb.gravityScale = gravityScale;

        // 최대 낙하 속도 제한
        if (rb.linearVelocity.y < -maxFallSpeed)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -maxFallSpeed);
        }
    }

    void ApplyGravityOnly()
    {
        ApplyGravity();
    }

    bool HasExternalVelocity()
    {
        return stateController.IsDashing;
    }

    #endregion

    #region Facing Direction

    void UpdateFacingDirection()
    {
        // 이동 방향과 조준 방향 분리 (계획서 [8])
        // 조준 입력이 있으면 조준 방향 우선, 없으면 이동 방향
        Vector2 aimInput = stateController != null ? GetAimDirection() : Vector2.zero;
        Vector2 moveInput = MoveInput;

        float targetScaleX = 0f;
        if (aimInput.x != 0f)
        {
            targetScaleX = aimInput.x > 0f ? 1f : -1f;
        }
        else if (moveInput.x != 0f)
        {
            targetScaleX = moveInput.x > 0f ? 1f : -1f;
        }

        if (targetScaleX != 0f && Mathf.Abs(transform.localScale.x - targetScaleX) > 0.01f)
        {
            transform.localScale = new Vector3(targetScaleX, transform.localScale.y, transform.localScale.z);
        }
    }

    Vector2 GetAimDirection()
    {
        // PlayerInputHandler의 AimInput 가져오기
        var inputHandler = GetComponent<PlayerInputHandler>();
        if (inputHandler != null)
        {
            // 리플렉션으로 AimInput 접근하거나 이벤트 구독 필요
            // 임시: 오른쪽 스틱/마우스 델타 기반
            return Vector2.right;
        }
        return Vector2.right;
    }

    #endregion

    #region External Velocity (Dash/Knockback)

    /// <summary>외부에서 넉백 등 속도 직접 설정 (StateController 경유 권장)</summary>
    public void SetVelocity(Vector2 velocity, bool overrideX = true, bool overrideY = false)
    {
        Vector2 current = rb.linearVelocity;
        if (overrideX) current.x = velocity.x;
        if (overrideY) current.y = velocity.y;
        rb.linearVelocity = current;
    }

    /// <summary>순간적인 힘 가함 (넉백 등)</summary>
    public void AddForce(Vector2 force, ForceMode2D mode = ForceMode2D.Impulse)
    {
        rb.AddForce(force, mode);
    }

    #endregion

    #region State Events

    void OnMoveStateChanged(PlayerState from, PlayerState to)
    {
        switch (to)
        {
            case PlayerState.Jump:
                // 점프 상태 진입 시 처리 완료됨 (ExecuteJump에서)
                break;
            case PlayerState.Dash:
                // 대시 속도는 StateController에서 externalVelocity로 설정
                break;
            case PlayerState.Hurt:
                // 피격 넉백은 StateController.ApplyKnockback으로 처리
                break;
        }
    }

    #endregion

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        
        // GroundCheck가 기즈모 그리므로 여기선 추가 정보만
        if (IsGrounded && groundCheck != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(groundCheck.GroundPoint, groundCheck.GroundNormal * 0.5f);
        }
    }
}