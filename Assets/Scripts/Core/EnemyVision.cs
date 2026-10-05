using UnityEngine;

/// <summary>
/// 적 시야/감지 시스템.
/// 계획서 enemy-ai-system.md [2] 반영.
/// - 거리, 시야각, Raycast(벽 체크)로 플레이어 감지
/// - 감지 해제 타이머로 Idle 복귀
/// </summary>
[DisallowMultipleComponent]
public class EnemyVision : MonoBehaviour
{
    [Header("Detection Settings")]
    [SerializeField] float detectRange = 10f;
    [SerializeField] float fieldOfView = 110f; // 도
    [SerializeField] LayerMask obstacleLayerMask; // 벽/지형 레이어
    [SerializeField] LayerMask playerLayerMask;   // 플레이어 레이어

    [Header("Eye Position")]
    [SerializeField] Transform eyeTransform; // 감지 기준점 (머리/눈 위치)
    [SerializeField] float eyeHeight = 1f;   // eyeTransform 없을 때 기본 높이

    [Header("Detection Lost")]
    [SerializeField] float loseSightTime = 3f; // 시야 잃고 완전 해제까지 시간
    [SerializeField] float recheckInterval = 0.2f; // 감지 재체크 간격

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    // 상태
    Transform playerTransform;
    bool canSeePlayer = false;
    float loseSightTimer = 0f;
    float recheckTimer = 0f;

    // 프로퍼티
    public bool CanSeePlayer => canSeePlayer;
    public Transform PlayerTransform => playerTransform;
    public float DetectRange => detectRange;
    public float FieldOfView => fieldOfView;

    // 이벤트
    public event System.Action<Transform> OnPlayerDetected;
    public event System.Action OnPlayerLost;

    void Awake()
    {
        if (eyeTransform == null)
            eyeTransform = transform;

        FindPlayer();
    }

    void Update()
    {
        if (playerTransform == null)
        {
            FindPlayer();
            return;
        }

        recheckTimer -= Time.deltaTime;

        bool wasSeeing = canSeePlayer;
        
        // 주기적 재체크 (매 프레임 Raycast 비용 절약)
        if (recheckTimer <= 0f)
        {
            canSeePlayer = PerformDetection();
            recheckTimer = recheckInterval;
        }

        // 감지 해제 타이머
        if (canSeePlayer)
        {
            loseSightTimer = 0f;
        }
        else if (wasSeeing)
        {
            loseSightTimer += Time.deltaTime;
            if (loseSightTimer >= loseSightTime)
            {
                // 완전 해제
                OnPlayerLost?.Invoke();
            }
        }

        // 상태 변경 이벤트
        if (!wasSeeing && canSeePlayer)
        {
            OnPlayerDetected?.Invoke(playerTransform);
        }
    }

    bool PerformDetection()
    {
        if (playerTransform == null) return false;

        Vector2 eyePos = (Vector2)eyeTransform.position + Vector2.up * eyeHeight;
        Vector2 toPlayer = (Vector2)playerTransform.position - eyePos;
        float distance = toPlayer.magnitude;

        // 1. 거리 체크
        if (distance > detectRange) return false;

        // 2. 시야각 체크
        Vector2 forward = transform.right * transform.localScale.x; // 바라보는 방향
        float angle = Vector2.Angle(forward, toPlayer.normalized);
        if (angle > fieldOfView * 0.5f) return false;

        // 3. 장애물 체크 (Raycast)
        var hit = Physics2D.Raycast(eyePos, toPlayer.normalized, distance, obstacleLayerMask);
        if (hit.collider != null)
        {
            // 플레이어 레이어도 같이 체크해서 플레이어가 먼저 맞았는지 확인
            var playerHit = Physics2D.Raycast(eyePos, toPlayer.normalized, distance, playerLayerMask);
            if (playerHit.collider == null || playerHit.distance > hit.distance)
            {
                // 벽이 플레이어보다 가까움 = 가려짐
                return false;
            }
        }

        return true;
    }

    void FindPlayer()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            playerTransform = player.transform;
    }

    /// <summary>외부에서 강제 감지 (피격 등)</summary>
    public void ForceDetect(Transform target)
    {
        playerTransform = target;
        canSeePlayer = true;
        loseSightTimer = 0f;
        OnPlayerDetected?.Invoke(target);
    }

    /// <summary>감지 설정 런타임 변경</summary>
    public void SetDetectionParams(float range, float fov, float loseTime)
    {
        detectRange = range;
        fieldOfView = fov;
        loseSightTime = loseTime;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector2 eyePos = (Vector2)eyeTransform.position + Vector2.up * eyeHeight;
        Vector2 forward = transform.right * transform.localScale.x;

        // 감지 범위 원
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.1f);
        Gizmos.DrawWireSphere(eyePos, detectRange);

        // 시야각
        Gizmos.color = canSeePlayer ? Color.green : Color.red;
        float halfFOV = fieldOfView * 0.5f;
        Vector2 leftDir = Quaternion.Euler(0, 0, -halfFOV) * forward;
        Vector2 rightDir = Quaternion.Euler(0, 0, halfFOV) * forward;
        Gizmos.DrawRay(eyePos, leftDir * detectRange);
        Gizmos.DrawRay(eyePos, rightDir * detectRange);
        Gizmos.DrawWireSphere(eyePos + forward * detectRange, 0.1f);

        // 플레이어 방향
        if (playerTransform != null)
        {
            Gizmos.color = canSeePlayer ? Color.green : Color.yellow;
            Gizmos.DrawLine(eyePos, playerTransform.position);
        }
    }
}