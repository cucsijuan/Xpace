using System;
using Godot;
using Xpace.Computer;
using Xpace.Native;

namespace Xpace.Ship;

/// <summary>
/// The ship's display card: the screens listed in <see cref="Screens"/>,
/// memory-mapped in segment D000. Bus slot 5 (150h):
///
///   +0 type 6  +1 version 2  +2 segment  +4 screen table (offset)  +6 screens
///
/// The screen table has 16 bytes a screen:
///
///   +0 name (6 characters, space-padded)  +6 mode (the program writes it:
///   0 text, 1 graphics)  +7 columns  +8 rows  +10 width  +12 height
///   +14 memory (offset, a multiple of 16)
///
/// Text: character and attribute pairs, as on a CGA. Graphics: 2 bits a
/// pixel (off, dim, half, bright), four to a byte with the leftmost in the
/// top bits, width/4 bytes a row.
/// </summary>
public partial class DisplayDevice : Node, IShipDevice
{
    public const int Slot = 5;
    public const byte Type = 6, Version = 2;
    public const int EntrySize = 16, MaxScreens = 16;
    public const ushort FirstBuffer = 0x2000;

    [Export] public Godot.Collections.Array<DisplayScreen> Screens = new();

    private ShipBody _ship;
    private Output[] _outputs = Array.Empty<Output>();

    private sealed class Output
    {
        public DisplayScreen Spec;
        public ushort Memory;
        public ImageTexture Texture;
        public Image Image;
        public byte[] Pixels = Array.Empty<byte>();
        public float[] Glow = Array.Empty<float>();
        public int Mode = -1;
    }

    public override void _Ready()
    {
        if (Screens.Count > MaxScreens)
            GD.PushError($"{Name}: at most {MaxScreens} screens");
        _outputs = new Output[Math.Min(Screens.Count, MaxScreens)];
        int memory = FirstBuffer;
        for (int i = 0; i < _outputs.Length; i++)
        {
            var o = new Output { Spec = Screens[i], Memory = (ushort)memory };
            memory += (o.Spec.MemorySize + 15) & ~15;
            o.Image = Image.CreateEmpty(o.Spec.Columns * 8, o.Spec.Rows * 16, false, Image.Format.Rgb8);
            o.Texture = ImageTexture.CreateFromImage(o.Image);
            _outputs[i] = o;
        }
        if (memory > 0x10000)
            GD.PushError($"{Name}: the screens need more than segment D000 holds");
    }

    /// <summary>The picture of the screen with that name, or null.</summary>
    public ImageTexture Screen(string name) => Find(name)?.Texture;

    /// <summary>The characters on a text screen, one line per row.</summary>
    public unsafe string Text(string name)
    {
        Output o = Find(name);
        if (_ship == null || o == null)
            return "";
        byte* cells = _ship.Computer.Memory + ShipMemory.Base + o.Memory;
        var sb = new System.Text.StringBuilder();
        int cols = o.Spec.Columns;
        for (int i = 0; i < cols * o.Spec.Rows; i++)
        {
            byte c = cells[i * 2];
            sb.Append(c >= 32 && c < 127 ? (char)c : ' ');
            if (i % cols == cols - 1)
                sb.Append('\n');
        }
        return sb.ToString();
    }

    private Output Find(string name)
    {
        foreach (Output o in _outputs)
            if (string.Equals(o.Spec.Name, name, StringComparison.OrdinalIgnoreCase))
                return o;
        return null;
    }

    public unsafe void BeforeStep(ShipBody ship, double delta)
    {
        if (_ship != null)
            return;
        _ship = ship;
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = Version;
        bus.SetWord(Slot, 2, unchecked((short)ShipMemory.Segment));
        bus.SetWord(Slot, 4, ShipMemory.Screens);
        bus[Slot, 6] = (byte)_outputs.Length;

        byte* table = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Screens;
        for (int i = 0; i < _outputs.Length; i++)
        {
            DisplayScreen s = _outputs[i].Spec;
            byte* e = table + i * EntrySize;
            string name = (s.Name ?? "").ToUpperInvariant().PadRight(6);
            for (int c = 0; c < 6; c++)
                e[c] = (byte)name[c];
            e[6] = 0;
            e[7] = (byte)s.Columns;
            e[8] = (byte)s.Rows;
            e[9] = 0;
            *(ushort*)(e + 10) = (ushort)s.Width;
            *(ushort*)(e + 12) = (ushort)s.Height;
            *(ushort*)(e + 14) = _outputs[i].Memory;
        }
    }

    public void AfterStep(ShipBody ship, double delta) { }

    public override unsafe void _Process(double delta)
    {
        if (_ship == null)
            return;
        byte* memory = _ship.Computer.Memory + ShipMemory.Base;
        for (int i = 0; i < _outputs.Length; i++)
        {
            Output o = _outputs[i];
            int mode = memory[ShipMemory.Screens + i * EntrySize + 6] == 1 ? 1 : 0;
            if (mode == 0)
                DrawText(o, memory + o.Memory);
            else
                DrawGraphics(o, memory + o.Memory, (float)delta);
        }
    }

    private unsafe void DrawText(Output o, byte* cells)
    {
        int w = o.Spec.Columns * 8, h = o.Spec.Rows * 16;
        Resize(o, 0, w, h);
        fixed (byte* p = o.Pixels)
            Pc86.xbus_render_text(cells, o.Spec.Columns, o.Spec.Rows, p);
        o.Image.SetData(w, h, false, Image.Format.Rgb8, o.Pixels);
        o.Texture.Update(o.Image);
    }

    private unsafe void DrawGraphics(Output o, byte* pixels, float delta)
    {
        int w = o.Spec.Width & ~3, h = o.Spec.Height;
        Resize(o, 1, w, h);
        float fade = Mathf.Pow(0.1f, delta / Mathf.Max(o.Spec.Persistence, 0.001f));
        float* colour = stackalloc float[3] { o.Spec.Phosphor.R, o.Spec.Phosphor.G, o.Spec.Phosphor.B };
        fixed (byte* p = o.Pixels)
        fixed (float* glow = o.Glow)
            Pc86.xbus_render_graphics(pixels, w, h, glow, fade, colour, p);
        o.Image.SetData(w, h, false, Image.Format.Rgb8, o.Pixels);
        o.Texture.Update(o.Image);
    }

    // A change of mode changes the picture's size: the texture takes the new image.
    private static void Resize(Output o, int mode, int w, int h)
    {
        if (o.Mode == mode)
            return;
        o.Mode = mode;
        o.Pixels = new byte[w * h * 3];
        o.Glow = mode == 1 ? new float[w * h] : Array.Empty<float>();
        o.Image = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        o.Texture.SetImage(o.Image);
    }
}
