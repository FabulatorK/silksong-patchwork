using System.Linq;
using Patchwork;
using Patchwork.Handlers;
using Patchwork.Packs;
using Patchwork.Util;
using UnityEngine;

namespace Patchwork.GUI;

/// <summary>
/// Always-on corner badge for end users.
/// Shows active pack count, conflict count, sprite count, and audio clip count.
/// Clicking opens the Pack Manager.
/// </summary>
public static class StatusOverlay
{
    private const float PaddingH     = 10f;
    private const float PaddingV     = 6f;
    private const float BadgeH       = 28f;
    private const float BottomMargin = 10f;
    private const float LeftMargin   = 10f;
    private const float StripH       = 2f;  // horizontal cassette strip height

    private static GUIStyle _labelStyle;
    private static int      _lastFontSize;

    public static void Draw()
    {
        EnsureStyles();

        int activePacks = PackManager.AllPacks.Count(p => p.IsEnabled);
        int conflicts   = ConflictTracker.Total;
        int sprites     = SpriteLoader.LoadedSpriteCount;
        int clips       = AudioHandler.CachedClipCount;

        // Rich-text label: each segment carries its design-token colour.
        string packPart = $"<color=#33AA88>\u25A0 {activePacks} pack{(activePacks != 1 ? "s" : "")}</color>";
        string statPart = $"<color=#8C8CA6>{sprites} sprites  |  {clips} clips</color>";
        string text = conflicts > 0
            ? $"{packPart}  <color=#FFBF33>\u26A0 {conflicts} conflict{(conflicts != 1 ? "s" : "")}</color>  {statPart}"
            : $"{packPart}  {statPart}";

        Vector2 textSize = _labelStyle.CalcSize(new GUIContent(text));
        float w       = textSize.x + GUIHelper.Scaled(PaddingH * 2);
        float h       = GUIHelper.Scaled(BadgeH);
        float x       = GUIHelper.Scaled(LeftMargin);
        float y       = Screen.height - h - GUIHelper.Scaled(BottomMargin);

        Rect bgRect = new Rect(x, y, w, h);

        // Dark panel background
        UnityEngine.GUI.color = new Color(GUIHelper.ColSurface.r, GUIHelper.ColSurface.g,
            GUIHelper.ColSurface.b, 0.88f);
        UnityEngine.GUI.DrawTexture(bgRect, Texture2D.whiteTexture);

        // Horizontal cassette strip along the bottom edge, fading to transparent
        GUIHelper.DrawCassetteStripHorizontal(bgRect, StripH);

        UnityEngine.GUI.color = Color.white;

        // Rich-text label — rect shortened so MiddleLeft centres above the strip
        float stripClear = GUIHelper.Scaled(StripH + 3f);
        Rect labelRect = new Rect(x + GUIHelper.Scaled(PaddingH), y, w, h - stripClear);
        UnityEngine.GUI.Label(labelRect, text, _labelStyle);

        // Invisible button over the whole badge — click opens Pack Manager
        if (UnityEngine.GUI.Button(bgRect, GUIContent.none, GUIStyle.none))
            Plugin.ShowPackManager = true;
    }

    private static void EnsureStyles()
    {
        int fontSize = GUIHelper.FontSize(13);
        if (_labelStyle == null || _lastFontSize != fontSize)
        {
            _labelStyle = new GUIStyle(UnityEngine.GUI.skin.label)
            {
                fontSize  = fontSize,
                alignment = TextAnchor.MiddleLeft,
                richText  = true,
            };
            _lastFontSize = fontSize;
        }
    }
}
