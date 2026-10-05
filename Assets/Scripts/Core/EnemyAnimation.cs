using UnityEngine;

/// <summary>
/// 적 애니메이션 시스템.
/// 계획서 enemy-ai-system.md [8], animation-state-system.md [1]~[6] 확장.
/// - PlayerAnimation과 같은 파라미터 구조 사용
/// - AI 상태에 따른 애니메이션 전환
/// - 조준/사격/피격/사망 연동
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class EnemyAnimation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] EnemyAI enemyAI;
    [SerializeField] EnemyHealth enemyHealth;
    [SerializeField] EnemyMovement enemyMovement;
    [SerializeField] Animator animator;
    [SerializeField] SpriteRenderer spriteRenderer;

    [Header("Animator Parameters")]
    [SerializeField] string speedParam = "Speed";
    [SerializeField] string velocityYParam = "VelocityY";
    [SerializeField] string groundedParam = "IsGrounded";
    [SerializeField] string aimParam = "IsAiming";       // bool: 조준 중
    [SerializeField] string shootTrigger = "Shoot";     // trigger: 발사
    [SerializeField] string hurtTrigger = "Hurt";       // trigger: 피격
    [SerializeField] string staggerTrigger = "Stagger"; // trigger: 경직
    [SerializeField] string deadParam = "IsDead";       // bool: 사망
    [SerializeField] string aimDirParam = "AimDir";     // int: 조준 방향 (플레이어와 동일)

    [Header("Aim Direction (5구역)")]
    [SerializeField] bool useAimDirection = true;

    [Header("Muzzle Sync")]
    [SerializeField] Transform muzzleTransform;
    [SerializeField] Vector2 muzzleOffsetForward = new Vector2(0.5f, 0.1f);
    [SerializeField] Vector2 muzzleOffsetUp = new Vector2(0.1f, 0.6f);
    [SerializeField] Vector2 muzzleOffsetUpDiagonal = new Vector2(0.35f, 0.4f);
    [SerializeField] Vector2 muzzleOffsetDown = new Vector2(0.5f, -0.3f);
    [SerializeField] Vector2 muzzleOffsetDownDiagonal = new Vector2(0.35f, -0.2f);

    [Header("Direction Flip")]
    [SerializeField] bool flipByScale = true;

    // 상태 캐시
    int currentAimDirIndex = 0;
    bool wasAiming = false;

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (enemyAI == null) enemyAI = GetComponent<EnemyAI>();
        if (enemyHealth == null) enemyHealth = GetComponent<EnemyHealth>();
        if (enemyMovement == null) enemyMovement = GetComponent<EnemyMovement>();

        // EnemyAI 이벤트 구독
        if (enemyAI != null)
        {
            // EnemyAI에 상태 변경 이벤트 추가 필요 (현재 없음)
            // 임시: LateUpdate에서 상태 폴링
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
        // Speed
        if (!string.IsNullOrEmpty(speedParam) && enemyMovement != null)
            animator.SetFloat(speedParam, enemyMovement.HorizontalSpeed, 0.1f, Time.deltaTime);

        // VelocityY
        if (!string.IsNullOrEmpty(velocityYParam) && enemyMovement != null)
            animator.SetFloat(velocityYParam, enemyMovement.VerticalSpeed);

        // IsGrounded
        if (!string.IsNullOrEmpty(groundedParam) && enemyMovement != null)
            animator.SetBool(groundedParam, enemyMovement.IsGrounded);

        // IsDead
        if (!string.IsNullOrEmpty(deadParam) && enemyHealth != null)
            animator.SetBool(deadParam, enemyHealth.IsDead);

        // IsAiming (AI 상태 기반)
        if (!string.IsNullOrEmpty(aimParam) && enemyAI != null)
        {
            bool aiming = enemyAI.CurrentState == EnemyAI.AIState.Aim || enemyAI.CurrentState == EnemyAI.AIState.Shoot;
            animator.SetBool(aimParam, aiming);
            
            if (aiming && !wasAiming)
                OnAimStart();
            else if (!aiming && wasAiming)
                OnAimEnd();
            
            wasAiming = aiming;
        }
    }

    void UpdateAimDirection()
    {
        if (!useAimDirection || enemyAI == null || !enemyAI.HasTarget) return;

        Vector2 toPlayer = enemyAI.TargetDirection;
        int newAimDir = CalculateAimDirectionIndex(toPlayer);

        if (newAimDir != currentAimDirIndex)
        {
            currentAimDirIndex = newAimDir;
            
            if (!string.IsNullOrEmpty(aimDirParam))
                animator.SetInteger(aimDirParam, currentAimDirIndex);
        }
    }

    int CalculateAimDirectionIndex(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.01f) return 0;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360f;

        // PlayerAnimation과 동일한 5구역 매핑
        if (angle >= 337.5f || angle < 22.5f) return 0;           // 정면 (우)
        if (angle >= 157.5f && angle < 202.5f) return 0;          // 정면 (좌)
        if (angle >= 67.5f && angle < 112.5f) return 1;           // 위
        if ((angle >= 22.5f && angle < 67.5f) || (angle >= 112.5f && angle < 157.5f)) return 2; // 대각위
        if (angle >= 247.5f && angle < 292.5f) return 3;          // 아래
        return 4; // 대각아래
    }

    void UpdateMuzzlePosition()
    {
        if (muzzleTransform == null) return;

        Vector2 offset = GetMuzzleOffsetForAimDir(currentAimDirIndex);
        int facing = transform.localScale.x > 0 ? 1 : -1;
        
        Vector3 localPos = new Vector3(offset.x * facing, offset.y, 0f);
        muzzleTransform.localPosition = localPos;
    }

    Vector2 GetMuzzleOffsetForAimDir(int aimDir)
    {
        switch (aimDir)
        {
            case 1: return muzzleOffsetUp;
            case 2: return muzzleOffsetUpDiagonal;
            case 3: return muzzleOffsetDown;
            case 4: return muzzleOffsetDownDiagonal;
            default: return muzzleOffsetForward;
        }
    }

    void UpdateDirectionFlip()
    {
        if (enemyAI == null || !enemyAI.HasTarget) return;

        Vector2 toPlayer = enemyAI.TargetDirection;
        float targetScaleX = toPlayer.x > 0f ? 1f : -1f;

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
    }

    #region Event Handlers (EnemyAI에서 호출)

    /// <summary>조준 시작 (예고) - EnemyAI에서 호출</summary>
    public void OnAimStart()
    {
        // 조준 애니메이션 트리거 또는 파라미터
    }

    /// <summary>조준 끝</summary>
    public void OnAimEnd()
    {
    }

    /// <summary>발사 순간 - EnemyAI.Shoot 상태 진입 시 호출</summary>
    public void OnShoot()
    {
        if (!string.IsNullOrEmpty(shootTrigger))
            animator.SetTrigger(shootTrigger);
    }

    /// <summary>피격 - EnemyHealth.OnDamaged에서 호출</summary>
    public void OnHurt()
    {
        if (!string.IsNullOrEmpty(hurtTrigger))
            animator.SetTrigger(hurtTrigger);
    }

    /// <summary>경직 (Poise 임계값) - EnemyHealth에서 호출</summary>
    public void OnStagger()
    {
        if (!string.IsNullOrEmpty(staggerTrigger))
            animator.SetTrigger(staggerTrigger);
    }

    /// <summary>사망 - EnemyHealth.OnDied에서 호출</summary>
    public void OnDeath()
    {
        if (!string.IsNullOrEmpty(deadParam))
            animator.SetBool(deadParam, true);
    }

    /// <summary>부활/리셋</summary>
    public void ResetAnimation()
    {
        if (!string.IsNullOrEmpty(deadParam))
            animator.SetBool(deadParam, false);
        if (!string.IsNullOrEmpty(aimParam))
            animator.SetBool(aimParam, false);
    }

    #endregion

    #region Public API (WeaponController 등에서 사용)

    public int GetCurrentAimDirectionIndex() => currentAimDirIndex;

    public Vector2 GetCurrentAimDirectionVector()
    {
        switch (currentAimDirIndex)
        {
            case 1: return Vector2.up;
            case 2: return new Vector2(0.707f, 0.707f);
            case 3: return Vector2.down;
            case 4: return new Vector2(0.707f, -0.707f);
            default: return transform.localScale.x > 0 ? Vector2.right : Vector2.left;
        }
    }

    public Vector3 GetMuzzleWorldPosition()
    {
        return muzzleTransform != null ? muzzleTransform.position : transform.position;
    }

    #endregion

    void OnDrawGizmosSelected()
    {
        if (muzzleTransform != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(muzzleTransform.position, 0.05f);
        }
    }
}