using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 전역 히트스톱 (Hit Pause) 시스템.
/// 계획서 health-damage-system.md [9], Phase 6 확장.
/// - 타임스케일 0으로 일시정지 → 지연 후 복구
/// - 여러 소스에서 요청 시 가장 긴 것 적용 (중첩 방지)
/// - 언스케일드 타임으로 동작 (Time.timeScale=0에서도 카운트)
/// </summary>
public static class HitStop
{
    class HitStopRequest
    {
        public float Duration;
        public float EndTime;
        public bool IsActive;
    }

    static readonly List<HitStopRequest> requests = new();
    static float previousTimeScale = 1f;
    static bool isActive = false;

    /// <summary>현재 히트스톱 활성 여부</summary>
    public static bool IsActive => isActive;

    /// <summary>히트스톱 요청 (언스케일드 시간 기준)</summary>
    /// <param name="duration">정지 지속 시간 (초, 언스케일드)</param>
    public static void Request(float duration)
    {
        if (duration <= 0f) return;

        float endTime = Time.unscaledTime + duration;

        // 기존 요청 중 더 긴 것 있으면 갱신, 없으면 추가
        var existing = requests.Find(r => r.IsActive);
        if (existing != null)
        {
            if (endTime > existing.EndTime)
            {
                existing.EndTime = endTime;
                existing.Duration = duration;
            }
        }
        else
        {
            requests.Add(new HitStopRequest
            {
                Duration = duration,
                EndTime = endTime,
                IsActive = true
            });
        }

        // 첫 요청이면 타임스케일 정지
        if (!isActive)
        {
            ActivateHitStop();
        }
    }

    /// <summary>프레임 단위 히트스톱 (편의 메서드)</summary>
    public static void RequestFrames(int frames)
    {
        Request(frames * 0.0166667f); // 60fps 기준
    }

    static void ActivateHitStop()
    {
        if (isActive) return;

        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        isActive = true;
    }

    /// <summary>매 프레임 호출 필요 (LateUpdate 권장)</summary>
    public static void Update()
    {
        if (!isActive) return;

        float now = Time.unscaledTime;
        bool anyActive = false;
        float maxEndTime = 0f;

        // 만료된 요청 정리, 가장 긴 것 찾기
        for (int i = requests.Count - 1; i >= 0; i--)
        {
            var req = requests[i];
            if (req.IsActive)
            {
                if (now >= req.EndTime)
                {
                    req.IsActive = false;
                }
                else
                {
                    anyActive = true;
                    if (req.EndTime > maxEndTime)
                        maxEndTime = req.EndTime;
                }
            }
        }

        // 모두 만료되면 복구
        if (!anyActive)
        {
            DeactivateHitStop();
        }
    }

    static void DeactivateHitStop()
    {
        Time.timeScale = previousTimeScale;
        isActive = false;
        requests.Clear();
    }

    /// <summary>강제 해제 (씬 전환 등)</summary>
    public static void ForceStop()
    {
        if (isActive)
        {
            Time.timeScale = previousTimeScale;
            isActive = false;
            requests.Clear();
        }
    }

    /// <summary>디버그용 현재 남은 시간</summary>
    public static float RemainingTime
    {
        get
        {
            if (!isActive) return 0f;
            float maxEnd = 0f;
            foreach (var req in requests)
            {
                if (req.IsActive && req.EndTime > maxEnd)
                    maxEnd = req.EndTime;
            }
            return Mathf.Max(0f, maxEnd - Time.unscaledTime);
        }
    }
}

/// <summary>
/// MonoBehaviour 래퍼 (씬에 하나 배치해서 Update에서 HitStop.Update 호출)
/// </summary>
[DisallowMultipleComponent]
public class HitStopUpdater : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // 이미 있으면 생성 안 함
        if (FindAnyObjectByType<HitStopUpdater>() == null)
        {
            var go = new GameObject("[HitStopUpdater]");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            go.AddComponent<HitStopUpdater>();
        }
    }

    void LateUpdate()
    {
        HitStop.Update();
    }
}