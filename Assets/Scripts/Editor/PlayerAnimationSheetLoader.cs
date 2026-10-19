using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// PlayerAnimation 시트 자동 등록 유틸 (에디터 전용 → 빌드 제외).
/// Assets/sprite/Player 구조에서 셀을 읽어 SerializedObject로 시트 필드를 채운다.
/// 로드 규칙 (sprite-asset-spec.md [2] · 실제 파일명과 1:1):
///   body/&lt;상태&gt;/body_&lt;상태&gt;_&lt;방향&gt;.png → 방향별 시트 (front/up45/up90/down45/down90 = AimDir 0~4)
///   body/death.png, body/wake.png, body/idle_dormant.png, body/dash/dash_128wide.png → 단일(방향무관) 프레임 배열
///   vfx/fx_&lt;방향&gt;_muzzle4.png → MuzzleFX (파일명은 _muzzle4지만 3프레임으로 클램프)
/// 셀 정렬은 y 위→아래, x 좌→우 (Unity 기본 셀 순서). Single 모드는 프레임 1개로 등록.
/// 프레임 수 = 실제 셀 수, 프레임 간격은 PlayerAnimation의 Frame Timing 기본값(플랜 [8])을 따른다.
/// </summary>
public static class PlayerAnimationSheetLoader
{
    public const string MenuPath = "Tools/PlayerAnimation/Fill Sheets From Folder";

    const string BodyRoot = "Assets/sprite/Player/body";
    const string VfxRoot = "Assets/sprite/Player/vfx";
    const int MuzzleFxFrameLimit = 3; // _muzzle4 이름에 속지 말 것: 실제 연출 길이는 3프레임

    static readonly string[] DirSuffixes = { "front", "up45", "up90", "down45", "down90" }; // PlayerStateController.AimDir* 순서

    static readonly (string prop, string folder, string pattern, int limit)[] DirectionSets =
    {
        ("aimSprites", BodyRoot + "/aim", "body_aim_{0}.png", -1),
        ("runSprites", BodyRoot + "/run", "body_run_{0}.png", -1),
        ("shootSprites", BodyRoot + "/shoot", "body_shoot_{0}.png", -1),
        ("hurtSprites", BodyRoot + "/hurt", "body_hurt_{0}.png", -1),
        ("jumpSprites", BodyRoot + "/jump", "body_jump_{0}.png", -1),
        ("fallSprites", BodyRoot + "/fall", "body_fall_{0}.png", -1),
        ("muzzleFxSheets", VfxRoot, "fx_{0}_muzzle4.png", MuzzleFxFrameLimit),
    };

    static readonly (string prop, string path)[] SingleSets =
    {
        ("deathFrames", BodyRoot + "/death.png"),
        ("wakeFrames", BodyRoot + "/wake.png"),
        ("dormantFrames", BodyRoot + "/idle_dormant.png"),
        ("dashFrames", BodyRoot + "/dash/dash_128wide.png"),
    };

    [MenuItem(MenuPath)]
    static void FillFromMenu()
    {
        // 선택 오브젝트 기준, 없으면 열린 씬 전체 (컨텍스트메뉴 경유 시 선택이 없을 수 있음)
        var targets = Selection.gameObjects
            .SelectMany(go => go.GetComponentsInChildren<PlayerAnimation>(true))
            .Distinct()
            .ToList();
        if (targets.Count == 0)
            targets = Object.FindObjectsByType<PlayerAnimation>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();

        if (targets.Count == 0)
        {
            Debug.LogWarning("[PlayerAnimationSheetLoader] 찾은 PlayerAnimation이 없음");
            return;
        }

        foreach (var target in targets)
            Fill(target);
    }

    public static void Fill(PlayerAnimation target)
    {
        var so = new SerializedObject(target);

        foreach (var (prop, folder, pattern, limit) in DirectionSets)
        {
            var dirArray = so.FindProperty(prop);
            if (dirArray == null || !dirArray.isArray)
            {
                Debug.LogWarning($"[PlayerAnimationSheetLoader] 필드 없음: {prop}");
                continue;
            }

            dirArray.arraySize = DirSuffixes.Length;
            for (int d = 0; d < DirSuffixes.Length; d++)
            {
                var framesProp = dirArray.GetArrayElementAtIndex(d).FindPropertyRelative("frames");
                if (framesProp == null) continue;
                string path = $"{folder}/{string.Format(pattern, DirSuffixes[d])}";
                WriteFrames(framesProp, LoadFrames(path, limit));
            }
        }

        foreach (var (prop, path) in SingleSets)
        {
            var framesProp = so.FindProperty(prop);
            if (framesProp == null || !framesProp.isArray)
            {
                Debug.LogWarning($"[PlayerAnimationSheetLoader] 필드 없음: {prop}");
                continue;
            }
            WriteFrames(framesProp, LoadFrames(path, -1));
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        Debug.Log($"[PlayerAnimationSheetLoader] 시트 등록 완료: {target.gameObject.name}");
    }

    /// <summary>에셋의 셀 전체를 Unity 셀 순서(y 위→아래, x 좌→우)로 로드. frameLimit>0이면 클램프, Single 모드는 프레임 1개</summary>
    static Sprite[] LoadFrames(string path, int frameLimit)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[PlayerAnimationSheetLoader] 에셋 없음: {path}");
            return System.Array.Empty<Sprite>();
        }

        Sprite[] frames;
        if (importer.spriteImportMode == SpriteImportMode.Multiple)
        {
            frames = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderByDescending(s => s.rect.y) // y 위 → 아래
                .ThenBy(s => s.rect.x)            // x 좌 → 우
                .ToArray();
            if (frameLimit > 0 && frames.Length > frameLimit)
                frames = frames.Take(frameLimit).ToArray();
        }
        else
        {
            var single = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            frames = single != null ? new[] { single } : System.Array.Empty<Sprite>();
        }

        if (frames.Length == 0)
            Debug.LogWarning($"[PlayerAnimationSheetLoader] 로드된 셀 없음 (슬라이스 미설정?): {path}");
        return frames;
    }

    static void WriteFrames(SerializedProperty framesProp, Sprite[] frames)
    {
        framesProp.arraySize = frames.Length;
        for (int i = 0; i < frames.Length; i++)
            framesProp.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
    }
}
