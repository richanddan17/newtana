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

    [Header("Boundary (Room Confiner)")]
    [SerializeField] bool confineToRoom = true;
    [SerializeField] float boundaryPadding = 0.5f; // 경계 안쪽 여백

    [Header("Transition")]
    [SerializeField] float transitionDuration = 0.5f; // Room 전환 시 부드러운 이동 시간
    [SerializeField] AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Shake Integration")]
    [SerializeField] bool applyShakeOffset = true; // CameraShake 오프셋 적용

    // 상태
    Room currentRoom;
    Vector3 targetPosition;
    Vector3 currentVelocity;
    Vector2 lookAheadOffset;
    bool isTransitioning = false;
    float transitionTimer = 0f;
    Room previousRoom;
    Vector3 transitionStartPos;
    Vector3 transitionTargetPos;

    // CameraShake 참조
    CameraShake cameraShake;

    void Awake()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

        if (roomManager == null)
            roomManager = FindAnyObjectByType<RoomManager>();

        if (playerAnimation == null)
            playerAnimation = FindAnyObjectByType<PlayerAnimation>();

        cameraShake = CameraShake.Instance;

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
        ApplyBoundary();
        ApplyShake();
    }

    void UpdateFollow()
    {
        // 플레이어 찾기
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        Vector3 playerPos = player.transform.position;
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

    void ApplyBoundary()
    {
        if (!confineToRoom || currentRoom == null) return;

        Bounds roomBounds = currentRoom.Bounds;
        Vector3 camPos = cameraTransform.position;

        // 카메라 크기 계산 (Orthographic)
        Camera cam = cameraTransform.GetComponent<Camera>();
        if (cam == null || !cam.orthographic) return;

        float camHeight = cam.orthographicSize;
        float camWidth = camHeight * cam.aspect;

        // 경계 내로 클램프 (패딩 고려)
        float minX = roomBounds.min.x + camWidth + boundaryPadding;
        float maxX = roomBounds.max.x - camWidth - boundaryPadding;
        float minY = roomBounds.min.y + camHeight + boundaryPadding;
        float maxY = roomBounds.max.y - camHeight - boundaryPadding;

        // 유효한 경계인지 확인 (방이 카메라보다 작을 수 있음)
        if (minX > maxX) minX = maxX = (roomBounds.min.x + roomBounds.max.x) * 0.5f;
        if (minY > maxY) minY = maxY = (roomBounds.min.y + roomBounds.max.y) * 0.5f;

        float clampedX = Mathf.Clamp(camPos.x + lookAheadOffset.x, minX, maxX);
        float clampedY = Mathf.Clamp(camPos.y + lookAheadOffset.y, minY, maxY);

        cameraTransform.position = new Vector3(clampedX, clampedY, camPos.z);
    }

    void ApplyShake()
    {
        if (!applyShakeOffset || cameraShake == null) return;

        // CameraShake의 흔들림 오프셋 적용 (CameraShake에서 localPosition 조작하므로 여기선 추가만)
        // CameraShake가 cameraTransform.localPosition을 직접 조작하므로 별도 처리 불필요
        // 단, 경계 적용 후 흔들림이 경계를 벗어나지 않게 하려면 여기서 보정 가능
    }

    void OnRoomChanged(Room newRoom, Room oldRoom)
    {
        if (newRoom == currentRoom) return;

        previousRoom = currentRoom;
        currentRoom = newRoom;

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

        // 현재 위치에서 새 Room 경계 내 중앙으로 전환
        transitionStartPos = cameraTransform.position;
        
        // 새 Room의 플레이어 위치 기준 타겟 계산
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && currentRoom != null)
        {
            Vector3 playerPos = player.transform.position;
            transitionTargetPos = new Vector3(
                playerPos.x + followOffset.x,
                playerPos.y + followOffset.y,
                cameraTransform.position.z
            );
            
            // 새 Room 경계 내로 클램프
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

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            Vector3 pos = new Vector3(
                player.transform.position.x + followOffset.x,
                player.transform.position.y + followOffset.y,
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