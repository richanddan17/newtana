using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/Scenes/AnimationPreview.unity for visual inspection of the 23 body clips.
/// Batch entry point: -executeMethod AnimationPreviewBuilder.Build
/// </summary>
public static class AnimationPreviewBuilder
{
    const string ScenePath = "Assets/Scenes/AnimationPreview.unity";
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string AnimationSetPath = "Assets/PlayerAnimationSet.asset";

    public static void Build()
    {
        try
        {
            BuildInternal();
            Debug.Log("[AnimationPreview] DONE");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[AnimationPreview] FAILED: " + e);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            else
                throw;
        }
    }

    static void BuildInternal()
    {
        var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(AnimationSetPath);
        if (set == null)
            throw new InvalidOperationException("PlayerAnimationSet not found: " + AnimationSetPath);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Player prefab not found: " + PlayerPrefabPath);

        int expectedFrames = ValidateClips(set, "source");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera + listener.
        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        var camera = cameraGo.AddComponent<Camera>();
        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 1f, -10f);
        camera.orthographicSize = 3f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(0x1E, 0x1E, 0x1E, 0xFF);
        cameraGo.AddComponent<AudioListener>();

        // Player instance (do not unpack, do not modify the prefab asset).
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.name = "Player";
        player.transform.position = Vector3.zero;
        player.transform.rotation = Quaternion.identity;

        Disable<PlayerInputHandler>(player);
        Disable<PlayerStateController>(player);
        Disable<PlayerMovement>(player);
        Disable<WeaponController>(player);
        Disable<PlayerAnimation>(player);
        Disable<CombatAnimationDemo>(player);
        Disable<Health>(player);
        Disable<PlayerCastFx>(player);

        var rb = player.GetComponent<Rigidbody2D>();
        if (rb == null)
            throw new InvalidOperationException("Player prefab has no Rigidbody2D.");
        rb.simulated = false;
        rb.gravityScale = 0f;

        var body = FindChild(player.transform, "Body");
        if (body == null)
            throw new InvalidOperationException("Player prefab has no child named 'Body'.");
        var bodyRenderer = body.GetComponent<SpriteRenderer>();
        if (bodyRenderer == null)
            throw new InvalidOperationException("Player/Body has no SpriteRenderer.");

        // Separate runner root.
        var liveSet = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(AnimationSetPath);
        if (liveSet == null)
            throw new InvalidOperationException("PlayerAnimationSet could not be reloaded after scene creation: " + AnimationSetPath);

        var runnerGo = new GameObject("PreviewRunner");
        var runner = runnerGo.AddComponent<AnimationPreviewRunner>();
        runner.ConfigureAndPreview(liveSet, bodyRenderer, camera);
        VerifyCameraFraming(camera, bodyRenderer, "edit");

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("Failed to save scene: " + ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AnimationPreview] scene saved: " + ScenePath);

        // Reload from disk and validate the persisted scene.
        var reloaded = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ValidateScene(reloaded, liveSet, expectedFrames);
    }

    static void Disable<T>(GameObject root) where T : Behaviour
    {
        var components = root.GetComponentsInChildren<T>(true);
        if (components.Length == 0)
        {
            Debug.LogWarning("[AnimationPreview] component not found: " + typeof(T).Name);
            return;
        }

        foreach (var component in components)
            component.enabled = false;
        Debug.Log("[AnimationPreview] disabled " + typeof(T).Name + " x" + components.Length);
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name == name)
                return transform;
        }
        return null;
    }

    static int ValidateClips(PlayerAnimationSet set, string label)
    {
        if (AnimationPreviewRunner.ClipIds.Length != 23)
            throw new InvalidOperationException("Allowlist must contain 23 ids, found " + AnimationPreviewRunner.ClipIds.Length);

        var errors = new List<string>();
        int totalFrames = 0;

        Debug.Log("[AnimationPreview] clip table (" + label + ")");
        Debug.Log("[AnimationPreview] id | frames | interval | loop");

        foreach (var id in AnimationPreviewRunner.ClipIds)
        {
            var clip = AnimationPreviewRunner.ResolveClip(set, id);
            if (clip == null)
            {
                errors.Add(id + ": missing clip");
                Debug.LogError("[AnimationPreview] " + id + ": missing clip");
                continue;
            }

            if (clip.frames == null || clip.frames.Length == 0)
            {
                errors.Add(id + ": empty frames");
                Debug.LogError("[AnimationPreview] " + id + ": empty frames");
                continue;
            }

            int nullFrames = 0;
            for (int i = 0; i < clip.frames.Length; i++)
            {
                if (clip.frames[i] == null)
                    nullFrames++;
            }
            if (nullFrames > 0)
            {
                errors.Add(id + ": " + nullFrames + " null frame(s)");
                Debug.LogError("[AnimationPreview] " + id + ": " + nullFrames + " null frame(s)");
            }

            totalFrames += clip.frames.Length;
            Debug.Log("[AnimationPreview] " + id + " | " + clip.frames.Length + " | " +
                      clip.frameInterval.ToString("0.###") + " | " + clip.loop);
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Clip validation failed: " + string.Join("; ", errors));

        Debug.Log("[AnimationPreview] clips OK (" + label + "): " + AnimationPreviewRunner.ClipIds.Length +
                  " clips, " + totalFrames + " frames");
        return totalFrames;
    }

    static void ValidateScene(Scene scene, PlayerAnimationSet expectedSet, int expectedFrames)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<AnimationPreviewRunner>();
        if (runner == null)
            throw new InvalidOperationException("AnimationPreviewRunner not found after reload.");

        var runnerSo = new SerializedObject(runner);
        var setProp = runnerSo.FindProperty("set");
        var targetProp = runnerSo.FindProperty("target");
        var set = setProp != null ? setProp.objectReferenceValue as PlayerAnimationSet : null;
        var target = targetProp != null ? targetProp.objectReferenceValue as SpriteRenderer : null;
        if (set == null)
            throw new InvalidOperationException("Runner set is null after reload.");
        if (target == null)
            throw new InvalidOperationException("Runner target is null after reload.");
        if (set != expectedSet)
            throw new InvalidOperationException("Runner set does not match the source asset.");
        Debug.Log("[AnimationPreview] runner reload verified: set=" + AssetDatabase.GetAssetPath(set) +
                  ", target=" + target.name);

        int playerInstances = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(root))
                continue;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == PlayerPrefabPath)
                playerInstances++;
        }
        if (playerInstances != 1)
            throw new InvalidOperationException("Expected 1 Player prefab instance, found " + playerInstances);
        Debug.Log("[AnimationPreview] player prefab instances=1");

        int missingScripts = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        }
        if (missingScripts != 0)
            throw new InvalidOperationException("Found " + missingScripts + " component(s) with unresolved scripts.");
        Debug.Log("[AnimationPreview] unresolved scripts=0");

        int totalFrames = ValidateClips(set, "reloaded");
        if (totalFrames != expectedFrames)
            throw new InvalidOperationException("Frame total changed after reload: " + expectedFrames + " -> " + totalFrames);

        var camera = Camera.main;
        if (camera == null)
            throw new InvalidOperationException("Main camera not found after reload.");
        var body = FindChild(target.transform.root, "Body");
        if (body == null)
            throw new InvalidOperationException("Body child not found after reload.");
        var bodyRenderer = body.GetComponent<SpriteRenderer>();
        if (bodyRenderer == null)
            throw new InvalidOperationException("Body SpriteRenderer not found after reload.");
        VerifyCameraFraming(camera, bodyRenderer, "reloaded");

        Debug.Log("[AnimationPreview] scene reload verified: runner=ok, set=non-null, target=non-null, " +
                  "playerInstances=1, missingScripts=0, clips=23, frames=" + totalFrames);
    }

    static void VerifyCameraFraming(Camera camera, SpriteRenderer body, string label)
    {
        if (camera == null || body == null || body.sprite == null)
            throw new InvalidOperationException("Cannot verify camera framing (" + label + "): missing camera/body/sprite.");

        Bounds bounds = body.bounds;
        float viewBottom = camera.transform.position.y - camera.orthographicSize;
        float viewTop = camera.transform.position.y + camera.orthographicSize;
        float viewLeft = camera.transform.position.x - camera.orthographicSize * camera.aspect;
        float viewRight = camera.transform.position.x + camera.orthographicSize * camera.aspect;
        bool groundVisible = viewBottom <= 0f && viewTop >= 0f;
        bool spriteVisible = bounds.min.y >= viewBottom - 0.001f && bounds.max.y <= viewTop + 0.001f &&
                             bounds.min.x >= viewLeft - 0.001f && bounds.max.x <= viewRight + 0.001f;

        Debug.Log("[AnimationPreview] camera(" + label + "): pos=" + camera.transform.position +
                  ", ortho=" + camera.orthographicSize.ToString("0.###") +
                  ", spriteBounds=" + bounds +
                  ", viewY=[" + viewBottom.ToString("0.###") + "," + viewTop.ToString("0.###") + "]" +
                  ", viewX=[" + viewLeft.ToString("0.###") + "," + viewRight.ToString("0.###") + "]" +
                  ", groundVisible=" + groundVisible +
                  ", spriteVisible=" + spriteVisible +
                  ", feetY=" + bounds.min.y.ToString("0.###"));

        if (!groundVisible)
            throw new InvalidOperationException("Ground line y=0 is outside the camera view (" + label + ").");
        if (!spriteVisible)
            throw new InvalidOperationException("Sprite bounds are outside the camera view (" + label + ").");
    }
}
