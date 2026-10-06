using UnityEngine;

/// <summary>
/// Room 카메라 시스템 - 플레이어 추적 + Room 경계 제한.
/// 계획서 room-camera-system.md [3], [4], [6] 반영.
/// - Cinemachine Confiner 또는 직접 구현
/// - Room 전환 시 부드러운 경계 교체
/// - 조준 방향 카메라 선행 (Look Ahead)
/// - 부활 시 카메라 즉시 이동
/// </summary>
[DisallowMultipleComponent]
public class RoomCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform cameraTransform; // 따라갈 카메라 (보통 Main Camera)
    [SerializeField] RoomManager roomManager; // Room 전환 감지용

    [Header("Follow Settings")]
    [SerializeField] float followSpeed = 10f; // 추적 속도
    [SerializeField] Vector2 followOffset = Vector2.zero; // 카메라 오프셋
    [SerializeField] bool useSmoothDamp = true; // SmoothDamp vs Lerp

    [Header("Look Ahead (조준 방향 선행)")]
    [SerializeField] bool enableLookAhead = true;
    [SerializeField] float lookAheadDistance = 3f;
    [SerializeField] float lookAheadSpeed = 5f;
    [SerializeField] PlayerAnimation playerAnimation; // 조준 방향 참조용

    [Header("Enemy Visibility")]
    [Tooltip("Room 경계가 적 감지 범위를 넘어설 때 경고합니다 (room-camera-system.md [3])")]
    [SerializeField] bool warnOnEnemyVisionOverflow = true;

    [Header("Boundary (Room Confiner)")]
    [SerializeField] bool confineToRoom = true;
    [SerializeField] float boundaryPadding = 0.5f; // 경계 안쪽 여백

    [Header("Transition")]
    [SerializeField] float transitionDuration = 0.5f; // Room 전환 시 부드러운 이동 시간
    [SerializeField] AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // 상태
    Room currentRoom;
    Transform playerTransform;
    Vector3 targetPosition;
    Vector3 currentVelocity;
    Vector2 lookAheadOffset;
    bool isTransitioning = false;
    float transitionTimer = 0f;
    Room previousRoom;
    Vector3 transitionStartPos;
    Vector3 transitionTargetPos;

    void Awake()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

        if (roomManager == null)
            roomManager = FindAnyObjectByType<RoomManager>();

        if (playerAnimation == null)
            playerAnimation = FindAnyObjectByType<PlayerAnimation>();

        CachePlayerTransform();

        // RoomManager 이벤트 구독
        if (roomManager != null)
        {
            roomManager.OnRoomChanged += OnRoomChanged;
        }

        // 초기 위치 설정
        if (cameraTransform != null && roomManager != null && roomManager.CurrentRoom != null)
        {
            currentRoom = roomManager.CurrentRoom;
            SnapToRoomBounds();
        }
    }

    void CachePlayerTransform()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        playerTransform = player != null ? player.transform : null;
    }

    void OnDestroy()
    {
        if (roomManager != null)
        {
            roomManager.OnRoomChanged -= OnRoomChanged;
        }
    }

    void LateUpdate()
    {
        if (cameraTransform == null) return;

        // Room 전환 중이면 전환 처리
        if (isTransitioning)
        {
            UpdateTransition();
            return;
        }

        // 일반 추적
        UpdateFollow();
        UpdateLookAhead();
        ApplyLookAhead();
        ApplyBoundary();
    }

    void UpdateFollow()
    {
        // 플레이어가 나중에 생성되거나 풀에서 재활성화되는 경우를 대비
        if (playerTransform == null)
            CachePlayerTransform();

        if (playerTransform == null) return;

        Vector3 playerPos = playerTransform.position;
        targetPosition = new Vector3(
            playerPos.x + followOffset.x,
            playerPos.y + followOffset.y,
            cameraTransform.position.z
        );

        if (useSmoothDamp)
        {
            cameraTransform.position = Vector3.SmoothDamp(
                cameraTransform.position, targetPosition, ref currentVelocity, 1f / followSpeed);
        }
        else
        {
            cameraTransform.position = Vector3.Lerp(
                cameraTransform.position, targetPosition, followSpeed * Time.deltaTime);
        }
    }

    void UpdateLookAhead()
    {
        if (!enableLookAhead || playerAnimation == null) return;

        Vector2 aimDir = playerAnimation.GetCurrentAimDirectionVector();
        Vector2 targetLookAhead = aimDir * lookAheadDistance;

        lookAheadOffset = Vector2.Lerp(
            lookAheadOffset, targetLookAhead, lookAheadSpeed * Time.deltaTime);
    }

    void ApplyLookAhead()
    {
        if (lookAheadOffset == Vector2.zero) return;

        Vector3 camPos = cameraTransform.position;
        cameraTransform.position = new Vector3(
            camPos.x + lookAheadOffset.x,
            camPos.y + lookAheadOffset.y,
            camPos.z);
    }

    void ApplyBoundary()
    {
        if (!confineToRoom || currentRoom == null) return;

        // Look Ahead를 이미 적용했으므로 현재 위치만 클램프한다
        cameraTransform.position = ClampToRoomBounds(cameraTransform.position, currentRoom);
    }

    void OnRoomChanged(Room newRoom, Room oldRoom)
    {
        if (newRoom == currentRoom) return;

        previousRoom = currentRoom;
        currentRoom = newRoom;

        ValidateEnemyVisionRange();

        if (transitionDuration > 0f && previousRoom != null)
        {
            StartTransition();
        }
        else
        {
            SnapToRoomBounds();
        }
    }

    void StartTransition()
    {
        isTransitioning = true;
        transitionTimer = 0f;

        transitionStartPos = cameraTransform.position;

        if (currentRoom == null) return;

        if (playerTransform != null)
        {
            Vector3 playerPos = playerTransform.position;
            transitionTargetPos = new Vector3(
                playerPos.x + followOffset.x,
                playerPos.y + followOffset.y,
                cameraTransform.position.z
            );

            transitionTargetPos = ClampToRoomBounds(transitionTargetPos, currentRoom);
        }
        else
        {
            transitionTargetPos = currentRoom.Bounds.center;
            transitionTargetPos.z = cameraTransform.position.z;
        }
    }

    void UpdateTransition()
    {
        transitionTimer += Time.deltaTime;
        float t = Mathf.Clamp01(transitionTimer / transitionDuration);
        float curvedT = transitionCurve.Evaluate(t);

        cameraTransform.position = Vector3.Lerp(transitionStartPos, transitionTargetPos, curvedT);

        if (t >= 1f)
        {
            isTransitioning = false;
            // 전환 완료 후 정확한 경계 적용
            ApplyBoundary();
        }
    }

    void SnapToRoomBounds()
    {
        if (currentRoom == null) return;

        if (playerTransform != null)
        {
            Vector3 pos = new Vector3(
                playerTransform.position.x + followOffset.x,
                playerTransform.position.y + followOffset.y,
                cameraTransform.position.z
            );
            cameraTransform.position = ClampToRoomBounds(pos, currentRoom);
        }
        else
        {
            Vector3 center = currentRoom.Bounds.center;
            center.z = cameraTransform.position.z;
            cameraTransform.position = center;
        }
    }

    Vector3 ClampToRoomBounds(Vector3 pos, Room room)
    {
        if (room == null) return pos;

        Bounds bounds = room.Bounds;
        Camera cam = cameraTransform.GetComponent<Camera>();
        if (cam == null || !cam.orthographic) return pos;

        float camHeight = cam.orthographicSize;
        float camWidth = camHeight * cam.aspect;

        float minX = bounds.min.x + camWidth + boundaryPadding;
        float maxX = bounds.max.x - camWidth - boundaryPadding;
        float minY = bounds.min.y + camHeight + boundaryPadding;
        float maxY = bounds.max.y - camHeight - boundaryPadding;

        if (minX > maxX) minX = maxX = (bounds.min.x + bounds.max.x) * 0.5f;
        if (minY > maxY) minY = maxY = (bounds.min.y + bounds.max.y) * 0.5f;

        return new Vector3(
            Mathf.Clamp(pos.x, minX, maxX),
            Mathf.Clamp(pos.y, minY, maxY),
            pos.z
        );
    }

    /// <summary>외부에서 강제 카메라 위치 설정 (부활 시 등)</summary>
    public void ForceCameraPosition(Vector3 position)
    {
        cameraTransform.position = position;
        currentVelocity = Vector3.zero;
        lookAheadOffset = Vector2.zero;
        isTransitioning = false;
    }

    /// <summary>부활 시 카메라 즉시 이동 (컷 전환) - room-camera-system.md [6]</summary>
    public void OnPlayerRespawn(Vector3 respawnPosition)
    {
        Vector3 targetPos = new Vector3(
            respawnPosition.x + followOffset.x,
            respawnPosition.y + followOffset.y,
            cameraTransform.position.z
        );

        if (currentRoom != null)
        {
            targetPos = ClampToRoomBounds(targetPos, currentRoom);
        }

        ForceCameraPosition(targetPos);
    }

    /// <summary>현재 Room 경계 반환 (외부 조회용)</summary>
    public Bounds GetCurrentRoomBounds() => currentRoom?.Bounds ?? new Bounds();

    /// <summary>
    /// 화면 밖에서 적이 플레이어를 쏘지 못하는지 검증한다 (room-camera-system.md [3]).
    /// 카메라 가시 반경보다 적 감지 거리가 크면 화면 밖 사격이 가능하므로 경고한다.
    /// </summary>
    public void ValidateEnemyVisionRange()
    {
        if (!warnOnEnemyVisionOverflow || currentRoom == null || cameraTransform == null) return;

        Camera cam = cameraTransform.GetComponent<Camera>();
        if (cam == null || !cam.orthographic) return;

        float halfWidth = cam.orthographicSize * cam.aspect;
        float halfHeight = cam.orthographicSize;
        float visibleRadius = Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);

        foreach (var enemyObj in currentRoom.Enemies)
        {
            if (enemyObj == null) continue;

            var vision = enemyObj.GetComponent<EnemyVision>();
            if (vision != null && vision.DetectRange > visibleRadius)
            {
                Debug.LogWarning(
                    $"[RoomCamera] '{enemyObj.name}'의 감지 거리({vision.DetectRange:F1})가 카메라 가시 반경({visibleRadius:F1})보다 큽니다. 화면 밖 사격이 가능합니다.",
                    this);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (currentRoom != null)
        {
            Bounds bounds = currentRoom.Bounds;
            Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        // Look Ahead 표시
        if (enableLookAhead && cameraTransform != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(cameraTransform.position, new Vector3(lookAheadOffset.x, lookAheadOffset.y, 0f));
        }
    }
}