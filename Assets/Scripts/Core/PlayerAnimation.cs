using UnityEngine;

/// <summary>
/// 플레이어 애니메이션 상태 시스템.
/// 계획서 animation-state-system.md [1]~[9] 반영.
/// - Animator 파라미터만 설정 (로직과 분리)
/// - 사격 오버레이 (통짜 스프라이트: 합본 클립 / 상체분리: 레이어)
/// - 조준 방향 연동 (AimDir)
/// - Muzzle 위치 동기화
/// - Hurt/Death 트리거
/// - 방향 전환 (Collider/Muzzle 포함)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class PlayerAnimation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerStateController stateController;
    [SerializeField] PlayerMovement movement;
    [SerializeField] Animator animator;
    [SerializeField] SpriteRenderer spriteRenderer;

    [Header("Animator Parameters (이름 일치 필요)")]
    [SerializeField] string speedParam = "Speed";           // float: 수평 속도 절댓값
    [SerializeField] string velocityYParam = "VelocityY";   // float: 수직 속도
    [SerializeField] string groundedParam = "IsGrounded";   // bool
    [SerializeField] string shootingParam = "IsShooting";   // bool: 사격 오버레이
    [SerializeField] string aimDirParam = "AimDir";         // int: 0=정면, 1=위, 2=대각위, 3=아래, 4=대각아래
    [SerializeField] string dashingParam = "IsDashing";     // bool
    [SerializeField] string hurtTrigger = "Hurt";           // trigger
    [SerializeField] string deadParam = "IsDead";           // bool
    [SerializeField] string reloadingParam = "IsReloading"; // bool

    [Header("Sprite Structure")]
    [SerializeField] bool upperBodySeparated = false; // true: 상체/하체 분리(Animator Layer), false: 통짜(합본 클립)

    [Header("Aim Direction (8방향 → 애니매션 5구역 매핑)")]
    [SerializeField] bool useAimDirection = true;
    [Tooltip("정면(→), 위(↑), 대각위(↗), 아래(↓), 대각아래(↘) 순서")]
    [SerializeField] string[] aimDirectionNames = { "Forward", "Up", "UpDiagonal", "Down", "DownDiagonal" };

    [Header("Muzzle Sync")]
    [SerializeField] Transform muzzleTransform; // 총구 위치 (방향별 오프셋 적용)
    [SerializeField] Vector2 muzzleOffsetForward = new Vector2(0.5f, 0.1f);
    [SerializeField] Vector2 muzzleOffsetUp = new Vector2(0.1f, 0.6f);
    [SerializeField] Vector2 muzzleOffsetUpDiagonal = new Vector2(0.35f, 0.4f);
    [SerializeField] Vector2 muzzleOffsetDown = new Vector2(0.5f, -0.3f);
    [SerializeField] Vector2 muzzleOffsetDownDiagonal = new Vector2(0.35f, -0.2f);

    [Header("Direction Flip")]
    [SerializeField] bool flipByScale = true; // true: Transform.scale.x 반전, false: SpriteRenderer.flipX
    [SerializeField] bool flipMuzzleWithScale = true;

    [Header("Events (외부 연결용)")]
    public event Action<int> OnAimDirectionChanged; // aimDir index
    public event Action<bool> OnShootingChanged;    // shooting active
    public event Action OnHurtAnimationStart;
    public event Action OnDeathAnimationStart;

    // 상태 캐시
    int currentAimDirIndex = 0;
    bool wasShooting = false;
    bool wasGrounded = true;
    float lastVelocityY = 0f;

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (stateController == null) stateController = GetComponent<PlayerStateController>();
        if (movement == null) movement = GetComponent<PlayerMovement>();

        // StateController 이벤트 구독
        if (stateController != null)
        {
            PlayerStateEvents.OnMoveStateChanged += OnMoveStateChanged;
            PlayerStateEvents.OnOverlayChanged += OnOverlayChanged;
        }

        // Movement 이벤트 구독
        if (movement != null)
        {
            // GroundCheck 이벤트는 Movement를 통해 간접 확인
        }
    }

    void OnDestroy()
    {
        if (stateController != null)
        {
            PlayerStateEvents.OnMoveStateChanged -= OnMoveStateChanged;
            PlayerStateEvents.OnOverlayChanged -= OnOverlayChanged;
        }
    }

    void LateUpdate()
    {
        if (animator == null) return;

        UpdateAnimatorParameters();
        UpdateAimDirection();
        UpdateMuzzlePosition();
        UpdateDirectionFlip();
    }

    void UpdateAnimatorParameters()
    {
        // Speed (수평 속도)
        if (!string.IsNullOrEmpty(speedParam))
            animator.SetFloat(speedParam, movement?.HorizontalSpeed ?? 0f, 0.1f, Time.deltaTime);

        // VelocityY
        if (!string.IsNullOrEmpty(velocityYParam))
            animator.SetFloat(velocityYParam, movement?.VerticalSpeed ?? 0f);

        // IsGrounded
        if (!string.IsNullOrEmpty(groundedParam))
            animator.SetBool(groundedParam, movement?.IsGrounded ?? true);

        // IsDashing
        if (!string.IsNullOrEmpty(dashingParam))
            animator.SetBool(dashingParam, stateController?.IsDashing ?? false);

        // IsDead
        if (!string.IsNullOrEmpty(deadParam))
            animator.SetBool(deadParam, stateController?.CurrentMoveState == PlayerState.Death);

        // IsReloading (오버레이)
        if (!string.IsNullOrEmpty(reloadingParam))
        {
            bool reloading = stateController?.CurrentOverlayState == PlayerState.Reloading;
            animator.SetBool(reloadingParam, reloading);
        }
    }

    void UpdateAimDirection()
    {
        if (!useAimDirection || stateController == null) return;

        Vector2 aimInput = GetAimInput();
        int newAimDir = CalculateAimDirectionIndex(aimInput);

        if (newAimDir != currentAimDirIndex)
        {
            currentAimDirIndex = newAimDir;
            
            if (!string.IsNullOrEmpty(aimDirParam))
                animator.SetInteger(aimDirParam, currentAimDirIndex);

            OnAimDirectionChanged?.Invoke(currentAimDirIndex);
        }
    }

    Vector2 GetAimInput()
    {
        // PlayerInputHandler의 AimInput 가져오기
        var inputHandler = GetComponent<PlayerInputHandler>();
        if (inputHandler != null)
        {
            // 리플렉션으로 비공개 필드 접근 또는 이벤트 구독 필요
            // 임시: StateController를 통해 접근 시도
        }

        // 임시: 조준 입력이 없으면 이동 방향 또는 바라보는 방향 사용
        Vector2 moveInput = stateController.InputDirection;
        if (moveInput.sqrMagnitude > 0.01f)
            return moveInput.normalized;

        // 기본: 정면
        return transform.localScale.x > 0 ? Vector2.right : Vector2.left;
    }

    int CalculateAimDirectionIndex(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.01f) return 0; // 정면

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        // -180 ~ 180 → 0 ~ 360
        if (angle < 0) angle += 360f;

        // 8방향을 5구역으로 매핑
        // 정면: -22.5 ~ 22.5, 157.5 ~ 202.5 (좌우)
        // 위: 67.5 ~ 112.5
        // 대각위: 22.5 ~ 67.5, 112.5 ~ 157.5
        // 아래: 247.5 ~ 292.5
        // 대각아래: 202.5 ~ 247.5, 292.5 ~ 337.5

        if (angle >= 337.5f || angle < 22.5f) return 0;           // 정면 (우)
        if (angle >= 157.5f && angle < 202.5f) return 0;          // 정면 (좌)
        if (angle >= 67.5f && angle < 112.5f) return 1;           // 위
        if ((angle >= 22.5f && angle < 67.5f) || (angle >= 112.5f && angle < 157.5f)) return 2; // 대각위
        if (angle >= 247.5f && angle < 292.5f) return 3;          // 아래
        return 4; // 대각아래 (202.5~247.5, 292.5~337.5)
    }

    void UpdateMuzzlePosition()
    {
        if (muzzleTransform == null) return;

        Vector2 offset = GetMuzzleOffsetForAimDir(currentAimDirIndex);
        int facing = transform.localScale.x > 0 ? 1 : -1;
        
        // 로컬 공간에서 오프셋 적용
        Vector3 localPos = new Vector3(offset.x * facing, offset.y, 0f);
        muzzleTransform.localPosition = localPos;
    }

    Vector2 GetMuzzleOffsetForAimDir(int aimDir)
    {
        switch (aimDir)
        {
            case 1: return muzzleOffsetUp;              // 위
            case 2: return muzzleOffsetUpDiagonal;      // 대각위
            case 3: return muzzleOffsetDown;            // 아래
            case 4: return muzzleOffsetDownDiagonal;    // 대각아래
            default: return muzzleOffsetForward;        // 정면
        }
    }

    void UpdateDirectionFlip()
    {
        if (stateController == null) return;

        // 조준 입력이 있으면 조준 방향 우선, 없으면 이동 방향
        Vector2 aimInput = GetAimInput();
        Vector2 moveInput = movement?.MoveInput ?? Vector2.zero;

        float targetScaleX = 0f;
        if (aimInput.x != 0f)
        {
            targetScaleX = aimInput.x > 0f ? 1f : -1f;
        }
        else if (moveInput.x != 0f)
        {
            targetScaleX = moveInput.x > 0f ? 1f : -1f;
        }
        else
        {
            targetScaleX = transform.localScale.x > 0 ? 1f : -1f; // 현재 유지
        }

        if (flipByScale)
        {
            if (Mathf.Abs(transform.localScale.x - targetScaleX) > 0.01f)
            {
                transform.localScale = new Vector3(targetScaleX, transform.localScale.y, transform.localScale.z);
            }
        }
        else if (spriteRenderer != null)
        {
            spriteRenderer.flipX = targetScaleX < 0f;
        }

        // Muzzle도 같이 뒤집힘 (자식 오브젝트이므로 scale 상속)
        // flipMuzzleWithScale=false면 별도 처리 필요
    }

    #region State Event Handlers

    void OnMoveStateChanged(PlayerState from, PlayerState to)
    {
        switch (to)
        {
            case PlayerState.Hurt:
                if (!string.IsNullOrEmpty(hurtTrigger))
                    animator.SetTrigger(hurtTrigger);
                OnHurtAnimationStart?.Invoke();
                break;

            case PlayerState.Death:
                if (!string.IsNullOrEmpty(deadParam))
                    animator.SetBool(deadParam, true);
                OnDeathAnimationStart?.Invoke();
                break;

            case PlayerState.Idle:
            case PlayerState.Run:
            case PlayerState.Jump:
            case PlayerState.Fall:
                // Death/Hurt에서 복귀 시 IsDead 리셋
                if (from == PlayerState.Death || from == PlayerState.Hurt)
                {
                    if (!string.IsNullOrEmpty(deadParam))
                        animator.SetBool(deadParam, false);
                }
                break;
        }
    }

    void OnOverlayChanged(PlayerState overlay, bool active)
    {
        switch (overlay)
        {
            case PlayerState.Shooting:
                if (!string.IsNullOrEmpty(shootingParam))
                    animator.SetBool(shootingParam, active);
                wasShooting = active;
                OnShootingChanged?.Invoke(active);
                break;

            case PlayerState.Reloading:
                if (!string.IsNullOrEmpty(reloadingParam))
                    animator.SetBool(reloadingParam, active);
                break;
        }
    }

    #endregion

    #region Public API

    /// <summary>피격 애니메이션 강제 트리거 (상태 시스템 경유 안 할 때)</summary>
    public void TriggerHurt()
    {
        if (!string.IsNullOrEmpty(hurtTrigger))
            animator.SetTrigger(hurtTrigger);
        OnHurtAnimationStart?.Invoke();
    }

    /// <summary>사망 애니메이션 시작</summary>
    public void TriggerDeath()
    {
        if (!string.IsNullOrEmpty(deadParam))
            animator.SetBool(deadParam, true);
        OnDeathAnimationStart?.Invoke();
    }

    /// <summary>부활 시 애니메이션 리셋</summary>
    public void ResetAnimation()
    {
        if (!string.IsNullOrEmpty(deadParam))
            animator.SetBool(deadParam, false);
        if (!string.IsNullOrEmpty(shootingParam))
            animator.SetBool(shootingParam, false);
        if (!string.IsNullOrEmpty(reloadingParam))
            animator.SetBool(reloadingParam, false);
        if (!string.IsNullOrEmpty(dashingParam))
            animator.SetBool(dashingParam, false);
    }

    /// <summary>현재 조준 방향 인덱스 반환 (무기 시스템에서 사용)</summary>
    public int GetCurrentAimDirectionIndex() => currentAimDirIndex;

    /// <summary>현재 조준 방향 벡터 반환 (발사 방향용)</summary>
    public Vector2 GetCurrentAimDirectionVector()
    {
        switch (currentAimDirIndex)
        {
            case 1: return Vector2.up;                          // 위
            case 2: return new Vector2(0.707f, 0.707f);         // 대각위
            case 3: return Vector2.down;                        // 아래
            case 4: return new Vector2(0.707f, -0.707f);        // 대각아래
            default: return transform.localScale.x > 0 ? Vector2.right : Vector2.left; // 정면
        }
    }

    /// <summary>Muzzle 월드 위치 반환 (총알 생성용)</summary>
    public Vector3 GetMuzzleWorldPosition()
    {
        return muzzleTransform != null ? muzzleTransform.position : transform.position;
    }

    #endregion

    void OnDrawGizmosSelected()
    {
        if (muzzleTransform != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(muzzleTransform.position, 0.05f);
            
            // 방향별 오프셋 표시
            Gizmos.color = Color.green;
            foreach (var offset in new[] { muzzleOffsetForward, muzzleOffsetUp, muzzleOffsetUpDiagonal, muzzleOffsetDown, muzzleOffsetDownDiagonal })
            {
                Vector3 pos = transform.position + new Vector3(offset.x * transform.localScale.x, offset.y, 0);
                Gizmos.DrawWireSphere(pos, 0.03f);
            }
        }
    }
}