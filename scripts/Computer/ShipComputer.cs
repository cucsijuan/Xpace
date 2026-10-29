using System;
using System.IO;
using System.Security.Cryptography;
using Godot;
using Xpace.Native;
using Xpace.Ship;

namespace Xpace.Computer;

/// <summary>
/// The ship's main computer: a pc86 machine with a VGA card, a monitor, a
/// keyboard and a 3.5" drive with the system diskette. It runs in step with
/// the game and shows its monitor in <see cref="Screen"/>.
///
/// The pilot's own ship keeps its diskette between games
/// (<see cref="KeptDiskPath"/>): written whenever the drive stops after
/// working, and when the game ends. The diskette goes down with the ship:
/// the insurer's new ship (<see cref="Rearm"/>) is a new computer, booting
/// the last backup the pilot sent the insurer (<see cref="SendBackup"/>),
/// or the factory diskette if there is none.
///
/// Both remember which factory diskette they started from (a ".factory"
/// file beside them, the factory image's hash). When the game comes with a
/// new factory diskette, or one of them has no such mark (kept before the
/// game wrote it), it is set aside beside it (ship.old.img) and the ship
/// starts from the new one: the pilot's own programs are in the old copy.
/// </summary>
public partial class ShipComputer : Node, IRearmable
{
    /// <summary>The factory diskette: in drive A: when there is no kept one.</summary>
    [Export(PropertyHint.File, "*.img")] public string DiskPath = "res://disks/cos.img";

    /// <summary>Where the ship's diskette is kept between games (the pilot's ship: user://disks/ship.img); empty, never kept.</summary>
    [Export] public string KeptDiskPath = "";

    /// <summary>The backup copy the insurer holds (user://disks/insurer.img); empty, no insurer's copy.</summary>
    [Export] public string BackupDiskPath = "";

    /// <summary>The computer keeping the pilot's diskette, if any (the in-game menu sends its backup).</summary>
    public static ShipComputer Kept { get; private set; }

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
    private byte[] _kept = Array.Empty<byte>();
    private string _factory = "";
    private bool _driveWas;
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

        // Tests run the game's scenes with the factory software, and never touch the pilot's diskettes
        if (!IsInsideTree() || (GetTree().CurrentScene?.SceneFilePath ?? "").StartsWith("res://tests/"))
        {
            if (!KeptDiskPath.StartsWith("user://test"))
                KeptDiskPath = "";
            if (!BackupDiskPath.StartsWith("user://test"))
                BackupDiskPath = "";
        }
        if (KeptDiskPath != "")
            Kept = this;
        _factory = Convert.ToHexString(SHA256.HashData(Factory()))[..16];
        SetAsideIfOld(KeptDiskPath);
        SetAsideIfOld(BackupDiskPath);
        byte[] image = Read(KeptDiskPath);
        if (image.Length == 0 || !InsertDisk(image))
            InsertDisk(Factory());
        _kept = DiskImage();

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

        // The drive stopped: whatever it wrote is kept
        bool light = DriveLight;
        if (_driveWas && !light)
            KeepDisk();
        _driveWas = light;
    }

    public override void _ExitTree()
    {
        if (_pc == IntPtr.Zero)
            return;
        KeepDisk();
        if (Kept == this)
            Kept = null;
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

    // ---------- the diskette ----------

    /// <summary>A copy of the diskette in drive A: (empty if there is none).</summary>
    public unsafe byte[] DiskImage()
    {
        if (_disk == IntPtr.Zero)
            return Array.Empty<byte>();
        return new ReadOnlySpan<byte>(Pc86.disk_data(_disk), (int)Pc86.disk_size(_disk)).ToArray();
    }

    /// <summary>Writes the diskette to <see cref="KeptDiskPath"/> if it changed since it was last kept.</summary>
    public void KeepDisk()
    {
        if (KeptDiskPath == "")
            return;
        byte[] image = DiskImage();
        if (image.Length == 0 || image.AsSpan().SequenceEqual(_kept))
            return;
        if (Write(KeptDiskPath, image) && Write(KeptDiskPath + ".factory", System.Text.Encoding.ASCII.GetBytes(_factory)))
            _kept = image;
    }

    /// <summary>Sends the insurer a copy of the diskette as it is now. False if this computer has no insurer's copy or it could not be written.</summary>
    public bool SendBackup() =>
        BackupDiskPath != "" && Write(BackupDiskPath, DiskImage())
        && Write(BackupDiskPath + ".factory", System.Text.Encoding.ASCII.GetBytes(_factory));

    /// <summary>Whether the insurer holds a copy.</summary>
    public bool HasBackup => BackupDiskPath != "" && Godot.FileAccess.FileExists(BackupDiskPath);

    /// <summary>
    /// The insurer's new ship: a new computer, booting the last backup (the
    /// factory diskette without one). The old diskette went down with the
    /// old ship.
    /// </summary>
    public void Rearm()
    {
        byte[] image = Read(BackupDiskPath);
        if (image.Length == 0 || !InsertDisk(image))
            InsertDisk(Factory());
        Pc86.pc_power_off(_pc);
        Pc86.pc_power_on(_pc);
        KeepDisk();
    }

    private byte[] Factory()
    {
        byte[] image = Read(DiskPath);
        if (image.Length == 0)
            GD.PushError($"ShipComputer: cannot read {DiskPath} (build it: see native/CMakeLists.txt)");
        return image;
    }

    /// <summary>
    /// Moves a diskette made from another factory diskette (or kept before
    /// they were marked) out of the way, to ship.old.img (ship.old2.img...).
    /// </summary>
    private void SetAsideIfOld(string path)
    {
        if (path == "" || !Godot.FileAccess.FileExists(path))
            return;
        string mark = Godot.FileAccess.FileExists(path + ".factory") ? Godot.FileAccess.GetFileAsString(path + ".factory").Trim() : "";
        if (mark == _factory)
            return;
        string stem = path.GetBaseName(), aside = stem + ".old.img";
        for (int n = 2; Godot.FileAccess.FileExists(aside); n++)
            aside = $"{stem}.old{n}.img";
        DirAccess.RenameAbsolute(path, aside);
        DirAccess.RemoveAbsolute(path + ".factory");
        GD.Print($"ShipComputer: {path} came from another factory diskette; set aside as {aside}");
    }

    private static byte[] Read(string path) =>
        path != "" && Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsBytes(path) : Array.Empty<byte>();

    private static bool Write(string path, byte[] image)
    {
        DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"ShipComputer: cannot write {path}");
            return false;
        }
        file.StoreBuffer(image);
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
