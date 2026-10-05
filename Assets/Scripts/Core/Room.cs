using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Room 정의 - 카메라 경계와 적/오브젝트 상태를 묶는 단위.
/// 계획서 room-camera-system.md [1], [5] 반영.
/// - Room 영역은 Collider/Bounds로 정의 (좌표 하드코딩 금지)
/// - Room 진입/이탈 이벤트로 적 활성화/비활성화 관리
/// </summary>
[DisallowMultipleComponent]
public class Room : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] string roomId;
    [SerializeField] string roomName;

    [Header("Bounds (카메라 경계)")]
    [SerializeField] Collider2D boundsCollider; // BoxCollider2D 권장, Trigger=true
    [SerializeField] bool autoCalculateBounds = true;

    [Header("Contents")]
    [SerializeField] List<GameObject> roomEnemies = new(); // 이 Room에 속한 적들
    [SerializeField] List<GameObject> roomObjects = new(); // 상호작용 오브젝트 등
    [SerializeField] List<Checkpoint> roomCheckpoints = new(); // 이 Room의 체크포인트

    [Header("Settings")]
    [SerializeField] bool activateEnemiesOnEnter = true; // 진입 시 적 활성화
    [SerializeField] bool deactivateEnemiesOnExit = true; // 이탈 시 적 비활성화 (성능)
    [SerializeField] float enemyActivationDelay = 0.2f; // 활성화 지연 (연출용)

    // 상태
    bool isPlayerInside = false;
    int playersInsideCount = 0; // 멀티플레이어 대비

    // 이벤트
    public event System.Action<Room> OnPlayerEntered;
    public event System.Action<Room> OnPlayerExited;
    public event System.Action<Room> OnRoomActivated;
    public event System.Action<Room> OnRoomDeactivated;

    // 프로퍼티
    public string RoomId => roomId;
    public string RoomName => roomName;
    public Bounds Bounds => boundsCollider ? boundsCollider.bounds : new Bounds(transform.position, Vector3.zero);
    public Vector2 Center => Bounds.center;
    public Vector2 Size => Bounds.size;
    public bool IsPlayerInside => isPlayerInside;
    public IReadOnlyList<GameObject> Enemies => roomEnemies;
    public IReadOnlyList<Checkpoint> Checkpoints => roomCheckpoints;

    void Awake()
    {
        // Room ID 자동 생성 (이름 기반)
        if (string.IsNullOrEmpty(roomId))
            roomId = gameObject.name;

        // Bounds Collider 설정
        if (boundsCollider == null)
            boundsCollider = GetComponent<Collider2D>();

        if (boundsCollider != null)
        {
            boundsCollider.isTrigger = true; // 반드시 Trigger
        }

        // 자식에서 적/체크포인트 자동 수집
        if (autoCalculateBounds)
            CollectContents();
    }

    void OnValidate()
    {
        if (boundsCollider != null)
            boundsCollider.isTrigger = true;
    }

    void CollectContents()
    {
        roomEnemies.Clear();
        roomObjects.Clear();
        roomCheckpoints.Clear();

        // 자식에서 EnemyAI, Checkpoint 찾기
        var enemies = GetComponentsInChildren<EnemyAI>(true);
        foreach (var e in enemies) roomEnemies.Add(e.gameObject);

        var checkpoints = GetComponentsInChildren<Checkpoint>(true);
        foreach (var cp in checkpoints) roomCheckpoints.Add(cp);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        playersInsideCount++;
        if (playersInsideCount == 1)
        {
            OnPlayerEnter();
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        playersInsideCount = Mathf.Max(0, playersInsideCount - 1);
        if (playersInsideCount == 0)
        {
            OnPlayerExit();
        }
    }

    void OnPlayerEnter()
    {
        isPlayerInside = true;
        
        // 적 활성화
        if (activateEnemiesOnEnter)
        {
            ActivateEnemies();
        }

        OnPlayerEntered?.Invoke(this);
        OnRoomActivated?.Invoke(this);

        Debug.Log($"[Room] Player entered: {roomName}");
    }

    void OnPlayerExit()
    {
        isPlayerInside = false;

        // 적 비활성화 (성능)
        if (deactivateEnemiesOnExit)
        {
            DeactivateEnemies();
        }

        OnPlayerExited?.Invoke(this);
        OnRoomDeactivated?.Invoke(this);

        Debug.Log($"[Room] Player exited: {roomName}");
    }

    void ActivateEnemies()
    {
        foreach (var enemyObj in roomEnemies)
        {
            if (enemyObj == null) continue;
            
            var ai = enemyObj.GetComponent<EnemyAI>();
            var health = enemyObj.GetComponent<EnemyHealth>();
            
            if (ai != null && health != null && !health.IsDead)
            {
                enemyObj.SetActive(true);
                // AI가 이미 활성화되어 있으면 상태 유지, 비활성화되어 있었으면 순찰/대기 시작
            }
        }
    }

    void DeactivateEnemies()
    {
        foreach (var enemyObj in roomEnemies)
        {
            if (enemyObj == null) continue;
            
            var health = enemyObj.GetComponent<EnemyHealth>();
            // 살아있는 적만 비활성화 (죽은 적은 이미 비활성화됨)
            if (health != null && !health.IsDead)
            {
                enemyObj.SetActive(false);
            }
        }
    }

    /// <summary>Room 내 모든 적 리셋 (체크포인트 부활 시)</summary>
    public void ResetEnemies()
    {
        foreach (var enemyObj in roomEnemies)
        {
            if (enemyObj == null) continue;
            
            var health = enemyObj.GetComponent<EnemyHealth>();
            var ai = enemyObj.GetComponent<EnemyAI>();
            
            if (health != null)
            {
                health.ResetHealth();
            }
            if (ai != null)
            {
                // AI 상태 리셋 (순찰/대기로)
                // ai.ResetState(); // EnemyAI에 메서드 추가 필요
            }
            
            enemyObj.SetActive(true);
        }
    }

    /// <summary>이 Room의 첫 번째 체크포인트 반환</summary>
    public Checkpoint GetFirstCheckpoint()
    {
        return roomCheckpoints.Count > 0 ? roomCheckpoints[0] : null;
    }

    /// <summary>특정 위치가 Room 내부에 있는지 확인</summary>
    public bool ContainsPoint(Vector2 point)
    {
        return Bounds.Contains(point);
    }

    /// <summary>편집기에서 내용물 재수집</summary>
    [ContextMenu("Collect Contents")]
    public void EditorCollectContents()
    {
        CollectContents();
    }

    void OnDrawGizmosSelected()
    {
        if (boundsCollider != null)
        {
            Gizmos.color = isPlayerInside ? new Color(0f, 1f, 0f, 0.3f) : new Color(0f, 0.5f, 1f, 0.2f);
            Gizmos.DrawCube(Bounds.center, Bounds.size);
            
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(Bounds.center, Bounds.size);
        }

        // Room ID 표시
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, $"Room: {roomId}");
    }
}