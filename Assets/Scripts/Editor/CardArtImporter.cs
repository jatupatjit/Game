using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the runtime card sprites from the user's final 2480x3508 card art. The originals stay outside the
/// project (multi-MB); each is area-downscaled to 640x904 (multiple of 4 so it can be block-compressed),
/// written as Assets/Art/UI/Resources/Cards/{key}.jpg and imported as a UI sprite. Menu: Tools/UI Kit/Rebuild Card Art.
/// </summary>
public static class CardArtImporter
{
    // Configure here. Skipped with a warning when the folder is absent.
    private const string SourceDir = "D:/Downloads/Final-20261019T075257Z-1-001/Card";
    private const string OutputDir = "Assets/Art/UI/Resources/Cards";
    private const int OutWidth = 640, OutHeight = 904;
    private const int JpgQuality = 90;

    // source file -> catalog key (CardCatalog is the source of truth for ids and names)
    private static readonly (string File, string Key)[] Sources =
    {
        ("IMG_0821.PNG", "strong_arms"), ("IMG_0839.JPG", "long_throw"), ("IMG_0840.JPG", "high_jumper"),
        ("IMG_0841.JPG", "endurance"), ("IMG_0842.JPG", "teamwork"), ("IMG_0843.JPG", "steady_hands"),
        ("IMG_0844.JPG", "light_step"), ("IMG_0845.JPG", "double_jump"), ("IMG_0846.JPG", "quick_recovery"),
        ("IMG_0847.JPG", "climber"), ("IMG_0848.JPG", "last_mile"), ("IMG_0849.JPG", "close_support"),
        ("IMG_0850.JPG", "fast_carrier"),
    };

    [MenuItem("Tools/UI Kit/Rebuild Card Art")]
    public static void RebuildMenu() => Debug.Log(Rebuild());

    public static string Rebuild()
    {
        var log = new System.Text.StringBuilder();
        if (!Directory.Exists(SourceDir))
        {
            Debug.LogWarning("[CardArtImporter] Source folder not found, skipping: " + SourceDir);
            return "SKIPPED: " + SourceDir + " missing";
        }
        Directory.CreateDirectory(OutputDir);
        foreach (var (file, key) in Sources)
        {
            string src = Path.Combine(SourceDir, file);
            if (!File.Exists(src)) { log.AppendLine("MISSING " + src); continue; }
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.LoadImage(File.ReadAllBytes(src));
            Texture2D outTex = Downscale(tex, OutWidth, OutHeight);
            byte[] bytes = outTex.EncodeToJPG(JpgQuality);
            File.WriteAllBytes($"{OutputDir}/{key}.jpg", bytes);
            log.AppendLine($"{file} ({tex.width}x{tex.height}) -> {key}.jpg {outTex.width}x{outTex.height} {bytes.Length / 1024} KB");
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(outTex);
        }
        AssetDatabase.Refresh();
        foreach (var (_, key) in Sources)
        {
            string path = $"{OutputDir}/{key}.jpg";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        // Retire the previous three illustrations (CARGO GUARD has no card any more).
        foreach (string old in new[] { "CardEndurance.jpg", "CardCargoGuard.jpg", "CardTeamwork.jpg" })
            if (AssetDatabase.DeleteAsset($"{OutputDir}/{old}")) log.AppendLine("Removed old " + old);
        return log.ToString();
    }

    /// <summary>Area-average (box) downscale; full-resolution source avoids the aliasing of a single bilinear tap.</summary>
    private static Texture2D Downscale(Texture2D src, int ow, int oh)
    {
        Color32[] sp = src.GetPixels32();
        int sw = src.width, sh = src.height;
        var result = new Color32[ow * oh];
        for (int y = 0; y < oh; y++)
        {
            int sy0 = y * sh / oh, sy1 = Mathf.Max(sy0 + 1, (y + 1) * sh / oh);
            for (int x = 0; x < ow; x++)
            {
                int sx0 = x * sw / ow, sx1 = Mathf.Max(sx0 + 1, (x + 1) * sw / ow);
                int r = 0, g = 0, b = 0, n = 0;
                for (int yy = sy0; yy < sy1; yy++)
                    for (int xx = sx0; xx < sx1; xx++)
                    {
                        Color32 c = sp[yy * sw + xx];
                        r += c.r; g += c.g; b += c.b; n++;
                    }
                result[y * ow + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
            }
        }
        var outTex = new Texture2D(ow, oh, TextureFormat.RGB24, false);
        outTex.SetPixels32(result);
        outTex.Apply();
        return outTex;
    }
}
