using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 플레이어 시트 그리드 자동 탐지 (에디터).
/// 오프라인 생성기(ManifestGen)와 동일한 알고리즘을 사용한다:
///   열 개수 x 셀 높이 후보를 전부 시도 → 격자선 절단(cut) 최소 → 채워진 셀 최대 → 접두사 → 행 수 최대.
/// 매니페스트를 다시 만들 때 사용한다 (Assets/sprite/Player/player/player_animation_manifest.json).
/// </summary>
public static class PlayerSheetGridDetector
{
    [Serializable]
    public class SheetEntry
    {
        public string id;
        public string file;
        public string dir; // optional project-relative folder override (external combat sheets)
        public string pivotMode;
        public int texW, texH;
        public int cellW, cellH;
        public int cols, rows;
        public int ppu; // 0 = use manifest.ppu (motion sheets are 4x; 1x VFX override to 300)
        public float[] pivot;
        public int[] contentUnion;
        public int[] cells; // {col,row,col,row,...} row-major
        public int[] durationsMs; // optional per-frame durations; empty = frameInterval
        public int loopStartFrame = -1; // -1/0 = first frame
        public int loopEndFrame = -1; // -1 = last frame
    }

    [Serializable]
    public class Manifest
    {
        public int version = 1;
        public int ppu = 1200;
        public int footAnchorPixels = 144;
        public SheetEntry[] motion;
        public SheetEntry[] vfx;
    }

    const int Ppu = 1200;
    const int FootAnchorPixels = 144;

    static readonly HashSet<string> BottomAnchoredVfx = new HashSet<string>
    {
        "jumpDust", "landDust", "dashDust"
    };

    public static Manifest Detect(string root)
    {
        var manifest = new Manifest
        {
            ppu = Ppu,
            footAnchorPixels = FootAnchorPixels,
            motion = DetectGroup(Path.Combine(root, "motion"), "feet"),
            vfx = DetectGroup(Path.Combine(root, "vfx"), "center")
        };
        return manifest;
    }

    static SheetEntry[] DetectGroup(string dir, string defaultPivotMode)
    {
        if (!Directory.Exists(dir)) return new SheetEntry[0];

        var files = Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        var byId = new Dictionary<string, SheetEntry>();
        foreach (var f in files)
        {
            var entry = DetectSheet(f, dir, defaultPivotMode);
            if (entry == null) continue;

            // Old and new replacement sheets can share an id (17_heavy_attack vs
            // 23_heavy_attack). Prefer the higher numeric file prefix.
            SheetEntry existing;
            if (!byId.TryGetValue(entry.id, out existing) ||
                FilePrefix(entry.file) > FilePrefix(existing.file))
            {
                byId[entry.id] = entry;
            }
        }
        return byId.Values.OrderBy(e => e.file, StringComparer.Ordinal).ToArray();
    }

    static SheetEntry DetectSheet(string path, string dir, string defaultPivotMode)
    {
        byte[] pngBytes = File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        bool loaded;
        try { loaded = tex.LoadImage(pngBytes); }
        finally { }
        if (!loaded) { Debug.LogWarning("[시트 탐지] 이미지 로드 실패: " + path); UnityEngine.Object.DestroyImmediate(tex); return null; }

        int w = tex.width, h = tex.height;
        var pixels = tex.GetPixels32();
        var alpha = new byte[w * h];
        for (int i = 0; i < w * h; i++) alpha[i] = pixels[i].a > 8 ? (byte)1 : (byte)0;
        UnityEngine.Object.DestroyImmediate(tex);

        var grid = FindBest(w, h, alpha);
        if (grid == null)
        {
            Debug.LogWarning("[시트 탐지] 그리드를 찾지 못함: " + path);
            return null;
        }

        string id = ToId(Path.GetFileNameWithoutExtension(Path.GetFileName(path)));
        string pivotMode = defaultPivotMode;
        if (defaultPivotMode == "center" && BottomAnchoredVfx.Contains(id)) pivotMode = "bottom";

        var cells = new List<int>();
        int ux0 = int.MaxValue, uy0 = int.MaxValue, ux1 = -1, uy1 = -1;
        for (int r = 0; r < grid.Rows; r++)
        {
            for (int c = 0; c < grid.Cols; c++)
            {
                if (!grid.CellFilled[r * grid.Cols + c]) continue;
                int lx0, ly0, lx1, ly1;
                if (!CellContent(alpha, w, h, c * grid.CellW, r * grid.CellH, grid.CellW, grid.CellH,
                                 out lx0, out ly0, out lx1, out ly1)) continue;
                cells.Add(c);
                cells.Add(r);
                if (lx0 < ux0) ux0 = lx0;
                if (ly0 < uy0) uy0 = ly0;
                if (lx1 > ux1) ux1 = lx1;
                if (ly1 > uy1) uy1 = ly1;
            }
        }
        if (cells.Count == 0) return null;

        double pivotX = (ux0 + ux1 + 1) / 2.0 / grid.CellW;
        double pivotY;
        if (pivotMode == "feet")
        {
            int feetFromBottom = grid.CellH - (uy1 + 1);
            pivotY = (feetFromBottom + FootAnchorPixels) / (double)grid.CellH;
        }
        else if (pivotMode == "bottom")
        {
            pivotY = (grid.CellH - (uy1 + 1)) / (double)grid.CellH;
        }
        else
        {
            double centerTopDown = (uy0 + uy1 + 1) / 2.0;
            pivotY = (grid.CellH - centerTopDown) / (double)grid.CellH;
        }

        return new SheetEntry
        {
            id = id,
            file = path.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'),
            pivotMode = pivotMode,
            texW = w,
            texH = h,
            cellW = grid.CellW,
            cellH = grid.CellH,
            cols = grid.Cols,
            rows = grid.Rows,
            pivot = new[] { (float)Mathf.Clamp01((float)pivotX), (float)Mathf.Clamp01((float)pivotY) },
            contentUnion = new[] { ux0, uy0, ux1 - ux0 + 1, uy1 - uy0 + 1 },
            cells = cells.ToArray()
        };
    }

    public static string ToJson(Manifest manifest)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"version\": ").Append(manifest.version).Append(",\n");
        sb.Append("  \"ppu\": ").Append(manifest.ppu).Append(",\n");
        sb.Append("  \"footAnchorPixels\": ").Append(manifest.footAnchorPixels).Append(",\n");
        sb.Append("  \"motion\": [\n").Append(GroupJson(manifest.motion)).Append("  ],\n");
        sb.Append("  \"vfx\": [\n").Append(GroupJson(manifest.vfx)).Append("  ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    static string GroupJson(SheetEntry[] entries)
    {
        if (entries == null || entries.Length == 0) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (i > 0) sb.Append(",\n");
            sb.Append("    {\n");
            sb.Append("      \"id\": \"").Append(e.id).Append("\",\n");
            sb.Append("      \"file\": \"").Append(e.file).Append("\",\n");
            if (!string.IsNullOrEmpty(e.dir))
                sb.Append("      \"dir\": \"").Append(e.dir.Replace('\\', '/')).Append("\",\n");
            sb.Append("      \"pivotMode\": \"").Append(e.pivotMode).Append("\",\n");
            sb.Append("      \"texW\": ").Append(e.texW).Append(", \"texH\": ").Append(e.texH).Append(",\n");
            sb.Append("      \"cellW\": ").Append(e.cellW).Append(", \"cellH\": ").Append(e.cellH).Append(",\n");
            sb.Append("      \"cols\": ").Append(e.cols).Append(", \"rows\": ").Append(e.rows).Append(",\n");
            if (e.ppu > 0) sb.Append("      \"ppu\": ").Append(e.ppu).Append(",\n");
            sb.Append("      \"pivot\": [").Append(e.pivot[0].ToString("0.#####")).Append(", ").Append(e.pivot[1].ToString("0.#####")).Append("],\n");
            sb.Append("      \"contentUnion\": [").Append(e.contentUnion[0]).Append(", ").Append(e.contentUnion[1]).Append(", ")
              .Append(e.contentUnion[2]).Append(", ").Append(e.contentUnion[3]).Append("],\n");
            if (e.durationsMs != null && e.durationsMs.Length > 0)
            {
                sb.Append("      \"durationsMs\": [");
                for (int k = 0; k < e.durationsMs.Length; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append(e.durationsMs[k]);
                }
                sb.Append("],\n");
            }
            if (e.loopEndFrame >= 0)
            {
                sb.Append("      \"loopStartFrame\": ").Append(Math.Max(0, e.loopStartFrame)).Append(",\n");
                sb.Append("      \"loopEndFrame\": ").Append(e.loopEndFrame).Append(",\n");
            }
            sb.Append("      \"cells\": [");
            for (int k = 0; k < e.cells.Length; k++)
            {
                if (k > 0) sb.Append(", ");
                sb.Append(e.cells[k]);
            }
            sb.Append("]\n");
            sb.Append("    }");
        }
        sb.Append("\n");
        return sb.ToString();
    }

    static int FilePrefix(string file)
    {
        file = Path.GetFileName(file);
        int us = file.IndexOf('_');
        int prefix;
        if (us > 0 && int.TryParse(file.Substring(0, us), out prefix)) return prefix;
        return -1;
    }

    static string ToId(string fileBase)
    {
        int dash = fileBase.IndexOf('-');
        string prefix = dash > 0 ? fileBase.Substring(0, dash) : fileBase;
        int us = prefix.IndexOf('_');
        if (us >= 0 && us + 1 < prefix.Length) prefix = prefix.Substring(us + 1);
        if (prefix.Length == 0) return "sheet";

        var sb = new System.Text.StringBuilder();
        bool upperNext = false;
        for (int i = 0; i < prefix.Length; i++)
        {
            char ch = prefix[i];
            if (ch == '_') { upperNext = true; continue; }
            if (sb.Length == 0) { sb.Append(char.ToLowerInvariant(ch)); continue; }
            sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
            upperNext = false;
        }
        return sb.ToString();
    }

    static bool CellContent(byte[] alpha, int w, int h, int x0, int y0, int cw, int ch,
                            out int bx0, out int by0, out int bx1, out int by1)
    {
        bx0 = int.MaxValue; by0 = int.MaxValue; bx1 = -1; by1 = -1;
        for (int y = y0; y < y0 + ch && y < h; y++)
        {
            int off = y * w;
            for (int x = x0; x < x0 + cw && x < w; x++)
            {
                if (alpha[off + x] == 0) continue;
                if (x < bx0) bx0 = x;
                if (x > bx1) bx1 = x;
                if (y < by0) by0 = y;
                if (y > by1) by1 = y;
            }
        }
        return bx1 >= bx0;
    }

    public class Grid
    {
        public int Cols, Rows, CellW, CellH, Filled, Cut;
        public bool Prefix;
        public bool[] CellFilled;
    }

    public static Grid FindBest(int w, int h, byte[] alpha)
    {
        Grid best = null;

        for (int cols = 1; cols <= 12; cols++)
        {
            if (w % cols != 0) continue;
            int cellW = w / cols;

            var rowMask = new byte[h];
            var colContent = new bool[w];
            for (int y = 0; y < h; y++)
            {
                int off = y * w;
                int m = 0;
                for (int x = 0; x < w; x++)
                {
                    if (alpha[off + x] != 0)
                    {
                        colContent[x] = true;
                        int slot = x / cellW;
                        if (slot > cols - 1) slot = cols - 1;
                        m |= 1 << slot;
                    }
                }
                rowMask[y] = (byte)m;
            }

            for (int cellH = 1; cellH <= h; cellH++)
            {
                if (h % cellH != 0) continue;
                int rows = h / cellH;
                if (rows < 1 || rows > 60) continue;
                if ((long)cols * rows > 250) continue;

                int cut = 0;
                for (int c = 1; c < cols; c++) if (colContent[c * cellW]) cut++;
                for (int r = 1; r < rows; r++) if (rowMask[r * cellH] != 0) cut++;

                var filled = new bool[rows * cols];
                int count = 0, last = -1;
                for (int r = 0; r < rows; r++)
                {
                    int m = 0;
                    for (int y = r * cellH; y < (r + 1) * cellH; y++) m |= rowMask[y];
                    for (int c = 0; c < cols; c++)
                    {
                        bool f = (m & (1 << c)) != 0;
                        filled[r * cols + c] = f;
                        if (f) { count++; last = r * cols + c; }
                    }
                }
                if (count == 0) continue;

                bool prefix = true;
                for (int i = 0; i <= last; i++) if (!filled[i]) { prefix = false; break; }

                var g = new Grid
                {
                    Cols = cols, Rows = rows, CellW = cellW, CellH = cellH,
                    Filled = count, Cut = cut, Prefix = prefix, CellFilled = filled
                };

                if (best == null) { best = g; continue; }
                if (cut != best.Cut) { if (cut < best.Cut) best = g; continue; }
                if (count != best.Filled) { if (count > best.Filled) best = g; continue; }
                if (prefix && !best.Prefix) { best = g; continue; }
                if (prefix == best.Prefix && g.Rows > best.Rows) best = g;
            }
        }
        return best;
    }
}
