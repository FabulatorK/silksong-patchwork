using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Patchwork.Util;

public static class SpriteUtil
{
    public static Rect GetSpriteRect(tk2dSpriteDefinition def, Texture tex)
    {
        Vector2[] uvs = def.uvs;

        int xMin= Mathf.FloorToInt(uvs.Min(uv => uv.x) * tex.width + 0.25f);
        int xMax=Mathf.CeilToInt(uvs.Max(uv=>uv.x) * tex.width - 0.25f);
        int yMin= Mathf.FloorToInt(uvs.Min(uv => uv.y) * tex.height + 0.25f);
        int yMax = Mathf.CeilToInt(uvs.Max(uv => uv.y) * tex.height - 0.25f);
        int width = math.min(xMax - xMin, tex.width);
        int height = math.min(yMax - yMin, tex.height);

        return new Rect(xMin, yMin, width, height);
    }
}