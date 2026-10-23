using Godot;

namespace Xpace.Ship;

/// <summary>
/// One screen on the ship's display card. Programs find their screen by its
/// name, so a cockpit can have any set of screens: a program whose screen
/// is not there just does not draw. Each screen shows text (character and
/// attribute cells, as on a CGA) or graphics (2 bits a pixel), whichever
/// the program picks.
/// </summary>
[GlobalClass]
public partial class DisplayScreen : Resource
{
    /// <summary>Up to 6 characters, upper case: RADAR, WEAPON, FLIGHT...</summary>
    [Export] public string Name = "SCREEN";

    /// <summary>Text mode: cells of 8 x 16 pixels.</summary>
    [Export(PropertyHint.Range, "1,80")] public int Columns = 40;
    [Export(PropertyHint.Range, "1,50")] public int Rows = 16;

    /// <summary>Graphics mode, in pixels; the width is a multiple of 4.</summary>
    [Export(PropertyHint.Range, "4,320,4")] public int Width = 160;
    [Export(PropertyHint.Range, "1,200")] public int Height = 128;

    /// <summary>The phosphor's colour in graphics mode.</summary>
    [Export] public Color Phosphor = new(0.35f, 1f, 0.4f);

    /// <summary>Seconds the phosphor takes to fade to a tenth after the beam leaves.</summary>
    [Export(PropertyHint.Range, "0.01,3,0.01")] public float Persistence = 0.3f;

    /// <summary>Bytes of memory it needs: enough for either mode.</summary>
    public int MemorySize => Mathf.Max(Columns * Rows * 2, Width / 4 * Height);
}
