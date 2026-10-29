using Godot;

namespace Xpace.Space;

/// <summary>
/// Digits to paint with (a .tres per font, in assets/fonts), white on a
/// clear or black ground so any colour can be laid over them: either one
/// picture per digit, the glyph filling it (<see cref="Digits"/>, as
/// Synty's PolygonGeneric decals come), or one <see cref="Atlas"/> with each
/// digit's cell in pixels (<see cref="Cells"/>, as the "01" ... "09" row of
/// Synty's sign texture). Used by <see cref="HangarMark"/>.
/// </summary>
[GlobalClass, Tool]
public partial class GlyphFont : Resource
{
    /// <summary>A picture per digit, 0 to 9.</summary>
    [Export] public Texture2D[] Digits = [];

    /// <summary>Or one picture holding them all...</summary>
    [Export] public Texture2D Atlas;

    /// <summary>...with each digit's cell in it, pixels, 0 to 9.</summary>
    [Export] public Godot.Collections.Array<Rect2> Cells = new();

    /// <summary>Where the paint goes is the picture's alpha (else its brightness).</summary>
    [Export] public bool CoverFromAlpha;

    /// <summary>Gap between characters, a share of their height.</summary>
    [Export] public float Spacing = 0.08f;

    /// <summary>The picture holding a digit and its cell there in UV.</summary>
    public (Texture2D Texture, Rect2 Uv) Glyph(int digit)
    {
        if (digit >= 0 && digit < Digits.Length && Digits[digit] != null)
            return (Digits[digit], new Rect2(0, 0, 1, 1));
        if (Atlas == null || digit < 0 || digit >= Cells.Count)
            return (null, default);
        Vector2 size = Atlas.GetSize();
        Rect2 cell = Cells[digit];
        return (Atlas, new Rect2(cell.Position / size, cell.Size / size));
    }

    /// <summary>A digit's width over its height.</summary>
    public float Aspect(int digit)
    {
        var (texture, uv) = Glyph(digit);
        if (texture == null)
            return 0.6f;
        Vector2 pixels = uv.Size * texture.GetSize();
        return pixels.X / pixels.Y;
    }
}
