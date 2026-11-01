using UnityEditor;
using UnityEngine;

public static class ApplyCombatTuning
{
    [MenuItem("Newtana Custom Tool/Apply Combat Tuning")]
    public static void Apply()
    {
        var prefabPath = "Assets/Prefabs/Player.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("[ApplyCombatTuning] Player.prefab not found at " + prefabPath);
            return;
        }

        var combat = prefab.GetComponent<PlayerCombatController>();
        var demo = prefab.GetComponent<CombatAnimationDemo>();
        var movement = prefab.GetComponent<PlayerMovement>();

        bool dirty = false;

        if (combat != null)
        {
            Undo.RecordObject(combat, "Apply Combat Tuning");
            combat.attack1DashStopDistance = 0.1f;
            combat.attack1DashRange = 1.0f;
            combat.attack1DashDuration = 0.15f;
            combat.attack3TeleportBehindOffset = 0.2f;
            combat.attack3TeleportRange = 1.0f;
            combat.attack3TeleportFrame = 4;
            combat.attack1Radius = 0.5f;
            combat.attack2Radius = 0.5f;
            combat.attack3Radius = 0.5f;
            combat.heavyRadius = 0.8f;
            combat.attack1Knockback = 1.0f;
            combat.attack2Knockback = 0.7f;
            combat.attack3Knockback = 1.0f;
            combat.heavyKnockback = 5.0f;
            combat.attack3ShakeAmplitude = 0.01f;
            combat.attack3ShakeDuration = 0.26f;
            combat.heavyShakeAmplitude = 0.02f;
            combat.heavyShakeDuration = 0.42f;
            dirty = true;
        }

        if (demo != null)
        {
            Undo.RecordObject(demo, "Apply Combat Tuning");
            demo.heavyLungeDistance = 0f;
            dirty = true;
        }

        if (movement != null)
        {
            Undo.RecordObject(movement, "Apply Combat Tuning");
            // movement에 적용할 튜닝값 있으면 여기에 추가
            dirty = true;
        }

        if (dirty)
        {
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ApplyCombatTuning] Player.prefab 튜닝 값 적용 완료");
        }
        else
        {
            Debug.LogWarning("[ApplyCombatTuning] 대상 컴포넌트를 찾지 못함");
        }
    }
}