using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 플레이어 시트 임포트 도구 (v5 player_1).
///   Newtana Custom Tool > Import Sheets From Manifest
///   1) 매니페스트(player_animation_manifest.json) 기준으로 각 시트를 Multiple 스프라이트로 슬라이스한다.
///      - 셀 rect = 내용 있는 셀만 row-major, 좌표는 텍스처 좌하단 원점
///      - pivot = 시트별 계산값 (motion=발 기준, vfx=내용 중심/지면 접촉)
///      - PPU 1200 → 몸 높이 288px = 0.24 유닛 (콜라이더 0.22×0.24 와 동일 스케일)
///   2) PlayerAnimationSet ScriptableObject 을 갱신한다.
///   3) 씬의 Player 에 Body/CastFX/AuraFX 계층을 만들고 배선한다.
/// </summary>
public static class PlayerAnimationSheetImporter
{
    const string ManifestAssetPath = "Assets/sprite/Player/player/player_animation_manifest.json";
    const string RootDir = "Assets/sprite/Player/player";
    const string MotionDir = RootDir + "/motion";
    const string VfxDir = RootDir + "/vfx";
    const string SetAssetPath = "Assets/PlayerAnimationSet.asset";
    const string MainScenePath = "Assets/Scenes/mainscene.unity";

    const string BodyName = "Body";
    const string CastFxName = "CastFX";
    const string AuraFxName = "AuraFX";
    const string MuzzleName = "Muzzle";
    const string MuzzleFxName = "MuzzleFX";
    const string CastPointName = "CastPoint";

    public static void ImportFromManifest()
    {
        var manifest = LoadManifest();
        if (manifest == null) return;

        var log = new List<string>();
        var frameCounts = new Dictionary<string, int>();

        foreach (var entry in manifest.motion)
        {
            int count = ApplySheet(MotionDir, entry, manifest.ppu, log);
            frameCounts[entry.id] = count;
        }
        foreach (var entry in manifest.vfx)
        {
            int count = ApplySheet(VfxDir, entry, manifest.ppu, log);
            frameCounts[entry.id] = count;
        }

        var set = LoadOrCreateSet();
        BuildSet(set, manifest, frameCounts);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        WirePlayer(set);

        Debug.Log("[플레이어 시트 임포트] 완료 — 모션 " + manifest.motion.Length + "장, VFX " + manifest.vfx.Length + "장. " +
                  "세트: " + SetAssetPath + " / 플레이어 계층 배선 완료." +
                  (log.Count > 0 ? "\n" + string.Join("\n", log.ToArray()) : ""));
    }

    public static void RegenerateManifest()
    {
        var manifest = PlayerSheetGridDetector.Detect(RootDir);
        string json = PlayerSheetGridDetector.ToJson(manifest);
        File.WriteAllText(ManifestAssetPath, json, new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(ManifestAssetPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("[매니페스트 재생성] " + ManifestAssetPath + " — 모션 " + manifest.motion.Length + "장, VFX " + manifest.vfx.Length + "장.");
    }

    /// <summary>배치모드 진입점: 씬을 열어 시트 임포트 → 카메라/룸 셋업 → 씬 저장 후 종료.</summary>
    public static void BatchImport()
    {
        try
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            ImportFromManifest();
            CameraSystemSetup.Setup();
            RoomSystemSetup.Setup();

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[배치 임포트] 완료 — 세트 " + SetAssetPath + ", 씬 " + MainScenePath + " 저장됨.");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[배치 임포트] 실패: " + e);
            EditorApplication.Exit(1);
        }
    }

    static PlayerSheetGridDetector.Manifest LoadManifest()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestAssetPath);
        if (asset == null)
        {
            Debug.LogError("[플레이어 시트 임포트] 매니페스트 없음: " + ManifestAssetPath +
                           "\n'Newtana Custom Tool > Regenerate Manifest From Sheets' 를 먼저 실행하세요.");
            return null;
        }

        var manifest = JsonUtility.FromJson<PlayerSheetGridDetector.Manifest>(asset.text);
        if (manifest == null || manifest.motion == null || manifest.vfx == null)
        {
            Debug.LogError("[플레이어 시트 임포트] 매니페스트 파싱 실패: " + ManifestAssetPath);
            return null;
        }
        return manifest;
    }

    /// <summary>시트 하나를 Multiple 스프라이트로 슬라이스하고, 로드된 프레임 수를 반환한다.</summary>
    static int ApplySheet(string dir, PlayerSheetGridDetector.SheetEntry entry, int ppu, List<string> log)
    {
        string assetPath = ResolveAssetPath(dir, entry.file);
        int cellCount = entry.cells == null ? 0 : entry.cells.Length / 2;
        if (cellCount == 0)
        {
            log.Add("스킵 (프레임 없음): " + assetPath);
            return 0;
        }

        var metas = new SpriteMetaData[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            int col = entry.cells[i * 2];
            int row = entry.cells[i * 2 + 1];
            var meta = new SpriteMetaData
            {
                name = entry.id + "_" + i,
                alignment = (int)SpriteAlignment.Custom,
                pivot = new Vector2(entry.pivot[0], entry.pivot[1]),
                border = Vector4.zero
            };
            meta.rect = new Rect(
                col * entry.cellW,
                entry.texH - (row + 1) * entry.cellH,
                entry.cellW,
                entry.cellH);
            metas[i] = meta;
        }

        // 최초 임포트 시 AssetImportWorker 와 경합해 설정이 덮이는 경우가 있어, 슬라이스 결과까지 검증하며 재적용한다.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                log.Add("스킵 (TextureImporter 없음): " + assetPath);
                return 0;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = entry.ppu > 0 ? entry.ppu : ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.spritesheet = metas;
            importer.SaveAndReimport();

            if (HasSlicedSprites(assetPath, entry.id, cellCount)) return cellCount;

            log.Add("슬라이스 미적용, 재시도 " + attempt + "/3: " + assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        log.Add("슬라이스 실패: " + assetPath);
        return 0;
    }

    static string ResolveAssetPath(string dir, string file)
    {
        string direct = Path.Combine(dir, file);
        if (File.Exists(direct)) return direct.Replace('\\', '/');

        string fileName = Path.GetFileName(file);
        string[] matches = Directory.GetFiles(dir, fileName, SearchOption.AllDirectories);
        return matches.Length > 0 ? matches[0].Replace('\\', '/') : direct.Replace('\\', '/');
    }

    static bool HasSlicedSprites(string assetPath, string sheetId, int expectedCount)
    {
        var names = new HashSet<string>();
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            var sprite = obj as Sprite;
            if (sprite != null) names.Add(sprite.name);
        }
        for (int i = 0; i < expectedCount; i++)
        {
            if (!names.Contains(sheetId + "_" + i)) return false;
        }
        return true;
    }

    static PlayerAnimationSet LoadOrCreateSet()
    {
        var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(SetAssetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<PlayerAnimationSet>();
            AssetDatabase.CreateAsset(set, SetAssetPath);
        }
        return set;
    }

    static void BuildSet(PlayerAnimationSet set, PlayerSheetGridDetector.Manifest manifest,
                         Dictionary<string, int> frameCounts)
    {
        set.idle = Clip(set, "idle", manifest, frameCounts, 0.05f, true);
        set.run = Clip(set, "run", manifest, frameCounts, 0.05f, true);
        set.jump = Clip(set, "jump", manifest, frameCounts, 0.04f, false);
        set.fall = Clip(set, "fall", manifest, frameCounts, 0.04f, true);
        set.crouch = Clip(set, "crouch", manifest, frameCounts, 0.06f, false);
        set.dash = Clip(set, "dash", manifest, frameCounts, 0.035f, false);

        set.attack1 = Clip(set, "attack1", manifest, frameCounts, 0.05f, false);
        set.attack2 = Clip(set, "attack2", manifest, frameCounts, 0.05f, false);
        set.attack3 = Clip(set, "attack3", manifest, frameCounts, 0.05f, false);
        set.jumpAttack = Clip(set, "jumpAttack", manifest, frameCounts, 0.05f, false);
        set.upAttack = Clip(set, "upAttack", manifest, frameCounts, 0.05f, false);
        set.downAttack = Clip(set, "downAttack", manifest, frameCounts, 0.05f, false);
        set.heavyAttack = Clip(set, "heavyAttack", manifest, frameCounts, 0.06f, false);
        set.bow = Clip(set, "bow", manifest, frameCounts, 0.05f, false);
        set.arrowRelease = Clip(set, "arrowRelease", manifest, frameCounts, 0.03f, false);
        set.energyArrow = Clip(set, "energyArrow", manifest, frameCounts, 0.04f, true);
        set.arrowImpact = Clip(set, "arrowImpact", manifest, frameCounts, 0.04f, false);
        set.swordHitFx = Clip(set, "release", manifest, frameCounts, 0.05f, false);
        set.heavyHitFx = Clip(set, "bigRelease", manifest, frameCounts, 0.05f, false);

        set.doubleJump = Clip(set, "doubleJump", manifest, frameCounts, 0.04f, false);
        set.wallSlide = Clip(set, "wallSlide", manifest, frameCounts, 0.05f, true);
        set.climb = Clip(set, "climb", manifest, frameCounts, 0.06f, true);
        set.enterDoor = Clip(set, "enterDoor", manifest, frameCounts, 0.06f, false);
        set.grapple = Clip(set, "grapple", manifest, frameCounts, 0.05f, false);
        set.cast = Clip(set, "cast", manifest, frameCounts, 0.05f, false);

        set.hurt = Clip(set, "hurt", manifest, frameCounts, 0.05f, false);
        set.hurtKnockback = Clip(set, "hurtKnockback", manifest, frameCounts, 0.05f, false);
        set.death = Clip(set, "death", manifest, frameCounts, 0.06f, false);

        set.muzzleBasic = Clip(set, "basicMuzzle", manifest, frameCounts, 0.03f, false);
        set.muzzleRapid = Clip(set, "rapidMuzzle", manifest, frameCounts, 0.03f, false);
        set.muzzleBig = Clip(set, "bigMuzzle", manifest, frameCounts, 0.03f, false);

        set.impactBasic = Clip(set, "basicImpact", manifest, frameCounts, 0.04f, false);
        set.impactRapid = Clip(set, "rapidImpact", manifest, frameCounts, 0.04f, false);
        set.impactBig = Clip(set, "bigImpact", manifest, frameCounts, 0.04f, false);

        set.castGather = Clip(set, "gather", manifest, frameCounts, 0.05f, false);
        set.castChargeLoop = Clip(set, "chargeLoop", manifest, frameCounts, 0.05f, true);
        set.castRelease = Clip(set, "release", manifest, frameCounts, 0.05f, false);
        set.castBigLoop = Clip(set, "bigLoop", manifest, frameCounts, 0.05f, true);
        set.castBigRelease = Clip(set, "bigRelease", manifest, frameCounts, 0.05f, false);
        set.castAura = Clip(set, "aura", manifest, frameCounts, 0.05f, true);
        set.basicBolt = Clip(set, "basicBolt", manifest, frameCounts, 0.04f, true);
        set.rapidBolt = Clip(set, "rapidBolt", manifest, frameCounts, 0.04f, true);
        set.bigOrb = Clip(set, "bigOrb", manifest, frameCounts, 0.04f, true);

        set.jumpDust = Clip(set, "jumpDust", manifest, frameCounts, 0.04f, false);
        set.landDust = Clip(set, "landDust", manifest, frameCounts, 0.04f, false);
        set.dashDust = Clip(set, "dashDust", manifest, frameCounts, 0.04f, false);
        set.airJumpRing = Clip(set, "airJumpRing", manifest, frameCounts, 0.04f, false);

        EditorUtility.SetDirty(set);
    }

    /// <summary>시트 id 로 프레임 스프라이트를 모아 클립을 만든다. 시트가 없으면 null(런타임에서 다른 클립으로 대체).</summary>
    static PlayerAnimationClip Clip(PlayerAnimationSet set, string sheetId,
                                     PlayerSheetGridDetector.Manifest manifest,
                                     Dictionary<string, int> frameCounts, float interval, bool loop)
    {
        var entry = manifest.motion.FirstOrDefault(e => e.id == sheetId)
                    ?? manifest.vfx.FirstOrDefault(e => e.id == sheetId);
        if (entry == null) return null;

        string dir = manifest.motion.Any(e => e.id == sheetId) ? MotionDir : VfxDir;
        string assetPath = ResolveAssetPath(dir, entry.file);

        var byName = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>()
            .GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());

        int count = entry.cells == null ? 0 : entry.cells.Length / 2;
        var frames = new List<Sprite>(count);
        for (int i = 0; i < count; i++)
        {
            Sprite sprite;
            if (byName.TryGetValue(entry.id + "_" + i, out sprite) && sprite != null) frames.Add(sprite);
        }
        if (frames.Count == 0)
        {
            Debug.LogWarning("[플레이어 시트 임포트] 프레임 로드 실패: " + assetPath + " (" + sheetId + ") — 임포트를 다시 실행하세요.");
            return null;
        }

        frameCounts[sheetId] = frames.Count;
        return new PlayerAnimationClip
        {
            id = sheetId,
            frames = frames.ToArray(),
            frameInterval = interval,
            loop = loop
        };
    }

    /// <summary>씬의 Player 에 Body/CastFX/AuraFX 계층을 만들고 세트를 배선한다.</summary>
    static void WirePlayer(PlayerAnimationSet set)
    {
        var player = FindPlayer();
        if (player == null)
        {
            Debug.LogWarning("[플레이어 시트 임포트] 씬에 Player 가 없습니다. " +
                             "'Newtana Custom Tool > Camera System' 을 먼저 실행하세요.");
            return;
        }

        var body = EnsureRendererChild(player.transform, BodyName, 0);
        var castFx = EnsureRendererChild(player.transform, CastFxName, 5);
        var auraFx = EnsureRendererChild(player.transform, AuraFxName, -1);

        var muzzle = EnsureChild(player.transform, MuzzleName);
        var muzzleFx = EnsureRendererChild(muzzle, MuzzleFxName, 10);
        EnsureChild(muzzle, CastPointName);

        var anim = player.GetComponent<PlayerAnimation>();
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
        }

        var health = player.GetComponent<Health>();
        if (health != null)
        {
            var so = new SerializedObject(health);
            SetRef(so, "spriteRenderer", body);
            so.ApplyModifiedProperties();
        }
    }

    static GameObject FindPlayer()
    {
        foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go.CompareTag("Player")) return go;
        }
        var stateController = UnityEngine.Object.FindAnyObjectByType<PlayerStateController>(FindObjectsInactive.Include);
        return stateController != null ? stateController.gameObject : null;
    }

    static Transform EnsureChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing;
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, name + " 생성");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static SpriteRenderer EnsureRendererChild(Transform parent, string name, int sortingOrder)
    {
        var t = EnsureChild(parent, name);
        var renderer = t.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = Undo.AddComponent<SpriteRenderer>(t.gameObject);
        if (renderer.sortingOrder != sortingOrder)
        {
            Undo.RecordObject(renderer, name + " 정렬");
            renderer.sortingOrder = sortingOrder;
        }
        return renderer;
    }

    static void SetRef(SerializedObject so, string propertyName, UnityEngine.Object value)
    {
        var prop = so.FindProperty(propertyName);
        if (prop != null && value != null) prop.objectReferenceValue = value;
    }
}
