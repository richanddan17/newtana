using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CombatAssetValidator
{
    const string SetPath = "Assets/PlayerAnimationSet.asset";
    const string PrefabPath = "Assets/Prefabs/Player.prefab";
    const string ScenePath = "Assets/Scenes/mainscene.unity";

    public static void Validate()
    {
        try
        {
            Run();
            Debug.Log("[CombatValidator] PASS");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[CombatValidator] FAIL: " + e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    static void Run()
    {
        var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(SetPath);
        if (set == null) throw new InvalidOperationException("Missing " + SetPath);

        var body = new Dictionary<string, PlayerAnimationClip>
        {
            { "idle", set.idle }, { "run", set.run }, { "jump", set.jump }, { "fall", set.fall },
            { "crouch", set.crouch }, { "dash", set.dash }, { "attack1", set.attack1 },
            { "attack2", set.attack2 }, { "attack3", set.attack3 }, { "jumpAttack", set.jumpAttack },
            { "upAttack", set.upAttack }, { "downAttack", set.downAttack }, { "heavyAttack", set.heavyAttack },
            { "bow", set.bow }, { "doubleJump", set.doubleJump }, { "wallSlide", set.wallSlide },
            { "climb", set.climb }, { "enterDoor", set.enterDoor }, { "grapple", set.grapple },
            { "cast", set.cast }, { "hurt", set.hurt }, { "hurtKnockback", set.hurtKnockback },
            { "death", set.death }
        };

        var vfx = new Dictionary<string, PlayerAnimationClip>
        {
            { "muzzleBasic", set.muzzleBasic }, { "muzzleRapid", set.muzzleRapid },
            { "muzzleBig", set.muzzleBig }, { "impactBasic", set.impactBasic },
            { "impactRapid", set.impactRapid }, { "impactBig", set.impactBig },
            { "castGather", set.castGather }, { "castChargeLoop", set.castChargeLoop },
            { "castRelease", set.castRelease }, { "castBigLoop", set.castBigLoop },
            { "castBigRelease", set.castBigRelease }, { "castAura", set.castAura },
            { "jumpDust", set.jumpDust }, { "landDust", set.landDust },
            { "dashDust", set.dashDust }, { "airJumpRing", set.airJumpRing }
        };

        int bodyFrames = ValidateGroup("body", body);
        int vfxFrames = ValidateGroup("vfx", vfx);

        if (set.heavyAttack == null || set.heavyAttack.frames.Length != 27)
            throw new InvalidOperationException("heavyAttack must have 27 frames");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Missing " + PrefabPath);

        var anim = prefab.GetComponent<PlayerAnimation>();
        var demo = prefab.GetComponent<CombatAnimationDemo>();
        if (anim == null) throw new InvalidOperationException("Player.prefab has no PlayerAnimation");
        if (demo == null) throw new InvalidOperationException("Player.prefab has no CombatAnimationDemo");

        var animSo = new SerializedObject(anim);
        var setProp = animSo.FindProperty("animationSet");
        var bodyProp = animSo.FindProperty("spriteRenderer");
        if (setProp == null || setProp.objectReferenceValue != set)
            throw new InvalidOperationException("Player.prefab animationSet is not wired");
        if (bodyProp == null || bodyProp.objectReferenceValue == null)
            throw new InvalidOperationException("Player.prefab Body renderer is not wired");

        var demoSo = new SerializedObject(demo);
        var demoAnimProp = demoSo.FindProperty("playerAnimation");
        if (demoAnimProp == null || demoAnimProp.objectReferenceValue != anim)
            throw new InvalidOperationException("CombatAnimationDemo.playerAnimation is not wired");

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PlayerAnimation sceneAnim = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var candidate = root.GetComponentInChildren<PlayerAnimation>(true);
            if (candidate != null)
            {
                sceneAnim = candidate;
                break;
            }
        }
        if (sceneAnim == null) throw new InvalidOperationException("Main scene has no PlayerAnimation");
        if (sceneAnim.GetComponent<CombatAnimationDemo>() == null)
            throw new InvalidOperationException("Main scene Player has no CombatAnimationDemo");

        Debug.Log("[CombatValidator] body=" + body.Count + " clips/" + bodyFrames + " frames, " +
                  "vfx=" + vfx.Count + " clips/" + vfxFrames + " frames, heavy=27, prefab/scene wired");
    }

    static int ValidateGroup(string label, Dictionary<string, PlayerAnimationClip> clips)
    {
        int total = 0;
        foreach (var pair in clips)
        {
            var clip = pair.Value;
            if (clip == null) throw new InvalidOperationException(label + " clip missing: " + pair.Key);
            if (clip.frames == null || clip.frames.Length == 0)
                throw new InvalidOperationException(label + " clip empty: " + pair.Key);
            for (int i = 0; i < clip.frames.Length; i++)
            {
                if (clip.frames[i] == null)
                    throw new InvalidOperationException(label + " clip null frame: " + pair.Key + "[" + i + "]");
            }
            total += clip.frames.Length;
            Debug.Log("[CombatValidator] " + label + " " + pair.Key + " | " + clip.frames.Length +
                      " | " + clip.frameInterval.ToString("0.###") + " | " + clip.loop);
        }
        return total;
    }
}
