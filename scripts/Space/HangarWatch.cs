using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;

namespace Xpace.Space;

/// <summary>
/// The eyes at a hangar's mouth: a node under its <see cref="HangarDock"/>, looking out along its own -Z. Two sensors,
/// the same physics ships go by:
///
///   an approach radar    a <see cref="RadarEmitter"/> child, sweeping the cone ahead: it sees the echoes its beam
///                        crosses, as far as their size and the jammers' noise let it (a warning receiver hears it,
///                        "A"); one sweep's sighting lasts until the next;
///   a heat camera        every ship whose heat (<see cref="IHeatSource"/>) reaches it above
///                        <see cref="HeatSensitivity"/>, inside <see cref="ConeDegrees"/>, with nothing in between.
///
/// Traffic control knows the ships it can tell: those with their radio on, and the station's own police. A ship it
/// can't tell, seen by either sensor as a thing of its own, is <see cref="Noticed"/>. Seen as a thing of its own: an
/// echo inside another ship's resolution cell (range within <see cref="RadarEmitter.RangeResolution"/>, bearing
/// within the beam, Doppler within <see cref="RadarEmitter.VelocityResolution"/>) is that ship's echo to the radar,
/// and heat within <see cref="HeatResolutionDegrees"/> of a ship it knows is that ship's to the camera. So a ship
/// with its radio off gets in unnoticed by keeping out of the cone (the mouth itself is at its edge: hugging the
/// hull), by being too small, too far, too jammed and too cold to be seen, or by keeping close behind a ship the
/// station cleared.
///
/// Where the station's security is tight, a registration reader (<see cref="RegistrationRange"/>) reads the hull of a
/// ship it knows as it comes near: a false callsign is found out there, and the only way in is not to be seen.
///
/// A noticed ship that turns its radio on is told now, and no longer noticed: the station knows who it was.
///
/// What a noticed ship means is the station's business (<see cref="StationRadio"/> shuts the door while
/// <see cref="Alarm"/> is up; <see cref="StationSecurity"/> engages one that goes in). Only where the station's
/// security acts (alone, or the session's host) does it watch.
/// </summary>
public partial class HangarWatch : Node3D
{
    /// <summary>How far out it cares about, metres.</summary>
    [Export] public float WatchRange = 2500f;
    /// <summary>Half the cone it looks into, degrees off its -Z.</summary>
    [Export] public float ConeDegrees = 60f;
    /// <summary>The least heat the camera sees (heat units / m²; a seeker head's is 1e-6).</summary>
    [Export] public float HeatSensitivity = 2e-5f;
    /// <summary>Two sources nearer each other than this, seen from the camera, are one blob of heat.</summary>
    [Export] public float HeatResolutionDegrees = 1.5f;
    /// <summary>A ship noticed is remembered this long after it was last seen.</summary>
    [Export] public double MemorySeconds = 15;
    /// <summary>The alarm stays up until no noticed ship was seen for this long.</summary>
    [Export] public double AlarmSeconds = 8;

    /// <summary>The dock it watches (its parent).</summary>
    public HangarDock Dock { get; private set; }
    public RadarEmitter Radar { get; private set; }

    /// <summary>A ship it can't tell was seen lately: the station keeps the door shut.</summary>
    public bool Alarm => _noticed.Values.Any(at => _now - at <= AlarmSeconds);

    /// <summary>
    /// A registration reader (0: none, as most stations): it reads the hull of a ship within this many metres in its
    /// cone, so a ship sending a false callsign is found out at the door (<see cref="StationSecurity.Unmask"/>).
    /// </summary>
    [Export] public float RegistrationRange;

    /// <summary>How often it looks, seconds (the radar's sightings are kept every step).</summary>
    [Export] public double LookSeconds = 0.1;

    /// <summary>The ships it noticed lately (they may be out of sight now).</summary>
    public IEnumerable<Node3D> Noticed => _noticed.Keys.Where(IsInstanceValid);

    /// <summary>Seconds since it last noticed that ship, if it did lately.</summary>
    public double? NoticedAgo(Node3D ship) => ship != null && _noticed.TryGetValue(ship, out double at) ? _now - at : null;

    private readonly Dictionary<Node3D, double> _noticed = new(), _echo = new();
    private readonly List<(Node3D Ship, bool Known, Vector3 To, float Range, float Radial, bool Echo, bool Hot)> _seen = new();
    private readonly Dictionary<Node3D, IHeatSource> _sources = new();
    private StationSecurity _security;
    private double _now, _sinceLook;

    public override void _Ready()
    {
        Dock = GetParent() as HangarDock;
        Radar = GetChildren().OfType<RadarEmitter>().FirstOrDefault();
        if (Dock != null)
            Dock.Watch = this;
    }

    private bool Acts
    {
        get
        {
            _security ??= Dock?.Station?.Security;
            return _security?.Radio?.Laws != null && !(SessionNode.Instance is SessionNode s && s.Active && !s.Session.IsHost);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _now += delta;
        if (Radar != null)
        {
            foreach (Node3D swept in Radar.Swept)
                if (Combat.ShipOf(swept) is Node3D ship)
                    _echo[ship] = _now;
        }
        _sinceLook += delta;
        if (!Acts || _sinceLook < LookSeconds)
            return;
        _sinceLook = 0;
        Look();
        foreach (Node3D ship in _noticed.Keys.Where(s => !IsInstanceValid(s) || _now - _noticed[s] > MemorySeconds).ToList())
            _noticed.Remove(ship);
        foreach (Node3D ship in _echo.Keys.Where(s => !IsInstanceValid(s) || _now - _echo[s] > (Radar?.ScanSeconds ?? 2) + 0.2).ToList())
            _echo.Remove(ship);
        foreach (Node3D ship in _sources.Keys.Where(s => !IsInstanceValid(s)).ToList())
            _sources.Remove(ship);
    }

    // What each sensor makes of the ships out front, and which of them it can't put down to a ship it knows
    private void Look()
    {
        _seen.Clear();
        Vector3 here = GlobalPosition, look = -GlobalBasis.Z.Normalized();
        float cos = Mathf.Cos(Mathf.DegToRad(ConeDegrees));
        var done = new HashSet<Node3D>();
        foreach (Node node in Sectors.InGroup(this, Radio.Group))
        {
            if (Combat.ShipOf(node) is not Node3D ship || !done.Add(ship) || !Combat.IsAlive(ship))
                continue;
            Vector3 to = ship.GlobalPosition - here;
            float range = to.Length();
            if (range < 1 || range > WatchRange || to.Dot(look) < cos * range)
                continue;
            Vector3 dir = to / range;
            bool known = node is IRadio { RadioOn: true } || _security.IsPolice(ship);
            if (known && range <= Mathf.Max(RegistrationRange, _security.RegistrationRange) && Xpace.World.Universe.Current?.IdentityOf(ship) is (Xpace.World.Identity.Forged, Xpace.World.ForgedRecord forged) &&
                !forged.Burned && !Obstacles.Blocked(GetWorld3D(), here, ship.GlobalPosition))
                _security.Unmask(ship, forged);
            if (known)
                _noticed.Remove(ship);                  // it says who it is now: the ship seen was this one
            bool echo = _echo.ContainsKey(ship);
            bool hot = Heat(ship, here) >= HeatSensitivity && !Obstacles.Blocked(GetWorld3D(), here, ship.GlobalPosition);
            if (echo || hot)
                _seen.Add((ship, known, dir, range, Velocity(ship).Dot(dir), echo, hot));
        }
        float beam = Mathf.DegToRad(Radar?.BeamwidthDegrees ?? 3), blob = Mathf.DegToRad(HeatResolutionDegrees);
        foreach (var s in _seen)
        {
            if (s.Known)
                continue;
            bool byEcho = s.Echo && !_seen.Any(k => k.Known && k.Echo && Mathf.Abs(k.Range - s.Range) < Radar.RangeResolution &&
                                                    k.To.AngleTo(s.To) < beam && Mathf.Abs(k.Radial - s.Radial) < Radar.VelocityResolution);
            bool byHeat = s.Hot && !_seen.Any(k => k.Known && k.Hot && k.To.AngleTo(s.To) < blob);
            if (byEcho || byHeat)
                _noticed[s.Ship] = _now;
        }
    }

    private float Heat(Node3D ship, Vector3 from)
    {
        if (!_sources.TryGetValue(ship, out IHeatSource source))
            _sources[ship] = source = ship as IHeatSource ?? ship.FindChildren("*", "", true, false).OfType<IHeatSource>().FirstOrDefault();
        return source?.HeatAt(from, out _) ?? 0;
    }

    private static Vector3 Velocity(Node3D ship) => ship switch
    {
        Xpace.Ship.ShipBody b => b.LinearVelocity,
        IRadarTarget t => t.Velocity,
        _ => Vector3.Zero,
    };
}
