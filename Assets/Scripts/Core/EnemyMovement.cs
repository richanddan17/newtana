using UnityEngine;

/// <summary>
/// 적 이동 시스템 (PlayerMovement와 유사하지만 AI 제어용).
/// 계획서 enemy-ai-system.md [3], [5] 반영.
/// - 거리 유지 이동 (접근/후퇴)
/// - 낭떠러지/벽 감지
/// - GroundCheck 연동
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] GroundCheck groundCheck;
    [SerializeField] Rigidbody2D rb;

    [Header("Move Settings")]
    [SerializeField] float maxMoveSpeed = 4f;
    [SerializeField] float acceleration = 40f;
    [SerializeField] float deceleration = 50f;
    [SerializeField] float airAcceleration = 20f;
    [SerializeField] float airDeceleration = 10f;

    [Header("Edge/Wall Avoidance")]
    [SerializeField] Transform frontCheck;
    [SerializeField] Transform backCheck;
    [SerializeField] float checkDistance = 0.5f;
    [SerializeField] LayerMask groundLayerMask;
    [SerializeField] bool avoidEdges = true;
    [SerializeField] bool avoidWalls = true;

    [Header("Slope Handling")]
    [SerializeField] bool handleSlopes = true;
    [SerializeField] float maxSlopeAngle = 45f;

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    // 상태
    float currentMoveSpeed = 0f;
    Vector2 moveInput = Vector2.zero;
    bool isGrounded;

    // 프로퍼티
    public bool IsGrounded => groundCheck != null ? groundCheck.IsGrounded : isGrounded;
    public Vector2 Velocity => rb ? rb.linearVelocity : Vector2.zero;
    public float HorizontalSpeed => Mathf.Abs(Velocity.x);
    public Vector2 MoveInput => moveInput;
    public int FacingDirection => transform.localScale.x > 0 ? 1 : -1;

    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (groundCheck == null) groundCheck = GetComponent<GroundCheck>();

        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.freezeRotation = true;
        rb.gravityScale = 1f;

        if (groundCheck != null)
        {
            groundCheck.OnGroundedChanged += OnGroundedChanged;
        }
    }

    void OnDestroy()
    {
        if (groundCheck != null) groundCheck.OnGroundedChanged -= OnGroundedChanged;
    }

    void OnGroundedChanged(bool grounded)
    {
        isGrounded = grounded;
    }

    void FixedUpdate()
    {
        HandleMovement();
        CheckEdgeAndWall();
    }

    void HandleMovement()
    {
        float targetSpeed = moveInput.x * currentMoveSpeed;
        float currentAccel = IsGrounded ? acceleration : airAcceleration;
        float currentDecel = IsGrounded ? deceleration : airDeceleration;

        float currentSpeed = rb.linearVelocity.x;
        float speedDiff = targetSpeed - currentSpeed;
        float accelRate = (targetSpeed == 0f) ? currentDecel : currentAccel;
        float movement = speedDiff * accelRate * Time.fixedDeltaTime;

        // 경사면 처리
        if (handleSlopes && IsGrounded)
        {
            float slopeAngle = groundCheck != null ? groundCheck.GetSlopeAngle() : 0f;
            if (slopeAngle > 0.1f && slopeAngle <= maxSlopeAngle)
            {
                Vector2 slopeDir = groundCheck.GetSlopeDirection();
                float inputAlongSlope = Vector2.Dot(new Vector2(moveInput.x, 0), slopeDir);
                targetSpeed = inputAlongSlope * currentMoveSpeed;
                speedDiff = targetSpeed - currentSpeed;
                movement = speedDiff * accelRate * Time.fixedDeltaTime;
            }
        }

        rb.linearVelocity = new Vector2(currentSpeed + movement, rb.linearVelocity.y);
    }

    void CheckEdgeAndWall()
    {
        if (!IsGrounded) return;

        Vector2 forward = transform.right * FacingDirection;

        // 낭떠러지 체크
        if (avoidEdges && frontCheck != null)
        {
            var hit = Physics2D.Raycast(frontCheck.position, Vector2.down, checkDistance, groundLayerMask);
            if (hit.collider == null)
            {
                // 낭떠러지 앞 -> 방향 반전 또는 정지
                if (moveInput.x != 0f)
                {
                    moveInput.x *= -1f;
                    FlipDirection();
                }
                else
                {
                    moveInput = Vector2.zero;
                }
            }
        }

        // 벽 체크
        if (avoidWalls && frontCheck != null)
        {
            var hit = Physics2D.Raycast(frontCheck.position, forward, checkDistance, groundLayerMask);
            if (hit.collider != null)
            {
                // 벽 앞 -> 방향 반전 또는 정지
                if (moveInput.x != 0f)
                {
                    moveInput.x *= -1f;
                    FlipDirection();
                }
                else
                {
                    moveInput = Vector2.zero;
                }
            }
        }
    }

    void FlipDirection()
    {
        Vector3 scale = transform.localScale;
        scale.x *= -1f;
        transform.localScale = scale;
    }

    #region Public API (AI에서 호출)

    /// <summary>이동 방향 설정 (-1 ~ 1)</summary>
    public void MoveTowards(Vector2 direction)
    {
        moveInput = direction.normalized;
    }

    /// <summary>이동 속도 설정</summary>
    public void SetMoveSpeed(float speed)
    {
        currentMoveSpeed = Mathf.Max(0f, speed);
    }

    /// <summary>즉시 정지</summary>
    public void Stop()
    {
        moveInput = Vector2.zero;
    }

    /// <summary>방향 강제 설정</summary>
    public void SetFacingDirection(int direction)
    {
        if (direction != 0 && Mathf.Sign(transform.localScale.x) != direction)
        {
            FlipDirection();
        }
    }

    #endregion

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        if (frontCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(frontCheck.position, Vector2.down * checkDistance);
            Gizmos.DrawRay(frontCheck.position, transform.right * FacingDirection * checkDistance);
        }
        if (backCheck != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(backCheck.position, Vector2.down * checkDistance);
        }

        if (IsGrounded && groundCheck != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(groundCheck.GroundPoint, groundCheck.GroundNormal * 0.5f);
        }
    }
}