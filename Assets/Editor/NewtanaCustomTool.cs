using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class NewtanaCustomTool : EditorWindow
{
    const string MainScenePath = "Assets/Scenes/mainscene.unity";
    const string PreviewScenePath = "Assets/Scenes/AnimationPreview.unity";

    Vector2 scroll;

    [MenuItem("Tools/Newtana Custom Tool")]
    static void Open()
    {
        GetWindow<NewtanaCustomTool>("Newtana Tool");
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        if (Application.isPlaying)
            EditorGUILayout.HelpBox("플레이 모드에서는 툴을 실행할 수 없습니다.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            DrawAnimationSection();
            EditorGUILayout.Space(8);
            DrawSceneSetupSection();
            EditorGUILayout.Space(8);
            DrawSceneSection();
        }

        EditorGUILayout.EndScrollView();
    }

    static void DrawAnimationSection()
    {
        EditorGUILayout.LabelField("Player Animation / Combat", EditorStyles.boldLabel);

        if (GUILayout.Button("Fix Manifest And Import Combat", GUILayout.Height(24)))
            CombatManifestBuilder.FixAndImport();

        if (GUILayout.Button("Import Sheets From Manifest", GUILayout.Height(24)))
            PlayerAnimationSheetImporter.ImportFromManifest();

        if (GUILayout.Button("Regenerate Manifest From Sheets", GUILayout.Height(24)))
            PlayerAnimationSheetImporter.RegenerateManifest();

        if (GUILayout.Button("Apply To Player Prefab", GUILayout.Height(24)))
            ApplyCombatToPlayerPrefab.Apply();

        if (GUILayout.Button("Validate Combat Assets", GUILayout.Height(24)))
            CombatAssetValidator.Validate();
    }

    static void DrawSceneSetupSection()
    {
        EditorGUILayout.LabelField("Scene Setup", EditorStyles.boldLabel);

        if (GUILayout.Button("Tags & Layers", GUILayout.Height(24)))
            ProjectTagsLayersSetup.Setup();

        if (GUILayout.Button("Camera System", GUILayout.Height(24)))
            CameraSystemSetup.Setup();

        if (GUILayout.Button("Room Layout", GUILayout.Height(24)))
            RoomSystemSetup.Setup();

        if (GUILayout.Button("Background Parallax", GUILayout.Height(24)))
            BackgroundSetupUtility.Setup();
    }

    static void DrawSceneSection()
    {
        EditorGUILayout.LabelField("Scenes", EditorStyles.boldLabel);

        if (GUILayout.Button("Build Animation Preview Scene", GUILayout.Height(24)))
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                AnimationPreviewBuilder.Build();
        }

        if (GUILayout.Button("Open Main Scene", GUILayout.Height(24)))
            OpenScene(MainScenePath);

        if (GUILayout.Button("Open Animation Preview Scene", GUILayout.Height(24)))
            OpenScene(PreviewScenePath);
    }

    static void OpenScene(string path)
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
    }
}
