using System;
using Xpace.Native;

namespace Xpace.Computer;

/// <summary>
/// A bare 8086 with its firmware and 32 I/O ports backed by registers
/// (native/mcu.c): the brain of a missile. No BIOS, no DOS: the firmware
/// sets up its own interrupt vector and the owner raises it once a step.
/// </summary>
public sealed unsafe class Microcontroller : IDisposable
{
    public const ushort BasePort = 0x100;
    public const int Ports = 32;
    public const byte TickVector = 0x08;

    /// <summary>Instructions a second; 400,000 is a 4.77 MHz 8088.</summary>
    public uint InstructionsPerSecond = 400_000;

    /// <summary>The firmware hit an instruction the CPU does not have: it stopped.</summary>
    public bool Failed => _mcu != IntPtr.Zero && Pc86.mcu_failed(_mcu) != 0;

    /// <summary>Instructions the last step ran (the rest of the step it waited in HLT).</summary>
    public uint LastSteps { get; private set; }

    private IntPtr _mcu;
    private readonly byte* _regs, _written;

    /// <summary>Null firmware or one too big: <see cref="Valid"/> is false.</summary>
    public Microcontroller(byte[] firmware)
    {
        Pc86.EnsureLoaded();
        if (firmware == null || firmware.Length == 0)
            return;
        fixed (byte* p = firmware)
            _mcu = Pc86.mcu_create(p, (nuint)firmware.Length, BasePort);
        if (_mcu == IntPtr.Zero)
            return;
        _regs = Pc86.mcu_regs(_mcu);
        _written = Pc86.mcu_written_flags(_mcu);
    }

    public bool Valid => _mcu != IntPtr.Zero;

    public void Dispose()
    {
        if (_mcu == IntPtr.Zero)
            return;
        Pc86.mcu_destroy(_mcu);
        _mcu = IntPtr.Zero;
    }

    /// <summary>The clock ticks: runs dt seconds' worth of instructions.</summary>
    public void Tick(double dt)
    {
        if (_mcu != IntPtr.Zero)
            LastSteps = Pc86.mcu_step(_mcu, TickVector, (uint)(InstructionsPerSecond * Math.Min(dt, 0.1)));
    }

    public byte this[int port]
    {
        get => _regs[port];
        set => _regs[port] = value;
    }

    public short Word(int port) => (short)(this[port] | this[port + 1] << 8);

    public void SetWord(int port, int value)
    {
        short v = (short)Math.Clamp(value, -32767, 32767);
        this[port] = (byte)v;
        this[port + 1] = (byte)(v >> 8);
    }

    /// <summary>True if the firmware wrote the port since the last call (and forgets it).</summary>
    public bool TakeWritten(int port)
    {
        bool set = _written[port] != 0;
        _written[port] = 0;
        return set;
    }
}
