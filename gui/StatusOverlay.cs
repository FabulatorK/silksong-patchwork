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
    private const float PaddingH   = 10f;
    private const float PaddingV   = 6f;
    private const float BadgeH     = 28f;
    private const float BottomMargin = 10f;
    private const float LeftMargin   = 10f;

    private static Texture2D _bgTex;
    private static GUIStyle  _labelStyle;
    private static int       _lastFontSize;

    public static void Draw()
    {
        EnsureStyles();

        int activePacks = PackManager.AllPacks.Count(p => p.IsEnabled);
        int conflicts   = ConflictTracker.Total;
        int sprites     = SpriteLoader.LoadedSpriteCount;
        int clips       = AudioHandler.CachedClipCount;

        string packPart = $"\u25A0 {activePacks} pack{(activePacks != 1 ? "s" : "")}";
        string text = conflicts > 0
            ? $"{packPart}  \u26A0 {conflicts} conflict{(conflicts != 1 ? "s" : "")}  {sprites} sprites  |  {clips} clips"
            : $"{packPart}  {sprites} sprites  |  {clips} clips";

        Vector2 textSize = _labelStyle.CalcSize(new GUIContent(text));
        float w = textSize.x + GUIHelper.Scaled(PaddingH * 2);
        float h = GUIHelper.Scaled(BadgeH);
        float x = GUIHelper.Scaled(LeftMargin);
        float y = Screen.height - h - GUIHelper.Scaled(BottomMargin);

        Rect bgRect = new Rect(x, y, w, h);

        // Background
        UnityEngine.GUI.color = new Color(0f, 0f, 0f, 0.72f);
        UnityEngine.GUI.DrawTexture(bgRect, _bgTex);
        UnityEngine.GUI.color = Color.white;

        // Text — warn colour when conflicts exist
        Rect labelRect = new Rect(x + GUIHelper.Scaled(PaddingH), y + GUIHelper.Scaled(PaddingV), w, h);
        UnityEngine.GUI.contentColor = conflicts > 0 ? new Color(1f, 0.82f, 0.2f) : Color.white;
        UnityEngine.GUI.Label(labelRect, text, _labelStyle);
        UnityEngine.GUI.contentColor = Color.white;

        // Invisible button over the whole badge — click opens Pack Manager
        if (UnityEngine.GUI.Button(bgRect, GUIContent.none, GUIStyle.none))
            Plugin.ShowPackManager = true;
    }

    private static void EnsureStyles()
    {
        if (_bgTex == null)
        {
            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.1f, 1f));
            _bgTex.Apply();
        }

        int fontSize = GUIHelper.FontSize(13);
        if (_labelStyle == null || _lastFontSize != fontSize)
        {
            _labelStyle = new GUIStyle(UnityEngine.GUI.skin.label)
            {
                fontSize  = fontSize,
                alignment = TextAnchor.MiddleLeft
            };
            _lastFontSize = fontSize;
        }
    }
}
