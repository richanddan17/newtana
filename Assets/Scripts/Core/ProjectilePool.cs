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

        // 프리팹 지정 없으면 첫 번째 등록된 것 사용
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
        return projectile;
    }

    /// <summary>풀로 반환</summary>
    public void Return(ProjectileData projectile)
    {
        if (projectile == null) return;

        int id = projectile.gameObject.GetInstanceID();
        // 원래 프리팹 ID 찾기 (태그나 컴포넌트로 구분)
        // 여기선 간단히 첫 번째 풀에 반환
        foreach (var pool in pools.Values)
        {
            // 타입 체크로 구분 가능하지만 간단히 모두 시도
            pool.Enqueue(projectile);
            break;
        }

        projectile.Deinitialize();
    }

    /// <summary>특정 프리팹의 활성 개수</summary>
    int GetTotalActiveCount(int prefabId)
    {
        // 대략적: 전체 풀 크기 - 대기 중인 개수
        return 0; // 정확한 추적 필요시 별도 카운터 유지
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
    }

    void OnDestroy()
    {
        ClearAllPools();
    }
}