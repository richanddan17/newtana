using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// 풀링 시스템 성능 튜닝 및 프로파일링 도구.
/// 계획서 Phase 8, weapon-projectile-system.md [5] 확장.
/// - 풀 크기 동적 조정
/// - 메모리/GC 압박 모니터링
/// - 런타임 통계 수집
/// </summary>
[DisallowMultipleComponent]
public class PoolProfiler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] ProjectilePool projectilePool;
    [SerializeField] RoomManager roomManager;

    [Header("Settings")]
    [SerializeField] bool enableProfiling = true;
    [SerializeField] float statsUpdateInterval = 1f;
    [SerializeField] bool autoAdjustPoolSizes = true;
    [SerializeField] int minPoolSize = 10;
    [SerializeField] int maxPoolSize = 500;
    [SerializeField] float poolUtilizationTarget = 0.7f; // 70% 활용률 목표

    [Header("UI (Optional)")]
    [SerializeField] TMPro.TextMeshProUGUI statsText;
    [SerializeField] bool showInGameUI = true;

    // 통계
    class PoolStats
    {
        public string PrefabName;
        public int TotalCreated;
        public int ActiveCount;
        public int InactiveCount;
        public int PeakActive;
        public float UtilizationRatio;
        public long TotalMemoryBytes;
    }

    System.Collections.Generic.Dictionary<string, PoolStats> poolStats = new();
    float statsTimer;
    long lastGCMemory;

    void Awake()
    {
        if (projectilePool == null) projectilePool = FindAnyObjectByType<ProjectilePool>();
        if (roomManager == null) roomManager = FindAnyObjectByType<RoomManager>();

        lastGCMemory = Profiler.GetTotalAllocatedMemoryLong();
    }

    void Update()
    {
        if (!enableProfiling) return;

        statsTimer += Time.deltaTime;
        if (statsTimer >= statsUpdateInterval)
        {
            statsTimer = 0f;
            UpdateStats();
            
            if (autoAdjustPoolSizes)
                AdjustPoolSizes();

            if (showInGameUI && statsText != null)
                UpdateStatsUI();
        }

        // GC 메모리 체크
        CheckGCPressure();
    }

    void UpdateStats()
    {
        // ProjectilePool은 내부 풀이 private이므로 리플렉션 또는 공개 API 필요
        // 여기서는 대략적인 통계만 수집
        
        long currentMemory = Profiler.GetTotalAllocatedMemoryLong();
        long memoryDelta = currentMemory - lastGCMemory;
        lastGCMemory = currentMemory;

        // GC 압박 감지
        if (memoryDelta > 1024 * 1024) // 1MB 이상 증가
        {
            Debug.LogWarning($"[PoolProfiler] Memory spike detected: {memoryDelta / 1024f:F1} KB");
        }
    }

    void AdjustPoolSizes()
    {
        // 각 프리팹별 활용률 계산 후 풀 크기 조정
        // ProjectilePool에 공개 메서드 추가 필요:
        // - GetPoolStats(prefabId) -> (total, active, inactive)
        // - ResizePool(prefabId, newSize)
        
        // 임시: 로그만 출력
        if (projectilePool != null)
        {
            // projectilePool.AdjustPoolSizes(poolUtilizationTarget, minPoolSize, maxPoolSize);
        }
    }

    void CheckGCPressure()
    {
        // Gen 2 GC 빈도 체크
        int gen2Count = System.GC.CollectionCount(2);
        // 이전 프레임과 비교하여 급증 시 경고
    }

    void UpdateStatsUI()
    {
        if (statsText == null) return;

        string stats = "=== Pool Profiler ===\n";
        stats += $"Memory: {Profiler.GetTotalAllocatedMemoryLong() / 1024f / 1024f:F1} MB\n";
        stats += $"GC Gen0: {System.GC.CollectionCount(0)} | Gen1: {System.GC.CollectionCount(1)} | Gen2: {System.GC.CollectionCount(2)}\n";
        
        if (projectilePool != null)
        {
            stats += "Projectile Pool: Active\n";
        }

        statsText.text = stats;
    }

    /// <summary>런타임에 풀 통계 출력 (디버그용)</summary>
    [ContextMenu("Log Pool Stats")]
    public void LogPoolStats()
    {
        Debug.Log("=== Pool Stats ===");
        Debug.Log($"Total Memory: {Profiler.GetTotalAllocatedMemoryLong() / 1024f / 1024f:F2} MB");
        Debug.Log($"GC Counts - Gen0: {System.GC.CollectionCount(0)}, Gen1: {System.GC.CollectionCount(1)}, Gen2: {System.GC.CollectionCount(2)}");
    }

    /// <summary>강제 GC 실행 (테스트용)</summary>
    [ContextMenu("Force GC")]
    public void ForceGC()
    {
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        Debug.Log("[PoolProfiler] Forced GC");
    }
}

/// <summary>
/// ProjectilePool 확장: 통계/리사이즈 API
/// ProjectilePool.cs에 추가할 메서드들
/// </summary>
public static class ProjectilePoolExtensions
{
    /// <summary>풀 통계 반환</summary>
    public static (int total, int active, int inactive) GetPoolStats(this ProjectilePool pool, GameObject prefab)
    {
        // ProjectilePool 내부 구현 필요
        return (0, 0, 0);
    }

    /// <summary>풀 크기 동적 조정</summary>
    public static void ResizePool(this ProjectilePool pool, GameObject prefab, int newSize)
    {
        // ProjectilePool 내부 구현 필요
    }

    /// <summary>모든 풀 자동 조정</summary>
    public static void AutoAdjustAllPools(this ProjectilePool pool, float targetUtilization, int minSize, int maxSize)
    {
        // ProjectilePool 내부 구현 필요
    }
}