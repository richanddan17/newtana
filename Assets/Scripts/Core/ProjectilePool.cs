using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 투사체 오브젝트 풀링.
/// 계획서 weapon-projectile-system.md [5], hitbox-hurtbox-system.md [5] 반영.
/// 프리팹별 풀 관리, 자동 확장, 상태 완전 초기화.
/// </summary>
public class ProjectilePool : MonoBehaviour
{
    [Header("Pool Settings")]
    [SerializeField] int initialPoolSize = 50;
    [SerializeField] int maxPoolSize = 200;
    [SerializeField] bool autoExpand = true; // 풀이 비면 자동 확장

    [Header("Projectile Prefabs (WeaponData에서 참조하지만 풀 등록용)")]
    [SerializeField] GameObject[] projectilePrefabs;

    // 풀 저장소: 프리팹 인스턴스 ID -> 큐
    readonly Dictionary<int, Queue<ProjectileData>> pools = new();
    readonly Dictionary<int, GameObject> prefabMap = new();

    // 투사체 GameObject 인스턴스 ID -> 원본 프리팹 인스턴스 ID (Return 시 소유 풀 판별용)
    readonly Dictionary<int, int> projectileToPrefab = new();

    // 프리팹 인스턴스 ID -> 풀 밖(활성) 투사체 수
    readonly Dictionary<int, int> activeCounts = new();

    // 싱글톤 접근용
    public static ProjectilePool Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 프리팹 사전 등록
        foreach (var prefab in projectilePrefabs)
        {
            if (prefab != null)
                RegisterPrefab(prefab);
        }

        // 초기 풀 생성
        foreach (var kvp in prefabMap)
        {
            WarmPool(kvp.Key, initialPoolSize);
        }
    }

    /// <summary>프리팹을 풀에 등록 (WeaponData.ProjectilePrefab 할당 시 호출)</summary>
    public void RegisterPrefab(GameObject prefab)
    {
        if (prefab == null) return;
        
        int id = prefab.GetInstanceID();
        if (!prefabMap.ContainsKey(id))
        {
            prefabMap[id] = prefab;
            pools[id] = new Queue<ProjectileData>();
        }
    }

    /// <summary>풀 예열 (초기 생성)</summary>
    public void WarmPool(int prefabId, int count)
    {
        if (!prefabMap.TryGetValue(prefabId, out var prefab)) return;

        for (int i = 0; i < count; i++)
        {
            var proj = CreateProjectile(prefab);
            proj.Deinitialize();
            pools[prefabId].Enqueue(proj);
        }
    }

    /// <summary>풀에서 투사체 꺼내기 (자동 등록 지원)</summary>
    public ProjectileData Get(GameObject prefab = null)
    {
        GameObject targetPrefab = prefab;

        // 폴백 명시: 프리팹 미지정(null) 시 첫 번째 등록 프리팹의 풀을 기본으로 사용한다.
        if (targetPrefab == null)
        {
            foreach (var p in prefabMap.Values)
            {
                targetPrefab = p;
                break;
            }
        }

        if (targetPrefab == null)
        {
            Debug.LogError("[ProjectilePool] No prefab registered!");
            return null;
        }

        int id = targetPrefab.GetInstanceID();
        
        // 미등록 프리팹이면 자동 등록
        if (!pools.ContainsKey(id))
        {
            RegisterPrefab(targetPrefab);
            WarmPool(id, Mathf.Min(10, initialPoolSize));
        }

        var pool = pools[id];

        // 풀에서 꺼내기
        ProjectileData projectile;
        if (pool.Count > 0)
        {
            projectile = pool.Dequeue();
        }
        else if (autoExpand && GetTotalActiveCount(id) < maxPoolSize)
        {
            projectile = CreateProjectile(targetPrefab);
        }
        else
        {
            // 풀 꽉 참: 가장 오래된 것 재사용 (옵션)
            if (pool.Count > 0)
            {
                projectile = pool.Dequeue();
            }
            else
            {
                Debug.LogWarning("[ProjectilePool] Pool exhausted!");
                return null;
            }
        }

        // 활성화 상태로 반환 (Initialize는 호출부에서)
        projectile.gameObject.SetActive(true);

        // 소유 프리팹 추적: Return()이 정확한 풀로 되돌리도록 함
        projectileToPrefab[projectile.gameObject.GetInstanceID()] = id;
        activeCounts[id] = GetTotalActiveCount(id) + 1;
        return projectile;
    }

    /// <summary>풀로 반환 (투사체가 발급된 프리팹의 풀로 되돌림)</summary>
    public void Return(ProjectileData projectile)
    {
        if (projectile == null) return;

        int projId = projectile.gameObject.GetInstanceID();

        // Get() 발급 시 기록한 원본 프리팹으로 소유 풀을 판별한다.
        if (!projectileToPrefab.TryGetValue(projId, out int prefabId) ||
            !pools.TryGetValue(prefabId, out var pool))
        {
            // 소유 풀을 알 수 없는 투사체는 잘못된 풀에 섞이지 않도록 파괴한다.
            Debug.LogWarning($"[ProjectilePool] Untracked projectile '{projectile.name}' returned. Destroying instead of pooling.");
            projectileToPrefab.Remove(projId);
            Destroy(projectile.gameObject);
            return;
        }

        projectileToPrefab.Remove(projId);
        activeCounts[prefabId] = Mathf.Max(0, GetTotalActiveCount(prefabId) - 1);

        pool.Enqueue(projectile);
        projectile.Deinitialize();
    }

    /// <summary>특정 프리팹의 활성(풀 밖) 투사체 수</summary>
    int GetTotalActiveCount(int prefabId)
    {
        return activeCounts.TryGetValue(prefabId, out int count) ? count : 0;
    }

    /// <summary>모든 풀의 활성 투사체 총합</summary>
    public int GetTotalActiveCount()
    {
        int total = 0;
        foreach (var count in activeCounts.Values)
            total += count;
        return total;
    }

    ProjectileData CreateProjectile(GameObject prefab)
    {
        var obj = Instantiate(prefab, transform);
        obj.SetActive(false);
        var proj = obj.GetComponent<ProjectileData>();
        if (proj == null)
        {
            proj = obj.AddComponent<ProjectileData>();
        }
        return proj;
    }

    /// <summary>씬 전환/리셋 시 모든 풀 정리</summary>
    public void ClearAllPools()
    {
        foreach (var pool in pools.Values)
        {
            while (pool.Count > 0)
            {
                var proj = pool.Dequeue();
                if (proj != null) Destroy(proj.gameObject);
            }
        }
        pools.Clear();
        prefabMap.Clear();
        projectileToPrefab.Clear();
        activeCounts.Clear();
    }

    void OnDestroy()
    {
        ClearAllPools();
    }
}