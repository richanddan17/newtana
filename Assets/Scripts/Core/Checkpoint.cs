using UnityEngine;

/// <summary>
/// 체크포인트 - 플레이어 접촉 시 활성화, 부활 위치 저장.
/// 계획서 checkpoint-respawn-system.md [1], [3] 반영.
/// - 월드 좌표 하드코딩 금지 (Transform 사용)
/// - Room과 연결 (부활 시 Room 갱신용)
/// - 활성화 피드백 (이펙트, 사운드)
/// </summary>
[DisallowMultipleComponent]
public class Checkpoint : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] string checkpointId;
    [SerializeField] string checkpointName;

    [Header("References")]
    [SerializeField] Room parentRoom; // 속한 Room (자동 할당)
    [SerializeField] Transform spawnPoint; // 부활 위치 (자식 Transform 권장)

    [Header("Activation")]
    [SerializeField] bool activateOnTouch = true; // 플레이어 접촉 시 자동 활성화
    [SerializeField] bool requireGrounded = false; // 지상에 있을 때만 활성화
    [SerializeField] LayerMask playerLayerMask; // 0이면 태그만으로 판정

    [Header("Feedback")]
    [SerializeField] GameObject activationEffectPrefab; // 활성화 시 이펙트
    [SerializeField] AudioClip activationSound; // 활성화 사운드
    [SerializeField] SpriteRenderer spriteRenderer; // 비주얼 변경용
    [SerializeField] Color inactiveColor = Color.gray;
    [SerializeField] Color activeColor = Color.cyan;
    [SerializeField] Color savedColor = Color.green; // 이미 저장된 체크포인트

    [Header("Settings")]
    [SerializeField] bool oneTimeActivation = true; // 한 번만 활성화 가능
    [SerializeField] float activationCooldown = 1f; // 재활성화 쿨다운

    // 상태
    bool isActivated = false;
    bool isCurrentCheckpoint = false;
    float lastActivationTime = -999f;
    RespawnSystem respawnSystem;

    // 이벤트
    public event System.Action<Checkpoint> OnActivated;
    public event System.Action<Checkpoint> OnDeactivated;
    public event System.Action<Checkpoint> OnBecameCurrent;
    public event System.Action<Checkpoint> OnLostCurrent;

    // 프로퍼티
    public string CheckpointId => checkpointId;
    public string CheckpointName => checkpointName;
    public Room ParentRoom => parentRoom;
    public Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : transform.position;
    public bool IsActivated => isActivated;
    public bool IsCurrentCheckpoint => isCurrentCheckpoint;

    void Awake()
    {
        if (string.IsNullOrEmpty(checkpointId))
            checkpointId = gameObject.name;

        if (spawnPoint == null)
            spawnPoint = transform; // 자기 자신 위치 사용

        // 부모 Room 자동 찾기
        if (parentRoom == null)
            parentRoom = GetComponentInParent<Room>();

        if (parentRoom == null)
            Debug.LogWarning($"[Checkpoint] '{checkpointName}'에 연결된 Room이 없습니다. 부활 시 현재 Room을 유지합니다.", this);

        // 스프라이트 렌더러 자동 찾기
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        respawnSystem = FindAnyObjectByType<RespawnSystem>();

        UpdateVisual();
    }

    void OnValidate()
    {
        if (spawnPoint == null)
            spawnPoint = transform;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!activateOnTouch) return;
        if (!other.CompareTag("Player")) return;

        if (playerLayerMask.value != 0 && (playerLayerMask.value & (1 << other.gameObject.layer)) == 0)
            return;

        if (Time.time - lastActivationTime < activationCooldown) return;

        // Grounded 체크 (옵션)
        if (requireGrounded)
        {
            var movement = other.GetComponent<PlayerMovement>();
            if (movement != null && !movement.IsGrounded) return;
        }

        Activate();
    }

    /// <summary>체크포인트 활성화 (자동/수동)</summary>
    public void Activate()
    {
        if (isActivated && oneTimeActivation) return;

        isActivated = true;
        lastActivationTime = Time.time;
        
        // 현재 체크포인트로 등록 (활성 체크포인트는 RespawnSystem이 단일 출처로 관리)
        if (respawnSystem == null)
            respawnSystem = FindAnyObjectByType<RespawnSystem>();

        if (respawnSystem != null)
        {
            respawnSystem.SetCurrentCheckpoint(this);
        }

        // 피드백
        PlayActivationFeedback();
        UpdateVisual();

        OnActivated?.Invoke(this);
        Debug.Log($"[Checkpoint] Activated: {checkpointName}");
    }

    /// <summary>현재 체크포인트 해제 (다른 체크포인트 활성화 시)</summary>
    public void DeactivateAsCurrent()
    {
        if (!isCurrentCheckpoint) return;

        isCurrentCheckpoint = false;
        UpdateVisual();
        OnLostCurrent?.Invoke(this);
        OnDeactivated?.Invoke(this);
    }

    /// <summary>현재 체크포인트로 설정</summary>
    public void SetAsCurrent()
    {
        isCurrentCheckpoint = true;
        UpdateVisual();
        OnBecameCurrent?.Invoke(this);
    }

    void PlayActivationFeedback()
    {
        // 이펙트
        if (activationEffectPrefab != null)
        {
            Instantiate(activationEffectPrefab, transform.position, Quaternion.identity);
        }

        // 사운드
        if (activationSound != null)
        {
            AudioSource.PlayClipAtPoint(activationSound, transform.position);
        }
    }

    void UpdateVisual()
    {
        if (spriteRenderer == null) return;

        if (isCurrentCheckpoint)
            spriteRenderer.color = savedColor;
        else if (isActivated)
            spriteRenderer.color = activeColor;
        else
            spriteRenderer.color = inactiveColor;
    }

    /// <summary>체크포인트 완전 리셋 (새 게임 시작 시)</summary>
    public void ResetCheckpoint()
    {
        isActivated = false;
        isCurrentCheckpoint = false;
        lastActivationTime = -999f;
        UpdateVisual();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = isActivated ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);

        if (spawnPoint != null && spawnPoint != transform)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, spawnPoint.position);
            Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
        }

        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, $"CP: {checkpointName}");
    }
}