using System;
using System.Collections.Generic;
using Godot;
using Xpace.Net;

namespace Xpace.Space;

/// <summary>
/// Something a radar warning receiver can hear: a radar that is
/// transmitting. Anything in the "radar_emitters" group with this interface
/// counts.
/// </summary>
public interface IRadarEmitter
{
    ushort EmitterId { get; }
    Vector3 GlobalPosition { get; }
    /// <summary>What its pulses look like (an RWR's library tells types apart by it): "D", "F"...</summary>
    string Signature { get; }
    /// <summary>
    /// Signal-to-noise ratio its main lobe gives a reference warning receiver
    /// 1 km away; it falls with the square of the distance (one way only, so a
    /// receiver hears a radar much further out than the radar sees it).
    /// </summary>
    float PowerAt1Km { get; }
    /// <summary>It is tracking something: its pulses have the waveform of a lock.</summary>
    bool Tracking { get; }
    /// <summary>Tracking, it also carries a datalink's words to a missile: a launch at what it tracks.</summary>
    bool Guiding { get; }
    /// <summary>Pulses a second: what a warning receiver plays in the pilot's headset.</summary>
    float PrfHz { get; }
    /// <summary>Its main lobe crossed or sat on this point during the last step.</summary>
    bool Illuminates(Vector3 point);
}

/// <summary>
/// A ship's radar as the others hear it. While searching, a fan beam
/// <see cref="BeamwidthDegrees"/> wide sweeps across the cone ahead, there
/// and back every <see cref="ScanSeconds"/>: something in the cone is lit
/// once each way, a short ping. Once the beam has passed over its target
/// inside <see cref="LockRange"/> it locks: the beam stays on the target and
/// the pulses change to the tracking waveform. The lock breaks when the
/// target leaves the cone or gets well out of range.
///
/// The echoes the beam passes over within <see cref="DetectRange"/> (for an
/// echo of <see cref="ReferenceRcs"/>; a bigger one is seen further, by the
/// fourth root of its size) are what it sees: <see cref="Swept"/>, step by
/// step. Its ship picks one of them as its <see cref="Target"/>; it locks
/// that one when the beam crosses it inside <see cref="LockRange"/> (scaled
/// the same way).
///
/// Only the main lobe counts: sidelobes are too weak to matter here.
/// Put it under a ship (it looks along the ship's -Z); it goes quiet while
/// the ship is dead. On a puppet
/// ship (a network session) it does what the owner's radar does: it
/// sweeps while that one searches and sits on what that one tracks.
/// </summary>
public partial class RadarEmitter : Node3D, IRadarEmitter, INetRadar
{
    [Export] public bool On = true;
    [Export] public string Signature { get; set; } = "D";
    [Export] public float PowerAt1Km { get; set; } = 1e4f;
    [Export] public float PrfHz { get; set; } = 900f;
    [Export] public float ConeDegrees = 60f;
    [Export] public float BeamwidthDegrees = 3f;
    [Export] public float ScanSeconds = 2f;
    [Export] public float LockRange = 2500f;
    [Export] public float DetectRange = 4000f;
    [Export] public float ReferenceRcs = 3f;           // m²: the echo the ranges are for

    public ushort EmitterId { get; private set; }
    /// <summary>What it hunts (its ship chose it): it locks this one when it can.</summary>
    public Node3D Target { get; set; }
    /// <summary>The echoes its beam passed over in the last step, searching.</summary>
    public IReadOnlyList<Node3D> Swept => _swept;
    public bool Tracking => Locked;
    public bool Locked { get; private set; }

    private double _scan;
    private float _beamFrom, _beamTo;            // the beam's azimuth at the start and end of the step
    private IRadarTarget _owner;
    private bool _puppet;
    private RadarMode _told;
    private readonly List<Node3D> _swept = new();

    public override void _Ready()
    {
        EmitterId = RadarIds.Next();
        AddToGroup("radar_emitters");
        for (Node n = GetParent(); n != null && _owner == null; n = n.GetParent())
            _owner = n as IRadarTarget;
        _scan = new RandomNumberGenerator { Seed = EmitterId * 104729UL }.Randf() * ScanSeconds;
        _beamFrom = _beamTo = BeamAzimuth(_scan);
    }

    private bool Transmitting => _puppet ? _told != RadarMode.Off : On && (_owner == null || _owner.Alive);

    public RadarMode Mode => !Transmitting ? RadarMode.Off : Locked ? RadarMode.Track : RadarMode.Search;
    public Node Tracked => Locked ? Target : null;
    public bool Guiding { get; private set; }

    public void Follow(RadarMode mode, Node3D tracked, bool guiding)
    {
        _puppet = true;
        _told = mode;
        Locked = mode == RadarMode.Track && tracked != null;
        Guiding = Locked && guiding;
        if (Locked)
            Target = tracked;
    }

    public override void _PhysicsProcess(double delta)
    {
        _scan = (_scan + delta) % ScanSeconds;
        _beamFrom = _beamTo;
        _beamTo = BeamAzimuth(_scan);
        _swept.Clear();
        if (_puppet)
            return;                             // the owner's radar decides
        if (!Transmitting)
        {
            Locked = false;
            return;
        }
        if (!Locked)
            Search();
        if (Target == null || !IsInstanceValid(Target) || Target is IRadarTarget { Alive: false })
        {
            Locked = false;
            return;
        }
        Vector3 local = GlobalBasis.Inverse() * (Target.GlobalPosition - GlobalPosition);
        float range = local.Length();
        bool inCone = range > 1 && Vector3.Forward.AngleTo(local) <= Mathf.DegToRad(ConeDegrees);
        float reach = Reach(Target, LockRange);
        if (Locked)
            Locked = inCone && range <= reach * 1.15f;
        else if (inCone && range <= reach && Crossed(local))
            Locked = true;
    }

    /// <summary>The echoes the beam crossed this step, inside the reach their size gives.</summary>
    private void Search()
    {
        foreach (Node node in GetTree().GetNodesInGroup("radar_targets"))
        {
            if (node is not IRadarTarget t || t == _owner || !t.Alive || node is not Node3D n || !n.IsVisibleInTree())
                continue;
            Vector3 local = GlobalBasis.Inverse() * (n.GlobalPosition - GlobalPosition);
            float range = local.Length();
            if (range > 1 && range <= Reach(n, DetectRange) && Vector3.Forward.AngleTo(local) <= Mathf.DegToRad(ConeDegrees) && Crossed(local))
                _swept.Add(n);
        }
    }

    /// <summary>A range for the reference echo, for this one: the echo's power goes with its size over range to the fourth.</summary>
    private float Reach(Node3D target, float range) =>
        target is IRadarTarget t ? range * MathF.Pow(MathF.Max(t.RadarCrossSection, 0) / ReferenceRcs, 0.25f) : range;

    public bool Illuminates(Vector3 point)
    {
        if (!Transmitting)
            return false;
        Vector3 local = GlobalBasis.Inverse() * (point - GlobalPosition);
        if (local.LengthSquared() < 1)
            return false;
        float half = Mathf.DegToRad(BeamwidthDegrees) / 2;
        if (Locked)
        {
            if (Target == null || !IsInstanceValid(Target))
                return false;                   // what it tracked is gone (chaff that blew away)
            Vector3 toTarget = GlobalBasis.Inverse() * (Target.GlobalPosition - GlobalPosition);
            return local.AngleTo(toTarget) <= half;
        }
        return Vector3.Forward.AngleTo(local) <= Mathf.DegToRad(ConeDegrees) && Crossed(local);
    }

    /// <summary>The fan beam passed over this direction (emitter axes) in the last step.</summary>
    private bool Crossed(Vector3 local)
    {
        float az = Mathf.Atan2(local.X, -local.Z);
        float half = Mathf.DegToRad(BeamwidthDegrees) / 2;
        return az >= MathF.Min(_beamFrom, _beamTo) - half && az <= MathF.Max(_beamFrom, _beamTo) + half;
    }

    /// <summary>A triangle wave: left edge to right edge and back in ScanSeconds.</summary>
    private float BeamAzimuth(double t)
    {
        float phase = (float)(t / ScanSeconds) * 2;
        float sweep = phase < 1 ? phase : 2 - phase;
        return Mathf.DegToRad(ConeDegrees) * (2 * sweep - 1);
    }
}
