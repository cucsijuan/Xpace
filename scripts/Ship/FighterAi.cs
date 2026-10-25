using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// A fighter pilot of the game's: it flies its ship the way a player does,
/// with the stick, the throttle and the buttons, and the ship's computer
/// does the rest (FLIGHT's assist, FIRECTL's lock, aim and lead, RWR.COM's
/// dispenser). It knows only what its ship tells it: a miss is the radar's
/// noise, the filter's lag, the turret's servos, its own hands on a fixed
/// laser, the target's turn.
///
/// Its friends are the ships on its <see cref="Datalink"/>'s channel: the
/// link says where they are. Anything else its radar sees is not known to
/// be a friend, and it goes after it:
///
///   patrolling   it flies from one point to another round where it
///                started, its radar searching ahead;
///   intercepting an echo that is no friend's (none of its friends is
///                where the echo is): it turns on it and, with it near
///                the nose, presses target-ahead; if the lock falls on a
///                friend it presses next-target;
///   attacking    on the radar's track: it rolls the target overhead and
///                pulls, holds the trigger in gun range near the nose (with
///                a laser fixed along the nose, it points the nose at the
///                lead point its computer works out, and fires once that is
///                within <see cref="LaserCone"/>), and now and then lets a
///                missile go: a radar missile far out, a heat seeker nearer
///                (it selects it, wakes its head, gives it a moment to find
///                the heat and fires), then goes back round to its gun;
///   searching    the lock lost, it flies to where the target was last
///                seen for <see cref="SearchSeconds"/>, then patrols again.
///
/// It defends itself against what its warning receiver tells it: a radar
/// guiding a missile at it (a launch: that one is for it) makes it break
/// and throw chaff; a plume (not a friend's: they say when they fire) is
/// only a threat if its bearing hardly moves
/// while it closes (it is coming at the ship), or if it is near enough to
/// see where it points: then it breaks and throws flares. Under a radar's
/// lock it jinks.
///
/// Rocks: with a <see cref="RockSensor"/> on its ship it watches the way
/// the ship is going; blocked within <see cref="AvoidSeconds"/> of flight,
/// whatever it was doing (a break too) gives way to steering round the
/// rock. It never flies faster than lets it stop, on its reverse thrust,
/// short of the nearest rock its sensor sees (but for a creep, to edge out
/// when boxed in): slow among thick rocks, fast in the open. With rocks in
/// sight it flies with FLIGHT's safe turns on (<see cref="SafeTurnsNearRocks"/>),
/// so the ship goes where it points; in the open it turns them off again. With none, it hits them.
///
/// Put it under the <see cref="ShipBody"/> it flies, beside the ship's
/// radar, launcher, warning receiver, datalink and rock sensor.
/// </summary>
public partial class FighterAi : Node
{
    [Export] public float PatrolRadius = 3000f;
    [Export] public float PatrolThrottle = 0.6f;
    [Export] public double SearchSeconds = 20;
    [Export] public float FriendGate = 250f;            // m: an echo this near where a friend says it is, is that friend
    [Export] public float TurnGain = 2.2f;              // stick per radian off the nose
    [Export] public float GunRange = 1500f;
    [Export] public float GunCone = 20f;                // degrees off the nose it fires within
    [Export] public float LockCone = 12f;               // degrees off the nose it presses target-ahead within
    [Export] public float LockRange = 7000f;
    [Export] public bool UsesMissiles = true;
    [Export] public float MissileMinRange = 2500f, MissileMaxRange = 4500f;     // a radar missile
    [Export] public float HeatSeekerMinRange = 800f, HeatSeekerMaxRange = 2500f;
    [Export] public float LaserCone = 1.5f;             // degrees off the lead point a fixed laser fires within
    [Export] public double MissileInterval = 15;
    [Export] public float CloseRange = 500f;            // nearer than this it eases off
    [Export] public double BreakSeconds = 4;
    [Export] public double CountermeasureInterval = 0.5;
    [Export] public float JinkAmount = 0.5f;            // of the stick, while locked by a radar
    [Export] public double JamSeconds = 6;              // the jammer stays on this long after the last lock on the ship
    [Export] public float PlumeThreatRange = 4000f;
    [Export] public float BearingRateThreat = 3f;       // degrees a second: a plume turning slower than this is coming at the ship
    [Export] public float SightRange = 2000f;           // near enough to see where a missile points
    [Export] public float FlareRange = 2500f;           // flares go only once a heat seeker is this near (they burn 2 s)
    [Export] public float SightCone = 15f;              // pointing within this of the ship: at it
    [Export] public float AvoidSeconds = 5f;            // the way ahead must be clear for this much flight
    [Export] public float FullThrottleSpeed = 250f;     // m/s: what full throttle asks of the flight assist (FLIGHT's MAX_SPEED)
    [Export] public float BrakeShare = 0.7f;            // of the reverse thrust it counts on to stop
    [Export] public float CreepSpeed = 8f;              // m/s: never slower than this among rocks (a bump at it barely scratches)
    [Export] public bool SafeTurnsNearRocks = true;

    /// <summary>What it is doing, for the tests and the curious.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>What its ship's radar is locked on, if it is no friend.</summary>
    public IRadarTarget Target { get; private set; }
    /// <summary>What it patrols round (where its ship was at the start, unless told otherwise).</summary>
    public Vector3 Anchor
    {
        get => _anchor;
        set
        {
            _anchor = value;
            _hasWaypoint = false;
        }
    }

    private ShipBody _ship;
    private RadarDevice _radar;
    private RwrDevice _rwr;
    private JammerDevice _jammer;
    private double _jamFor;
    private LauncherDevice _launcher;
    private LaserDevice _laser;
    private Datalink _link;
    private RockSensor _eyes;
    private PilotControls _controls;
    private readonly Dictionary<string, int> _held = new(), _rest = new();
    private readonly Queue<(string Button, int Wait)> _plan = new();
    private int _planWait;
    private double _breakLeft, _sinceMissile = double.MaxValue, _sinceCountermeasure, _jinkLeft, _sinceLockPress, _sinceSeen = double.MaxValue;
    private bool _breakFlares;
    private float _threatRange;
    private Vector2 _jink;
    private float _breakSide = 1;
    private Vector3 _lastTrack, _trackVelocity, _lastSeen, _anchor, _waypoint;
    private bool _hadTrack, _hasWaypoint;
    private readonly Dictionary<RwrDevice.Contact, Queue<(double At, Vector3 Way, float Range)>> _plumes = new();
    private double _clock, _sinceSafePress = double.MaxValue;
    private readonly RandomNumberGenerator _rng = new() { Seed = 9001 };

    public override void _Ready()
    {
        _ship = GetParent<ShipBody>();
        ProcessPhysicsPriority = -10;                   // its hands move before the ship's step
        _ship.ControlsSource = () => _controls;
        foreach (Node n in _ship.FindChildren("*", "", true, false))
        {
            _radar ??= n as RadarDevice;
            _rwr ??= n as RwrDevice;
            _jammer ??= n as JammerDevice;
            _launcher ??= n as LauncherDevice;
            _laser ??= n as LaserDevice;
            _link ??= n as Datalink;
            _eyes ??= n as RockSensor;
        }
        _anchor = _ship.GlobalPosition;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _clock += delta;
        var c = new PilotControls();
        if (Net.NetBody.IsPuppet(_ship))
            return;                                     // another machine flies this ship
        if (_ship.Wrecked)
        {
            Doing = "wrecked";
            Target = null;
            _breakLeft = 0;
            _controls = Buttons(c);
            return;
        }
        _sinceMissile += delta;
        _sinceCountermeasure += delta;
        _sinceLockPress += delta;
        _sinceSeen += delta;
        _sinceSafePress += delta;

        Threats();
        Jam(delta);
        if (_breakLeft > 0)
        {
            Break(ref c, delta);
            Avoid(ref c, delta);
            _controls = Buttons(c);
            return;
        }

        // The radar's lock: on no friend, a target; on a friend, the next contact
        Target = null;
        if (_radar?.Locked != null)
        {
            if (Friendly(_radar.TrackPosition))
            {
                if (_sinceLockPress > 0.6)
                {
                    _sinceLockPress = 0;
                    Press("next");
                }
            }
            else
                Target = _radar.Locked;
        }

        if (Target != null)
            Attack(ref c, dt);
        else if (Interest() is Vector3 echo)
        {
            Doing = "intercepting";
            _hadTrack = false;
            Vector3 local = _ship.GlobalBasis.Inverse() * (echo - _ship.GlobalPosition);
            Steer(ref c, local);
            c.Throttle = 1;
            float range = local.Length();
            if (range < LockRange && Mathf.RadToDeg(Vector3.Forward.AngleTo(local)) < LockCone && _sinceLockPress > 0.6)
            {
                _sinceLockPress = 0;
                Press("target");
            }
        }
        else if (_sinceSeen < SearchSeconds)
        {
            Doing = "searching";
            _hadTrack = false;
            Steer(ref c, _ship.GlobalBasis.Inverse() * (_lastSeen - _ship.GlobalPosition));
            c.Throttle = 1;
        }
        else
            Patrol(ref c);
        if (_rwr?.LockWarning == true)
            Jink(ref c, delta);
        Avoid(ref c, delta);
        _controls = Buttons(c);
    }

    private bool Friendly(Vector3 point) => _link?.Friendly(point, FriendGate) ?? false;

    /// <summary>The nearest echo of the last sweep that is none of its friends.</summary>
    private Vector3? Interest()
    {
        if (_radar == null)
            return null;
        Vector3? best = null;
        float nearest = float.MaxValue;
        foreach (RadarDevice.Echo e in _radar.Echoes)
        {
            float range = e.Seen.DistanceTo(_ship.GlobalPosition);
            if (range < nearest && !Friendly(e.Seen))
            {
                nearest = range;
                best = e.Seen;
            }
        }
        return best;
    }

    private void Attack(ref PilotControls c, float dt)
    {
        Doing = "attacking";
        Vector3 at = _radar.TrackPosition;
        _lastSeen = at;
        _sinceSeen = 0;
        Vector3 velocity = TrackVelocity(at, dt);
        Vector3 to = at - _ship.GlobalPosition;
        float range = to.Length();
        // A fixed laser: the computer's lead point on the nose. A turret aims itself: the nose only near the target's way
        Vector3 local;
        float cone = GunCone;
        if (_laser?.LeadPoint is Vector3 leadPoint)
        {
            local = _ship.GlobalBasis.Inverse() * (leadPoint - _laser.GlobalPosition);
            cone = LaserCone;
        }
        else
        {
            float speed = _laser?.BoltSpeed ?? 1200f;
            local = _ship.GlobalBasis.Inverse() * (to + (velocity - _ship.LinearVelocity) * (range / speed));
        }
        float off = Mathf.RadToDeg(Vector3.Forward.AngleTo(local));
        Steer(ref c, local);
        c.Throttle = range > 2000 ? 1 : range < CloseRange ? 0.25f : 0.7f;
        c.Trigger = range < GunRange && off < cone && _plan.Count == 0;
        if (!UsesMissiles || _plan.Count > 0 || _sinceMissile < MissileInterval || off > 10 || _launcher == null)
            return;
        // Round the weapons from the gun (the cannon or the laser): radar missile, heat seeker, gun
        if (range > MissileMinRange && range < MissileMaxRange && _launcher.MissilesLeft > 0)
        {
            _sinceMissile = 0;
            Doing = "firing a missile";
            _plan.Enqueue(("weapon", 8));
            _plan.Enqueue(("fire", 8));
            _plan.Enqueue(("weapon", 8));
            _plan.Enqueue(("weapon", 8));
        }
        else if (range > HeatSeekerMinRange && range < HeatSeekerMaxRange && _launcher.HeatSeekersLeft > 0)
        {
            _sinceMissile = 0;
            Doing = "firing a heat seeker";
            _plan.Enqueue(("weapon", 8));
            _plan.Enqueue(("weapon", 8));
            _plan.Enqueue(("fire", 50));                // its head wakes: a moment to find the heat
            _plan.Enqueue(("fire", 8));
            _plan.Enqueue(("weapon", 8));
        }
    }

    private void Patrol(ref PilotControls c)
    {
        Doing = "patrolling";
        _hadTrack = false;
        if (!_hasWaypoint || _ship.GlobalPosition.DistanceTo(_waypoint) < 400)
        {
            var way = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-1, 1)).Normalized();
            _waypoint = _anchor + way * _rng.RandfRange(0.4f, 1f) * PatrolRadius;
            _hasWaypoint = true;
        }
        Steer(ref c, _ship.GlobalBasis.Inverse() * (_waypoint - _ship.GlobalPosition));
        c.Pitch *= 0.5f;                                // easy turns: it is looking, not fighting
        c.Yaw *= 0.5f;
        c.Roll *= 0.5f;
        c.Throttle = PatrolThrottle;
    }

    // ---------- threats ----------

    /// <summary>What its warning receiver says, weighed: a launch at it, or a plume coming its way.</summary>
    private void Threats()
    {
        if (_rwr == null)
            return;
        if (_rwr.LaunchWarning)
            Threatened(flares: false, _rwr.Contacts.FirstOrDefault(x => x.Launch));
        foreach (RwrDevice.Contact m in _rwr.Contacts.Where(x => x.IsMissile))
        {
            if (IsInstanceValid(m.Missile) && _link?.Knows(m.Missile.Shooter) == true)
                continue;                               // a friend said it fired that one
            Vector3 way = _ship.GlobalBasis * new Vector3(MathF.Sin(m.Bearing), 0, -MathF.Cos(m.Bearing));
            if (!_plumes.TryGetValue(m, out var seen))
                _plumes[m] = seen = new Queue<(double, Vector3, float)>();
            seen.Enqueue((_clock, way, m.Range));
            while (seen.Count > 1 && _clock - seen.Peek().At > 1)
                seen.Dequeue();
            (double at, Vector3 thenWay, float thenRange) = seen.Peek();
            double span = _clock - at;
            // Near enough, it sees where the missile points; further out it only has the plume's bearing and how bright it grows
            bool coming = m.Range < SightRange
                ? IsInstanceValid(m.Missile) &&
                  Mathf.RadToDeg((m.Missile.Velocity - _ship.LinearVelocity).AngleTo(_ship.GlobalPosition - m.Missile.GlobalPosition)) < SightCone
                : span > 0.5 && m.Range < PlumeThreatRange && m.Range < thenRange &&
                  Mathf.RadToDeg(thenWay.AngleTo(way)) / span < BearingRateThreat;
            if (coming)
                Threatened(flares: true, m);
        }
        foreach (RwrDevice.Contact gone in _plumes.Keys.Where(k => !_rwr.Contacts.Contains(k)).ToList())
            _plumes.Remove(gone);
    }

    /// <summary>
    /// The jammer, by its button (the warning receiver's program works it):
    /// on while a radar has the ship locked, and for a while after;
    /// deceiving if it can, else noise. Off the rest of the time: it would
    /// tell everyone where the ship is.
    /// </summary>
    private void Jam(double delta)
    {
        if (_jammer == null || _rwr == null)
            return;
        JamMode want = JamMode.Off;
        if (_rwr.LockWarning || _rwr.LaunchWarning)
            _jamFor = JamSeconds;
        else
            _jamFor -= delta;
        if (_jamFor > 0)
            want = _jammer.Spec?.Deception == true ? JamMode.Deception : JamMode.Noise;
        if (_jammer.Mode != want)
            Press("jam");
    }

    private void Threatened(bool flares, RwrDevice.Contact from)
    {
        if (_breakLeft <= 0)
            _breakSide = from == null ? (_rng.Randf() < 0.5f ? -1 : 1) : from.Bearing >= 0 ? 1 : -1;
        _breakLeft = BreakSeconds;
        _breakFlares = flares;
        _threatRange = from?.Range ?? 0;
    }

    /// <summary>Across the missile's line of sight, as hard as it turns, motor full; flares against a heat seeker, chaff against a radar's.</summary>
    private void Break(ref PilotControls c, double delta)
    {
        _breakLeft -= delta;
        Doing = _breakFlares ? "breaking (flares)" : "breaking (chaff)";
        c.Roll = 0.6f * _breakSide;
        c.Pitch = 1;
        c.Throttle = 1;
        if (_sinceCountermeasure >= CountermeasureInterval && (!_breakFlares || _threatRange < FlareRange))
        {
            _sinceCountermeasure = 0;
            Press(_breakFlares ? "flare" : "chaff");
        }
    }

    // ---------- flying ----------

    /// <summary>Roll the aim point overhead and pull; yaw for the last bit.</summary>
    private void Steer(ref PilotControls c, Vector3 local)
    {
        float yaw = Mathf.Atan2(local.X, -local.Z);
        float pitch = Mathf.Atan2(local.Y, -local.Z);
        float aside = Mathf.Atan2(local.X, local.Y);    // round the nose from overhead, right positive
        bool farOff = Vector3.Forward.AngleTo(local) > Mathf.DegToRad(15);
        c.Roll = farOff ? Mathf.Clamp(aside * 1.5f, -1, 1) : 0;
        c.Pitch = Mathf.Clamp(pitch * TurnGain, -1, 1);
        c.Yaw = Mathf.Clamp(yaw * TurnGain, -1, 1);
    }

    /// <summary>
    /// A rock in the way the ship is going: steer for the clear way nearest
    /// it. And never faster than stopping short of the nearest rock it sees
    /// allows, but for a creep: boxed in, it edges out.
    /// </summary>
    private void Avoid(ref PilotControls c, double delta)
    {
        if (_eyes == null)
            return;
        Vector3 going = _ship.LinearVelocity;
        float speed = going.Length();
        if (speed < 5)
            going = -_ship.GlobalBasis.Z;
        _eyes.Look(going, delta);
        Vector3 way = _eyes.Steer(going, MathF.Max(speed, 50) * AvoidSeconds);
        if (_eyes.Avoiding)
        {
            Doing = "avoiding a rock";
            Steer(ref c, _ship.GlobalBasis.Inverse() * way);
        }
        if (SafeTurnsNearRocks && _eyes.Clear.Length > 0)
        {
            // What FLIGHT shows on the thrusters' mode: bit 1, safe turns
            bool rocks = _eyes.Clear.Min() < _eyes.Range * 0.99f;
            bool safe = (_ship.Computer.Bus[ShipBody.SlotThrusters, 15] & 2) != 0;
            if (rocks != safe && _sinceSafePress > 1)
            {
                _sinceSafePress = 0;
                Press("safe");
            }
        }
        if (_eyes.Clear.Length > 0)
        {
            float brake = BrakeShare * _ship.Spec.ReverseThrust / _ship.Spec.Mass;
            float room = MathF.Max(0, _eyes.Clear.Min() - _eyes.Clearance);      // turning, it may sweep into any of them
            float safe = MathF.Max(CreepSpeed, MathF.Sqrt(2 * brake * room));
            c.Throttle = MathF.Min(c.Throttle, safe / FullThrottleSpeed);
        }
    }

    private void Jink(ref PilotControls c, double delta)
    {
        _jinkLeft -= delta;
        if (_jinkLeft <= 0)
        {
            _jinkLeft = _rng.RandfRange(0.8f, 1.8f);
            _jink = new Vector2(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)).Normalized() * JinkAmount;
        }
        c.Pitch = Mathf.Clamp(c.Pitch + _jink.Y, -1, 1);
        c.Yaw = Mathf.Clamp(c.Yaw + _jink.X, -1, 1);
    }

    /// <summary>The target's velocity from the radar's track, smoothed.</summary>
    private Vector3 TrackVelocity(Vector3 at, float dt)
    {
        if (_hadTrack && dt > 0)
            _trackVelocity = _trackVelocity.Lerp((at - _lastTrack) / dt, 0.05f);
        else
            _trackVelocity = Vector3.Zero;
        _lastTrack = at;
        _hadTrack = true;
        return _trackVelocity;
    }

    // ---------- the buttons: a press is held a few steps, then let go a few ----------

    private void Press(string button)
    {
        if (!_held.ContainsKey(button) && !_rest.ContainsKey(button))
            _held[button] = 4;
    }

    private PilotControls Buttons(PilotControls c)
    {
        if (_plan.Count > 0 && --_planWait <= 0)
        {
            (string button, int wait) = _plan.Dequeue();
            Press(button);
            _planWait = wait;
        }
        foreach (string b in _held.Keys.ToList())
            if (--_held[b] <= 0)
            {
                _held.Remove(b);
                _rest[b] = 4;
            }
        foreach (string b in _rest.Keys.ToList())
            if (--_rest[b] <= 0)
                _rest.Remove(b);
        c.TargetAhead = _held.ContainsKey("target");
        c.NextTarget = _held.ContainsKey("next");
        c.NextWeapon = _held.ContainsKey("weapon");
        c.Chaff = _held.ContainsKey("chaff");
        c.Flare = _held.ContainsKey("flare");
        c.SafeButton = _held.ContainsKey("safe");
        c.Jammer = _held.ContainsKey("jam");
        c.Trigger |= _held.ContainsKey("fire");
        return c;
    }
}
