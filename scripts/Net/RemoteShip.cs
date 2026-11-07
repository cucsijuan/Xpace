using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// A ship someone else flies: no computer and no physics here, only its
/// owner's snapshots, interpolated (<see cref="RemoteTrack"/>). The model
/// and the hitboxes (box collision shapes) go below it.
///
/// The sensors here sense it like any ship: radars see its echo, seekers
/// its heat (the hull's, and its motor's as hard as the snapshots say it
/// pushes), warning receivers hear its radar (a <see cref="RadarEmitter"/>
/// it makes below itself, doing what the owner's radar does). Its
/// engines' plumes (<see cref="EnginePlume"/> nodes below it) glow as hard as
/// the snapshots say it pushes. Its jammer (a <see cref="Xpace.Ship.JammerDevice"/>
/// it makes below itself) sends what the owner's sends, as hard as the
/// model the snapshots name (<see cref="SessionNode.Jammers"/>; not
/// there: <see cref="Jammer"/>). Radios hear it and speak to it (a
/// <see cref="RemoteRadio"/> it makes below itself). A warhead's
/// fuse goes off on it; the damage is the host's to judge and its owner's
/// to take.
/// </summary>
public partial class RemoteShip : Node3D, Space.ICruiseShown, INetEntity, IRadarTarget, IHeatSource, IDamageable, Xpace.Ship.ISolidShip
{
    /// <summary>The hull's echo (m²), and in cruise the wake's on top of it, as its owner's snapshots say.</summary>
    [Export] public float RadarCrossSection
    {
        get => _hullEcho + WakeLevel * WakeEcho;
        set => _hullEcho = value;
    }
    private float _hullEcho = 4f;
    /// <summary>What its wake adds to its echo at its fullest (m²).</summary>
    [Export] public float WakeEcho = 300f;
    /// <summary>What it stirs in cruise, 0 to 1.</summary>
    public float WakeLevel { get; private set; }
    [Export] public float HullHeatAtFull = 30f;     // as a ship's signature: its hull at 100%
    [Export] public float MotorHeat = 80f;
    [Export] public float FrontHeat = 0.3f;
    [Export] public string RadarSignature = "F";    // how its radar's pulses look to a warning receiver
    [Export] public float RadarPowerAt1Km = 4e4f;
    /// <summary>Its jammer's model when the snapshots name none this machine knows.</summary>
    [Export] public Xpace.Ship.JammerSpec Jammer;
    /// <summary>Its hull: its hardpoints show the weapons the hull comes with from the factory.</summary>
    [Export] public Xpace.Ship.ShipSpec Hull;

    public RemoteTrack Track { get; } = new();

    public ushort NetId { get; set; }
    public bool IsLocal => false;

    /// <summary>A session advances the track; alone, the ship does it itself.</summary>
    public bool Driven { get; set; }

    public Hitbox[] Hitboxes { get; private set; }

    /// <summary>Metres a second, world axes, as last shown (for sensors).</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>Its motor's push as last shown, 0 to 1.</summary>
    public float Thrust { get; private set; }

    /// <summary>Its hull's heat as last shown, 1 at 100%.</summary>
    public float HullLevel { get; private set; }

    public ushort RadarId { get; private set; }
    public bool Alive => Visible && !_down;

    public Hitbox[] SolidBoxes => Hitboxes;
    public float SolidReach => _reach ??= Xpace.Ship.ShipContact.Bound(Hitboxes);
    public Vector3 SolidVelocity => Velocity;
    public float SolidMass => Hull?.Mass ?? 20000f;
    public bool Solid => Alive;
    public bool Immovable => false;
    public bool MovedHere => false;

    /// <summary>Its owner works out its side of a crash, there.</summary>
    public void Rammed(Vector3 push, Vector3 speedChange, float joules) { }

    private float? _reach;

    private FloatingOrigin _origin;
    private RadarEmitter _radar;
    private Xpace.Ship.JammerDevice _jammer;
    private EnginePlume[] _plumes;
    private bool _down;                         // blown up on its owner's machine
    private double _downFor;

    public override void _Ready()
    {
        for (Node n = GetParent(); n != null && _origin == null; n = n.GetParent())
            _origin = n as FloatingOrigin;
        Track.TickSeconds = 1f / Engine.PhysicsTicksPerSecond;
        Hitboxes = Hitbox.FromShapes(this);
        Visible = false;                        // until there is something to show
        RadarId = RadarIds.Next();
        AddToGroup(Xpace.Ship.ShipCollision.Group);
        AddToGroup("radar_targets");
        AddToGroup("heat_sources");
        _radar = new RadarEmitter { Name = "Radar", Signature = RadarSignature, PowerAt1Km = RadarPowerAt1Km };
        AddChild(_radar);
        _radar.Follow(RadarMode.Off, null, false);
        _jammer = new Xpace.Ship.JammerDevice { Name = "Jammer", Spec = Jammer };
        AddChild(_jammer);
        _jammer.Follow(JamMode.Off, false, Vector3.Forward);
        _plumes = FindChildren("*", "", true, false).OfType<EnginePlume>().ToArray();
        // As its owner has it, or as it came from the factory until its owner says
        if (Fitted == null)
            Factory();
        else
            Show(Fitted);
        AddChild(new RemoteRadio { Name = "Radio" });
    }

    /// <summary>
    /// What its owner has on its hardpoints and its pylons' rails, as last heard (<see cref="Fit"/>); null: not heard
    /// yet.
    /// </summary>
    public string Fitted { get; private set; }

    /// <summary>
    /// Shows what its owner has on its hardpoints (<see cref="Xpace.Ship.ShipBody.Fitted"/>) and, after a '|', the
    /// missiles on its rails (<see cref="Xpace.Ship.LauncherDevice.Loads"/>): looks only.
    /// </summary>
    public void Fit(string fitted)
    {
        Fitted = fitted ?? "";
        if (IsInsideTree())
            Show(Fitted);
    }

    /// <summary>The rails of its pylons, as shown.</summary>
    public System.Collections.Generic.IReadOnlyList<Node3D> Rails => _rails;

    private System.Collections.Generic.List<Node3D> _rails = new();

    private void Show(string fitted)
    {
        int bar = fitted.IndexOf('|');
        Mount(Xpace.Ship.Fittings.Parse(bar < 0 ? fitted : fitted[..bar]));
        var loads = new System.Collections.Generic.List<(PackedScene Look, int Count)>();
        foreach (string pair in (bar < 0 ? "" : fitted[(bar + 1)..]).Split(';', System.StringSplitOptions.RemoveEmptyEntries))
        {
            int at = pair.IndexOf('=');
            string path = at > 0 ? pair[..at] : "";
            int.TryParse(at > 0 ? pair[(at + 1)..] : "", out int count);
            PackedScene look = path.StartsWith("res://assets/weapons/missiles/") && ResourceLoader.Exists(path)
                && Xpace.World.ResourceCache.Load<Resource>(path) is MissileSpec model ? model.Look : null;
            loads.Add((look, System.Math.Clamp(count, 0, _rails.Count)));
        }
        while (loads.Count < 2)
            loads.Add((null, 0));
        Xpace.Ship.MissileRails.Show(_rails, loads[0].Look, loads[0].Count, loads[1].Look, System.Math.Min(loads[1].Count, _rails.Count - loads[0].Count));
    }

    // As its hull comes from the factory: its fitting, and its missiles on the rails
    private void Factory()
    {
        Mount(Xpace.Ship.Fittings.Of(Hull));
        MissileSpec[] models = Hull?.FactoryMissiles ?? System.Array.Empty<MissileSpec>();
        int each = models.Length == 0 ? 0 : System.Math.Min(Hull.MissileRack, _rails.Count / models.Length);
        Xpace.Ship.MissileRails.Show(_rails, models.Length > 0 ? models[0]?.Look : null, each, models.Length > 1 ? models[1]?.Look : null, models.Length > 1 ? each : 0);
    }

    private void Mount(System.Collections.Generic.Dictionary<string, Resource> fitted)
    {
        var hardpoints = FindChildren("*", "", true, false).OfType<Xpace.Ship.Hardpoint>().ToList();
        foreach (Xpace.Ship.Hardpoint h in hardpoints)
            h.Mount(fitted.GetValueOrDefault(h.Name.ToString()), working: false);
        _rails = Xpace.Ship.MissileRails.Of(hardpoints);
        _turret = FindChildren("*", "", true, false).OfType<Xpace.Ship.TurretRig>().FirstOrDefault();
    }

    public void Paint() { }

    public void Hit(float damage) { }           // the host judges it, the owner takes it

    public ShipSnapshot Snapshot(uint tick) => default;

    public void TakeHit(HitEvent hit) { }      // its owner takes it

    public float HeatAt(Vector3 from, out Vector3 direction)
    {
        float heat = Heat.Point(GlobalPosition, 1, from, out direction);
        if (!Alive)
            return 0;
        float behind = Mathf.Max(0, -direction.Dot(GlobalBasis.Z.Normalized()));
        return heat * (HullHeatAtFull * HullLevel + _jammer.Glow + MotorHeat * Thrust * (FrontHeat + (1 - FrontHeat) * behind));
    }

    /// <summary>A packet from the owner's <see cref="ShipStateSender"/>.</summary>
    public void Receive(byte[] packet)
    {
        var reader = new BitReader(packet);
        ShipSnapshot s = ShipSnapshot.Read(reader);
        if (!reader.Overrun)
            Track.Receive(s, SectorClock.Instance?.Tick ?? 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Driven)
            Track.Advance(SectorClock.Instance?.Tick ?? 0);
        if (!Track.ShownTick.HasValue)
            return;
        RemoteTrack.Pose p = Track.Sample();
        GlobalTransform = new Transform3D(new Basis(p.Orientation), (_origin?.Origin ?? default).To(p.Position));
        Velocity = p.Velocity;
        Thrust = p.Destroyed ? 0 : p.Thrust;
        HullLevel = p.Destroyed ? 0 : p.Heat;
        foreach (EnginePlume plume in _plumes)
            plume.Thrust = Thrust;
        if (p.Destroyed)
        {
            // blown up on its owner's machine: it goes once the fire and smoke hide it
            if (!_down)
            {
                _down = true;
                _downFor = 0;
                if (Visible)
                    Explosion.Spawn(GetParent(), GlobalPosition, 30f, 1.6, 16f);
            }
            else
                _downFor += delta;
            if (Visible)
                Explosion.Fade(this, Explosion.Gone(_downFor));
        }
        else
        {
            if (_down || !Visible)
                Explosion.Fade(this, 0);
            _down = false;
        }
        WakeLevel = p.Destroyed ? 0 : p.Wake;
        _radar.Follow(p.Radar, p.Radar == RadarMode.Track ? SessionNode.Instance?.NodeOf(p.RadarTarget) : null, p.RadarGuiding);
        if (p.Jam != JamMode.Off)
            _jammer.Spec = SessionNode.Instance?.JammerModel(p.JamModel) ?? Jammer ?? _jammer.Spec;
        _jammer.Follow(p.Destroyed ? JamMode.Off : p.Jam, p.JamAimed, p.JamAim);
        if (p.Turret)
            _turret?.Aim(p.TurretAzimuth, p.TurretElevation);
    }

    private Xpace.Ship.TurretRig _turret;       // the turret's look on its hardpoint, if it has one
}
