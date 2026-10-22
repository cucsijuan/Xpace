using System;
using System.IO;
using Godot;
using Xpace.Native;

namespace Xpace.Computer;

/// <summary>
/// The ship's main computer: a pc86 machine with a VGA card, a monitor, a
/// keyboard and a 3.5" drive with the system diskette. It runs in step with
/// the game and shows its monitor in <see cref="Screen"/>.
/// </summary>
public partial class ShipComputer : Node
{
    /// <summary>The diskette in drive A: when the game starts.</summary>
    [Export(PropertyHint.File, "*.img")] public string DiskPath = "res://disks/cos.img";

    /// <summary>pc86 built-in monitor (0 mono VGA, 1 color VGA, 2 CGA, 3 multisync).</summary>
    [Export] public int MonitorModel = Pc86.MonitorVgaColor;

    /// <summary>Instructions per second; 400,000 is a 4.77 MHz 8088.</summary>
    [Export] public uint InstructionsPerSecond = 400_000;

    [Export] public bool PowerOnAtStart = true;

    /// <summary>
    /// Runs in step with the frames. Off when something else (the ship's
    /// physics) calls <see cref="Advance"/> in its own step.
    /// </summary>
    [Export] public bool RunsItself = true;

    /// <summary>The ship's expansion bus, in slot 2.</summary>
    public ShipBus Bus { get; private set; }

    /// <summary>The machine's 1 MB address space (devices with memory windows read and write it).</summary>
    public unsafe byte* Memory => Pc86.pc_memory(_pc);

    /// <summary>What the monitor shows, 640 x 400.</summary>
    public ImageTexture Screen { get; private set; }

    public bool Powered => _pc != IntPtr.Zero && Pc86.pc_status(_pc) != Pc86.PcOff;
    public bool DriveLight => _drive != IntPtr.Zero && Pc86.fdd_motor(_drive) != 0;
    public string Error => _pc == IntPtr.Zero ? "" : Pc86.ErrorText(_pc);
    public double EmulatedSeconds => _pc == IntPtr.Zero ? 0 : Pc86.pc_time_us(_pc) / 1e6;

    private IntPtr _pc, _vga, _fdc, _monitor, _keyboard, _drive, _disk;
    private byte[] _pixels;
    private Image _image;
    private Image.Format _format;

    public override void _Ready()
    {
        Pc86.EnsureLoaded();
        _pc = Pc86.pc_create();
        _vga = Pc86.pc_vga_card_create();
        _fdc = Pc86.pc_fdc_card_create();
        _monitor = Pc86.monitor_create(Pc86.monitor_builtin_model(MonitorModel));
        _keyboard = Pc86.kbd_create(Pc86.kbd_builtin_model(Pc86.KbdEnhanced101));
        _drive = Pc86.fdd_create(Pc86.fdd_builtin_model(Pc86.Fdd35Hd));

        Pc86.pc_insert_card(_pc, 0, Pc86.pc_vga_card_slot(_vga));
        Pc86.pc_vga_card_connect_monitor(_vga, _monitor);
        Pc86.pc_fdc_card_connect_drive(_fdc, 0, _drive);
        Pc86.pc_insert_card(_pc, 1, Pc86.pc_fdc_card_slot(_fdc));
        Bus = new ShipBus();
        Pc86.pc_insert_card(_pc, 2, Bus.Card);
        Pc86.pc_connect_keyboard(_pc, _keyboard);
        Pc86.pc_set_speed(_pc, InstructionsPerSecond);

        byte[] image = Godot.FileAccess.GetFileAsBytes(DiskPath);
        if (image.Length == 0)
            GD.PushError($"ShipComputer: cannot read {DiskPath} (run native\\build.bat)");
        else
            InsertDisk(image);

        int channels = Pc86.monitor_channels(_monitor);
        _format = channels == 3 ? Image.Format.Rgb8 : Image.Format.L8;
        _pixels = new byte[Pc86.ScreenWidth * Pc86.ScreenHeight * channels];
        _image = Image.CreateFromData(Pc86.ScreenWidth, Pc86.ScreenHeight, false, _format, _pixels);
        Screen = ImageTexture.CreateFromImage(_image);

        if (PowerOnAtStart)
            Pc86.pc_power_on(_pc);
    }

    /// <summary>Runs the machine for dt seconds of its time.</summary>
    public void Advance(double dt)
    {
        // A long hitch (a breakpoint, loading) is not worth catching up on.
        Pc86.pc_run(_pc, Math.Min(dt, 0.1));
    }

    public override unsafe void _Process(double delta)
    {
        if (RunsItself)
            Advance(delta);
        fixed (byte* p = _pixels)
            Pc86.pc_vga_card_display(_vga, (float)delta, p);
        _image.SetData(Pc86.ScreenWidth, Pc86.ScreenHeight, false, _format, _pixels);
        Screen.Update(_image);
    }

    public override void _ExitTree()
    {
        if (_pc == IntPtr.Zero)
            return;
        Pc86.pc_power_off(_pc);
        Pc86.pc_destroy(_pc);
        Bus.Dispose();
        Pc86.pc_vga_card_destroy(_vga);
        Pc86.pc_fdc_card_destroy(_fdc);
        Pc86.monitor_destroy(_monitor);
        Pc86.kbd_destroy(_keyboard);
        Pc86.fdd_eject(_drive);
        Pc86.fdd_destroy(_drive);
        if (_disk != IntPtr.Zero)
            Pc86.disk_destroy(_disk);
        _pc = IntPtr.Zero;
    }

    // ---------- controls ----------

    /// <summary>The text on screen in a text mode (40 or 80 columns), one line per row.</summary>
    public unsafe string ScreenText(int rows = 25)
    {
        byte* memory = Pc86.pc_memory(_pc);
        int cols = memory[0x44A] == 40 ? 40 : 80;  // the BIOS keeps the width at 0040:004A
        byte* video = memory + 0xB8000;
        var sb = new System.Text.StringBuilder();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                byte c = video[(row * cols + col) * 2];
                sb.Append(c >= 32 && c < 127 ? (char)c : ' ');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Runs a .COM program in place of whatever was running. False if it does not fit.</summary>
    public unsafe bool LoadCom(byte[] program)
    {
        fixed (byte* p = program)
            return Pc86.pc_load_com(_pc, p, (nuint)program.Length) == 0;
    }

    public void PowerOn() => Pc86.pc_power_on(_pc);
    public void PowerOff() => Pc86.pc_power_off(_pc);
    public void Reset() => Pc86.pc_reset(_pc);

    /// <summary>A key by its pc86 name (see <see cref="KeyMap"/>). False if the PC has no such key.</summary>
    public bool Key(string code, bool pressed)
    {
        ushort key = code == null ? (ushort)0 : Pc86.kbd_key_from_code(code);
        if (key == 0)
            return false;
        Pc86.kbd_key(_keyboard, key, pressed ? 1 : 0);
        return true;
    }

    public void ReleaseKeys() => Pc86.kbd_release_all(_keyboard);

    /// <summary>Replaces the diskette in drive A: with a 1.44 MB or 720 KB image.</summary>
    public unsafe bool InsertDisk(byte[] image)
    {
        IntPtr disk;
        fixed (byte* p = image)
            disk = Pc86.disk_create_image(p, (nuint)image.Length);
        if (disk == IntPtr.Zero)
            return false;
        Pc86.fdd_eject(_drive);
        if (_disk != IntPtr.Zero)
            Pc86.disk_destroy(_disk);
        _disk = disk;
        Pc86.fdd_insert(_drive, _disk);
        return true;
    }

    // ---------- saving ----------

    private const uint SaveMagic = 0x31435058; // "XPC1"

    /// <summary>
    /// The whole computer as bytes: the machine with its cards, the keyboard,
    /// the drive and the diskette in it.
    /// </summary>
    public unsafe byte[] SaveState()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(SaveMagic);
        WriteBlock(w, (int)Pc86.pc_state_size(_pc), (p, n) => Pc86.pc_save_state(_pc, p, n));
        WriteBlock(w, (int)Pc86.kbd_state_size(), (p, n) => Pc86.kbd_save_state(_keyboard, p, n));
        WriteBlock(w, (int)Pc86.fdd_state_size(), (p, n) => Pc86.fdd_save_state(_drive, p, n));
        int diskLen = _disk == IntPtr.Zero ? 0 : (int)Pc86.disk_size(_disk);
        w.Write(diskLen);
        if (diskLen > 0)
            w.Write(new ReadOnlySpan<byte>(Pc86.disk_data(_disk), diskLen));
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Restores what <see cref="SaveState"/> made. False if the bytes do not fit this computer.</summary>
    public unsafe bool LoadState(byte[] data)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(data));
            if (r.ReadUInt32() != SaveMagic)
                return false;
            byte[] machine = r.ReadBytes(r.ReadInt32());
            byte[] keyboard = r.ReadBytes(r.ReadInt32());
            byte[] drive = r.ReadBytes(r.ReadInt32());
            byte[] disk = r.ReadBytes(r.ReadInt32());
            if (disk.Length > 0 && !InsertDisk(disk))
                return false;
            fixed (byte* m = machine)
            fixed (byte* k = keyboard)
            fixed (byte* d = drive)
                return Pc86.pc_load_state(_pc, m, (nuint)machine.Length) == 0
                    && Pc86.kbd_load_state(_keyboard, k, (nuint)keyboard.Length) == 0
                    && Pc86.fdd_load_state(_drive, d, (nuint)drive.Length) == 0;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private unsafe delegate nuint Saver(byte* buf, nuint len);

    private static unsafe void WriteBlock(BinaryWriter w, int size, Saver save)
    {
        byte[] block = new byte[size];
        fixed (byte* p = block)
            save(p, (nuint)size);
        w.Write(size);
        w.Write(block);
    }
}
