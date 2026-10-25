using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The session over <see cref="StarTransport"/>, the transport EOS play uses, on links in memory (the EOS side only
/// carries the packets). The host lets in only who it admits, as it does a lobby's members:
///
///   - A and B are admitted, numbered 1 and 2, take the host's clock and see each other's ships through the host;
///   - C is not, gets no answer, and keeps saying hello: once admitted it is let in;
///   - a machine speaking another version is told so and turned away; packets from an address never let in are dropped;
///   - A's rounds are judged by the host and heard;
///   - B leaving the lobby (dropped by the host) leaves the session: everyone lets its ship go, and B hears the host gone;
///   - D, whose game stops dead (no goodbye), is dropped once it has been silent for 10 s.
///
/// Run: Godot --headless --path . res://tests/star_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class StarTest : Node
{
    private const float Dt = 1f / 60;
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private int _failures;

    private sealed class Ship : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => new[] { Box };
        public SectorPoint Position;
        public int Hits;

        public ShipSnapshot Snapshot(uint tick) => new() { Tick = tick, Position = Position, Orientation = Quaternion.Identity };

        public void TakeHit(HitEvent hit) => Hits++;
    }

    private sealed class Machine
    {
        public StarTransport Transport;
        public Session Session;
        public uint Tick;
        public Ship Own;
        public readonly Dictionary<ushort, Ship> Seen = new();
        public int Heard, Forgotten;
        public bool HostGone;
    }

    private readonly MemoryLinks _links = new();
    private readonly HashSet<string> _members = new() { "host", "a", "b" };
    private readonly List<Machine> _machines = new();
    private Machine _host, _a, _b, _c, _d;
    private IPeerLink _old, _stranger;
    private readonly List<byte[]> _oldHeard = new();
    private int _frame, _fired, _strangerSent;

    public override void _Ready()
    {
        _host = Add(StarTransport.Host(_links.Open("host"), _members.Contains, Seconds), 50_000);
        _a = Add(StarTransport.Join(_links.Open("a"), "host", Seconds), 0);
        _b = Add(StarTransport.Join(_links.Open("b"), "host", Seconds), 123_456);
        _c = Add(StarTransport.Join(_links.Open("c"), "host", Seconds), 7);
        In(_host, Session.HostPeer, new SectorPoint(0, 0, -500));
        foreach (Machine m in new[] { _a, _b, _c })
        {
            Machine me = m;
            m.Transport.PeerJoined += id => In(me, id, new SectorPoint(100 * id, 0, 0));
            m.Transport.PeerLeft += peer => me.HostGone |= peer == Session.HostPeer;
        }

        // Another version of the game, and a machine sending session packets it was never let in for
        _old = _links.Open("old");
        _old.Send("host", 2, new byte[] { 1, (byte)(StarTransport.Version + 1) }, true);
        _stranger = _links.Open("stranger");
        _members.Add("old");
    }

    /// <summary>The test's time: frames at 60 a second, however fast they run.</summary>
    private double Seconds() => _frame * Dt;

    private Machine Add(StarTransport t, uint tick)
    {
        var m = new Machine { Transport = t, Tick = tick };
        _machines.Add(m);
        return m;
    }

    private static void In(Machine m, byte peer, SectorPoint at)
    {
        m.Session = new Session(peer, m.Transport) { TickSeconds = Dt };
        m.Session.AdjustClock = ticks => m.Tick = (uint)(m.Tick + ticks);
        m.Session.SpawnRemote = id => m.Seen[id] = new Ship { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } };
        m.Session.HitConfirmed += _ => m.Heard++;
        m.Session.Forgotten += _ => m.Forgotten++;
        m.Own = new Ship { NetId = NetIds.PlayerShip(peer), IsLocal = true, Position = at };
        m.Session.Add(m.Own);
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;
        foreach (Machine m in _machines)
        {
            m.Tick++;
            if (m.Session == null)
                m.Transport.Service();
            else
                m.Session.Tick(m.Tick);
        }
        while (_old.Receive(out _, out _, out byte[] said))
            _oldHeard.Add(said);
        if (_frame % 10 == 0 && _frame < 300)
        {
            _stranger.Send("host", 1, new byte[] { 2, 0, 0, 0 }, true);       // a fire event's kind, from nobody
            _strangerSent++;
        }

        if (_frame >= 120 && _frame < 300 && _frame % 12 == 0 && _a.Session?.Synced == true)
        {
            _a.Session.Fire(_a.Tick, _a.Own.NetId, new SectorPoint(0, 0, 0), new Vector3(0, 0, -1200), 10);
            _fired++;
        }

        if (_frame == 150)
        {
            Check($"A and B are numbered by the host (A {_a.Session?.LocalPeer}, B {_b.Session?.LocalPeer})",
                  _a.Session?.LocalPeer == 1 && _b.Session?.LocalPeer == 2);
            Check($"C, not admitted, gets no answer ({_c.Transport.LocalPeer?.ToString() ?? "none"}) and is not turned away ({!_c.HostGone})",
                  _c.Session == null && !_c.HostGone);
            Check($"another version is told so and turned away ({_oldHeard.Count} answers, {(_oldHeard.Count > 0 ? _oldHeard[0][0] : 0)})",
                  _oldHeard.Count == 1 && _oldHeard[0][0] == 3 && !_host.Transport.Peers.Contains((byte)3));
            _members.Add("c");
        }
        if (_frame == 300)
        {
            Check($"once admitted, C is let in on its next hello (peer {_c.Session?.LocalPeer})", _c.Session?.LocalPeer == 3);
            Check($"their clocks are the host's (A {(int)(_a.Tick - _host.Tick)}, B {(int)(_b.Tick - _host.Tick)}, C {(int)(_c.Tick - _host.Tick)} ticks off)",
                  Math.Abs((int)(_a.Tick - _host.Tick)) <= 1 && Math.Abs((int)(_b.Tick - _host.Tick)) <= 1 && Math.Abs((int)(_c.Tick - _host.Tick)) <= 1);
            Check($"every machine sees the other three ships (host {_host.Seen.Count}, A {_a.Seen.Count}, B {_b.Seen.Count}, C {_c.Seen.Count})",
                  _host.Seen.Count == 3 && _a.Seen.Count == 3 && _b.Seen.Count == 3 && _c.Seen.Count == 3);
            Check($"packets from an address never let in are dropped ({_strangerSent} sent, host still has {_host.Transport.Peers.Count} guests)",
                  _strangerSent > 0 && _host.Transport.Peers.Count == 3);
            _members.Remove("b");
            _host.Transport.Drop("b");
        }
        if (_frame == 310)
        {
            _members.Add("d");
            _d = Add(StarTransport.Join(_links.Open("d"), "host", Seconds), 99);
            _d.Transport.PeerJoined += id => In(_d, id, new SectorPoint(0, 300, 0));
        }
        if (_frame == 560)
            _machines.Remove(_d);              // its game stops dead: no goodbye
        if (_frame == 600)
        {
            Check($"A fired {_fired} rounds at the host's ship, which took {_host.Own.Hits}; A heard {_a.Heard}",
                  _fired >= 14 && _host.Own.Hits == _fired && _a.Heard == _fired);
            Check($"B, out of the lobby, is out of the session: the host and A let its ship go ({_host.Forgotten}, {_a.Forgotten})",
                  _host.Forgotten == 1 && _a.Forgotten == 1 && _host.Session.Entity(NetIds.PlayerShip(2)) == null);
            Check("B hears the host gone", _b.HostGone);
            Check("A and C do not", !_a.HostGone && !_c.HostGone);
            Check($"D is let in (peer {_d.Session?.LocalPeer}) and seen by A", _d.Session?.LocalPeer is byte && _a.Seen.ContainsKey(NetIds.PlayerShip(_d.Session.LocalPeer)));
        }
        if (_frame == 560 + (int)(StarTransport.SilenceTimeout * 60) - 30)
            Check("D, stopped dead, is still numbered a moment before 10 s of silence", _host.Transport.Peers.Contains(_d.Session.LocalPeer));
        if (_frame == 560 + (int)(StarTransport.SilenceTimeout * 60) + 30)
        {
            Check($"after 10 s of silence the host drops it ({_host.Transport.Peers.Count} guests left: A and C)",
                  !_host.Transport.Peers.Contains(_d.Session.LocalPeer) && _host.Transport.Peers.Count == 2);
            foreach (Machine m in _machines)
                m.Transport.Dispose();
            GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
