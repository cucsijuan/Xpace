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
/// It also carries heat seekers, which need neither the radar nor the
/// datalink. Powered (+7), the head of the next one on the rail looks
/// along the launcher's boresight; heat in its view makes it growl, and it
/// follows that heat on its gimbal (locked on once it has held it a
/// moment). With the radar locked, the head looks where the track is
/// instead. A heat seeker can always be fired: it leaves looking where the
/// head on the rail looked, and if that saw nothing it flies ahead and
/// follows the first heat that comes into view. The head is the
/// missile's own: with none left there is nothing to power, and the next
/// one brought to the rail after a launch starts with its head off.
///
/// Registers: +0 type 7  +2 radar missiles left  +3 missiles in flight
/// +4 status: bit 0 ready to fire a radar missile, bit 1 the radar has a
/// lock, bit 2 ready to fire a heat seeker, bit 3 the heat seeker's head
/// sees heat (it growls), bit 4 the head is locked on
/// +6 write 1: fire a radar missile at the radar's locked target,
/// write 2: a heat seeker  +7 bit 0: the heat seeker's head powered
/// The newest missile, as its datalink reports it: +8 time to go (1/8 s,
/// 0FFFFh unknown)  +10 its status (bit 0 guiding, 1 no word from the ship
/// or the head sees nothing, 2 lost)  +11 how it went (0 none, 1 flying,
/// 2 hit, 3 missed)  +12 its fuel (the speed change left, m/s)
/// +14 heat seekers left  +15 the newest's kind (1 radar, 2 heat seeker)
/// </summary>
public partial class LauncherDevice : Node3D, IShipDevice, IRearmable
{
    public const int Slot = 6;
    public const byte Type = 7;

    [Export] public PackedScene MissileScene;
    [Export] public int Missiles = 4;
    [Export] public PackedScene HeatSeekerScene;
    [Export] public int HeatSeekers = 4;
    [Export] public double ReloadSeconds = 1.0;     // between launches
    [Export] public float EjectSpeed = 20f;         // m/s, ahead of the ship
    [Export] public float DatalinkRange = 20000f;
    [Export] public NodePath RadarPath = "../Radar";
    [Export] public double HeadLockSeconds = 0.3;   // heat held this long: locked on

    public int MissilesLeft { get; private set; }
    public int HeatSeekersLeft { get; private set; }
    /// <summary>The heat seeker's head on the rail is powered (the computer selected it).</summary>
    public bool HeadPowered { get; private set; }
    /// <summary>The head on the rail sees heat: it growls.</summary>
    public bool HeatTone => HeadPowered && _head.Tone;
    /// <summary>The head on the rail has held the heat a moment: locked on.</summary>
    public bool HeadLocked => HeatTone && _toneFor >= HeadLockSeconds;
    /// <summary>How much heat the head sees, in times the least it notices.</summary>
    public float HeadSignal => HeadPowered && _railHead != null ? _head.HeatSeen / _railHead.SeekerSensitivity : 0;
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
    private Missile _railHead;                      // the next heat seeker, for its head's figures
    private readonly SeekerHead _head = new();      // its head, on the rail
    private double _toneFor;

    public override void _Ready()
    {
        MissilesLeft = Missiles;
        HeatSeekersLeft = HeatSeekers;
        _radar = GetNodeOrNull<RadarDevice>(RadarPath);
        _railHead = HeatSeekerScene?.Instantiate<Missile>();
        _railHead?.SetUpHead(_head);
        _head.Look = -GlobalBasis.Z;
    }

    public override void _ExitTree() => _railHead?.Free();

    /// <summary>A full load again (a new ship, a resupply).</summary>
    public void Rearm()
    {
        MissilesLeft = Missiles;
        HeatSeekersLeft = HeatSeekers;
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
        HeadPowered = _railHead != null && HeatSeekersLeft > 0 && (bus[Slot, 7] & 1) != 0;
        WatchHead(delta);
        bus[Slot, 2] = (byte)MissilesLeft;
        bus[Slot, 3] = (byte)_inFlight.Count;
        bus[Slot, 4] = (byte)((locked && CanFire ? 1 : 0) | (locked ? 2 : 0)
                              | (CanFireHeat ? 4 : 0) | (HeatTone ? 8 : 0) | (HeadLocked ? 16 : 0));
        bus[Slot, 14] = (byte)HeatSeekersLeft;
        bus[Slot, 15] = (byte)(!_anyFired ? 0 : Newest?.HeatSeeker == true ? 2 : 1);
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
        int fire = bus.TakeWritten(Slot, 6) ? bus[Slot, 6] : 0;
        if (fire == 1 && CanFire && _radar?.Locked != null)
            Launch(ship, heat: false);
        else if (fire == 2 && CanFireHeat)
            Launch(ship, heat: true);

        // The datalink: this step's radar measurement, to the missiles fired on that track (on the radar's beam: the target's warning receiver can tell)
        if (_radar == null)
            return;
        _radar.Guiding = false;
        if (_radar.Locked == null)
            return;
        foreach (Missile m in _inFlight)
            if (IsInstanceValid(m) && m.TrackNumber == _radar.TrackNumber && m.GlobalPosition.DistanceTo(GlobalPosition) <= DatalinkRange)
            {
                m.Uplink(_radar.TrackPosition - m.GlobalPosition);
                _radar.Guiding = true;
            }
    }

    private bool CanFire => MissilesLeft > 0 && _reload <= 0;
    private bool CanFireHeat => _railHead != null && HeatSeekersLeft > 0 && _reload <= 0;

    private Vector3 ToTrack => (_radar.TrackPosition - GlobalPosition).Normalized();

    /// <summary>
    /// The head on the rail: where the radar's track is if there is one;
    /// otherwise it follows the heat it sees, and with nothing in view it
    /// goes back to the boresight. Unpowered, it rests on the boresight.
    /// </summary>
    private void WatchHead(double delta)
    {
        Vector3 nose = -GlobalBasis.Z;
        float dt = (float)delta;
        if (!HeadPowered)
            _head.Look = nose;
        else if (_radar?.Locked != null)
            _head.Point(GetTree(), GlobalPosition, nose, ToTrack, dt);
        else if (!_head.Track(GetTree(), GlobalPosition, nose, dt))
            _head.Point(GetTree(), GlobalPosition, nose, nose, dt);
        _toneFor = HeatTone ? _toneFor + delta : 0;
    }

    private void Launch(ShipBody ship, bool heat)
    {
        Missile m = heat ? HeatSeekerScene.Instantiate<Missile>() : MissileScene?.Instantiate<Missile>() ?? new Missile();
        m.Target = _radar?.RealLocked ?? (HeatTone ? _head.Sees as IRadarTarget : null);
        m.TrackNumber = heat ? -1 : _radar.TrackNumber;   // a heat seeker hears no datalink
        m.Shooter = ship;
        m.SeekerLook = heat && HeadPowered ? _head.Look : -GlobalBasis.Z;
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
        if (heat)
        {
            HeatSeekersLeft--;
            ship.Computer.Bus[Slot, 7] = 0;             // the next one's head, off
            _head.Look = -GlobalBasis.Z;
            _toneFor = 0;
        }
        else
            MissilesLeft--;
        Launched++;
        _reload = ReloadSeconds;
    }
}
