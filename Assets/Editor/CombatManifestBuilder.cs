using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class CombatManifestBuilder
{
    const string ManifestPath = "Assets/sprite/Player/player/player_animation_manifest.json";
    const string VfxDir = "Assets/sprite/Player/player/vfx";

    class VfxSpec
    {
        public string id;
        public string file;
        public int cellW;
        public int cellH;
        public int cols;
        public int rows;
        public int frames;
        public float[] pivot;
        public string pivotMode;
    }

    static readonly VfxSpec[] VfxSpecs =
    {
        new VfxSpec { id = "gather", file = "01_gather.png", cellW = 127, cellH = 109, cols = 10, rows = 1, frames = 10, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "chargeLoop", file = "02_charge_loop.png", cellW = 82, cellH = 52, cols = 8, rows = 1, frames = 8, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "release", file = "03_release.png", cellW = 103, cellH = 100, cols = 6, rows = 1, frames = 6, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "bigLoop", file = "04_big_loop.png", cellW = 112, cellH = 82, cols = 8, rows = 1, frames = 8, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "bigRelease", file = "05_big_release.png", cellW = 121, cellH = 121, cols = 6, rows = 1, frames = 6, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "aura", file = "06_aura.png", cellW = 88, cellH = 103, cols = 16, rows = 1, frames = 16, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "basicMuzzle", file = "08_basic_muzzle.png", cellW = 49, cellH = 43, cols = 4, rows = 3, frames = 12, pivot = new[] { 0.2755f, 0.5116f }, pivotMode = "center" },
        new VfxSpec { id = "basicBolt", file = "09_basic_bolt.png", cellW = 48, cellH = 15, cols = 4, rows = 1, frames = 4, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "basicImpact", file = "10_basic_impact.png", cellW = 91, cellH = 94, cols = 7, rows = 1, frames = 7, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "rapidMuzzle", file = "11_rapid_muzzle.png", cellW = 49, cellH = 37, cols = 3, rows = 4, frames = 12, pivot = new[] { 0.2143f, 0.5135f }, pivotMode = "center" },
        new VfxSpec { id = "rapidBolt", file = "12_rapid_bolt.png", cellW = 42, cellH = 15, cols = 4, rows = 1, frames = 4, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "rapidImpact", file = "13_rapid_impact.png", cellW = 49, cellH = 49, cols = 5, rows = 1, frames = 5, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "bigMuzzle", file = "14_big_muzzle.png", cellW = 121, cellH = 109, cols = 6, rows = 1, frames = 6, pivot = new[] { 0.2851f, 0.5046f }, pivotMode = "center" },
        new VfxSpec { id = "bigOrb", file = "15_big_orb.png", cellW = 82, cellH = 39, cols = 6, rows = 1, frames = 6, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "bigImpact", file = "16_big_impact.png", cellW = 166, cellH = 160, cols = 8, rows = 1, frames = 8, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "arrowRelease", file = "17_arrow_release.png", cellW = 21, cellH = 27, cols = 3, rows = 1, frames = 3, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "energyArrow", file = "18_energy_arrow.png", cellW = 66, cellH = 15, cols = 3, rows = 1, frames = 3, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "arrowImpact", file = "19_arrow_impact.png", cellW = 48, cellH = 51, cols = 4, rows = 1, frames = 4, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "jumpDust", file = "20_jump_dust.png", cellW = 73, cellH = 37, cols = 6, rows = 1, frames = 6, pivot = new[] { 0.49315f, 0.08108f }, pivotMode = "bottom" },
        new VfxSpec { id = "landDust", file = "21_land_dust.png", cellW = 127, cellH = 28, cols = 7, rows = 1, frames = 7, pivot = new[] { 0.49606f, 0.10714f }, pivotMode = "bottom" },
        new VfxSpec { id = "dashDust", file = "22_dash_dust.png", cellW = 70, cellH = 40, cols = 7, rows = 1, frames = 7, pivot = new[] { 0.49286f, 0.075f }, pivotMode = "bottom" },
        new VfxSpec { id = "airJumpRing", file = "23_air_jump_ring.png", cellW = 67, cellH = 37, cols = 7, rows = 1, frames = 7, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" }
    };

    public static void FixAndImport()
    {
        var manifest = LoadManifest();
        if (manifest == null) return;

        var motion = manifest.motion
            .Where(e => e.id != "heavyAttack" && e.id != "heavyAttackLegacy")
            .ToList();
        motion.Add(BuildHeavyEntry());
        manifest.motion = motion.ToArray();

        var vfx = new List<PlayerSheetGridDetector.SheetEntry>();
        foreach (var spec in VfxSpecs)
        {
            var entry = BuildVfxEntry(spec);
            if (entry != null) vfx.Add(entry);
        }
        manifest.vfx = vfx.ToArray();
        manifest.ppu = 1200;
        manifest.footAnchorPixels = 144;

        File.WriteAllText(ManifestPath, PlayerSheetGridDetector.ToJson(manifest), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        PlayerAnimationSheetImporter.ImportFromManifest();
        ApplyCombatToPlayerPrefab.Apply();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[CombatManifestBuilder] motion=" + manifest.motion.Length + " vfx=" + manifest.vfx.Length);
    }

    static PlayerSheetGridDetector.Manifest LoadManifest()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
        if (asset == null)
        {
            Debug.LogError("[CombatManifestBuilder] manifest not found: " + ManifestPath);
            return null;
        }

        var manifest = JsonUtility.FromJson<PlayerSheetGridDetector.Manifest>(asset.text);
        if (manifest == null || manifest.motion == null)
        {
            Debug.LogError("[CombatManifestBuilder] manifest parse failed: " + ManifestPath);
            return null;
        }
        return manifest;
    }

    static PlayerSheetGridDetector.SheetEntry BuildHeavyEntry()
    {
        return new PlayerSheetGridDetector.SheetEntry
        {
            id = "heavyAttack",
            file = "23_heavy_attack.PNG",
            pivotMode = "feet",
            texW = 2400,
            texH = 2736,
            cellW = 480,
            cellH = 456,
            cols = 5,
            rows = 6,
            pivot = new[] { 0.5f, 0.315789f },
            contentUnion = new[] { 0, 0, 480, 456 },
            cells = Cells(5, 6, 27)
        };
    }

    static PlayerSheetGridDetector.SheetEntry BuildVfxEntry(VfxSpec spec)
    {
        string path = VfxDir + "/" + spec.file;
        if (!File.Exists(path))
        {
            Debug.LogWarning("[CombatManifestBuilder] vfx missing: " + path);
            return null;
        }

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        bool loaded;
        try { loaded = tex.LoadImage(File.ReadAllBytes(path)); }
        finally { }
        int width = tex.width;
        int height = tex.height;
        UnityEngine.Object.DestroyImmediate(tex);

        if (!loaded)
        {
            Debug.LogWarning("[CombatManifestBuilder] vfx load failed: " + path);
            return null;
        }

        int expectedW = spec.cellW * spec.cols;
        int expectedH = spec.cellH * spec.rows;
        if (width != expectedW || height != expectedH)
        {
            Debug.LogWarning("[CombatManifestBuilder] vfx size mismatch: " + path +
                             " actual=" + width + "x" + height +
                             " expected=" + expectedW + "x" + expectedH);
        }

        return new PlayerSheetGridDetector.SheetEntry
        {
            id = spec.id,
            file = spec.file,
            pivotMode = spec.pivotMode,
            texW = width,
            texH = height,
            cellW = spec.cellW,
            cellH = spec.cellH,
            cols = spec.cols,
            rows = spec.rows,
            ppu = 300,
            pivot = spec.pivot,
            contentUnion = new[] { 0, 0, width, height },
            cells = Cells(spec.cols, spec.rows, spec.frames)
        };
    }

    static int[] Cells(int cols, int rows, int count)
    {
        var cells = new List<int>(count * 2);
        int limit = Math.Min(count, cols * rows);
        for (int i = 0; i < limit; i++)
        {
            cells.Add(i % cols);
            cells.Add(i / cols);
        }
        return cells.ToArray();
    }
}
