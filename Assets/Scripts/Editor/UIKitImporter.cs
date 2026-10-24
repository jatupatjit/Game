using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the runtime UI sprites from the user's design kit (Assets/Art/UI/Source, copied unmodified
/// from the kit folder). Each source is cropped to its alpha bounds, area-downscaled to UI size,
/// written to Assets/Art/UI/Resources/UIKit/ (so UITheme can Resources.Load it) and imported as a
/// 2D/UI sprite with 9-slice borders where the art is a panel, button or bar.
/// Menu: Tools/UI Kit/Rebuild Sprites.
/// </summary>
public static class UIKitImporter
{
    private const string SourceDir = "Assets/Art/UI/Source";
    private const string OutputDir = "Assets/Art/UI/Resources/UIKit";

    // source file, output name, scale, 9-slice border L,B,R,T (in output pixels; 0 = simple sprite), max size
    private struct Entry
    {
        public string Source, Output; public float Scale; public Vector4 Border; public int MaxSize;
        public Entry(string s, string o, float scale, Vector4 border, int max = 1024)
        { Source = s; Output = o; Scale = scale; Border = border; MaxSize = max; }
    }

    private static readonly Entry[] Entries =
    {
        new Entry("Settings.png",               "PanelBeige",        0.35f, new Vector4(16, 16, 16, 52)),
        new Entry("UI.png",                     "PanelDark",         0.30f, new Vector4(24, 24, 24, 24)),
        new Entry("Button Long.png",            "ButtonSecondary",   0.40f, new Vector4(16, 14, 16, 14), 512),
        new Entry("Selected Button Long.png",   "ButtonPrimary",     0.40f, new Vector4(16, 14, 16, 14), 512),
        new Entry("Selected Button.png",        "ButtonPrimaryWide", 0.55f, new Vector4(16, 14, 16, 14), 512),
        new Entry("CustomizeButtonPink.png",    "ButtonCustomizePink", 0.55f, new Vector4(18, 16, 18, 16), 512),
        new Entry("CustomizeButtonPurple.png",  "ButtonCustomizePurple", 0.55f, new Vector4(18, 16, 18, 16), 512),
        new Entry("Card Selected LineGrow.png", "CardSelectedFrame", 0.60f, Vector4.zero, 512),
        new Entry("Stamina Bar.png",            "StaminaBar",        0.40f, new Vector4(52, 0, 14, 0), 512),
        new Entry("Stamina Bar U.png",          "StaminaFrame",      0.40f, new Vector4(52, 0, 14, 0), 512),
        new Entry("Stamina Bar D.png",          "StaminaInner",      0.40f, new Vector4(18, 0, 18, 0), 512),
        new Entry("Keyboard.png",               "KeyCap",            0.40f, new Vector4(14, 14, 14, 14), 256),
        new Entry("Keyboard Long.png",          "KeyCapLong",        0.40f, new Vector4(14, 14, 14, 14), 256),
        new Entry("IMG_0878.PNG",               "IconCarrier",       0.12f, Vector4.zero, 256),
        new Entry("IMG_0892.PNG",               "IconStrength",      0.13f, Vector4.zero, 256),
        new Entry("IMG_0893.PNG",               "IconCube",          0.12f, Vector4.zero, 256),
        new Entry("IMG_0879.PNG",               "FaceGrey",          0.127f, Vector4.zero, 256),
        new Entry("IMG_0881.PNG",               "FacePurple",        0.127f, Vector4.zero, 256),
        new Entry("IMG_0882.PNG",               "FaceBlue",          0.127f, Vector4.zero, 256),
        new Entry("IMG_0883.PNG",               "FaceTeal",          0.127f, Vector4.zero, 256),
        new Entry("IMG_0884.PNG",               "FaceGreen",         0.127f, Vector4.zero, 256),
        new Entry("IMG_0885.PNG",               "FaceLime",          0.127f, Vector4.zero, 256),
        new Entry("IMG_0886.PNG",               "FaceBrown",         0.127f, Vector4.zero, 256),
        new Entry("IMG_0887.PNG",               "FaceCrimson",       0.127f, Vector4.zero, 256),
        new Entry("IMG_0888.PNG",               "CardBackRed",       0.30f, Vector4.zero, 512),
        new Entry("IMG_0890.PNG",               "CardBackBlue",      0.30f, Vector4.zero, 512),
        new Entry("IMG_0891.PNG",               "CardBackYellow",    0.30f, Vector4.zero, 512),
        new Entry("BG Blur.png",                "BackdropBlur",      0.55f, Vector4.zero, 1024),
    };

    /// <summary>Creates/updates Assets/Art/UI/Resources/UIFontSet.asset pointing at the Mitr weights.</summary>
    [MenuItem("Tools/UI Kit/Rebuild Font Set")]
    public static string RebuildFontSet()
    {
        const string dir = "Assets/Art/Fonts/Mitr";
        const string path = "Assets/Art/UI/Resources/UIFontSet.asset";
        var set = AssetDatabase.LoadAssetAtPath<UIFontSet>(path);
        bool created = set == null;
        if (created) set = ScriptableObject.CreateInstance<UIFontSet>();
        set.Regular = AssetDatabase.LoadAssetAtPath<Font>($"{dir}/Mitr-Regular.ttf");
        set.Medium = AssetDatabase.LoadAssetAtPath<Font>($"{dir}/Mitr-Medium.ttf");
        set.SemiBold = AssetDatabase.LoadAssetAtPath<Font>($"{dir}/Mitr-SemiBold.ttf");
        set.Bold = AssetDatabase.LoadAssetAtPath<Font>($"{dir}/Mitr-Bold.ttf");
        if (created) AssetDatabase.CreateAsset(set, path);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        return $"UIFontSet: regular={set.Regular != null} medium={set.Medium != null} semibold={set.SemiBold != null} bold={set.Bold != null}";
    }

    [MenuItem("Tools/UI Kit/Rebuild Sprites")]
    public static void RebuildMenu() => Debug.Log(Rebuild());

    public static string Rebuild()
    {
        Directory.CreateDirectory(OutputDir);
        var log = new System.Text.StringBuilder();
        foreach (Entry e in Entries)
        {
            string src = Path.Combine(SourceDir, e.Source);
            if (!File.Exists(src)) { log.AppendLine("MISSING " + src); continue; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(src));
            RectInt box = AlphaBounds(tex, 8);
            Texture2D outTex = CropAndScale(tex, box, e.Scale);
            string path = $"{OutputDir}/{e.Output}.png";
            File.WriteAllBytes(path, outTex.EncodeToPNG());
            log.AppendLine($"{e.Source} -> {e.Output}.png crop {box.width}x{box.height} -> {outTex.width}x{outTex.height}");
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(outTex);
        }
        AssetDatabase.Refresh();
        foreach (Entry e in Entries)
        {
            string path = $"{OutputDir}/{e.Output}.png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = e.Border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = true; // IMGUI screens use the textures directly
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = e.MaxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        return log.ToString();
    }

    private static RectInt AlphaBounds(Texture2D tex, byte threshold)
    {
        Color32[] px = tex.GetPixels32();
        int w = tex.width, h = tex.height, x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > threshold)
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
        if (x1 < 0) return new RectInt(0, 0, w, h);
        // 2 px transparent margin so bilinear sampling never bleeds at the edge
        x0 = Mathf.Max(0, x0 - 2); y0 = Mathf.Max(0, y0 - 2);
        x1 = Mathf.Min(w - 1, x1 + 2); y1 = Mathf.Min(h - 1, y1 + 2);
        return new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>Area-average downscale with premultiplied alpha (no dark fringes).</summary>
    private static Texture2D CropAndScale(Texture2D src, RectInt box, float scale)
    {
        int ow = Mathf.Max(1, Mathf.RoundToInt(box.width * scale));
        int oh = Mathf.Max(1, Mathf.RoundToInt(box.height * scale));
        Color[] sp = src.GetPixels();
        int sw = src.width;
        var result = new Color[ow * oh];
        float fx = box.width / (float)ow, fy = box.height / (float)oh;
        for (int y = 0; y < oh; y++)
        {
            int sy0 = box.y + Mathf.FloorToInt(y * fy), sy1 = box.y + Mathf.Max(Mathf.FloorToInt(y * fy) + 1, Mathf.FloorToInt((y + 1) * fy));
            for (int x = 0; x < ow; x++)
            {
                int sx0 = box.x + Mathf.FloorToInt(x * fx), sx1 = box.x + Mathf.Max(Mathf.FloorToInt(x * fx) + 1, Mathf.FloorToInt((x + 1) * fx));
                float r = 0, g = 0, b = 0, a = 0; int n = 0;
                for (int yy = sy0; yy < sy1; yy++)
                    for (int xx = sx0; xx < sx1; xx++)
                    {
                        Color c = sp[yy * sw + xx];
                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a; n++;
                    }
                result[y * ow + x] = a > 0f ? new Color(r / a, g / a, b / a, a / n) : new Color(0, 0, 0, 0);
            }
        }
        var outTex = new Texture2D(ow, oh, TextureFormat.RGBA32, false);
        outTex.SetPixels(result);
        outTex.Apply();
        return outTex;
    }
}
