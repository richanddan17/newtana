using UnityEditor;
using UnityEngine;

public static class ApplyCombatToPlayerPrefab
{
    const string SetAssetPath = "Assets/PlayerAnimationSet.asset";
    const string PrefabPath = "Assets/Prefabs/Player.prefab";

    const string BodyName = "Body";
    const string CastFxName = "CastFX";
    const string AuraFxName = "AuraFX";
    const string MuzzleName = "Muzzle";
    const string MuzzleFxName = "MuzzleFX";
    const string CastPointName = "CastPoint";

    public static void Apply()
    {
        var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(SetAssetPath);
        if (set == null)
        {
            Debug.LogError("[ApplyCombatToPrefab] PlayerAnimationSet.asset not found: " + SetAssetPath);
            return;
        }

        var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError("[ApplyCombatToPrefab] Player prefab not found: " + PrefabPath);
            return;
        }

        string path = AssetDatabase.GetAssetPath(prefabRoot);
        GameObject root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            var body = EnsureRendererChild(root.transform, BodyName, 0);
            var castFx = EnsureRendererChild(root.transform, CastFxName, 5);
            var auraFx = EnsureRendererChild(root.transform, AuraFxName, -1);

            var muzzle = EnsureChild(root.transform, MuzzleName);
            var muzzleFx = EnsureRendererChild(muzzle, MuzzleFxName, 10);
            EnsureChild(muzzle, CastPointName);

            var anim = root.GetComponent<PlayerAnimation>();
            if (anim != null)
            {
                var so = new SerializedObject(anim);
                SetRef(so, "animationSet", set);
                SetRef(so, "spriteRenderer", body);
                SetRef(so, "castFxRenderer", castFx);
                SetRef(so, "auraFxRenderer", auraFx);
                SetRef(so, "muzzleTransform", muzzle);
                SetRef(so, "muzzleFxRenderer", muzzleFx);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(anim);

                var demo = root.GetComponent<CombatAnimationDemo>();
                if (demo == null) demo = root.AddComponent<CombatAnimationDemo>();
                var demoSo = new SerializedObject(demo);
                SetRef(demoSo, "playerAnimation", anim);
                demoSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(demo);
            }
            else
            {
                Debug.LogWarning("[ApplyCombatToPrefab] PlayerAnimation component not found on prefab root");
            }

            var health = root.GetComponent<Health>();
            if (health != null)
            {
                var so = new SerializedObject(health);
                SetRef(so, "spriteRenderer", body);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(health);
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ApplyCombatToPrefab] Applied animation set to Player.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static Transform EnsureChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static SpriteRenderer EnsureRendererChild(Transform parent, string name, int sortingOrder)
    {
        var t = EnsureChild(parent, name);
        var renderer = t.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = t.gameObject.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    static void SetRef(SerializedObject so, string propertyName, Object value)
    {
        var prop = so.FindProperty(propertyName);
        if (prop != null && value != null) prop.objectReferenceValue = value;
    }
}
