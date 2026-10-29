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
[GlobalClass]
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

    // Read through Get, not the fields: in the editor (where HangarMark runs
    // to show the number) a resource whose script is not a tool's is a
    // placeholder keeping only these values. A tool resource instead made the
    // editor crash on exit after exporting the game.

    /// <summary>The picture holding a digit of <paramref name="font"/> and its cell there in UV.</summary>
    public static (Texture2D Texture, Rect2 Uv) Glyph(Resource font, int digit)
    {
        var digits = font.Get(PropertyName.Digits).AsGodotArray();
        if (digit >= 0 && digit < digits.Count && digits[digit].As<Texture2D>() is Texture2D picture)
            return (picture, new Rect2(0, 0, 1, 1));
        var atlas = font.Get(PropertyName.Atlas).As<Texture2D>();
        var cells = font.Get(PropertyName.Cells).AsGodotArray();
        if (atlas == null || digit < 0 || digit >= cells.Count)
            return (null, default);
        Vector2 size = atlas.GetSize();
        Rect2 cell = cells[digit].AsRect2();
        return (atlas, new Rect2(cell.Position / size, cell.Size / size));
    }

    /// <summary>A digit's width over its height.</summary>
    public static float Aspect(Resource font, int digit)
    {
        var (texture, uv) = Glyph(font, digit);
        if (texture == null)
            return 0.6f;
        Vector2 pixels = uv.Size * texture.GetSize();
        return pixels.X / pixels.Y;
    }

    public static bool CoversFromAlpha(Resource font) => font.Get(PropertyName.CoverFromAlpha).AsBool();

    public static float SpacingOf(Resource font) => font.Get(PropertyName.Spacing).AsSingle();
}
