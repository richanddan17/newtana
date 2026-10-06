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
    [SerializeField] float respawnDelay = 1.5f; // 사망 연출 후 실제 부활까지 대기
    [SerializeField] float fadeDuration = 0.5f; // 페이드 인/아웃 시간
    [SerializeField] float respawnInvincibilityDuration = 1.5f; // 부활 후 무적 시간

    [Header("Respawn Safety (부활 위치 안전 규칙)")]
    [SerializeField] LayerMask respawnSafetyMask; // 0이면 검사 생략
    [SerializeField] float respawnSafetyRadius = 0.3f; // 플레이어 캡슐 근사 반경
    [SerializeField] float respawnSafetySearchHeight = 2f; // 막혔을 때 위로 탐색하는 높이

    [Header("Enemy Reset Policy")]
    [SerializeField] EnemyResetPolicy enemyResetPolicy = EnemyResetPolicy.CurrentRoomOnly;
    [SerializeField] bool resetPlayerAmmo = true; // false면 탄약 유지 (UseAmmo=true일 때)
    [SerializeField] bool clearEnemyProjectilesOnRespawn = true; // 남은 적 총알 정리

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
    int reserveAmmoBeforeDeath;

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
        reserveAmmoBeforeDeath = playerWeapon != null ? playerWeapon.CurrentReserveAmmo : 0;
        OnPlayerDied?.Invoke();
        OnRespawnStarted?.Invoke();

        // 사망 중에는 이동/사격 입력을 처리하지 않는다 (checkpoint-respawn-system.md [2])
        if (playerState != null)
            playerState.SetInputBlocked(true);

        if (logRespawnEvents)
            Debug.Log("[RespawnSystem] Player died, starting respawn sequence");

        // 리스폰 코루틴 시작
        respawnCoroutine = StartCoroutine(RespawnCoroutine());
    }

    System.Collections.IEnumerator RespawnCoroutine()
    {
        // 1. 페이드 아웃
        yield return StartCoroutine(FadeCoroutine(0f, 1f, fadeDuration));

        // 2. 사망 애니메이션과 실제 부활 시점 분리 (respawnDelay).
        // 히트스톱(Time.timeScale=0) 중에도 진행되도록 언스케일드 대기 사용
        if (respawnDelay > 0f)
            yield return new WaitForSecondsRealtime(respawnDelay);

        // 3. 부활 위치 결정 (막힌 위치 회피)
        Room targetRoom = GetTargetRoom();
        Vector3 respawnPosition = GetSafeRespawnPosition(GetRespawnPosition(), targetRoom);

        // 4. 플레이어 상태 초기화 (위치 이동 전)
        ResetPlayerState(respawnPosition);

        // 5. 적 리셋
        ResetEnemies(targetRoom);

        // 6. Room 먼저 갱신한 뒤 카메라 컷 이동 (이전 Room 경계 사용 방지)
        UpdateCameraAndRoom(respawnPosition, targetRoom);

        // 7. 부활 직후 무적 (페이드는 이미 완료된 상태)
        if (playerHealth != null)
        {
            playerHealth.GrantRespawnInvincibility(respawnInvincibilityDuration);
        }

        // 8. 페이드 인
        yield return StartCoroutine(FadeCoroutine(1f, 0f, fadeDuration));

        // 9. 사망 중 입력 잠금 해제
        if (playerState != null)
            playerState.SetInputBlocked(false);

        isRespawning = false;
        OnRespawnCompleted?.Invoke();

        if (logRespawnEvents)
            Debug.Log($"[RespawnSystem] Respawn completed at: {respawnPosition}");
    }

    Vector3 GetRespawnPosition()
    {
        if (currentCheckpoint != null)
            return currentCheckpoint.SpawnPosition;

        if (defaultSpawnPoint != null)
            return defaultSpawnPoint.position;

        // 프로젝트 기본 시작 위치 미지정 시 Room의 첫 체크포인트 → 현재 Room 중앙 → 플레이어 위치 순으로 대체.
        // 월드 좌표를 코드에 직접 쓰지 않는다 (공통 규약 7).
        if (roomManager != null)
        {
            Room room = roomManager.CurrentRoom;
            Checkpoint roomCheckpoint = room?.GetFirstCheckpoint();
            if (roomCheckpoint != null)
                return roomCheckpoint.SpawnPosition;

            if (room != null && room.Bounds.size != Vector3.zero)
                return room.Bounds.center;
        }

        Debug.LogWarning("[RespawnSystem] No checkpoint or default spawn point! Respawning at current position.");
        return playerMovement != null ? playerMovement.transform.position : transform.position;
    }

    /// <summary>
    /// 부활 위치 안전 규칙: 지형/적과 겹치면 위로 올려 빈 공간을 찾는다 (checkpoint-respawn-system.md [3]).
    /// </summary>
    Vector3 GetSafeRespawnPosition(Vector3 desired, Room room)
    {
        Vector3 position = desired;

        if (respawnSafetyMask.value == 0 || respawnSafetyRadius <= 0f)
            return position;

        Bounds bounds = room != null ? room.Bounds : new Bounds();
        float startY = position.y;
        float maxY = startY + respawnSafetySearchHeight;
        if (bounds.size != Vector3.zero)
            maxY = Mathf.Min(maxY, bounds.max.y);

        // respawnSafetyRadius 간격으로 위치를 올려 빈 공간 탐색
        for (float y = startY; y <= maxY; y += respawnSafetyRadius)
        {
            Vector3 probe = new Vector3(position.x, y, position.z);
            if (Physics2D.OverlapCircle(probe, respawnSafetyRadius, respawnSafetyMask) == null)
                return probe;
        }

        return new Vector3(position.x, maxY, position.z);
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

    void ResetPlayerState(Vector3 position)
    {
        Transform playerTransform = playerMovement != null
            ? playerMovement.transform
            : playerHealth != null ? playerHealth.transform : null;

        // Health.disableOnDeath로 꺼져 있을 수 있으므로 부활 전에 다시 켠다.
        if (playerTransform != null && !playerTransform.gameObject.activeSelf)
            playerTransform.gameObject.SetActive(true);

        // 위치 이동 + Rigidbody2D 속도 초기화 (checkpoint-respawn-system.md [4])
        if (playerMovement != null)
        {
            playerMovement.SetVelocity(Vector2.zero, true, true);
        }

        if (playerTransform != null)
        {
            var rb = playerTransform.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.position = position;
            }
            else
            {
                playerTransform.position = position;
            }
        }

        // HP와 무적 상태 초기화
        if (playerHealth != null)
        {
            playerHealth.ResetHealth();
        }

        // 이동 상태, 입력 버퍼, 대시 상태 초기화
        if (playerState != null)
        {
            playerState.ResetState();
        }

        // 사격 상태 (연사 쿨다운, 재장전 진행) 초기화
        if (playerWeapon != null)
        {
            playerWeapon.ResetWeaponState();

            // 탄약 유지 정책: ResetWeaponState가 탄약을 채우므로,
            // 유지가 필요하면 사기 전 잔량으로 되돌린다.
            if (!resetPlayerAmmo && playerWeapon.CurrentWeapon != null && playerWeapon.CurrentWeapon.UseAmmo)
            {
                int restoreReserve = Mathf.Max(0, reserveAmmoBeforeDeath - playerWeapon.CurrentReserveAmmo);
                if (restoreReserve > 0)
                    playerWeapon.AddAmmo(restoreReserve);
            }
        }
    }

    void ResetEnemies(Room targetRoom)
    {
        if (clearEnemyProjectilesOnRespawn)
            ClearEnemyProjectiles();

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

    /// <summary>남아 있는 적 총알을 풀로 반환한다 (checkpoint-respawn-system.md [5]).</summary>
    void ClearEnemyProjectiles()
    {
        if (ProjectilePool.Instance == null) return;

        var projectiles = FindObjectsByType<ProjectileData>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var projectile in projectiles)
        {
            if (projectile != null && projectile.OwnerTeam == Team.Enemy)
                ProjectilePool.Instance.Return(projectile);
        }
    }

    void UpdateCameraAndRoom(Vector3 respawnPosition, Room targetRoom)
    {
        // Room 갱신을 먼저 해야 RoomCamera가 새 경계로 클램프한다.
        // 반대 순서면 이전 Room 경계로 클램프되어 카메라가 엉뚱한 위치에 남는다.
        if (roomManager != null && targetRoom != null)
        {
            roomManager.ForceSetCurrentRoom(targetRoom);
        }

        // RoomCamera 즉시 이동 (컷 전환)
        if (roomCamera != null)
        {
            roomCamera.OnPlayerRespawn(respawnPosition);
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