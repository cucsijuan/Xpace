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
/// noise, the filter's lag, the turret's servos, the target's turn.
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
///                pulls, holds the trigger in gun range near the nose, and
///                now and then selects a radar missile, lets one go and
///                goes back to the gun;
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
/// Put it under the <see cref="ShipBody"/> it flies, beside the ship's
/// radar, launcher, warning receiver and datalink.
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
    [Export] public float MissileMinRange = 1200f, MissileMaxRange = 4000f;
    [Export] public double MissileInterval = 15;
    [Export] public float CloseRange = 500f;            // nearer than this it eases off
    [Export] public double BreakSeconds = 4;
    [Export] public double CountermeasureInterval = 0.5;
    [Export] public float JinkAmount = 0.5f;            // of the stick, while locked by a radar
    [Export] public float PlumeThreatRange = 4000f;
    [Export] public float BearingRateThreat = 3f;       // degrees a second: a plume turning slower than this is coming at the ship
    [Export] public float SightRange = 2000f;           // near enough to see where a missile points
    [Export] public float FlareRange = 2500f;           // flares go only once a heat seeker is this near (they burn 2 s)
    [Export] public float SightCone = 15f;              // pointing within this of the ship: at it

    /// <summary>What it is doing, for the tests and the curious.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>What its ship's radar is locked on, if it is no friend.</summary>
    public IRadarTarget Target { get; private set; }

    private ShipBody _ship;
    private RadarDevice _radar;
    private RwrDevice _rwr;
    private LauncherDevice _launcher;
    private Datalink _link;
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
    private double _clock;
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
            _launcher ??= n as LauncherDevice;
            _link ??= n as Datalink;
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

        Threats();
        if (_breakLeft > 0)
        {
            Break(ref c, delta);
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
        Vector3 lead = to + (velocity - _ship.LinearVelocity) * (range / 1200f);
        Vector3 local = _ship.GlobalBasis.Inverse() * lead;
        float off = Mathf.RadToDeg(Vector3.Forward.AngleTo(local));
        Steer(ref c, local);
        c.Throttle = range > 2000 ? 1 : range < CloseRange ? 0.25f : 0.7f;
        c.Trigger = range < GunRange && off < GunCone && _plan.Count == 0;
        if (UsesMissiles && _plan.Count == 0 && _sinceMissile > MissileInterval && off < 10 &&
            range > MissileMinRange && range < MissileMaxRange && (_launcher?.MissilesLeft ?? 0) > 0)
        {
            // The radar missile: select it, let one go, back round to the gun (gun, missile, heat seeker, gun)
            _sinceMissile = 0;
            Doing = "firing a missile";
            _plan.Enqueue(("weapon", 8));
            _plan.Enqueue(("fire", 8));
            _plan.Enqueue(("weapon", 8));
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
        c.Trigger |= _held.ContainsKey("fire");
        return c;
    }
}
