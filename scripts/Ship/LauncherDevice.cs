using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// A missile launcher with its datalink, bus slot 6 (160h). Told to fire,
/// it lets a missile go at the target the radar has locked; from then on,
/// every step the radar keeps that track, the datalink sends each of its
/// missiles where the radar puts it, from the missile (the ship knows where
/// its own missiles are). The track is whatever the radar's tracker
/// follows: if chaff took it, the missiles are sent to the chaff. No track,
/// no word to the missile: it flies on what it last knew.
///
/// Registers: +0 type 7  +2 missiles left  +3 missiles in flight
/// +4 status: bit 0 ready to fire, bit 1 the radar has a lock
/// +6 write 1: fire at the radar's locked target
/// The newest missile, as its datalink reports it: +8 time to go (1/8 s,
/// 0FFFFh unknown)  +10 its status (bit 0 guiding, 1 no word from the ship,
/// 2 lost)  +11 how it went (0 none, 1 flying, 2 hit, 3 missed)
/// +12 its fuel (the speed change left, m/s)
/// </summary>
public partial class LauncherDevice : Node3D, IShipDevice
{
    public const int Slot = 6;
    public const byte Type = 7;

    [Export] public PackedScene MissileScene;
    [Export] public int Missiles = 4;
    [Export] public double ReloadSeconds = 1.0;     // between launches
    [Export] public float EjectSpeed = 20f;         // m/s, ahead of the ship
    [Export] public float DatalinkRange = 20000f;
    [Export] public NodePath RadarPath = "../Radar";

    public int MissilesLeft { get; private set; }
    public IReadOnlyList<Missile> InFlight => _inFlight;
    public Missile Newest { get; private set; }
    /// <summary>How the newest missile went (it is gone once it has).</summary>
    public Missile.Outcome NewestResult => _newestResult;
    /// <summary>What the newest missile's warhead did to its target.</summary>
    public float NewestDamage { get; private set; }
    /// <summary>Fired since the start, for the tests.</summary>
    public int Launched { get; private set; }

    private RadarDevice _radar;
    private readonly List<Missile> _inFlight = new();
    private double _reload;
    private bool _registersSet;
    private Missile.Outcome _newestResult;
    private bool _anyFired;

    public override void _Ready()
    {
        MissilesLeft = Missiles;
        _radar = GetNodeOrNull<RadarDevice>(RadarPath);
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (!_registersSet)
        {
            _registersSet = true;
            bus[Slot, 0] = Type;
            bus[Slot, 1] = 1;
        }
        _inFlight.RemoveAll(m => !IsInstanceValid(m) || m.Result != Missile.Outcome.Flying);
        if (Newest != null && IsInstanceValid(Newest))
            _newestResult = Newest.Result;

        bool locked = _radar?.Locked != null;
        bus[Slot, 2] = (byte)MissilesLeft;
        bus[Slot, 3] = (byte)_inFlight.Count;
        bus[Slot, 4] = (byte)((locked && CanFire ? 1 : 0) | (locked ? 2 : 0));
        bool flying = _anyFired && _newestResult == Missile.Outcome.Flying && IsInstanceValid(Newest);
        bus.SetWord(Slot, 8, flying && Newest.TimeToGo >= 0 ? (int)(Newest.TimeToGo * 8) : -1);
        bus[Slot, 10] = flying ? Newest.Status : (byte)0;
        bus[Slot, 11] = (byte)(!_anyFired ? 0 : _newestResult switch
        {
            Missile.Outcome.Flying => 1,
            Missile.Outcome.Hit => 2,
            _ => 3,
        });
        bus.SetWord(Slot, 12, flying ? (int)Newest.FuelLeft : 0);
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        _reload -= delta;
        bool fire = bus.TakeWritten(Slot, 6) && (bus[Slot, 6] & 1) != 0;
        if (fire && CanFire && _radar?.Locked != null)
            Launch(ship);

        // The datalink: this step's radar measurement, to the missiles fired on that track
        if (_radar?.Locked == null)
            return;
        foreach (Missile m in _inFlight)
            if (IsInstanceValid(m) && m.TrackNumber == _radar.TrackNumber && m.GlobalPosition.DistanceTo(GlobalPosition) <= DatalinkRange)
                m.Uplink(_radar.TrackPosition - m.GlobalPosition);
    }

    private bool CanFire => MissilesLeft > 0 && _reload <= 0;

    private void Launch(ShipBody ship)
    {
        Missile m = MissileScene?.Instantiate<Missile>() ?? new Missile();
        m.Target = _radar.Locked;
        m.TrackNumber = _radar.TrackNumber;
        m.TreeExiting += () =>
        {
            if (m == Newest)
            {
                _newestResult = m.Result;
                NewestDamage = m.DamageDealt;
            }
        };
        ship.GetParent().AddChild(m);
        m.GlobalTransform = GlobalTransform;
        m.Velocity = ship.LinearVelocity - ship.GlobalBasis.Z * EjectSpeed;
        _inFlight.Add(m);
        Newest = m;
        _newestResult = Missile.Outcome.Flying;
        NewestDamage = 0;
        _anyFired = true;
        MissilesLeft--;
        Launched++;
        _reload = ReloadSeconds;
    }
}
