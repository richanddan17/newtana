using UnityEngine;
using System;
using System.Collections.Generic;

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
    [SerializeField] string crouchParam = "IsCrouching";    // bool: 앉기
    [SerializeField] string shootingParam = "IsShooting";   // bool: 사격 오버레이
    [SerializeField] string aimDirParam = "AimDir";         // int: 0=정면, 1=위, 2=아래(공중만)
    [SerializeField] string deadParam = "IsDead";           // bool
    // 대시/재장전/피격트리거: 에셋에 없음 → 비활성
    // [SerializeField] string dashingParam = "IsDashing";
    // [SerializeField] string hurtTrigger = "Hurt";
    // [SerializeField] string reloadingParam = "IsReloading";

    [Header("Sprite Structure")]
    [SerializeField] bool upperBodySeparated = false; // true: 상체/하체 분리(Animator Layer), false: 통짜(합본 클립)

    [Header("Aim Direction (3방향: 정면/위/공중아래)")]
    [SerializeField] bool useAimDirection = true;
    [Tooltip("정면(→), 위(↑), 아래(↓) 순서 - 대각선 없음")]
    [SerializeField] string[] aimDirectionNames = { "Forward", "Up", "Down" };

    [System.Serializable]
    public struct MuzzleOffsetSet
    {
        public PlayerState moveState;
        public int aimDir; // 0=정면, 1=위, 2=아래
        public Vector2 offset;
    }

    [Header("Muzzle Sync (자세별 x 조준방향별 오프셋)")]
    [SerializeField] Transform muzzleTransform; // 총구 위치
    [SerializeField] MuzzleOffsetSet[] muzzleOffsets = new MuzzleOffsetSet[]
    {
        // Idle/Run 정면
        new MuzzleOffsetSet { moveState = PlayerState.Idle, aimDir = 0, offset = new Vector2(0.5f, 0.1f) },
        new MuzzleOffsetSet { moveState = PlayerState.Run, aimDir = 0, offset = new Vector2(0.5f, 0.1f) },
        // Idle/Run 위
        new MuzzleOffsetSet { moveState = PlayerState.Idle, aimDir = 1, offset = new Vector2(0.1f, 0.6f) },
        new MuzzleOffsetSet { moveState = PlayerState.Run, aimDir = 1, offset = new Vector2(0.1f, 0.6f) },
        // 공중 정면/위/아래
        new MuzzleOffsetSet { moveState = PlayerState.Jump, aimDir = 0, offset = new Vector2(0.5f, 0.1f) },
        new MuzzleOffsetSet { moveState = PlayerState.Fall, aimDir = 0, offset = new Vector2(0.5f, 0.1f) },
        new MuzzleOffsetSet { moveState = PlayerState.Jump, aimDir = 1, offset = new Vector2(0.1f, 0.6f) },
        new MuzzleOffsetSet { moveState = PlayerState.Fall, aimDir = 1, offset = new Vector2(0.1f, 0.6f) },
        new MuzzleOffsetSet { moveState = PlayerState.Jump, aimDir = 2, offset = new Vector2(0.5f, -0.3f) },
        new MuzzleOffsetSet { moveState = PlayerState.Fall, aimDir = 2, offset = new Vector2(0.5f, -0.3f) },
        // Crouch 정면만
        new MuzzleOffsetSet { moveState = PlayerState.Crouch, aimDir = 0, offset = new Vector2(0.4f, 0f) },
    };

    [Header("Direction Flip")]
    [SerializeField] bool flipByScale = true; // true: Transform.scale.x 반전, false: SpriteRenderer.flipX
    [SerializeField] bool flipMuzzleWithScale = true;

    [Header("Events (외부 연결용)")]
    [SerializeField] int eventsHeaderDummy; // Header용 더미 필드
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

        // IsCrouching
        if (!string.IsNullOrEmpty(crouchParam))
        {
            bool isCrouching = stateController?.CurrentMoveState == PlayerState.Crouch;
            animator.SetBool(crouchParam, isCrouching);
        }

        // IsDead
        if (!string.IsNullOrEmpty(deadParam))
            animator.SetBool(deadParam, stateController?.CurrentMoveState == PlayerState.Death);
    }

    void UpdateAimDirection()
    {
        if (!useAimDirection || stateController == null) return;

        int newAimDir = stateController.AimDirectionIndex;

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
        // A1이 제공하는 단일 출처 사용
        return stateController != null ? stateController.AimInput : Vector2.zero;
    }

    void UpdateMuzzlePosition()
    {
        if (muzzleTransform == null) return;

        // 현재 이동 상태 결정 (Crouch/Idle/Run/Jump/Fall)
        PlayerState currentMoveState = GetCurrentMoveStateForMuzzle();
        Vector2 offset = GetMuzzleOffset(currentMoveState, currentAimDirIndex);
        int facing = transform.localScale.x > 0 ? 1 : -1;
        
        Vector3 localPos = new Vector3(offset.x * facing, offset.y, 0f);
        muzzleTransform.localPosition = localPos;
    }

    PlayerState GetCurrentMoveStateForMuzzle()
    {
        if (stateController == null) return PlayerState.Idle;
        
        // Crouch 상태면 Crouch
        // 애니메이터 파라미터로 IsCrouching 확인 가능하면 좋지만, 여기선 상태 시스템 기반으로
        var moveState = stateController.CurrentMoveState;
        if (moveState == PlayerState.Crouch) return PlayerState.Crouch;
        
        // 공중 상태면 Jump/Fall 구분
        if (moveState == PlayerState.Jump || moveState == PlayerState.Fall)
            return moveState;
        
        // 지상: Idle/Run
        return moveState;
    }

    Vector2 GetMuzzleOffset(PlayerState moveState, int aimDir)
    {
        // 배열에서 매칭되는 것 찾기 (이동상태 + 조준방향)
        foreach (var set in muzzleOffsets)
        {
            if (set.moveState == moveState && set.aimDir == aimDir)
                return set.offset;
        }
        
        // 폴백: 같은 이동상태의 정면 오프셋
        foreach (var set in muzzleOffsets)
        {
            if (set.moveState == moveState && set.aimDir == 0)
                return set.offset;
        }
        
        // 최종 폴백
        return new Vector2(0.5f, 0.1f);
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

        // flipByScale=true일 때 scale 쓰기는 PlayerMovement가 담당 — 여기서는 읽기만 (중복 기록 제거)
        if (!flipByScale && spriteRenderer != null)
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
            case PlayerState.Death:
                if (!string.IsNullOrEmpty(deadParam))
                    animator.SetBool(deadParam, true);
                OnDeathAnimationStart?.Invoke();
                break;

            case PlayerState.Idle:
            case PlayerState.Run:
            case PlayerState.Jump:
            case PlayerState.Fall:
            case PlayerState.Crouch:
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
            // Reloading: 비활성 (에셋에 클립 없음)
        }
    }

    #endregion

    #region Public API

    /// <summary>피격 플래시 트리거 (Hurt 클립 없음 → 플래시만)</summary>
    public void TriggerHurtFlash()
    {
        OnHurtAnimationStart?.Invoke(); // DamageFlash 컴포넌트가 구독해서 처리
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
        if (!string.IsNullOrEmpty(crouchParam))
            animator.SetBool(crouchParam, false);
    }

    /// <summary>현재 조준 방향 인덱스 반환 (무기 시스템에서 사용)</summary>
    public int GetCurrentAimDirectionIndex() => currentAimDirIndex;

    /// <summary>현재 조준 방향 벡터 반환 (발사 방향용)</summary>
    public Vector2 GetCurrentAimDirectionVector()
    {
        switch (currentAimDirIndex)
        {
            case 1: return Vector2.up;                          // 위
            case 2: return Vector2.down;                        // 아래 (공중만)
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
            
            // 방향별 오프셋 표시 (muzzleOffsets 배열에서 고유한 오프셋만)
            Gizmos.color = Color.green;
            var uniqueOffsets = new HashSet<Vector2>();
            foreach (var set in muzzleOffsets)
            {
                if (uniqueOffsets.Add(set.offset))
                {
                    Vector3 pos = transform.position + new Vector3(set.offset.x * transform.localScale.x, set.offset.y, 0);
                    Gizmos.DrawWireSphere(pos, 0.03f);
                }
            }
        }
    }
}