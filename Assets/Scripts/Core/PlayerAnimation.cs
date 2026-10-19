using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 플레이어 애니메이션 상태 시스템 (스크립트 스프라이트 교체 + 프레임 타이머).
/// 계획서 animation-state-system.md [1]~[8] 반영.
/// - (이동 상태, AimDir) 2차원 조회로 시트 선택, 값이 바뀔 때만 교체
/// - Animator 미사용 (RequireComponent/필드/파라미터 세팅 전부 제거)
/// - 프레임 진행/루프/1회 재생·MuzzleFX 연출은 PlayerAnimation.Playback.cs (partial) 담당
/// - Muzzle 위치 동기화 / 방향 전환 (scale.x 읽기만)
/// </summary>
[DisallowMultipleComponent]
public partial class PlayerAnimation : MonoBehaviour
{
    const int AimDirCount = PlayerStateController.AimDirDown90 + 1; // 5 (0~4)

    /// <summary>방향별 프레임 시트. frames 순서 = Unity 셀 순서 (y 위→아래, x 좌→우)</summary>
    [Serializable]
    public struct DirectionSheet
    {
        public Sprite[] frames;
    }

    [Header("References")]
    [SerializeField] PlayerStateController stateController;
    [SerializeField] PlayerMovement movement;
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] WeaponController weapon; // MuzzleFX 트리거용 (OnFire 구독)

    [Header("Sprite Sheets (방향별 5장: 정면/위45/위90/아래45/아래90)")]
    [SerializeField] DirectionSheet[] aimSprites = new DirectionSheet[AimDirCount]; // Idle 대기 (aim 시트)
    [SerializeField] DirectionSheet[] runSprites = new DirectionSheet[AimDirCount];
    [SerializeField] DirectionSheet[] jumpSprites = new DirectionSheet[AimDirCount];
    [SerializeField] DirectionSheet[] fallSprites = new DirectionSheet[AimDirCount];
    [SerializeField] DirectionSheet[] shootSprites = new DirectionSheet[AimDirCount];
    [SerializeField] DirectionSheet[] hurtSprites = new DirectionSheet[AimDirCount];

    [Header("Sprite Sheets (단일 방향무관 · 프레임 배열)")]
    [SerializeField] Sprite[] deathFrames;   // 방향 무시, 6프레임
    [SerializeField] Sprite[] wakeFrames;    // 부활 연출, 5프레임
    [SerializeField] Sprite[] dormantFrames; // 시작 전/사후 대기, 1프레임 (Death 완료 후 ResolveSprite가 선택)
    [SerializeField] Sprite[] dashFrames;    // Dash, 13프레임 (상태 미사용 → 필드만, 미연결)

    [Header("Aim Direction (5방향: 정면/위45/위90/아래45/아래90)")]
    [SerializeField] bool useAimDirection = true;

    [System.Serializable]
    public struct MuzzleOffsetSet
    {
        public int aimDir; // 0=정면, 1=위45, 2=위90, 3=아래45, 4=아래90
        public Vector2 offset;
    }

    [Header("Muzzle Sync (조준 방향별 총구 오프셋)")]
    [SerializeField] Transform muzzleTransform; // 총구 위치
    // sprite-asset-spec [4] 5방향 표. 값은 보정 전 시작값 (TBD — 씬에서 총구 끝 픽셀 기준 재보정 후 갱신)
    // 필드명 개명(muzzleOffsets→aimMuzzleOffsets): moveState 포함 레거시 직렬화 데이터를 강제 리셋
    [SerializeField] MuzzleOffsetSet[] aimMuzzleOffsets = new MuzzleOffsetSet[]
    {
        new MuzzleOffsetSet { aimDir = 0, offset = new Vector2(0.5f, 0.1f) },    // 정면
        new MuzzleOffsetSet { aimDir = 1, offset = new Vector2(0.3f, 0.4f) },    // 위45
        new MuzzleOffsetSet { aimDir = 2, offset = new Vector2(0.1f, 0.6f) },    // 위90
        new MuzzleOffsetSet { aimDir = 3, offset = new Vector2(0.5f, -0.15f) },  // 아래45
        new MuzzleOffsetSet { aimDir = 4, offset = new Vector2(0.5f, -0.3f) },   // 아래90
    };

    [Header("Direction Flip")]
    [SerializeField] bool flipByScale = true; // true: Transform.scale.x 반전, false: SpriteRenderer.flipX
    [SerializeField] bool flipMuzzleWithScale = true;

    // 이벤트 (외부 연결용)
    public event Action<int> OnAimDirectionChanged; // aimDir index
    public event Action<bool> OnShootingChanged;    // shooting active
    public event Action OnHurtAnimationStart;
    public event Action OnDeathAnimationStart;

    // 상태 캐시
    int currentAimDirIndex = 0;

    /// <summary>부활 연출 중 여부 (선택 순서 2순위). RespawnSystem이 재생 시작 시 설정하고,
    /// Playback이 wake 5프레임 완료 시 자동 해제 (플랜 [7])</summary>
    public bool IsWakePlaying { get; set; }

    void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (stateController == null) stateController = GetComponent<PlayerStateController>();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (weapon == null) weapon = GetComponent<WeaponController>();
        FindMuzzleFxRenderer(); // 없으면 null 유지 → MuzzleFX 처리 전부 스킵 (자동 생성 안 함)

        // StateController 이벤트 구독
        if (stateController != null)
        {
            PlayerStateEvents.OnMoveStateChanged += OnMoveStateChanged;
            PlayerStateEvents.OnOverlayChanged += OnOverlayChanged;
        }

        // 발사 시 MuzzleFX 3프레임 연출 시작 (플랜 [8] MuzzleFX = OnFire 동시)
        if (weapon != null) weapon.OnFire += OnWeaponFired;
    }

    void OnDestroy()
    {
        if (stateController != null)
        {
            PlayerStateEvents.OnMoveStateChanged -= OnMoveStateChanged;
            PlayerStateEvents.OnOverlayChanged -= OnOverlayChanged;
        }
        if (weapon != null) weapon.OnFire -= OnWeaponFired;
    }

    void LateUpdate()
    {
        // 조준 방향을 먼저 갱신: 스프라이트와 Muzzle localPosition이 같은 프레임에 같은 방향으로 교체 (플랜 [5])
        UpdateAimDirection();
        UpdateSprite();
        UpdateMuzzlePosition();
        UpdateDirectionFlip();
        UpdateMuzzleFx();
    }

    /// <summary>시트 선택 순서 (animation-state-system.md [3]+[7]): death(+완료 시 dormant) → wake → hurt → shoot → 이동 상태×AimDir.
    /// 반환값은 프레임 배열(셀 순서)이며 프레임 진행은 Playback 측 타이머 담당.
    /// Death/Dormant/Wake 중에는 사격 오버레이(4순위)가 우선하지 않음 — 아래 death/wake 분기에서 차단</summary>
    Sprite[] ResolveSprite()
    {
        if (stateController == null) return null;

        PlayerState moveState = stateController.CurrentMoveState;

        // 1. 사망 (방향 무시, 루트 반전으로 처리). 6프레임 완료 후 Dormant(idle_dormant) 유지 — 자동 전환 없음 (플랜 [7])
        if (moveState == PlayerState.Death) return deathDone ? dormantFrames : deathFrames;

        // 2. 부활 연출
        if (IsWakePlaying) return wakeFrames;

        // 3. Hurt (AimDir 사용)
        if (moveState == PlayerState.Hurt) return PickDirectionSheet(hurtSprites);

        // 4. 사격 오버레이 (이동 중이어도 우선)
        if (stateController.CurrentOverlayState == PlayerState.Shooting)
            return PickDirectionSheet(shootSprites);

        // 5. 이동 상태 시트 (Idle→aim, Run→run, Jump→jump, Fall→fall)
        switch (moveState)
        {
            case PlayerState.Idle: return PickDirectionSheet(aimSprites);
            case PlayerState.Run: return PickDirectionSheet(runSprites);
            case PlayerState.Jump: return PickDirectionSheet(jumpSprites);
            case PlayerState.Fall: return PickDirectionSheet(fallSprites);
            default: return null; // Crouch: 애니메이션 경로 제거됨, Dash: 미연결 — 시트 없음 → 조용히 스킵
        }
    }

    /// <summary>방향별 시트의 프레임 배열 조회. 배열 미등록/인덱스 범위 밖/슬롯 null은 조용히 스킵 (에러 없음)</summary>
    Sprite[] PickDirectionSheet(DirectionSheet[] sheet)
    {
        int index = useAimDirection ? currentAimDirIndex : PlayerStateController.AimDirFront;
        if (sheet == null || index < 0 || index >= sheet.Length) return null;
        return sheet[index].frames;
    }

    void UpdateAimDirection()
    {
        if (!useAimDirection || stateController == null) return;

        int newAimDir = stateController.AimDirectionIndex;
        if (newAimDir != currentAimDirIndex)
        {
            currentAimDirIndex = newAimDir;
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

        // 조준 방향 기준 오프셋 (이동 상태별 표는 삭제 — 방향별만 유지, 플랜 [10] 216)
        Vector2 offset = GetMuzzleOffset(currentAimDirIndex);
        int facing = transform.localScale.x > 0 ? 1 : -1;

        Vector3 localPos = new Vector3(offset.x * facing, offset.y, 0f);
        muzzleTransform.localPosition = localPos;
    }

    Vector2 GetMuzzleOffset(int aimDir)
    {
        // 배열에서 조준 방향 매칭 항목 찾기
        if (aimMuzzleOffsets != null)
        {
            foreach (var set in aimMuzzleOffsets)
            {
                if (set.aimDir == aimDir)
                    return set.offset;
            }
        }

        // 최종 폴백 (정면 기본값)
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
        // 시트 선택은 ResolveSprite가 상태값으로 판별. 여기서는 외부 구독자 알림만 발화 (플랜 [10] 215)
        if (to == PlayerState.Death)
            OnDeathAnimationStart?.Invoke();
        else if (to == PlayerState.Hurt)
            OnHurtAnimationStart?.Invoke();
    }

    void OnOverlayChanged(PlayerState overlay, bool active)
    {
        // Reloading 연출 없음 (에셋에 클립 없음)
        if (overlay == PlayerState.Shooting)
            OnShootingChanged?.Invoke(active);
    }

    #endregion

    #region Public API

    /// <summary>피격 플래시 트리거 (OnHurtAnimationStart 구독자에게 전달)</summary>
    public void TriggerHurtFlash()
    {
        OnHurtAnimationStart?.Invoke();
    }

    /// <summary>사망 애니메이션 시작 (death 시트 선택은 ResolveSprite가 상태로 처리)</summary>
    public void TriggerDeath()
    {
        OnDeathAnimationStart?.Invoke();
    }

    /// <summary>부활 시 애니메이션 리셋 (사격/피격 잔여 상태 정리 — 플랜 [7])</summary>
    public void ResetAnimation()
    {
        IsWakePlaying = false;
        deathDone = false; // 다음 사망에서 death 6프레임이 다시 재생되도록
        StopMuzzleFx();    // 사격 연출 잔여 프레임 제거
    }

    /// <summary>현재 조준 방향 인덱스 반환 (무기 시스템에서 사용)</summary>
    public int GetCurrentAimDirectionIndex() => currentAimDirIndex;

    /// <summary>현재 조준 방향 벡터 반환 (발사 방향용)</summary>
    public Vector2 GetCurrentAimDirectionVector()
    {
        // 벡터는 우측 기준(+x)이고 좌우 반전은 루트 scale.x가 담당
        float facing = transform.localScale.x > 0f ? 1f : -1f;
        const float kDiagonal = 0.7071068f; // √2/2

        switch (currentAimDirIndex)
        {
            case PlayerStateController.AimDirUp45:
                return new Vector2(kDiagonal * facing, kDiagonal);   // 위45 (±0.707, 0.707)
            case PlayerStateController.AimDirUp90:
                return Vector2.up;                                   // 위90 (0, 1)
            case PlayerStateController.AimDirDown45:
                return new Vector2(kDiagonal * facing, -kDiagonal);  // 아래45 (±0.707, -0.707)
            case PlayerStateController.AimDirDown90:
                return Vector2.down;                                 // 아래90 (0, -1)
            default:
                return new Vector2(facing, 0f);                      // 정면 (±1, 0)
        }
    }

    /// <summary>Muzzle 월드 위치 반환 (총알 생성용)</summary>
    public Vector3 GetMuzzleWorldPosition()
    {
        return muzzleTransform != null ? muzzleTransform.position : transform.position;
    }

#if UNITY_EDITOR
    /// <summary>컴포넌트 컨텍스트메뉴에서 폴더 기반 시트 자동 등록.
    /// 런타임 어셈블리는 에디터 어셈블리를 참조할 수 없으므로 메뉴 호출로 연결
    /// (문자열은 PlayerAnimationSheetLoader.MenuPath와 일치해야 함)</summary>
    [ContextMenu("Fill Sheets From Folder")]
    void FillSheetsFromFolder()
    {
        UnityEditor.EditorApplication.ExecuteMenuItem("Tools/PlayerAnimation/Fill Sheets From Folder");
    }
#endif

    #endregion

    void OnDrawGizmosSelected()
    {
        if (muzzleTransform != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(muzzleTransform.position, 0.05f);
            
            // 방향별 오프셋 표시 (aimMuzzleOffsets 배열에서 고유한 오프셋만)
            Gizmos.color = Color.green;
            var uniqueOffsets = new HashSet<Vector2>();
            foreach (var set in aimMuzzleOffsets)
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