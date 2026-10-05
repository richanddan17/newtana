using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Room 매니저 - 현재 Room 관리, 전환, 적 활성화/비활성화.
/// 계획서 room-camera-system.md [4], [5], checkpoint-respawn-system.md [6] 반영.
/// - 씬의 모든 Room 수집
/// - 플레이어 위치 기반 현재 Room 결정
/// - Room 전환 이벤트 발생
/// - 적 활성화/비활성화 정책 관리
/// </summary>
[DisallowMultipleComponent]
public class RoomManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] RoomCamera roomCamera;
    [SerializeField] RespawnSystem respawnSystem;

    [Header("Settings")]
    [SerializeField] bool autoFindRooms = true;
    [SerializeField] bool debugLogTransitions = true;

    // 상태
    Room currentRoom;
    List<Room> allRooms = new();

    // 이벤트
    public event System.Action<Room, Room> OnRoomChanged; // newRoom, oldRoom

    public Room CurrentRoom => currentRoom;
    public IReadOnlyList<Room> AllRooms => allRooms;

    void Awake()
    {
        if (autoFindRooms)
        {
            FindAllRooms();
        }

        // Room 이벤트 구독
        foreach (var room in allRooms)
        {
            room.OnPlayerEntered += OnRoomPlayerEntered;
            room.OnPlayerExited += OnRoomPlayerExited;
        }

        // 초기 Room 결정 (플레이어 위치 기반)
        DetermineInitialRoom();
    }

    void OnDestroy()
    {
        foreach (var room in allRooms)
        {
            room.OnPlayerEntered -= OnRoomPlayerEntered;
            room.OnPlayerExited -= OnRoomPlayerExited;
        }
    }

    void FindAllRooms()
    {
        allRooms.Clear();
        var rooms = FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var room in rooms)
        {
            allRooms.Add(room);
        }

        if (debugLogTransitions)
            Debug.Log($"[RoomManager] Found {allRooms.Count} rooms");
    }

    void DetermineInitialRoom()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        Vector2 playerPos = player.transform.position;
        currentRoom = null;

        // 플레이어가 속한 Room 찾기
        foreach (var room in allRooms)
        {
            if (room.ContainsPoint(playerPos))
            {
                currentRoom = room;
                break;
            }
        }

        // 못 찾으면 첫 번째 Room
        if (currentRoom == null && allRooms.Count > 0)
        {
            currentRoom = allRooms[0];
        }

        if (currentRoom != null)
        {
            if (debugLogTransitions)
                Debug.Log($"[RoomManager] Initial room: {currentRoom.RoomName}");

            // RoomCamera에 알림
            if (roomCamera != null)
            {
                // RoomCamera 내부에서 OnRoomChanged 호출됨
            }
        }
    }

    void OnRoomPlayerEntered(Room room)
    {
        if (room == currentRoom) return;

        Room oldRoom = currentRoom;
        currentRoom = room;

        if (debugLogTransitions)
            Debug.Log($"[RoomManager] Room changed: {oldRoom?.RoomName ?? "None"} -> {currentRoom.RoomName}");

        OnRoomChanged?.Invoke(currentRoom, oldRoom);

        // RespawnSystem에 현재 Room 알림 (부활 시 사용)
        if (respawnSystem != null)
        {
            // RespawnSystem 내부에서 현재 Room 참조
        }
    }

    void OnRoomPlayerExited(Room room)
    {
        // Room 이탈은 Room 진입 시 처리되므로 여기선 별도 처리 안 함
        // (플레이어가 다른 Room에 진입하면 OnRoomPlayerEntered 호출됨)
    }

    /// <summary>외부에서 강제 Room 설정 (부활 시 등)</summary>
    public void ForceSetCurrentRoom(Room room)
    {
        if (room == currentRoom) return;

        Room oldRoom = currentRoom;
        currentRoom = room;

        if (debugLogTransitions)
            Debug.Log($"[RoomManager] Force room set: {oldRoom?.RoomName ?? "None"} -> {currentRoom.RoomName}");

        OnRoomChanged?.Invoke(currentRoom, oldRoom);
    }

    /// <summary>특정 위치가 속한 Room 찾기</summary>
    public Room GetRoomAtPosition(Vector2 position)
    {
        foreach (var room in allRooms)
        {
            if (room.ContainsPoint(position))
                return room;
        }
        return null;
    }

    /// <summary>Room 이름으로 찾기</summary>
    public Room GetRoomById(string roomId)
    {
        return allRooms.Find(r => r.RoomId == roomId);
    }

    /// <summary>모든 Room 반환 (적 리셋용)</summary>
    public List<Room> GetAllRooms() => new List<Room>(allRooms);

    /// <summary>현재 Room의 적들 활성화</summary>
    public void ActivateCurrentRoomEnemies()
    {
        if (currentRoom != null)
        {
            // Room의 ActivateEnemies는 진입 시 자동 호출되지만 강제 호출용
            var enemies = currentRoom.Enemies;
            foreach (var enemyObj in enemies)
            {
                if (enemyObj == null) continue;
                var health = enemyObj.GetComponent<EnemyHealth>();
                if (health != null && !health.IsDead)
                {
                    enemyObj.SetActive(true);
                }
            }
        }
    }

    /// <summary>현재 Room의 적들 비활성화</summary>
    public void DeactivateCurrentRoomEnemies()
    {
        if (currentRoom != null)
        {
            var enemies = currentRoom.Enemies;
            foreach (var enemyObj in enemies)
            {
                if (enemyObj == null) continue;
                var health = enemyObj.GetComponent<EnemyHealth>();
                if (health != null && !health.IsDead)
                {
                    enemyObj.SetActive(false);
                }
            }
        }
    }

    /// <summary>에디터에서 Room 재탐색</summary>
    [ContextMenu("Refresh Rooms")]
    public void RefreshRooms()
    {
        FindAllRooms();
    }

    void OnDrawGizmosSelected()
    {
        foreach (var room in allRooms)
        {
            if (room == currentRoom)
            {
                Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            }
            else
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            }
            
            if (room.Bounds.size != Vector3.zero)
            {
                Gizmos.DrawCube(room.Bounds.center, room.Bounds.size);
            }
        }
    }
}