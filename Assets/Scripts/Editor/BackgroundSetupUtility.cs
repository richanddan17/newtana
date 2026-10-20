using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 배경/패럴랙스 구조 자동 배치 유틸 (에디터 전용 → 빌드 제외).
/// 선택한 Room 아래에 Background 구조를 생성한다 (background-parallax-system.md [1][2][5]).
/// 구조: Room/Background/{BG_SkySprite, BG_Cloud2, BG_Cloud1, BG_Main}
/// Idempotent: 이미 있는 자식은 재사용하고 중복 생성하지 않는다 (Transform 수동 조작값은 유지).
/// Sorting Layer(BG_Far/BG_Main/FG)는 TagManager.asset에 없으면 추가한다.
/// </summary>
public static class BackgroundSetupUtility
{
    public const string MenuPath = "newtana/배경 패럴랙스 배치";

    const string BackgroundName = "Background";
    const string TagManagerPath = "ProjectSettings/TagManager.asset";
    const string BgFarLayer = "BG_Far";
    const string BgMainLayer = "BG_Main";
    const string FgLayer = "FG";
    const string UndoName = "배경 패럴랙스 배치";

    // (자식 이름, 스프라이트 경로, Sorting Layer, orderInLayer, parallaxX, parallaxY) — 플랜 [2] 표의 시작값
    static readonly (string name, string path, string layer, int order, float px, float py)[] Layers =
    {
        ("BG_SkySprite", "Assets/sprite/background/nuvens/nuvens_3.png", BgFarLayer,  0, 0.10f, 0.05f),
        ("BG_Cloud2",    "Assets/sprite/background/nuvens/nuvens_2.png", BgFarLayer,  1, 0.20f, 0.10f),
        ("BG_Cloud1",    "Assets/sprite/background/nuvens/nuvens_1.png", BgFarLayer,  2, 0.35f, 0.15f),
        ("BG_Main",      "Assets/sprite/background/subway/subway_BG.png", BgMainLayer, 0, 0.70f, 0.50f),
    };

    [MenuItem(MenuPath)]
    static void SetupFromMenu()
    {
        // 태그·레이어·정렬 레이어를 먼저 보장 (BG_Far/BG_Main/FG + Player 태그/레이어)
        ProjectTagsLayersSetup.EnsureAllQuiet();

        // 선택된 오브젝트가 Room이면 그걸 사용, 아니면 씬의 Room을 찾고, 없으면 생성한다.
        var selected = Selection.activeGameObject;
        Room room = selected != null ? selected.GetComponent<Room>() : null;
        if (room == null)
            room = Object.FindAnyObjectByType<Room>(FindObjectsInactive.Include);
        if (room == null)
        {
            var roomGo = new GameObject("Room");
            Undo.RegisterCreatedObjectUndo(roomGo, UndoName);
            roomGo.transform.position = Vector3.zero;
            room = Undo.AddComponent<Room>(roomGo);
            Debug.Log("[BackgroundSetupUtility] 씬에 Room이 없어 'Room' 오브젝트를 새로 만들었습니다.");
        }

        EnsureSortingLayers();

        int created = 0, reused = 0;

        // Background 빈 GO (Room 자식). localPosition=0 — ParallaxLayer이 초기 위치를 방 배경 기준점으로 캐시
        var background = room.transform.Find(BackgroundName);
        if (background == null)
        {
            var go = new GameObject(BackgroundName);
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            Undo.SetTransformParent(go.transform, selected.transform, UndoName);
            go.transform.localPosition = Vector3.zero;
            background = go.transform;
            created++;
        }
        else
        {
            reused++;
        }

        foreach (var (name, path, layer, order, px, py) in Layers)
        {
            var child = background.Find(name);
            if (child == null)
            {
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, UndoName);
                Undo.SetTransformParent(go.transform, background, UndoName);
                go.transform.localPosition = Vector3.zero;
                child = go.transform;
                created++;
            }
            else
            {
                // 재사용 시 Transform 수동 조작값 유지 (플랜 [5] — 실제 수치는 씬에서 조작)
                reused++;
            }

            var renderer = child.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = Undo.AddComponent<SpriteRenderer>(child.gameObject);
            renderer.sprite = LoadFirstSprite(path);
            renderer.sortingLayerName = layer;
            renderer.sortingOrder = order;

            var parallax = child.GetComponent<ParallaxLayer>();
            if (parallax == null)
                parallax = Undo.AddComponent<ParallaxLayer>(child.gameObject);

            var so = new SerializedObject(parallax);
            so.FindProperty("parallaxX").floatValue = px;
            so.FindProperty("parallaxY").floatValue = py;
            so.FindProperty("anchor").objectReferenceValue = background;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log($"[BackgroundSetupUtility] 배경 배치 완료: {selected.name} — 생성 {created}개 / 재사용 {reused}개");
    }

    /// <summary>TagManager.asset에 BG_Far/BG_Main/FG가 없으면 추가 (uniqueId 10000~10002).
    /// Sorting Layer 순서 = m_SortingLayers 배열 순서이므로 추가 위치가 곧 정렬 순서가 된다</summary>
    static void EnsureSortingLayers()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning("[BackgroundSetupUtility] TagManager 로드 실패");
            return;
        }

        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("m_SortingLayers");
        if (layers == null || !layers.isArray)
        {
            Debug.LogWarning("[BackgroundSetupUtility] m_SortingLayers 없음");
            return;
        }

        var existingNames = new List<string>();
        for (int i = 0; i < layers.arraySize; i++)
            existingNames.Add(layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);

        // 삽입 후 name/uniqueID/locked 속성만 존재
        (string name, int id)[] toAdd = { (BgFarLayer, 10000), (BgMainLayer, 10001), (FgLayer, 10002) };
        foreach (var (layerName, id) in toAdd)
        {
            if (existingNames.Contains(layerName)) continue;

            layers.InsertArrayElementAtIndex(layers.arraySize);
            var entry = layers.GetArrayElementAtIndex(layers.arraySize - 1);
            entry.FindPropertyRelative("name").stringValue = layerName;
            var idProp = entry.FindPropertyRelative("uniqueID") ?? entry.FindPropertyRelative("uniqueId");
            if (idProp != null) idProp.intValue = id;
            var lockedProp = entry.FindPropertyRelative("locked");
            if (lockedProp != null) lockedProp.boolValue = false;
        }

        so.ApplyModifiedProperties();
    }

    /// <summary>에셋의 Sprite 하위 에셋을 이름 순으로 불러 첫 셀 반환. SpriteRenderer는 단일 스프라이트만 표시 가능</summary>
    static Sprite LoadFirstSprite(string path)
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .OrderBy(s => s.name)
            .ToArray();
        if (sprites.Length == 0)
        {
            Debug.LogWarning($"[BackgroundSetupUtility] 스프라이트 없음 (경로/임포트 모드 확인): {path}");
            return null;
        }
        return sprites[0];
    }
}
