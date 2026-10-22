using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;

namespace Xpace.Native;

/// <summary>
/// pc86.dll: the machine (pc.h), its cards (pc_vga.h, pc_fdc.h) and the
/// peripherals plugged into them (monitor.h, kbd.h, fdd.h). Every handle is
/// an opaque pointer owned by whoever created it.
/// </summary>
public static unsafe class Pc86
{
    private const string Lib = "pc86";

    // pc_status_t
    public const int PcOff = 0, PcRunning = 1, PcHalted = 2, PcStopped = 3, PcError = 4;

    // Built-in models
    public const int MonitorVgaMono = 0, MonitorVgaColor = 1, MonitorCgaColor = 2, MonitorMultisync = 3;
    public const int KbdEnhanced101 = 1;
    public const int Fdd35Hd = 0;

    public const int ScreenWidth = 640, ScreenHeight = 400;

    private static bool _resolverSet;

    /// <summary>
    /// Points the DllImports at native/bin/pc86.dll in the project (the editor)
    /// or next to the executable (an exported game). Call before anything else.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_resolverSet)
            return;
        _resolverSet = true;
        NativeLibrary.SetDllImportResolver(typeof(Pc86).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib && name != XbusLib)
            return IntPtr.Zero;
        string inProject = ProjectSettings.GlobalizePath($"res://native/bin/{name}.dll");
        string nextToExe = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", $"{name}.dll");
        foreach (string file in new[] { inProject, nextToExe })
            if (File.Exists(file) && NativeLibrary.TryLoad(file, out IntPtr handle))
                return handle;
        return IntPtr.Zero;
    }

    // ---------- machine ----------
    [DllImport(Lib)] public static extern IntPtr pc_create();
    [DllImport(Lib)] public static extern void pc_destroy(IntPtr pc);
    [DllImport(Lib)] public static extern byte* pc_memory(IntPtr pc);
    [DllImport(Lib)] public static extern int pc_insert_card(IntPtr pc, int slot, IntPtr card);
    [DllImport(Lib)] public static extern void pc_connect_keyboard(IntPtr pc, IntPtr keyboard);
    [DllImport(Lib)] public static extern void pc_power_on(IntPtr pc);
    [DllImport(Lib)] public static extern void pc_power_off(IntPtr pc);
    [DllImport(Lib)] public static extern void pc_reset(IntPtr pc);
    [DllImport(Lib)] public static extern int pc_status(IntPtr pc);
    [DllImport(Lib)] private static extern IntPtr pc_error(IntPtr pc);
    [DllImport(Lib)] public static extern void pc_set_speed(IntPtr pc, uint ips);
    [DllImport(Lib)] public static extern uint pc_speed(IntPtr pc);
    [DllImport(Lib)] public static extern void pc_run(IntPtr pc, double dtSeconds);
    [DllImport(Lib)] public static extern ulong pc_time_us(IntPtr pc);
    [DllImport(Lib)] public static extern int pc_load_com(IntPtr pc, byte* data, nuint len);
    [DllImport(Lib)] public static extern double pc_speaker_hz(IntPtr pc);
    [DllImport(Lib)] public static extern nuint pc_state_size(IntPtr pc);
    [DllImport(Lib)] public static extern nuint pc_save_state(IntPtr pc, byte* buf, nuint len);
    [DllImport(Lib)] public static extern int pc_load_state(IntPtr pc, byte* buf, nuint len);

    public static string ErrorText(IntPtr pc) => Marshal.PtrToStringAnsi(pc_error(pc)) ?? "";

    // ---------- VGA card and monitor ----------
    [DllImport(Lib)] public static extern IntPtr pc_vga_card_create();
    [DllImport(Lib)] public static extern void pc_vga_card_destroy(IntPtr card);
    [DllImport(Lib)] public static extern IntPtr pc_vga_card_slot(IntPtr card);
    [DllImport(Lib)] public static extern int pc_vga_card_connect_monitor(IntPtr card, IntPtr monitor);
    [DllImport(Lib)] public static extern int pc_vga_card_display(IntPtr card, float dtSeconds, byte* output);

    [DllImport(Lib)] public static extern IntPtr monitor_builtin_model(int id);
    [DllImport(Lib)] public static extern IntPtr monitor_create(IntPtr model);
    [DllImport(Lib)] public static extern void monitor_destroy(IntPtr monitor);
    [DllImport(Lib)] public static extern int monitor_channels(IntPtr monitor);

    // ---------- keyboard ----------
    [DllImport(Lib)] public static extern IntPtr kbd_builtin_model(int id);
    [DllImport(Lib)] public static extern IntPtr kbd_create(IntPtr model);
    [DllImport(Lib)] public static extern void kbd_destroy(IntPtr keyboard);
    [DllImport(Lib)] public static extern void kbd_key(IntPtr keyboard, ushort key, int pressed);
    [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern ushort kbd_key_from_code(string code);
    [DllImport(Lib)] public static extern void kbd_release_all(IntPtr keyboard);
    [DllImport(Lib)] public static extern byte kbd_leds(IntPtr keyboard);
    [DllImport(Lib)] public static extern nuint kbd_state_size();
    [DllImport(Lib)] public static extern nuint kbd_save_state(IntPtr keyboard, byte* buf, nuint len);
    [DllImport(Lib)] public static extern int kbd_load_state(IntPtr keyboard, byte* buf, nuint len);

    // ---------- diskette controller, drives and diskettes ----------
    [DllImport(Lib)] public static extern IntPtr pc_fdc_card_create();
    [DllImport(Lib)] public static extern void pc_fdc_card_destroy(IntPtr card);
    [DllImport(Lib)] public static extern IntPtr pc_fdc_card_slot(IntPtr card);
    [DllImport(Lib)] public static extern int pc_fdc_card_connect_drive(IntPtr card, int n, IntPtr drive);

    [DllImport(Lib)] public static extern IntPtr fdd_builtin_model(int id);
    [DllImport(Lib)] public static extern IntPtr fdd_create(IntPtr model);
    [DllImport(Lib)] public static extern void fdd_destroy(IntPtr drive);
    [DllImport(Lib)] public static extern int fdd_insert(IntPtr drive, IntPtr disk);
    [DllImport(Lib)] public static extern IntPtr fdd_eject(IntPtr drive);
    [DllImport(Lib)] public static extern int fdd_motor(IntPtr drive);
    [DllImport(Lib)] public static extern nuint fdd_state_size();
    [DllImport(Lib)] public static extern nuint fdd_save_state(IntPtr drive, byte* buf, nuint len);
    [DllImport(Lib)] public static extern int fdd_load_state(IntPtr drive, byte* buf, nuint len);

    // ---------- the ship's bus card (xpace_native.dll, native/xbus.c) ----------
    private const string XbusLib = "xpace_native";
    [DllImport(XbusLib)] public static extern IntPtr xbus_create(ushort basePort, ushort size, int irqLine);
    [DllImport(XbusLib)] public static extern void xbus_destroy(IntPtr bus);
    [DllImport(XbusLib)] public static extern IntPtr xbus_slot(IntPtr bus);
    [DllImport(XbusLib)] public static extern byte* xbus_regs(IntPtr bus);
    [DllImport(XbusLib)] public static extern byte* xbus_read_flags(IntPtr bus);
    [DllImport(XbusLib)] public static extern byte* xbus_written_flags(IntPtr bus);
    [DllImport(XbusLib)] public static extern void xbus_raise_irq(IntPtr bus);

    [DllImport(Lib)] public static extern IntPtr disk_create_image(byte* data, nuint len);
    [DllImport(Lib)] public static extern void disk_destroy(IntPtr disk);
    [DllImport(Lib)] public static extern byte* disk_data(IntPtr disk);
    [DllImport(Lib)] public static extern nuint disk_size(IntPtr disk);
}
