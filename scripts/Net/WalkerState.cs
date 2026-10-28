using System;
using System.Collections.Generic;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// A player on foot, as the other machines see them: in which station, and where and how in its frame (the station
/// node's, the parent of its radio): a station is the same on every machine, wherever each one's floating origin is.
/// </summary>
public struct WalkerState
{
    /// <summary>The station, by its callsign.</summary>
    public string Station;
    /// <summary>The feet, in the station's frame.</summary>
    public Vector3 Position;
    /// <summary>Which way the body faces, in the station's frame.</summary>
    public Quaternion Facing;
    /// <summary>Metres a second, in the station's frame.</summary>
    public Vector3 Velocity;
    public bool Grounded;
    /// <summary>The sender's tick when it was so.</summary>
    public uint Tick;

    public const int Bytes = 4 + 1 + Radio.CallsignLength + 12 + 16 + 12;

    public byte[] ToBytes()
    {
        var body = new List<byte>(Bytes);
        body.AddRange(BitConverter.GetBytes(Tick));
        body.Add((byte)(Grounded ? 1 : 0));
        foreach (char c in (Station ?? "").PadRight(Radio.CallsignLength)[..Radio.CallsignLength])
            body.Add(c is >= ' ' and <= '~' ? (byte)c : (byte)'?');
        foreach (float f in new[] { Position.X, Position.Y, Position.Z, Facing.X, Facing.Y, Facing.Z, Facing.W, Velocity.X, Velocity.Y, Velocity.Z })
            body.AddRange(BitConverter.GetBytes(f));
        return body.ToArray();
    }

    /// <summary>A state from <paramref name="bytes"/> at <paramref name="at"/>, or null if they aren't one.</summary>
    public static WalkerState? Read(byte[] bytes, int at)
    {
        if (bytes.Length - at != Bytes)
            return null;
        var s = new WalkerState
        {
            Tick = BitConverter.ToUInt32(bytes, at),
            Grounded = bytes[at + 4] != 0,
            Station = System.Text.Encoding.ASCII.GetString(bytes, at + 5, Radio.CallsignLength).TrimEnd(),
        };
        int f = at + 5 + Radio.CallsignLength;
        float F(int i) => BitConverter.ToSingle(bytes, f + i * 4);
        s.Position = new Vector3(F(0), F(1), F(2));
        s.Facing = new Quaternion(F(3), F(4), F(5), F(6));
        s.Velocity = new Vector3(F(7), F(8), F(9));
        if (!s.Position.IsFinite() || !s.Velocity.IsFinite() || !s.Facing.IsFinite() || s.Facing.LengthSquared() < 0.5f)
            return null;
        s.Facing = s.Facing.Normalized();
        return s;
    }
}
