using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 부활/리스폰 시스템 - 사망 처리, 체크포인트 부활, 상태 초기화, 적 리셋.
/// 계획서 checkpoint-respawn-system.md [2]~[6] 반영.
/// - 사망 → 부활 연출 → 상태 초기화 → 적 리셋
/// - Room 시스템과 연동 (부활 시 Room/카메라 갱신)
/// - 영구 진행 상태 구분 (획득 무기, 업그레이드 등 유지)
/// </summary>
[DisallowMultipleComponent]
public class RespawnSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Health playerHealth;
    [SerializeField] PlayerStateController playerState;
    [SerializeField] PlayerMovement playerMovement;
    [SerializeField] WeaponController playerWeapon;
    [SerializeField] RoomCamera roomCamera;
    [SerializeField] RoomManager roomManager;

    [Header("Respawn Settings")]
    [SerializeField] Transform defaultSpawnPoint; // 체크포인트 없을 때 기본 시작 위치
    [SerializeField] float respawnDelay = 1.5f; // 사망 ~ 부활 연출 시간
    [SerializeField] float fadeDuration = 0.5f; // 페이드 인/아웃 시간
    [SerializeField] float respawnInvincibilityDuration = 1.5f; // 부활 후 무적 시간

    [Header("Enemy Reset Policy")]
    [SerializeField] EnemyResetPolicy enemyResetPolicy = EnemyResetPolicy.CurrentRoomOnly;
    [SerializeField] bool resetPlayerAmmo = true; // 탄약 리셋 (UseAmmo=true일 때)
    [SerializeField] bool keepPermanentProgress = true; // 영구 진행 유지

    [Header("Visual Feedback")]
    [SerializeField] UnityEngine.UI.Image fadeImage; // 화면 페이드용 이미지 (Canvas에 배치)
    [SerializeField] Color fadeColor = Color.black;
    [SerializeField] AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Debug")]
    [SerializeField] bool logRespawnEvents = true;

    // 상태
    Checkpoint currentCheckpoint;
    bool isRespawning = false;
    Coroutine respawnCoroutine;

    // 이벤트
    public event System.Action<Checkpoint> OnCheckpointChanged;
    public event System.Action OnRespawnStarted;
    public event System.Action OnRespawnCompleted;
    public event System.Action OnPlayerDied;

    public enum EnemyResetPolicy
    {
        None,               // 적 리셋 안 함 (죽은 적 유지)
        CurrentRoomOnly,    // 현재 Room의 적만 리셋 (기본)
        AllRooms,           // 모든 Room의 적 리셋
        KilledOnlyKeep      // 처치한 적은 유지, 살아있는 적만 리셋
    }

    public Checkpoint CurrentCheckpoint => currentCheckpoint;
    public bool IsRespawning => isRespawning;

    void Awake()
    {
        // 자동 참조
        if (playerHealth == null) playerHealth = FindAnyObjectByType<Health>();
        if (playerState == null) playerState = FindAnyObjectByType<PlayerStateController>();
        if (playerMovement == null) playerMovement = FindAnyObjectByType<PlayerMovement>();
        if (playerWeapon == null) playerWeapon = FindAnyObjectByType<WeaponController>();
        if (roomCamera == null) roomCamera = FindAnyObjectByType<RoomCamera>();
        if (roomManager == null) roomManager = FindAnyObjectByType<RoomManager>();

        // Health 사망 이벤트 구독
        if (playerHealth != null)
        {
            playerHealth.OnDied += OnPlayerDeath;
        }
    }

    void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDied -= OnPlayerDeath;
        }
    }

    /// <summary>체크포인트 설정 (Checkpoint에서 호출)</summary>
    public void SetCurrentCheckpoint(Checkpoint checkpoint)
    {
        if (currentCheckpoint == checkpoint) return;

        // 이전 체크포인트 해제
        if (currentCheckpoint != null)
        {
            currentCheckpoint.DeactivateAsCurrent();
        }

        currentCheckpoint = checkpoint;
        
        if (currentCheckpoint != null)
        {
            currentCheckpoint.SetAsCurrent();
        }

        OnCheckpointChanged?.Invoke(currentCheckpoint);

        if (logRespawnEvents)
            Debug.Log($"[RespawnSystem] Checkpoint changed: {currentCheckpoint?.CheckpointName ?? "None"}");
    }

    /// <summary>체크포인트 가져오기 (부활 위치용)</summary>
    public Checkpoint GetCurrentCheckpoint() => currentCheckpoint;

    void OnPlayerDeath()
    {
        if (isRespawning) return; // 이미 리스폰 중이면 무시

        isRespawning = true;
        OnPlayerDied?.Invoke();

        if (logRespawnEvents)
            Debug.Log("[RespawnSystem] Player died, starting respawn sequence");

        // 리스폰 코루틴 시작
        respawnCoroutine = StartCoroutine(RespawnCoroutine());
    }

    System.Collections.IEnumerator RespawnCoroutine()
    {
        // 1. 페이드 아웃
        yield return StartCoroutine(FadeCoroutine(0f, 1f, fadeDuration));

        // 2. 부활 위치 결정
        Vector3 respawnPosition = GetRespawnPosition();
        Room targetRoom = GetTargetRoom();

        // 3. 플레이어 상태 초기화 (위치 이동 전)
        ResetPlayerState(respawnPosition, targetRoom);

        // 4. 적 리셋
        ResetEnemies(targetRoom);

        // 5. 카메라/Room 갱신
        UpdateCameraAndRoom(respawnPosition, targetRoom);

        // 6. 페이드 인
        yield return StartCoroutine(FadeCoroutine(1f, 0f, fadeDuration));

        // 7. 부활 후 무적 부여
        if (playerHealth != null)
        {
            playerHealth.GrantRespawnInvincibility(respawnInvincibilityDuration);
        }

        isRespawning = false;
        OnRespawnCompleted?.Invoke();

        if (logRespawnEvents)
            Debug.Log($"[RespawnSystem] Respawn completed at: {respawnPosition}");
    }

    Vector3 GetRespawnPosition()
    {
        if (currentCheckpoint != null)
        {
            return currentCheckpoint.SpawnPosition;
        }
        else if (defaultSpawnPoint != null)
        {
            return defaultSpawnPoint.position;
        }
        else
        {
            // 최후 수단: (0,0,0)
            Debug.LogWarning("[RespawnSystem] No checkpoint or default spawn point! Using origin.");
            return Vector3.zero;
        }
    }

    Room GetTargetRoom()
    {
        if (currentCheckpoint != null && currentCheckpoint.ParentRoom != null)
        {
            return currentCheckpoint.ParentRoom;
        }
        else if (roomManager != null && roomManager.CurrentRoom != null)
        {
            return roomManager.CurrentRoom;
        }
        return null;
    }

    void ResetPlayerState(Vector3 position, Room targetRoom)
    {
        // 위치 이동
        if (playerMovement != null)
        {
            playerMovement.SetVelocity(Vector2.zero, true, true);
            playerMovement.transform.position = position;
        }
        else
        {
            var rb = playerHealth?.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.position = position;
            }
        }

        // Health/State 리셋
        if (playerHealth != null)
        {
            playerHealth.ResetHealth();
        }

        if (playerState != null)
        {
            playerState.ResetState();
        }

        // 무기 상태 리셋 (탄약 포함)
        if (playerWeapon != null)
        {
            playerWeapon.ResetWeaponState();
            
            // 탄약 리셋 옵션
            if (resetPlayerAmmo && playerWeapon.CurrentWeapon != null && playerWeapon.CurrentWeapon.UseAmmo)
            {
                // ResetWeaponState에서 이미 처리됨
            }
        }

        // 영구 진행 상태는 유지 (무기 슬롯, 업그레이드 등)
        // keepPermanentProgress = true면 여기서 아무것도 안 함
    }

    void ResetEnemies(Room targetRoom)
    {
        if (enemyResetPolicy == EnemyResetPolicy.None) return;

        switch (enemyResetPolicy)
        {
            case EnemyResetPolicy.CurrentRoomOnly:
                if (targetRoom != null)
                {
                    targetRoom.ResetEnemies();
                }
                break;

            case EnemyResetPolicy.AllRooms:
                if (roomManager != null)
                {
                    var allRooms = roomManager.GetAllRooms();
                    foreach (var room in allRooms)
                    {
                        room.ResetEnemies();
                    }
                }
                break;

            case EnemyResetPolicy.KilledOnlyKeep:
                // 죽은 적은 이미 비활성화되어 있음 (EnemyHealth에서 SetActive(false))
                // 살아있는 적만 리셋
                if (roomManager != null)
                {
                    var allRooms = roomManager.GetAllRooms();
                    foreach (var room in allRooms)
                    {
                        foreach (var enemyObj in room.Enemies)
                        {
                            if (enemyObj == null) continue;
                            var health = enemyObj.GetComponent<EnemyHealth>();
                            if (health != null && !health.IsDead)
                            {
                                health.ResetHealth();
                                enemyObj.SetActive(true);
                            }
                        }
                    }
                }
                break;
        }
    }

    void UpdateCameraAndRoom(Vector3 respawnPosition, Room targetRoom)
    {
        // RoomCamera 즉시 이동
        if (roomCamera != null)
        {
            roomCamera.OnPlayerRespawn(respawnPosition);
        }

        // RoomManager 현재 Room 갱신
        if (roomManager != null && targetRoom != null)
        {
            roomManager.ForceSetCurrentRoom(targetRoom);
        }
    }

    System.Collections.IEnumerator FadeCoroutine(float from, float to, float duration)
    {
        if (fadeImage == null) yield break;

        float timer = 0f;
        fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, from);
        fadeImage.gameObject.SetActive(true);

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime; // 언스케일드 타임 (히트스톱 중에도 페이드)
            float t = fadeCurve.Evaluate(timer / duration);
            float alpha = Mathf.Lerp(from, to, t);
            fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, alpha);
            yield return null;
        }

        fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, to);
        
        if (to == 0f)
        {
            fadeImage.gameObject.SetActive(false);
        }
    }

    /// <summary>게임 시작 시 기본 체크포인트 설정</summary>
    public void SetDefaultCheckpoint(Checkpoint checkpoint)
    {
        if (currentCheckpoint == null)
        {
            SetCurrentCheckpoint(checkpoint);
        }
    }

    /// <summary>강제 리스폰 (테스트/디버그용)</summary>
    [ContextMenu("Force Respawn")]
    public void ForceRespawn()
    {
        if (!isRespawning)
        {
            OnPlayerDeath();
        }
    }

    /// <summary>영구 진행 상태 데이터 (향후 확장용)</summary>
    [System.Serializable]
    public class PermanentProgress
    {
        public List<string> unlockedWeapons = new();
        public List<string> acquiredUpgrades = new();
        public int maxHealthUpgrades = 0;
        // 필요시 확장
    }

    PermanentProgress permanentProgress = new();

    public PermanentProgress GetPermanentProgress() => permanentProgress;

    public void AddPermanentWeapon(string weaponId)
    {
        if (!permanentProgress.unlockedWeapons.Contains(weaponId))
            permanentProgress.unlockedWeapons.Add(weaponId);
    }

    public void AddPermanentUpgrade(string upgradeId)
    {
        if (!permanentProgress.acquiredUpgrades.Contains(upgradeId))
            permanentProgress.acquiredUpgrades.Add(upgradeId);
    }
}