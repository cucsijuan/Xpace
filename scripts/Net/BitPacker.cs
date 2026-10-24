using System;
using System.Collections.Generic;

namespace Xpace.Net;

/// <summary>Writes values of any width up to 32 bits into bytes, low bits first.</summary>
public sealed class BitWriter
{
    private readonly List<byte> _bytes = new();
    private ulong _scratch;
    private int _bits;

    public int BitCount => _bytes.Count * 8 + _bits;

    public void Write(uint value, int bits)
    {
        _scratch |= (ulong)(value & Mask(bits)) << _bits;
        _bits += bits;
        while (_bits >= 8)
        {
            _bytes.Add((byte)_scratch);
            _scratch >>= 8;
            _bits -= 8;
        }
    }

    public void WriteSigned(int value, int bits) => Write((uint)value, bits);

    /// <summary>A value in -range..range, in steps of range / (2^(bits-1) - 1), clamped.</summary>
    public void WriteScaled(double value, double range, int bits)
    {
        int steps = (1 << (bits - 1)) - 1;
        WriteSigned((int)Math.Round(Math.Clamp(value / range, -1, 1) * steps), bits);
    }

    public byte[] ToArray()
    {
        var bytes = new List<byte>(_bytes);
        if (_bits > 0)
            bytes.Add((byte)_scratch);
        return bytes.ToArray();
    }

    internal static uint Mask(int bits) => bits == 32 ? uint.MaxValue : (1u << bits) - 1;
}

/// <summary>Reads what a <see cref="BitWriter"/> wrote. Reading past the end gives zeros and sets <see cref="Overrun"/>.</summary>
public sealed class BitReader
{
    private readonly byte[] _bytes;
    private int _next;
    private ulong _scratch;
    private int _bits;

    public BitReader(byte[] bytes) { _bytes = bytes; }

    public bool Overrun { get; private set; }

    public uint Read(int bits)
    {
        while (_bits < bits)
        {
            if (_next < _bytes.Length)
                _scratch |= (ulong)_bytes[_next] << _bits;
            else
                Overrun = true;
            _next++;
            _bits += 8;
        }
        uint value = (uint)(_scratch & BitWriter.Mask(bits));
        _scratch >>= bits;
        _bits -= bits;
        return value;
    }

    public int ReadSigned(int bits)
    {
        int shift = 32 - bits;
        return (int)(Read(bits) << shift) >> shift;
    }

    public double ReadScaled(double range, int bits) => ReadSigned(bits) * range / ((1 << (bits - 1)) - 1);
}
