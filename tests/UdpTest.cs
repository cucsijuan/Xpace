using System;
using System.Collections.Generic;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The session over real UDP sockets (EnetTransport on this machine's
/// loopback): a host and two guests in one process, each with its own
/// clock, started far apart. The guests are numbered by the host, take its
/// tick, see each other's ships through it, a guest's rounds are judged and
/// heard by everyone, and when a guest leaves, the others let its ship go.
/// Run: Godot --headless --path . res://tests/udp_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class UdpTest : Node
{
    private const float Dt = 1f / 60;
    private const int Port = 24699;
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private int _failures;

    private sealed class Ship : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => new[] { Box };
        public SectorPoint Position;
        public Vector3 Velocity;
        public int Hits;

        public ShipSnapshot Snapshot(uint tick) => new()
        {
            Tick = tick, Position = Position, Orientation = Quaternion.Identity, Velocity = Velocity,
        };

        public void TakeHit(HitEvent hit) => Hits++;
    }

    /// <summary>One machine: its transport, its clock, its session once it is in.</summary>
    private sealed class Machine
    {
        public EnetTransport Transport;
        public Session Session;
        public uint Tick;
        public Ship Own;
        public readonly Dictionary<ushort, Ship> Seen = new();
        public int Heard, Forgotten;
        public bool HostGone;
    }

    private readonly List<Machine> _machines = new();
    private Machine _host, _a, _b;
    private int _frame;
    private int _fired;

    public override void _Ready()
    {
        _host = Add(EnetTransport.Host(Port), 50_000);
        _a = Add(EnetTransport.Join("127.0.0.1", Port), 0);
        _b = Add(EnetTransport.Join("127.0.0.1", Port), 123_456);
        In(_host, Session.HostPeer, new SectorPoint(0, 0, -500), Vector3.Zero);
        _a.Transport.PeerJoined += id => In(_a, id, new SectorPoint(0, 0, 0), Vector3.Zero);
        _b.Transport.PeerJoined += id => In(_b, id, new SectorPoint(200, 0, 0), new Vector3(0, 50, 0));
        _a.Transport.PeerLeft += peer => _a.HostGone |= peer == Session.HostPeer;
    }

    private Machine Add(EnetTransport t, uint tick)
    {
        var m = new Machine { Transport = t, Tick = tick };
        _machines.Add(m);
        return m;
    }

    private static void In(Machine m, byte peer, SectorPoint at, Vector3 velocity)
    {
        m.Session = new Session(peer, m.Transport) { TickSeconds = Dt };
        m.Session.AdjustClock = ticks => m.Tick = (uint)(m.Tick + ticks);
        m.Session.SpawnRemote = id => m.Seen[id] = new Ship { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } };
        m.Session.HitConfirmed += _ => m.Heard++;
        m.Session.Forgotten += _ => m.Forgotten++;
        m.Own = new Ship { NetId = NetIds.PlayerShip(peer), IsLocal = true, Position = at, Velocity = velocity };
        m.Session.Add(m.Own);
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;
        foreach (Machine m in _machines)
        {
            m.Tick++;
            if (m.Session == null)
            {
                m.Transport?.Service();
                continue;
            }
            m.Session.Tick(m.Tick);
            m.Own.Position += m.Own.Velocity * Dt;
        }

        // A fires at the host's ship, five rounds a second
        if (_frame >= 120 && _frame < 300 && _frame % 12 == 0 && _a.Session?.Synced == true)
        {
            _a.Session.Fire(_a.Tick, _a.Own.NetId, new SectorPoint(0, 0, 0), new Vector3(0, 0, -1200), 10);
            _fired++;
        }

        if (_frame == 300)
            Checks1();
        if (_frame == 330)                          // B leaves
        {
            _b.Transport.Dispose();
            _machines.Remove(_b);
        }
        if (_frame == 600)
        {
            Checks2();
            _host.Transport.Dispose();
            _machines.Remove(_host);
        }
        if (_frame == 1100)
        {
            Check("A hears that the host is gone", _a.HostGone);
            _a.Transport.Dispose();
            GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private void Checks1()
    {
        Check($"the guests are numbered by the host (A {_a.Session?.LocalPeer}, B {_b.Session?.LocalPeer})",
              _a.Session?.LocalPeer == 1 && _b.Session?.LocalPeer == 2);
        Check($"their clocks are the host's (A {(int)(_a.Tick - _host.Tick)}, B {(int)(_b.Tick - _host.Tick)} ticks off; round trips {_a.Session?.RoundTrip}, {_b.Session?.RoundTrip})",
              Math.Abs((int)(_a.Tick - _host.Tick)) <= 1 && Math.Abs((int)(_b.Tick - _host.Tick)) <= 1);
        Check($"every machine sees the other two ships (host {_host.Seen.Count}, A {_a.Seen.Count}, B {_b.Seen.Count})",
              _host.Seen.Count == 2 && _a.Seen.Count == 2 && _b.Seen.Count == 2);
        float off = Off(_a.Seen.GetValueOrDefault(NetIds.PlayerShip(2)), _b);
        Check($"A sees B's ship where B had it at the tick shown ({off * 100:0.0} cm off)", off < 0.05f);
    }

    private void Checks2()
    {
        Check($"A fired {_fired} rounds at the host's ship, which took {_host.Own.Hits}", _fired >= 14 && _host.Own.Hits == _fired);
        Check($"A and the host heard every hit ({_a.Heard}, {_host.Heard} of {_fired})", _a.Heard == _fired && _host.Heard == _fired);
        Check($"when B left, the host and A let its ship go ({_host.Forgotten}, {_a.Forgotten})",
              _host.Forgotten == 1 && _a.Forgotten == 1 && _host.Session.Entity(NetIds.PlayerShip(2)) == null && _a.Session.Entity(NetIds.PlayerShip(2)) == null);
    }

    /// <summary>How far a copy shows a ship moving at constant velocity from where its owner had it at the shown tick.</summary>
    private static float Off(Ship copy, Machine owner)
    {
        if (copy?.Track.ShownTick is not double shown)
            return float.MaxValue;
        // the owner moves its ship after each tick's snapshot: it is at its tick + 1 now
        SectorPoint then = owner.Own.Position + owner.Own.Velocity * (float)((shown - owner.Tick - 1) * Dt);
        return then.To(copy.Track.Sample().Position).Length();
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
