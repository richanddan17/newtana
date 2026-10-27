using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 프로젝트 태그/레이어 일괄 셋업 유틸 (에디터 전용 → 빌드 제외).
/// TagManager.asset을 직접 편집해 코드가 기대하는 태그·레이어 이름을 보장한다.
///   태그: Player / Enemy / Neutral (FindGameObjectWithTag·CompareTag 계약)
///   User Layer: 3=Ground, 6=Player, 7=Enemy, 8=Neutral (Team.LayerIndex / GroundCheck 기본값)
///   Sorting Layer: BG_Far / BG_Main / FG / Player (배경 패럴랙스 + 플레이어 정렬 계약)
/// idempotent: 이미 있는 항목은 건너뛰고 없는 것만 채운다.
/// </summary>
public static class ProjectTagsLayersSetup
{
    const string TagManagerPath = "ProjectSettings/TagManager.asset";

    // 태그 (내장 태그 외 사용자 태그)
    static readonly string[] RequiredTags = { "Player", "Enemy", "Neutral" };

    // User Layer 이름 (인덱스 = 배열 위치). Unity 내장: 0=Default,1=TransparentFX,2=Ignore Raycast,4=Water,5=UI
    static readonly (int index, string name)[] RequiredLayers =
    {
        (3, "Ground"),   // GroundCheck/PlayerMovement/ProjectileData 기본 지형 마스크
        (6, "Player"),   // Team.Player.LayerIndex()
        (7, "Enemy"),    // Team.Enemy.LayerIndex()
        (8, "Neutral"),  // Team.Neutral.LayerIndex()
    };

    // Sorting Layer (배열 순서 = 렌더 정렬 순서, 뒤일수록 앞)
    static readonly (string name, int id)[] RequiredSortingLayers =
    {
        ("BG_Far", 10000),
        ("BG_Main", 10001),
        ("FG", 10002),
        ("Player", 10003),
    };

    public static void Setup()
    {
        int tags = EnsureTags();
        int layers = EnsureLayers();
        int sorting = EnsureSortingLayers();
        AssetDatabase.SaveAssets();

        Debug.Log($"[태그·레이어 셋업] 완료 — 태그 {tags}개 / User Layer {layers}개 / Sorting Layer {sorting}개 추가. " +
            "이미 있던 항목은 유지했습니다.");
    }

    /// <summary>에셋/씬 셋업 스크립트가 태그 정의를 전제할 때 호출하는 무음 보장 함수 (로그 최소화)</summary>
    public static void EnsureAllQuiet()
    {
        EnsureTags();
        EnsureLayers();
        EnsureSortingLayers();
    }

    static SerializedObject LoadTagManager()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning("[태그·레이어 셋업] TagManager 로드 실패: " + TagManagerPath);
            return null;
        }
        return new SerializedObject(assets[0]);
    }

    static int EnsureTags()
    {
        var so = LoadTagManager();
        if (so == null) return 0;

        var tags = so.FindProperty("tags");
        if (tags == null || !tags.isArray)
        {
            Debug.LogWarning("[태그·레이어 셋업] tags 프로퍼티 없음");
            return 0;
        }

        var existing = new HashSet<string>();
        for (int i = 0; i < tags.arraySize; i++)
            existing.Add(tags.GetArrayElementAtIndex(i).stringValue);

        int added = 0;
        foreach (var tag in RequiredTags)
        {
            if (existing.Contains(tag)) continue;
            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            existing.Add(tag);
            added++;
        }
        so.ApplyModifiedProperties();
        return added;
    }

    static int EnsureLayers()
    {
        var so = LoadTagManager();
        if (so == null) return 0;

        var layers = so.FindProperty("layers");
        if (layers == null || !layers.isArray)
        {
            Debug.LogWarning("[태그·레이어 셋업] layers 프로퍼티 없음");
            return 0;
        }

        int added = 0;
        foreach (var (index, name) in RequiredLayers)
        {
            if (index < 0 || index >= layers.arraySize)
            {
                Debug.LogWarning($"[태그·레이어 셋업] 레이어 인덱스 범위 밖: {index} ({name})");
                continue;
            }
            var entry = layers.GetArrayElementAtIndex(index);
            // Unity는 빈 User Layer 슬롯을 빈 문자열로 둔다. 이미 다른 이름이 있으면 덮어쓰지 않는다.
            if (!string.IsNullOrEmpty(entry.stringValue))
            {
                if (entry.stringValue != name)
                    Debug.LogWarning($"[태그·레이어 셋업] User Layer {index}에 이미 '{entry.stringValue}'가 있어 '{name}'를 건너뜁니다 — 수동 확인 필요.");
                continue;
            }
            entry.stringValue = name;
            added++;
        }
        so.ApplyModifiedProperties();
        return added;
    }

    static int EnsureSortingLayers()
    {
        var so = LoadTagManager();
        if (so == null) return 0;

        var sorting = so.FindProperty("m_SortingLayers");
        if (sorting == null || !sorting.isArray)
        {
            Debug.LogWarning("[태그·레이어 셋업] m_SortingLayers 없음");
            return 0;
        }

        var existing = new List<string>();
        for (int i = 0; i < sorting.arraySize; i++)
            existing.Add(sorting.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);

        int added = 0;
        foreach (var (name, id) in RequiredSortingLayers)
        {
            if (existing.Contains(name)) continue;
            sorting.InsertArrayElementAtIndex(sorting.arraySize);
            var entry = sorting.GetArrayElementAtIndex(sorting.arraySize - 1);
            entry.FindPropertyRelative("name").stringValue = name;
            entry.FindPropertyRelative("uniqueID").intValue = id;
            entry.FindPropertyRelative("locked").boolValue = false;
            existing.Add(name);
            added++;
        }
        so.ApplyModifiedProperties();
        return added;
    }
}
