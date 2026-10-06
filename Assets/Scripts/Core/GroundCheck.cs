using UnityEngine;
using System;

/// <summary>
/// 독립적인 지상 판정 컴포넌트.
/// PlayerMovement, EnemyAI, CameraSystem 등에서 공통 사용.
/// 계획서 [5]: 이동용 Ground와 카메라용 Room Boundary 분리.
/// </summary>
[DisallowMultipleComponent]
public class GroundCheck : MonoBehaviour
{
    [Header("Check Settings")]
    [SerializeField] LayerMask groundLayerMask = 1 << 3; // Ground 레이어
    [SerializeField] float checkRadius = 0.2f;
    [SerializeField] float checkDistance = 0.05f; // BoxCast 거리
    [SerializeField] bool useBoxCheck = true; // true: BoxCast, false: Circle Overlap
    [SerializeField] Vector2 boxSize = new Vector2(0.6f, 0.1f);

    [Header("Multiple Points (경사면/가장자리 대응)")]
    [SerializeField] bool useMultiplePoints = false;
    [SerializeField] Transform[] checkPoints; // 여러 체크 포인트

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    // 상태
    public bool IsGrounded { get; private set; }
    public Vector2 GroundNormal { get; private set; }
    public Collider2D GroundCollider { get; private set; }
    public Vector2 GroundPoint { get; private set; }

    // 이벤트
    public event Action<bool> OnGroundedChanged; // grounded

    void FixedUpdate()
    {
        bool wasGrounded = IsGrounded;
        PerformCheck();

        if (wasGrounded != IsGrounded)
        {
            OnGroundedChanged?.Invoke(IsGrounded);
        }
    }

    void PerformCheck()
    {
        IsGrounded = false;
        GroundNormal = Vector2.up;
        GroundCollider = null;
        GroundPoint = Vector2.zero;

        if (useMultiplePoints && checkPoints != null && checkPoints.Length > 0)
        {
            // 여러 포인트 중 하나라도 닿으면 grounded
            foreach (var point in checkPoints)
            {
                if (point == null) continue;
                if (CheckSinglePoint(point.position, out var hit))
                {
                    IsGrounded = true;
                    GroundNormal = hit.normal;
                    GroundCollider = hit.collider;
                    GroundPoint = hit.point;
                    break;
                }
            }
        }
        else
        {
            // 단일 포인트 (transform.position 또는 지정 포인트)
            Vector2 checkPos = transform.position;
            if (CheckSinglePoint(checkPos, out var hit))
            {
                IsGrounded = true;
                GroundNormal = hit.normal;
                GroundCollider = hit.collider;
                GroundPoint = hit.point;
            }
        }
    }

    bool CheckSinglePoint(Vector2 position, out RaycastHit2D hit)
    {
        hit = default;

        if (useBoxCheck)
        {
            // BoxCast로 평평한 바닥 감지 (가장자리에서 떨어지지 않게)
            hit = Physics2D.BoxCast(position, boxSize, 0f, Vector2.down, checkDistance, groundLayerMask);
            return hit.collider != null && hit.collider.gameObject != gameObject;
        }
        else
        {
            // Circle Overlap
            var overlap = Physics2D.OverlapCircle(position, checkRadius, groundLayerMask);
            if (overlap != null && overlap.gameObject != gameObject)
            {
                // Overlap은 normal이 없으므로 아래로 Raycast로 보정
                hit = Physics2D.Raycast(position, Vector2.down, checkRadius + 0.05f, groundLayerMask);
                return hit.collider != null;
            }
            return false;
        }
    }

    /// <summary>특정 레이어만으로 체크 (일방향 플랫폼 등)</summary>
    public bool CheckWithLayerMask(LayerMask mask)
    {
        if (useBoxCheck)
        {
            var hit = Physics2D.BoxCast(transform.position, boxSize, 0f, Vector2.down, checkDistance, mask);
            return hit.collider != null && hit.collider.gameObject != gameObject;
        }
        else
        {
            var overlap = Physics2D.OverlapCircle(transform.position, checkRadius, mask);
            return overlap != null && overlap.gameObject != gameObject;
        }
    }

    /// <summary>경사면 각도 반환 (도)</summary>
    public float GetSlopeAngle()
    {
        if (!IsGrounded) return 0f;
        return Vector2.Angle(Vector2.up, GroundNormal);
    }

    /// <summary>경사면 방향 벡터 (수평)</summary>
    public Vector2 GetSlopeDirection()
    {
        if (!IsGrounded) return Vector2.right;
        return Vector2.Perpendicular(GroundNormal).normalized;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = IsGrounded ? Color.green : Color.red;

        if (useMultiplePoints && checkPoints != null)
        {
            foreach (var point in checkPoints)
            {
                if (point == null) continue;
                DrawCheckGizmo(point.position);
            }
        }
        else
        {
            DrawCheckGizmo(transform.position);
        }

        // 법선 표시
        if (IsGrounded)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(GroundPoint, GroundNormal * 0.5f);
        }
    }

    void DrawCheckGizmo(Vector2 position)
    {
        if (useBoxCheck)
        {
            Gizmos.DrawWireCube(position + Vector2.down * checkDistance * 0.5f, new Vector3(boxSize.x, checkDistance, 0f));
        }
        else
        {
            Gizmos.DrawWireSphere(position, checkRadius);
        }
    }
}