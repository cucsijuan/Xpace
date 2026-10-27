using System;
using Xpace.Native;

namespace Xpace.Computer;

/// <summary>
/// The ship's expansion bus as the game sees it: a window of I/O ports whose
/// registers the game reads and writes between machine steps (native/xbus.c).
/// Device slot n is at <see cref="BasePort"/> + n * 16: 32 slots, 100h to 2FFh.
/// </summary>
public sealed unsafe class ShipBus : IDisposable
{
    public const ushort BasePort = 0x100;
    public const int SlotSize = 16;
    public const int Slots = 32;
    public const int Irq = 5;

    public IntPtr Card => Pc86.xbus_slot(_bus);

    private IntPtr _bus;
    private readonly byte* _regs, _read, _written;

    public ShipBus()
    {
        _bus = Pc86.xbus_create(BasePort, Slots * SlotSize, Irq);
        _regs = Pc86.xbus_regs(_bus);
        _read = Pc86.xbus_read_flags(_bus);
        _written = Pc86.xbus_written_flags(_bus);
    }

    public void Dispose()
    {
        if (_bus == IntPtr.Zero)
            return;
        Pc86.xbus_destroy(_bus);
        _bus = IntPtr.Zero;
    }

    public byte this[int slot, int offset]
    {
        get => _regs[slot * SlotSize + offset];
        set => _regs[slot * SlotSize + offset] = value;
    }

    public short Word(int slot, int offset) => (short)(this[slot, offset] | this[slot, offset + 1] << 8);

    public void SetWord(int slot, int offset, int value)
    {
        short v = (short)Math.Clamp(value, -32767, 32767);
        this[slot, offset] = (byte)v;
        this[slot, offset + 1] = (byte)(v >> 8);
    }

    /// <summary>True if the program read the port since the last call (and forgets it).</summary>
    public bool TakeRead(int slot, int offset) => Take(_read, slot * SlotSize + offset);

    /// <summary>True if the program wrote the port since the last call (and forgets it).</summary>
    public bool TakeWritten(int slot, int offset) => Take(_written, slot * SlotSize + offset);

    /// <summary>Writes a port as the program's OUT would (the register and its written flag): for tests and tools.</summary>
    public void Out(int slot, int offset, byte value)
    {
        this[slot, offset] = value;
        _written[slot * SlotSize + offset] = 1;
    }

    public void RaiseIrq() => Pc86.xbus_raise_irq(_bus);

    private static bool Take(byte* flags, int i)
    {
        bool set = flags[i] != 0;
        flags[i] = 0;
        return set;
    }
}
