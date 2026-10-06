using UnityEngine;

/// <summary>
/// 플레이어 이동 시스템.
/// 계획서 player-movement-system.md [1]~[9] 반영.
/// PlayerStateController의 상태/허용 규칙을 참조하여 이동 허용 여부 결정.
/// 지상 판정은 StateController 단일 출처 (폴백: GroundCheck 컴포넌트).
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
    [SerializeField] float turnDeceleration = 200f; // 반전 감속 (크면 급정거)
    [SerializeField] bool instantTurnStop = false;   // 방향 전환 시 즉시 정지 옵션

    [Header("Air Move Settings")]
    [SerializeField] float airAcceleration = 30f;
    [SerializeField] float airDeceleration = 15f;
    [SerializeField] float airTurnDeceleration = 40f; // 공중 반전 감속 (기본: 지상보다 약하게)
    [SerializeField] float airControlMultiplier = 1f; // 공중 가속/감속 배율
    [SerializeField] float maxAirMoveSpeed = 5f;

    [Header("Shoot Move Modifier")]
    [SerializeField] bool shootMoveEnabled = true;
    [SerializeField] float whileShootingMultiplier = 1f; // 1.0 = 감속 없음
    [SerializeField] float airWhileShootingMultiplier = 1f; // 공중 사격 중 별도 배율
    [SerializeField] bool lockAimWhileShooting = false; // 조준 고정 옵션 (기본 false)

    [Header("Jump Settings")]
    [SerializeField] float jumpForce = 14f;
    [SerializeField] float maxJumpHeight = 4f;
    [SerializeField] bool enableJumpCut = true; // 키 일찍 떼면 낮은 점프
    [SerializeField] float jumpCutGravityScale = 2.5f;
    [SerializeField] float maxFallSpeed = 25f;
    [SerializeField] float fallGravityScale = 1.5f;

    [Header("Apex Modifier (정점 보정)")]
    [SerializeField] bool enableApexModifier = true;
    [SerializeField] float apexVelocityThreshold = 2f; // |vy|가 이 값 미만이면 정점 구간
    [SerializeField] float apexGravityMultiplier = 0.5f; // 정점 구간 중력 약화
    [SerializeField] float apexAccelerationBonus = 20f;  // 정점 구간 공중 가속 보너스

    [Header("Corner Correction (코너 보정)")]
    [SerializeField] bool enableCornerCorrection = true;
    [SerializeField] float cornerCorrectionDistance = 0.3f; // 보정 거리 한도
    [SerializeField] float cornerCheckDistance = 0.3f;      // 머리 좌/우 체크 거리
    [SerializeField] LayerMask cornerCorrectionMask = 1 << 3;

    [Header("Ledge Forgiveness (레지 보정)")]
    [SerializeField] bool enableLedgeForgiveness = true;
    [SerializeField] float ledgeForgivenessHeight = 0.15f;     // 올려주는 최대 높이 (작게)
    [SerializeField] float ledgeForgivenessHorizontal = 0.1f;  // 가로 허용 범위 (작게)
    [SerializeField] LayerMask ledgeForgivenessMask = 1 << 3;

    [Header("Dash Corner Correction")]
    [SerializeField] bool enableDashCornerCorrection = true;
    [SerializeField] float dashCornerCorrectionDistance = 0.3f;

    [Header("Dash/Knockback (외부 속도 연동)")]
    [SerializeField] bool overrideExternalVelocity = true;

    [Header("Slope Handling")]
    [SerializeField] bool handleSlopes = true;
    [SerializeField] float maxSlopeAngle = 45f;

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    // 내부 상태
    bool cornerCorrectionUsed; // 점프당 1회만 보정

    // 프로퍼티 (애니메이션/다른 시스템이 읽기만 함)
    // 지상 판정 단일 출처: StateController.IsGrounded (폴백: GroundCheck 컴포넌트)
    public bool IsGrounded => stateController != null
        ? stateController.IsGrounded
        : (groundCheck != null && groundCheck.IsGrounded);
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

        // StateController 이벤트 구독
        if (stateController != null)
        {
            PlayerStateEvents.OnMoveStateChanged += OnMoveStateChanged;
        }
    }

    void OnDestroy()
    {
        PlayerStateEvents.OnMoveStateChanged -= OnMoveStateChanged;
    }

    void Update()
    {
        // 방향 전환 (StateController가 관리하지만 여기에서도 동기화)
        UpdateFacingDirection();
    }

    void FixedUpdate()
    {
        if (stateController == null) return;

        // 상태별 이동 허용 확인
        bool canMove = stateController.CanMove;
        bool isDashing = stateController.IsDashing;
        bool isCrouching = stateController.IsCrouching;

        // 지상이면 코너 보정 1회 제한 리셋
        if (IsGrounded) cornerCorrectionUsed = false;

        // 외부 속도(대시/넉백) 처리
        if (isDashing)
        {
            // 대시 중에는 대시 코너 보정만 허용
            TryDashCornerCorrection();
            ApplyGravityOnly();
            return;
        }

        if (overrideExternalVelocity && HasExternalVelocity())
        {
            // 외부 속도 중에는 기본 코너 보정만 허용
            TryCornerCorrection();
            ApplyGravityOnly();
            return;
        }

        if (!canMove || isCrouching)
        {
            Decelerate();
            ApplyGravity();
            return;
        }

        // 이동 처리
        HandleMovement();

        // 점프 처리 (StateController의 버퍼와 연동)
        HandleJump();

        // 관용 시스템 (이동 시스템 안에서 처리, 결과만 전달)
        TryCornerCorrection();
        TryLedgeForgiveness();

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
            if (lockAimWhileShooting || !shootMoveEnabled)
            {
                inputDir = Vector2.zero; // 조준 고정 시 이동 입력 무시
            }
            else
            {
                speedMultiplier = IsGrounded ? whileShootingMultiplier : airWhileShootingMultiplier;
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

        // 현재 속도
        float currentSpeed = rb.linearVelocity.x;

        // 목표 속도 계산
        if (inputDir.x != 0f)
        {
            targetSpeed = inputDir.x * maxMoveSpeed * speedMultiplier;
            if (IsGrounded)
            {
                currentAccel = acceleration;
            }
            else
            {
                currentAccel = airAcceleration * airControlMultiplier;
                // 정점 보정: 같은 구간에서 공중 가속 보너스
                if (IsInApexWindow()) currentAccel += apexAccelerationBonus;
            }
        }
        else
        {
            currentDecel = IsGrounded ? deceleration : airDeceleration * airControlMultiplier;
        }

        // 가속/감속/반전 감속 결정
        float accelRate;
        if (targetSpeed == 0f)
        {
            accelRate = currentDecel;
        }
        else if (currentSpeed != 0f && Mathf.Sign(targetSpeed) != Mathf.Sign(currentSpeed))
        {
            // 반전 감속 (Turn Deceleration)
            if (instantTurnStop)
            {
                // 즉시 정지 후 남은 프레임에서 반대 방향 가속이 이어지도록
                currentSpeed = 0f;
                accelRate = IsGrounded ? acceleration : airAcceleration * airControlMultiplier;
            }
            else
            {
                accelRate = IsGrounded ? turnDeceleration : airTurnDeceleration;
            }
        }
        else
        {
            accelRate = currentAccel;
        }

        float speedDiff = targetSpeed - currentSpeed;
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
        float decel = IsGrounded ? deceleration : airDeceleration * airControlMultiplier;
        float newSpeed = Mathf.MoveTowards(currentSpeed, 0f, decel * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newSpeed, rb.linearVelocity.y);
    }

    #endregion

    #region Jump

    void HandleJump()
    {
        // StateController의 WasJumpPressedThisFrame + 단일 출처 Coyote 확인
        bool jumpInput = stateController.WasJumpPressedThisFrame;
        bool canJump = IsGrounded || stateController.CanUseCoyoteTime;

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
        // StateController에 점프 실행 통지 (coyote/buffer 소비)
        stateController.NotifyJumpExecuted();

        // 점프 힘 계산 (최대 높이 기반 보정)
        float gravity = Physics2D.gravity.y * rb.gravityScale;
        float requiredVelocity = Mathf.Sqrt(-2f * gravity * maxJumpHeight);
        float jumpVel = Mathf.Max(jumpForce, requiredVelocity);

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVel);
    }

    #endregion

    #region Gravity

    bool IsInApexWindow()
    {
        return enableApexModifier && !IsGrounded && Mathf.Abs(rb.linearVelocity.y) < apexVelocityThreshold;
    }

    void ApplyGravity()
    {
        float gravityScale = 1f;

        // 정점 보정 우선순위 최상: 정점 구간에서는 하강 배율/점프 컷 미적용
        if (IsInApexWindow())
        {
            gravityScale = apexGravityMultiplier;
        }
        else if (rb.linearVelocity.y < 0f)
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

    #region Forgiveness (코너/레지/대시 보정)

    void TryCornerCorrection()
    {
        if (!enableCornerCorrection || cornerCorrectionUsed) return;
        // 상승 중(vy > 0)에만 작동, 충돌로 vy=0이 되기 전에 판정
        if (rb.linearVelocity.y <= 0f) return;

        Vector2 headPos = (Vector2)transform.position + Vector2.up * 0.5f;
        bool leftBlocked = Physics2D.Raycast(headPos, Vector2.left, cornerCheckDistance, cornerCorrectionMask);
        bool rightBlocked = Physics2D.Raycast(headPos, Vector2.right, cornerCheckDistance, cornerCorrectionMask);

        // 한쪽만 막혔는지 확인
        if (leftBlocked == rightBlocked) return;

        // 막히지 않은 쪽으로 보정
        float dir = leftBlocked ? 1f : -1f;

        // 보정 방향에 빈 공간이 있는지 먼저 확인 (벽 쪽으로 끌려가는 이동 방지)
        bool spaceFree = !Physics2D.Raycast(headPos, new Vector2(dir, 0.5f).normalized, cornerCorrectionDistance, cornerCorrectionMask);
        if (!spaceFree) return;

        // 수직 속도 유지, 수평으로만 1회 밀어줌
        rb.position += new Vector2(dir * cornerCorrectionDistance, 0f);
        cornerCorrectionUsed = true;
    }

    void TryLedgeForgiveness()
    {
        if (!enableLedgeForgiveness || IsGrounded) return;
        // 하강 중에만 작동
        if (rb.linearVelocity.y > 0f) return;

        Vector2 feetPos = (Vector2)transform.position + Vector2.down * 0.5f;

        // 발 아래로 짧게 체크: 아주 작은 범위에서만 작동 (벽 타기 방지)
        RaycastHit2D below = Physics2D.Raycast(feetPos, Vector2.down, ledgeForgivenessHeight, ledgeForgivenessMask);
        if (below.collider == null) return;

        // 발끝이 발판 모서리에 살짝 걸치는 경우: 발판 위로 올려줌
        float lift = feetPos.y - below.point.y;
        if (lift > 0f && lift <= ledgeForgivenessHeight)
        {
            // 가로 허용 범위 내인지 확인
            float horizontalGap = Mathf.Abs(below.point.x - feetPos.x);
            if (horizontalGap <= ledgeForgivenessHorizontal)
            {
                rb.position += new Vector2(0f, lift);
            }
        }
    }

    void TryDashCornerCorrection()
    {
        if (!enableDashCornerCorrection) return;
        // 대시 중 지형 모서리에 걸리면 살짝 밀어서 통과 (코너 보정과 같은 방식, 별도 거리)
        Vector2 headPos = (Vector2)transform.position + Vector2.up * 0.5f;
        bool leftBlocked = Physics2D.Raycast(headPos, Vector2.left, cornerCheckDistance, cornerCorrectionMask);
        bool rightBlocked = Physics2D.Raycast(headPos, Vector2.right, cornerCheckDistance, cornerCorrectionMask);

        if (leftBlocked == rightBlocked) return;

        float dir = leftBlocked ? 1f : -1f;
        bool spaceFree = !Physics2D.Raycast(headPos, new Vector2(dir, 0.5f).normalized, dashCornerCorrectionDistance, cornerCorrectionMask);
        if (!spaceFree) return;

        rb.position += new Vector2(dir * dashCornerCorrectionDistance, 0f);
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
        // A1이 제공하는 단일 출처 사용
        return stateController != null ? stateController.AimInput : Vector2.zero;
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
