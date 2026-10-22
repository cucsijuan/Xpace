using Godot;
using Xpace.Computer;
using Xpace.Native;

namespace Xpace.Ship;

/// <summary>
/// The two side screens of the cockpit: text displays of 40 x 16 cells,
/// memory-mapped like a CGA card (character, attribute) at D000:0000 and
/// D000:0800. Bus slot 5 (150h) tells programs where they are:
///
///   +0 type 6  +2 segment  +4 left offset  +6 right offset  +8 columns  +9 rows
/// </summary>
public partial class DisplayDevice : Node, IShipDevice
{
    public const int Slot = 5;
    public const byte Type = 6;
    public const int Cols = 40, Rows = 16;
    public const int Width = Cols * 8, Height = Rows * 16;

    public ImageTexture Left { get; private set; }
    public ImageTexture Right { get; private set; }

    private ShipBody _ship;
    private readonly byte[] _pixels = new byte[Width * Height * 3];
    private Image _image;

    public override void _Ready()
    {
        _image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgb8);
        Left = ImageTexture.CreateFromImage(_image);
        Right = ImageTexture.CreateFromImage(_image);
    }

    public ImageTexture Screen(int index) => index == 0 ? Left : Right;

    /// <summary>The characters on a screen (0 left, 1 right), one line per row.</summary>
    public unsafe string Text(int index)
    {
        if (_ship == null)
            return "";
        byte* cells = _ship.Computer.Memory + ShipMemory.Base + (index == 0 ? ShipMemory.LeftDisplay : ShipMemory.RightDisplay);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Cols * Rows; i++)
        {
            byte c = cells[i * 2];
            sb.Append(c >= 32 && c < 127 ? (char)c : ' ');
            if (i % Cols == Cols - 1)
                sb.Append('\n');
        }
        return sb.ToString();
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        if (_ship == null)
        {
            _ship = ship;
            ShipBus bus = ship.Computer.Bus;
            bus[Slot, 0] = Type;
            bus[Slot, 1] = 1;
            bus.SetWord(Slot, 2, unchecked((short)ShipMemory.Segment));
            bus.SetWord(Slot, 4, ShipMemory.LeftDisplay);
            bus.SetWord(Slot, 6, ShipMemory.RightDisplay);
            bus[Slot, 8] = Cols;
            bus[Slot, 9] = Rows;
        }
    }

    public void AfterStep(ShipBody ship, double delta) { }

    public override unsafe void _Process(double delta)
    {
        if (_ship == null)
            return;
        byte* memory = _ship.Computer.Memory;
        Draw(memory + ShipMemory.Base + ShipMemory.LeftDisplay, Left);
        Draw(memory + ShipMemory.Base + ShipMemory.RightDisplay, Right);
    }

    private unsafe void Draw(byte* cells, ImageTexture texture)
    {
        fixed (byte* p = _pixels)
            Pc86.xbus_render_text(cells, Cols, Rows, p);
        _image.SetData(Width, Height, false, Image.Format.Rgb8, _pixels);
        texture.Update(_image);
    }
}
