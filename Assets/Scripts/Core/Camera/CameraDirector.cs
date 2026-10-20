using System.Collections;
using UnityEngine;
using Cinemachine;

/// <summary>
/// 카메라 시스템 진입점 — Cinemachine 2.10 기반 단일 싱글턴 (room-camera-system.md v5).
/// 카메라 트랜스폼은 CinemachineBrain이 소유하며 이 스크립트는 직접 움직이지 않는다.
/// 추적은 Follow vcam(FramingTransposer + Look Ahead proxy), 대화는 Dialogue vcam 우선순위
/// 스위칭 + Cut 블렌드, 경계는 Room별 CameraBoundaryMode로 Confiner2D를 on/off한다.
/// </summary>
[DisallowMultipleComponent]
public class CameraDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] CinemachineBrain brain;
    [SerializeField] CinemachineVirtualCamera followVcam;
    [SerializeField] CinemachineVirtualCamera dialogueVcam;
    [SerializeField] RoomManager roomManager;
    [SerializeField] PlayerAnimation playerAnimation;

    [Header("Look Ahead (조준 선행)")]
    [SerializeField] bool enableLookAhead = true;
    [Tooltip("조준 방향으로 최대 이 거리만큼 카메라가 선행 (월드 단위)")]
    [SerializeField] float lookAheadDistance = 3f;
    [Tooltip("선행 오프셋 반영 계수 (클수록 빠름)")]
    [SerializeField] float lookAheadSmoothing = 5f;

    [Header("Dialogue")]
    [Tooltip("대화 클로즈업 배율 — dialogue ortho = follow ortho / 값 (영상 기준 약 3.5배)")]
    [SerializeField] float dialogueZoomDivisor = 3.5f;
    [SerializeField] int followPriority = 10;
    [SerializeField] int dialogueIdlePriority = 0;
    [SerializeField] int dialogueActivePriority = 100;

    [Header("Boundary")]
    [Tooltip("Soft 모드에서 Room Bounds 각 변 바깥으로 허용할 여유 (월드 단위)")]
    [SerializeField] float softBoundaryPadding = 4f;
    [Tooltip("Confiner2D 위치 감쇠 (월드 단위)")]
    [SerializeField] float boundaryDamping = 0.5f;

    [Header("Warnings")]
    [Tooltip("적 감지 거리가 카메라 가시 반경보다 크면 경고")]
    [SerializeField] bool warnOnEnemyVisionOverflow = true;

    public static CameraDirector Instance { get; private set; }

    const string kLookAheadProxyName = "LookAheadProxy";
    const string kDialogueProxyName = "DialogueProxy";
    const string kBoundaryProxyName = "CameraBoundaryProxy";

    Transform lookAheadProxy;
    Transform dialogueProxy;
    Transform playerTransform;
    Vector2 lookAheadOffset;
    Room currentRoom;
    bool inDialogue;
    Transform dialogueTargetA;
    Transform dialogueTargetB;
    float baseOrthoSize;
    CinemachineBlendDefinition roomBlend;
    CinemachineConfiner2D confiner;
    PolygonCollider2D boundaryShape;
    Bounds lastBoundaryBounds;
    bool hasAppliedBoundary;
    Coroutine restoreBlendRoutine;
    bool roomManagerSubscribed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnsureReferences();
        EnsureProxies();
        EnsureVcamComponents();
        CaptureBaseState();
        SubscribeRoomManager();
    }

    void Start()
    {
        EnsureReferences();
        SubscribeRoomManager();
        SyncInitialRoom();
        CachePlayer();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnsubscribeRoomManager();
    }

    void Update()
    {
        if (playerTransform == null) CachePlayer();
        UpdateLookAheadProxy();
        UpdateDialogueProxy();
    }

    void EnsureReferences()
    {
        if (brain == null) brain = FindAnyObjectByType<CinemachineBrain>();
        if (roomManager == null) roomManager = FindAnyObjectByType<RoomManager>();
        if (playerAnimation == null) playerAnimation = FindAnyObjectByType<PlayerAnimation>();

        if (followVcam == null || dialogueVcam == null)
        {
            foreach (var vcam in FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None))
            {
                if (vcam == null) continue;
                if (vcam.gameObject.name.Contains("Dialogue"))
                {
                    if (dialogueVcam == null) dialogueVcam = vcam;
                }
                else if (followVcam == null)
                {
                    followVcam = vcam;
                }
            }
        }
    }

    void EnsureProxies()
    {
        if (lookAheadProxy == null) lookAheadProxy = GetOrCreateChild(kLookAheadProxyName);
        if (dialogueProxy == null) dialogueProxy = GetOrCreateChild(kDialogueProxyName);
    }

    Transform GetOrCreateChild(string childName)
    {
        Transform child = transform.Find(childName);
        if (child == null)
        {
            var proxyGo = new GameObject(childName);
            proxyGo.transform.SetParent(transform, false);
            child = proxyGo.transform;
        }
        return child;
    }

    void EnsureVcamComponents()
    {
        if (followVcam != null)
        {
            if (followVcam.GetCinemachineComponent<CinemachineFramingTransposer>() == null)
                followVcam.AddCinemachineComponent<CinemachineFramingTransposer>();
            EnsureConfiner();
            EnsureImpulseListener(followVcam);
            // 추적 대상 계약: Look Ahead proxy가 Follow — proxy가 플레이어+조준 오프셋을 대리한다
            followVcam.Follow = lookAheadProxy;
        }
        if (dialogueVcam != null)
        {
            if (dialogueVcam.GetCinemachineComponent<CinemachineFramingTransposer>() == null)
                dialogueVcam.AddCinemachineComponent<CinemachineFramingTransposer>();
            EnsureImpulseListener(dialogueVcam);
            dialogueVcam.Follow = dialogueProxy;
        }
    }

    void EnsureConfiner()
    {
        if (followVcam == null) return;
        if (confiner == null)
            confiner = followVcam.GetComponent<CinemachineConfiner2D>();
        if (confiner == null)
            confiner = followVcam.gameObject.AddComponent<CinemachineConfiner2D>();
        confiner.m_Damping = boundaryDamping;
        if (confiner.m_BoundingShape2D == null)
            confiner.enabled = false;
    }

    void EnsureImpulseListener(CinemachineVirtualCamera vcam)
    {
        var listener = vcam.GetComponent<CinemachineImpulseListener>();
        if (listener == null)
            listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();
        // AddComponent 직후 필드 기본값은 0이라 임펄스가 묵음 — CameraShake 채널(1)과 동기화
        listener.m_ChannelMask = 1;
        listener.m_Gain = 1f;
    }

    void CaptureBaseState()
    {
        if (followVcam != null)
            baseOrthoSize = followVcam.m_Lens.OrthographicSize;
        if (brain != null)
            roomBlend = brain.m_DefaultBlend;
    }

    void SubscribeRoomManager()
    {
        if (roomManager == null || roomManagerSubscribed) return;
        roomManager.OnRoomChanged += OnRoomChanged;
        roomManagerSubscribed = true;
    }

    void UnsubscribeRoomManager()
    {
        if (!roomManagerSubscribed) return;
        if (roomManager != null) roomManager.OnRoomChanged -= OnRoomChanged;
        roomManagerSubscribed = false;
    }

    void SyncInitialRoom()
    {
        if (currentRoom != null || roomManager == null) return;
        Room initial = roomManager.CurrentRoom;
        if (initial == null) return; // RoomManager.Start 이전이면 OnRoomChanged로 확정된다
        currentRoom = initial;
        ApplyBoundary();
        ValidateEnemyVisionRange();
    }

    void CachePlayer()
    {
        if (playerTransform != null) return;
        var playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null) return;
        playerTransform = playerGo.transform;
        if (playerAnimation == null)
            playerAnimation = playerGo.GetComponent<PlayerAnimation>();
    }

    void UpdateLookAheadProxy()
    {
        if (lookAheadProxy == null || playerTransform == null) return;

        Vector2 target = playerTransform.position;
        if (enableLookAhead && playerAnimation != null)
            target += playerAnimation.GetCurrentAimDirectionVector() * lookAheadDistance;

        Vector2 smoothed = Vector2.Lerp((Vector2)lookAheadProxy.position, target, lookAheadSmoothing * Time.deltaTime);
        lookAheadProxy.position = new Vector3(smoothed.x, smoothed.y, 0f);
        lookAheadOffset = smoothed - (Vector2)playerTransform.position;
    }

    void UpdateDialogueProxy()
    {
        if (dialogueProxy == null || !inDialogue) return;
        dialogueProxy.position = GetDialogueMidpoint();
    }

    Vector3 GetDialogueMidpoint()
    {
        bool hasA = dialogueTargetA != null;
        bool hasB = dialogueTargetB != null;
        if (hasA && hasB)
        {
            Vector2 mid = ((Vector2)dialogueTargetA.position + (Vector2)dialogueTargetB.position) * 0.5f;
            return new Vector3(mid.x, mid.y, 0f);
        }
        if (hasA) return new Vector3(dialogueTargetA.position.x, dialogueTargetA.position.y, 0f);
        if (hasB) return new Vector3(dialogueTargetB.position.x, dialogueTargetB.position.y, 0f);
        return dialogueProxy.position;
    }

    /// <summary>대화 진입: Dialogue vcam으로 Cut 전환하고 클로즈업 줌을 적용한다.</summary>
    public void EnterDialogue(Transform targetA, Transform targetB)
    {
        EnsureReferences();
        EnsureProxies();

        dialogueTargetA = targetA;
        dialogueTargetB = targetB;
        inDialogue = true;

        UpdateDialogueProxy(); // Cut 전에 중앙점을 확정해 전환 프레임의 좌표 지연을 없앤다

        ForceCutBlend();
        if (followVcam != null) followVcam.Priority = dialogueIdlePriority;
        if (dialogueVcam != null)
        {
            dialogueVcam.Priority = dialogueActivePriority;
            ApplyDialogueLens();
        }
    }

    /// <summary>대화 종료: Follow vcam으로 복귀하고 2프레임 후 블렌드 설정을 되돌린다.</summary>
    public void ExitDialogue()
    {
        if (!inDialogue) return;
        inDialogue = false;
        dialogueTargetA = null;
        dialogueTargetB = null;

        if (followVcam != null) followVcam.Priority = followPriority;
        if (dialogueVcam != null) dialogueVcam.Priority = dialogueIdlePriority;

        ForceCutBlend();
        StartRestoreBlendRoutine();
    }

    /// <summary>부활 처리: 카메라를 부활점으로 스냅하고 Follow 모드 + 흔들림 초기화 (plan [6]).</summary>
    public void OnPlayerRespawn(Vector3 respawnPosition)
    {
        EnsureReferences();
        EnsureProxies();

        inDialogue = false;
        dialogueTargetA = null;
        dialogueTargetB = null;
        if (dialogueVcam != null)
        {
            dialogueVcam.Priority = dialogueIdlePriority;
            dialogueVcam.PreviousStateIsValid = false;
        }
        if (followVcam != null)
        {
            followVcam.Priority = followPriority;
            followVcam.PreviousStateIsValid = false;
        }

        ForceCameraPosition(respawnPosition);

        SyncInitialRoom(); // 이벤트 누락 방어
        ApplyBoundary();

        ForceCutBlend();
        StartRestoreBlendRoutine();
        CameraShake.StopAll();
    }

    /// <summary>카메라 추적 대상을 즉시 해당 위치로 스냅한다 (이전 위치에서 날아오는 연출 방지).</summary>
    public void ForceCameraPosition(Vector3 position)
    {
        EnsureProxies();
        lookAheadProxy.position = new Vector3(position.x, position.y, 0f);
        lookAheadOffset = Vector2.zero;
        if (followVcam != null) followVcam.PreviousStateIsValid = false;
        if (dialogueVcam != null) dialogueVcam.PreviousStateIsValid = false;
    }

    /// <summary>현재 Room의 카메라 경계 (Room 미확정이면 빈 Bounds).</summary>
    public Bounds GetCurrentRoomBounds()
    {
        return currentRoom != null
            ? currentRoom.Bounds
            : new Bounds(transform.position, Vector3.zero);
    }

    /// <summary>현재 Room 적들의 감지 거리가 카메라 가시 반경을 넘으면 경고한다 (plan [9] 점검).</summary>
    public void ValidateEnemyVisionRange()
    {
        if (!warnOnEnemyVisionOverflow || currentRoom == null) return;
        Camera cam = GetOutputCamera();
        if (cam == null || !cam.orthographic) return;

        float halfWidth = cam.orthographicSize * cam.aspect;
        float halfHeight = cam.orthographicSize;
        float visibleRadius = Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);

        foreach (var enemyObj in currentRoom.Enemies)
        {
            if (enemyObj == null) continue;
            var vision = enemyObj.GetComponent<EnemyVision>();
            if (vision != null && vision.DetectRange > visibleRadius)
                Debug.LogWarning(
                    $"[CameraDirector] '{enemyObj.name}'의 감지 거리({vision.DetectRange:F1})가 카메라 가시 반경({visibleRadius:F1})보다 큽니다. 화면 밖에서 사격할 수 있습니다.",
                    this);
        }
    }

    void OnRoomChanged(Room newRoom, Room oldRoom)
    {
        currentRoom = newRoom;
        ApplyBoundary();
        ValidateEnemyVisionRange();
    }

    void ApplyBoundary()
    {
        if (followVcam == null) return;
        EnsureConfiner();

        if (currentRoom == null || currentRoom.BoundaryMode == CameraBoundaryMode.Off)
        {
            confiner.enabled = false;
            hasAppliedBoundary = false;
            return;
        }

        Bounds bounds = currentRoom.Bounds;
        if (bounds.size == Vector3.zero)
        {
            confiner.enabled = false;
            hasAppliedBoundary = false;
            return;
        }

        if (currentRoom.BoundaryMode == CameraBoundaryMode.Soft)
        {
            Vector3 pad = Vector3.one * softBoundaryPadding;
            bounds.SetMinMax(bounds.min - pad, bounds.max + pad);
        }

        SetBoundaryShape(bounds);
        confiner.enabled = true;
        confiner.m_Damping = boundaryDamping;
        confiner.m_MaxWindowSize = followVcam.m_Lens.OrthographicSize;
        confiner.m_BoundingShape2D = boundaryShape;
        confiner.InvalidateCache();

        lastBoundaryBounds = bounds;
        hasAppliedBoundary = true;
    }

    void SetBoundaryShape(Bounds bounds)
    {
        if (boundaryShape == null)
        {
            Transform existing = transform.Find(kBoundaryProxyName);
            GameObject shapeGo;
            if (existing != null)
            {
                shapeGo = existing.gameObject;
            }
            else
            {
                shapeGo = new GameObject(kBoundaryProxyName);
                shapeGo.transform.SetParent(transform, false);
            }
            boundaryShape = shapeGo.GetComponent<PolygonCollider2D>();
            if (boundaryShape == null)
                boundaryShape = shapeGo.AddComponent<PolygonCollider2D>();
            // 레이어 2(Ignore Raycast): 투사체 지형 마스크(0/3)와 기본 Raycast에 걸리지 않게
            shapeGo.layer = 2;
            boundaryShape.isTrigger = true;
        }

        if (boundaryShape.pathCount != 1)
            boundaryShape.pathCount = 1;

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        boundaryShape.SetPath(0, new Vector2[]
        {
            new Vector2(min.x, min.y),
            new Vector2(max.x, min.y),
            new Vector2(max.x, max.y),
            new Vector2(min.x, max.y)
        });

        // Confiner2D는 GetPath로 형상 데이터만 읽으므로 비활성 콜라이더도 인식한다.
        // 비활성 콜라이더는 레이어/마스크와 무관하게 레이캐스트·Overlap·트리거에 전혀 걸리지 않는다.
        boundaryShape.enabled = false;
    }

    void ApplyDialogueLens()
    {
        if (dialogueVcam == null) return;
        float sourceOrtho = baseOrthoSize;
        if (sourceOrtho <= 0.01f && followVcam != null)
            sourceOrtho = followVcam.m_Lens.OrthographicSize;
        if (sourceOrtho <= 0.01f)
            sourceOrtho = dialogueVcam.m_Lens.OrthographicSize;
        dialogueVcam.m_Lens.OrthographicSize = sourceOrtho / Mathf.Max(1f, dialogueZoomDivisor);
    }

    void ForceCutBlend()
    {
        if (brain != null)
            brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0f);
    }

    void StartRestoreBlendRoutine()
    {
        if (restoreBlendRoutine != null)
            StopCoroutine(restoreBlendRoutine);
        restoreBlendRoutine = StartCoroutine(RestoreBlendRoutine());
    }

    IEnumerator RestoreBlendRoutine()
    {
        // Brain이 우선순위 스왑/Cut 전환을 소비할 때까지 2프레임 대기 후 방 블렌드 복구
        yield return null;
        yield return null;
        restoreBlendRoutine = null;
        if (!inDialogue && brain != null)
            brain.m_DefaultBlend = roomBlend;
    }

    Camera GetOutputCamera()
    {
        if (brain != null && brain.OutputCamera != null) return brain.OutputCamera;
        return Camera.main;
    }

    void OnDrawGizmosSelected()
    {
        if (currentRoom != null)
        {
            Bounds roomBounds = currentRoom.Bounds;
            Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
            Gizmos.DrawCube(roomBounds.center, roomBounds.size);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(roomBounds.center, roomBounds.size);
        }

        if (hasAppliedBoundary)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(lastBoundaryBounds.center, lastBoundaryBounds.size);
        }

        if (lookAheadOffset != Vector2.zero)
        {
            Vector3 origin = playerTransform != null
                ? playerTransform.position
                : (lookAheadProxy != null ? lookAheadProxy.position : Vector3.zero);
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(origin, new Vector3(lookAheadOffset.x, lookAheadOffset.y, 0f));
        }
    }
}
