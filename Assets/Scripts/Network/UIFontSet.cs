using UnityEngine;

/// <summary>
/// References the UI font weights (Mitr, Assets/Art/Fonts/Mitr) so UITheme can load them at runtime
/// from Resources/UIFontSet without moving the font files. Created by Tools/UI Kit/Rebuild Font Set.
/// </summary>
[CreateAssetMenu(menuName = "UI/Font Set")]
public sealed class UIFontSet : ScriptableObject
{
    public Font Regular;
    public Font Medium;
    public Font SemiBold;
    public Font Bold;
}
