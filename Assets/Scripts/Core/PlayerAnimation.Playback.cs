using UnityEngine;

/// <summary>
/// PlayerAnimation 프레임 재생부 (partial): 프레임 타이머 + MuzzleFX 연출.
/// 시트 선택은 PlayerAnimation.cs의 ResolveSprite가 담당하고, 이 파일은
/// 선택된 시트의 프레임 인덱스만 진행한다. 1회 클립 완료 시 상태 연동
/// (Death→Dormant 전환, Wake 완료 해제)과 Death/Wake 중 MuzzleFX 가드도 여기서 처리 (플랜 [7])
/// </summary>
public partial class PlayerAnimation
{
    enum ClipKind { Static, Dormant, Run, Shoot, Hurt, Death, Wake, Dash }

    [Header("Frame Timing (프레임 간격/루프. 기본값: 플랜 [8] 프레임 타이밍 표)")]
    [SerializeField] float runFrameInterval = 0.08f;
    [SerializeField] bool runLoop = true;
    [SerializeField] float shootFrameInterval = 0.06f;
    [SerializeField] bool shootLoop = false; // false=1회 후 마지막 프레임 유지 (같은 방향 연속 사격 유지 규칙)
    [SerializeField] float hurtFrameInterval = 0.08f;
    [SerializeField] float deathFrameInterval = 0.10f;
    [SerializeField] float wakeFrameInterval = 0.08f;
    [SerializeField] float muzzleFxFrameInterval = 0.04f;
    [SerializeField] float dashFrameInterval = 0.04f; // Dash 미연결 (상태 시스템 미사용, 필드만)

    [Header("MuzzleFX (자식 MuzzleFX의 SpriteRenderer. 없으면 자동 생성 없이 스킵)")]
    [SerializeField] SpriteRenderer muzzleFxRenderer;
    [SerializeField] DirectionSheet[] muzzleFxSheets = new DirectionSheet[AimDirCount];

    // 프레임 타이머 상태 캐시
    Sprite[] activeSheet;
    ClipKind activeKind;
    int activeFrame;
    float activeTimer;
    Sprite[] fxSheet;
    int fxFrame;
    float fxTimer;
    bool fxPlaying;
    bool deathDone; // death 6프레임 완료 여부 → ResolveSprite가 dormantFrames로 전환 (ResetAnimation에서 리셋)

    /// <summary>선택된 시트의 프레임을 진행해 SpriteRenderer에 대입 (이전 값과 다를 때만 교체)</summary>
    void UpdateSprite()
    {
        if (spriteRenderer == null) return;

        Sprite[] sheet = ResolveSprite();
        if (sheet == null || sheet.Length == 0) return; // 미등록/빈 시트: 현재 스프라이트 유지 (NRE 없음)

        if (!ReferenceEquals(sheet, activeSheet))
        {
            // 시트가 바뀔 때만 타이머 리셋 → 방향/상태 전환 시 처음부터, 같은 시트는 이어서 유지
            activeSheet = sheet;
            activeKind = GetClipKind(sheet);
            activeFrame = 0;
            activeTimer = 0f;
        }
        else
        {
            AdvanceFrame(sheet);
        }

        Sprite next = sheet[activeFrame];
        if (next != null && spriteRenderer.sprite != next)
            spriteRenderer.sprite = next;
    }

    /// <summary>프레임 간격만큼 타이머 진행. 1회 재생은 마지막 프레임에서 정지, 루프는 0으로 순환</summary>
    void AdvanceFrame(Sprite[] sheet)
    {
        float interval = GetFrameInterval(activeKind);
        if (interval <= 0f || sheet.Length <= 1)
        {
            // Wake은 간격 0(정지 설정)이어도 완료 처리 — 플래그 미해제 시 정상 시트 복귀가 영구 차단됨
            if (activeKind == ClipKind.Wake) IsWakePlaying = false;
            return; // Static/Dormant: 1프레임 정지
        }

        activeTimer += Time.deltaTime;
        while (activeTimer >= interval)
        {
            activeTimer -= interval;
            if (activeFrame + 1 < sheet.Length)
            {
                activeFrame++;
            }
            else if (IsLooping(activeKind))
            {
                activeFrame = 0;
            }
            else
            {
                // 1회 클립 완료: Death→Dormant 전환 플래그, Wake→정상 시트 복귀 (플랜 [7])
                if (activeKind == ClipKind.Death) deathDone = true;
                else if (activeKind == ClipKind.Wake) IsWakePlaying = false;
                activeTimer = 0f;
                break;
            }
        }
    }

    /// <summary>시트 참조로 클립 유형 판별 — ResolveSprite와 같은 시트를 반환하므로 선택 우선순위와 자동으로 일치</summary>
    ClipKind GetClipKind(Sprite[] sheet)
    {
        if (ReferenceEquals(sheet, deathFrames)) return ClipKind.Death;
        if (ReferenceEquals(sheet, wakeFrames)) return ClipKind.Wake;
        if (ReferenceEquals(sheet, dormantFrames)) return ClipKind.Dormant;
        if (ReferenceEquals(sheet, dashFrames)) return ClipKind.Dash;
        if (ContainsSheet(hurtSprites, sheet)) return ClipKind.Hurt;
        if (ContainsSheet(shootSprites, sheet)) return ClipKind.Shoot;
        if (ContainsSheet(runSprites, sheet)) return ClipKind.Run;
        return ClipKind.Static; // aim/jump/fall + 미식별 시트
    }

    static bool ContainsSheet(DirectionSheet[] set, Sprite[] sheet)
    {
        if (set == null || sheet == null) return false;
        foreach (var entry in set)
            if (ReferenceEquals(entry.frames, sheet)) return true;
        return false;
    }

    float GetFrameInterval(ClipKind kind)
    {
        switch (kind)
        {
            case ClipKind.Run: return runFrameInterval;
            case ClipKind.Shoot: return shootFrameInterval;
            case ClipKind.Hurt: return hurtFrameInterval;
            case ClipKind.Death: return deathFrameInterval;
            case ClipKind.Wake: return wakeFrameInterval;
            case ClipKind.Dash: return dashFrameInterval;
            default: return 0f; // Static/Dormant: 정지
        }
    }

    bool IsLooping(ClipKind kind)
    {
        switch (kind)
        {
            case ClipKind.Run: return runLoop;
            case ClipKind.Shoot: return shootLoop;
            default: return false;
        }
    }

    #region MuzzleFX

    /// <summary>자식에서 MuzzleFX SpriteRenderer 탐색. 없으면 null 유지 → FX 관련 처리 전부 조용히 스킵</summary>
    void FindMuzzleFxRenderer()
    {
        if (muzzleFxRenderer != null) return;

        foreach (var r in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r.gameObject.name == "MuzzleFX")
            {
                muzzleFxRenderer = r;
                return;
            }
        }
    }

    void OnWeaponFired()
    {
        StartMuzzleFx();
    }

    /// <summary>발사 시 현재 조준 방향의 MuzzleFX 시트(3프레임)를 프레임 0부터 재생.
    /// Death/Dormant/Wake 중에는 사격 연출을 재개하지 않는다 (플랜 [7])</summary>
    void StartMuzzleFx()
    {
        if (muzzleFxRenderer == null || muzzleFxSheets == null) return;
        if (IsWakePlaying) return;
        if (stateController != null && stateController.CurrentMoveState == PlayerState.Death) return;

        int index = currentAimDirIndex;
        if (index < 0 || index >= muzzleFxSheets.Length) return;

        Sprite[] frames = muzzleFxSheets[index].frames;
        if (frames == null || frames.Length == 0) return;

        fxSheet = frames;
        fxFrame = 0;
        fxTimer = 0f;
        fxPlaying = true;
        muzzleFxRenderer.sprite = frames[0];
    }

    /// <summary>MuzzleFX 프레임 진행. 재생이 끝나면 SpriteRenderer.sprite를 비운다 (오브젝트 비활성화는 안 함)</summary>
    void UpdateMuzzleFx()
    {
        if (!fxPlaying || muzzleFxRenderer == null || fxSheet == null) return;

        if (muzzleFxFrameInterval > 0f)
        {
            fxTimer += Time.deltaTime;
            while (fxTimer >= muzzleFxFrameInterval)
            {
                fxTimer -= muzzleFxFrameInterval;
                fxFrame++;
                if (fxFrame >= fxSheet.Length)
                {
                    fxPlaying = false;
                    muzzleFxRenderer.sprite = null;
                    return;
                }
            }
        }

        Sprite next = fxSheet[fxFrame];
        if (next != null && muzzleFxRenderer.sprite != next)
            muzzleFxRenderer.sprite = next;
    }

    /// <summary>MuzzleFX 재생 중단 + 스프라이트 제거 (ResetAnimation에서 잔여 연출 정리)</summary>
    void StopMuzzleFx()
    {
        fxPlaying = false;
        fxSheet = null;
        if (muzzleFxRenderer != null) muzzleFxRenderer.sprite = null;
    }

    #endregion
}
