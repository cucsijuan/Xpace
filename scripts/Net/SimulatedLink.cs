using System;
using System.Collections.Generic;

namespace Xpace.Net;

/// <summary>
/// A one-way unreliable link with the faults of a real one: latency,
/// jitter (so packets also arrive out of order) and loss. For tests and
/// for trying the networking on one machine. Seeded: the same faults on
/// every run.
/// </summary>
public sealed class SimulatedLink
{
    public double Latency = 0.1;            // seconds, one way
    public double Jitter = 0.02;            // seconds, spread either side
    public double Loss = 0.05;              // fraction of packets lost

    public int Sent { get; private set; }
    public int Lost { get; private set; }
    public long BytesSent { get; private set; }

    private readonly Random _random;
    private readonly List<(double due, byte[] packet)> _flying = new();

    public SimulatedLink(int seed = 1) { _random = new Random(seed); }

    public void Send(byte[] packet, double now)
    {
        Sent++;
        BytesSent += packet.Length;
        if (_random.NextDouble() < Loss)
        {
            Lost++;
            return;
        }
        double delay = Math.Max(0, Latency + (_random.NextDouble() * 2 - 1) * Jitter);
        _flying.Add((now + delay, packet));
    }

    /// <summary>The packets due by <paramref name="now"/>, in the order they arrive.</summary>
    public IEnumerable<byte[]> Receive(double now)
    {
        _flying.Sort((a, b) => a.due.CompareTo(b.due));
        int due = 0;
        while (due < _flying.Count && _flying[due].due <= now)
            due++;
        var arrived = _flying.GetRange(0, due);
        _flying.RemoveRange(0, due);
        foreach (var (_, packet) in arrived)
            yield return packet;
    }
}
