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
    const string CombatRoot = "Assets/Atttack 4/Atttack 3";
    const string SwordDir = CombatRoot + "/Attack";
    const string MagicDir = CombatRoot + "/magic_sprite_sheets";
    const string HitDir = CombatRoot + "/sword_hit";

    class VfxSpec
    {
        public string id;
        public string file;
        public string dir;
        public int cellW;
        public int cellH;
        public int cols;
        public int rows;
        public int frames;
        public int ppu;
        public float[] pivot;
        public string pivotMode;
        public int[] durationsMs;
    }

    class MotionSpec
    {
        public string id;
        public string file;
        public string dir;
        public int cellW;
        public int cellH;
        public int cols;
        public int rows;
        public int frames;
        public int ppu;
        public float[] pivot;
        public string pivotMode;
        public int[] durationsMs;
        public string durationsPath;
        public int loopStartFrame = -1;
        public int loopEndFrame = -1;
    }

    static readonly HashSet<string> ReplacedMotionIds = new HashSet<string>
    {
        "attack1", "attack2", "attack3", "heavyAttack", "heavyAttackLegacy", "bow",
        "magicBasic", "magicRapid", "magicBig"
    };

    static readonly MotionSpec[] MotionSpecs =
    {
        new MotionSpec
        {
            id = "attack1", file = "attack1_gifextract.png", dir = SwordDir,
            cellW = 1584, cellH = 828, cols = 5, rows = 2, frames = 8, ppu = 1200,
            pivot = new[] { 432f / 1584f, 0f }, pivotMode = "feet",
            durationsMs = new[] { 50, 50, 50, 50, 50, 50, 50, 100 }
        },
        new MotionSpec
        {
            id = "attack2", file = "attack2_gifextract.png", dir = SwordDir,
            cellW = 1584, cellH = 828, cols = 5, rows = 1, frames = 5, ppu = 1200,
            pivot = new[] { 414f / 1584f, 0f }, pivotMode = "feet",
            durationsMs = new[] { 50, 50, 50, 50, 100 }
        },
        new MotionSpec
        {
            id = "attack3", file = "attack3_gifextract.png", dir = SwordDir,
            cellW = 1584, cellH = 828, cols = 5, rows = 2, frames = 10, ppu = 1200,
            pivot = new[] { 426f / 1584f, 0f }, pivotMode = "feet",
            durationsMs = new[] { 50, 50, 50, 50, 50, 50, 50, 50, 100, 100 }
        },
        new MotionSpec
        {
            id = "heavyAttack", file = "heavy_gifextract.png", dir = SwordDir,
            cellW = 1584, cellH = 828, cols = 5, rows = 6, frames = 30, ppu = 1200,
            pivot = new[] { 426f / 1584f, 0f }, pivotMode = "feet",
            durationsMs = new[]
            {
                100, 100, 100,
                50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50,
                100, 100, 100, 100, 100, 100
            },
            loopStartFrame = 3, loopEndFrame = 20
        },
        new MotionSpec
        {
            id = "bow", file = "bow_muzzle_sheet.png", dir = CombatRoot,
            cellW = 376, cellH = 304, cols = 5, rows = 5, frames = 23, ppu = 1200,
            pivot = new[] { 164f / 376f, 8f / 304f }, pivotMode = "feet",
            durationsPath = CombatRoot + "/bow_muzzle_sheet.json",
            loopStartFrame = 5, loopEndFrame = 10
        },
        new MotionSpec
        {
            id = "magicBasic", file = "magic_basic_sheet.png", dir = MagicDir,
            cellW = 1152, cellH = 672, cols = 8, rows = 3, frames = 19, ppu = 1200,
            pivot = new[] { 606f / 1152f, 0f }, pivotMode = "feet",
            durationsPath = MagicDir + "/magic_basic_sheet.json"
        },
        new MotionSpec
        {
            id = "magicRapid", file = "magic_rapid_sheet.png", dir = MagicDir,
            cellW = 1152, cellH = 672, cols = 8, rows = 5, frames = 37, ppu = 1200,
            pivot = new[] { 0.5f, 0f }, pivotMode = "feet",
            durationsPath = MagicDir + "/magic_rapid_sheet.json",
            loopStartFrame = 8, loopEndFrame = 11
        },
        new MotionSpec
        {
            id = "magicBig", file = "magic_big_sheet.png", dir = MagicDir,
            cellW = 1152, cellH = 672, cols = 8, rows = 6, frames = 44, ppu = 1200,
            pivot = new[] { 0.5f, 0f }, pivotMode = "feet",
            durationsPath = MagicDir + "/magic_big_sheet.json",
            loopStartFrame = 16, loopEndFrame = 23
        }
    };

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
        new VfxSpec { id = "airJumpRing", file = "23_air_jump_ring.png", cellW = 67, cellH = 37, cols = 7, rows = 1, frames = 7, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center" },
        new VfxSpec { id = "attack1Hit", file = "hit_attack1_1x.png", dir = HitDir, cellW = 96, cellH = 96, cols = 6, rows = 1, frames = 6, ppu = 300, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center", durationsMs = Repeat(6, 50) },
        new VfxSpec { id = "attack2Hit", file = "hit_attack2_1x.png", dir = HitDir, cellW = 108, cellH = 108, cols = 6, rows = 1, frames = 6, ppu = 300, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center", durationsMs = Repeat(6, 50) },
        new VfxSpec { id = "attack3Hit", file = "hit_attack3_1x.png", dir = HitDir, cellW = 156, cellH = 156, cols = 6, rows = 1, frames = 6, ppu = 300, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center", durationsMs = Repeat(6, 50) },
        new VfxSpec { id = "heavyHit", file = "hit_heavy_1x.png", dir = HitDir, cellW = 180, cellH = 180, cols = 6, rows = 1, frames = 6, ppu = 300, pivot = new[] { 0.5f, 0.5f }, pivotMode = "center", durationsMs = Repeat(6, 50) }
    };

    public static void FixAndImport()
    {
        if (!RebuildManifest()) return;

        PlayerAnimationSheetImporter.ImportMissingFromManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static bool RebuildManifest()
    {
        var manifest = LoadManifest();
        if (manifest == null) return false;

        var motion = manifest.motion
            .Where(e => !ReplacedMotionIds.Contains(e.id))
            .ToList();
        motion.AddRange(BuildMotionEntries());
        manifest.motion = motion.ToArray();
        manifest.version = 2;

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

        Debug.Log("[CombatManifestBuilder] motion=" + manifest.motion.Length + " vfx=" + manifest.vfx.Length);
        return true;
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

    static List<PlayerSheetGridDetector.SheetEntry> BuildMotionEntries()
    {
        var entries = new List<PlayerSheetGridDetector.SheetEntry>(MotionSpecs.Length);
        foreach (var spec in MotionSpecs)
        {
            var entry = BuildMotionEntry(spec);
            if (entry != null) entries.Add(entry);
        }
        return entries;
    }

    static PlayerSheetGridDetector.SheetEntry BuildMotionEntry(MotionSpec spec)
    {
        string dir = string.IsNullOrEmpty(spec.dir) ? CombatRoot : spec.dir;
        string path = dir + "/" + spec.file;
        if (!File.Exists(path))
        {
            Debug.LogWarning("[CombatManifestBuilder] motion missing: " + path);
            return null;
        }

        int width, height;
        if (!TryGetTextureSize(path, out width, out height))
        {
            Debug.LogWarning("[CombatManifestBuilder] motion load failed: " + path);
            return null;
        }

        int expectedW = spec.cellW * spec.cols;
        int expectedH = spec.cellH * spec.rows;
        if (width != expectedW || height != expectedH)
        {
            Debug.LogWarning("[CombatManifestBuilder] motion size mismatch: " + path +
                             " actual=" + width + "x" + height +
                             " expected=" + expectedW + "x" + expectedH);
        }

        int[] durations = spec.durationsMs;
        if (!string.IsNullOrEmpty(spec.durationsPath))
        {
            durations = ReadDurations(spec.durationsPath, spec.frames);
        }

        return new PlayerSheetGridDetector.SheetEntry
        {
            id = spec.id,
            file = spec.file,
            dir = dir,
            pivotMode = spec.pivotMode,
            texW = width,
            texH = height,
            cellW = spec.cellW,
            cellH = spec.cellH,
            cols = spec.cols,
            rows = spec.rows,
            ppu = spec.ppu,
            pivot = spec.pivot,
            contentUnion = new[] { 0, 0, spec.cellW, spec.cellH },
            cells = Cells(spec.cols, spec.rows, spec.frames),
            durationsMs = durations,
            loopStartFrame = spec.loopStartFrame,
            loopEndFrame = spec.loopEndFrame
        };
    }

    static PlayerSheetGridDetector.SheetEntry BuildVfxEntry(VfxSpec spec)
    {
        string dir = string.IsNullOrEmpty(spec.dir) ? VfxDir : spec.dir;
        string path = dir + "/" + spec.file;
        if (!File.Exists(path))
        {
            Debug.LogWarning("[CombatManifestBuilder] vfx missing: " + path);
            return null;
        }

        int width, height;
        if (!TryGetTextureSize(path, out width, out height))
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
            dir = dir,
            pivotMode = spec.pivotMode,
            texW = width,
            texH = height,
            cellW = spec.cellW,
            cellH = spec.cellH,
            cols = spec.cols,
            rows = spec.rows,
            ppu = spec.ppu > 0 ? spec.ppu : 300,
            pivot = spec.pivot,
            contentUnion = new[] { 0, 0, width, height },
            cells = Cells(spec.cols, spec.rows, spec.frames),
            durationsMs = spec.durationsMs
        };
    }

    static bool TryGetTextureSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!File.Exists(path)) return false;

        using (var stream = File.OpenRead(path))
        {
            var header = new byte[24];
            if (stream.Read(header, 0, header.Length) != header.Length) return false;
            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
                return false;

            width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return width > 0 && height > 0;
        }
    }

    static int[] ReadDurations(string path, int expectedCount)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning("[CombatManifestBuilder] durations JSON missing: " + path);
            return null;
        }

        string json = File.ReadAllText(path);
        int key = json.IndexOf("\"durations_ms\"", StringComparison.Ordinal);
        if (key < 0)
        {
            Debug.LogWarning("[CombatManifestBuilder] durations_ms missing: " + path);
            return null;
        }

        int open = json.IndexOf('[', key);
        int close = open < 0 ? -1 : json.IndexOf(']', open);
        if (open < 0 || close < 0)
        {
            Debug.LogWarning("[CombatManifestBuilder] durations_ms malformed: " + path);
            return null;
        }

        var values = new List<int>();
        int i = open + 1;
        while (i < close)
        {
            while (i < close && !char.IsDigit(json[i]) && json[i] != '-') i++;
            if (i >= close) break;

            int sign = 1;
            if (json[i] == '-') { sign = -1; i++; }
            int value = 0;
            bool hasDigit = false;
            while (i < close && char.IsDigit(json[i]))
            {
                value = value * 10 + (json[i] - '0');
                hasDigit = true;
                i++;
            }
            if (hasDigit) values.Add(sign * value);
        }

        if (expectedCount > 0 && values.Count != expectedCount)
        {
            Debug.LogWarning("[CombatManifestBuilder] durations count mismatch: " + path +
                             " actual=" + values.Count + " expected=" + expectedCount);
        }

        return values.Count > 0 ? values.ToArray() : null;
    }

    static int[] Repeat(int count, int value)
    {
        var result = new int[Math.Max(0, count)];
        for (int i = 0; i < result.Length; i++) result[i] = value;
        return result;
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
