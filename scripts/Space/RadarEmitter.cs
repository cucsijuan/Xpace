using System;
using Godot;

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
/// Only the main lobe counts: sidelobes are too weak to matter here.
/// Put it under a ship (it looks along the ship's -Z) and give it the
/// target it hunts; it goes quiet while the ship is dead.
/// </summary>
public partial class RadarEmitter : Node3D, IRadarEmitter
{
    [Export] public bool On = true;
    [Export] public string Signature { get; set; } = "D";
    [Export] public float PowerAt1Km { get; set; } = 1e4f;
    [Export] public float PrfHz { get; set; } = 900f;
    [Export] public float ConeDegrees = 60f;
    [Export] public float BeamwidthDegrees = 3f;
    [Export] public float ScanSeconds = 2f;
    [Export] public float LockRange = 2500f;

    /// <summary>What it hunts: it locks this one when it can.</summary>
    [Export] public NodePath TargetPath;

    public ushort EmitterId { get; private set; }
    public Node3D Target { get; set; }
    public bool Tracking => Locked;
    public bool Locked { get; private set; }

    private double _scan;
    private float _beamFrom, _beamTo;            // the beam's azimuth at the start and end of the step
    private IRadarTarget _owner;

    public override void _Ready()
    {
        EmitterId = RadarIds.Next();
        AddToGroup("radar_emitters");
        Target ??= TargetPath == null || TargetPath.IsEmpty ? null : GetNodeOrNull<Node3D>(TargetPath);
        for (Node n = GetParent(); n != null && _owner == null; n = n.GetParent())
            _owner = n as IRadarTarget;
        _scan = new RandomNumberGenerator { Seed = EmitterId * 104729UL }.Randf() * ScanSeconds;
        _beamFrom = _beamTo = BeamAzimuth(_scan);
    }

    private bool Transmitting => On && (_owner == null || _owner.Alive);

    public override void _PhysicsProcess(double delta)
    {
        _scan = (_scan + delta) % ScanSeconds;
        _beamFrom = _beamTo;
        _beamTo = BeamAzimuth(_scan);
        if (!Transmitting || Target == null || !IsInstanceValid(Target))
        {
            Locked = false;
            return;
        }
        Vector3 local = GlobalBasis.Inverse() * (Target.GlobalPosition - GlobalPosition);
        float range = local.Length();
        bool inCone = range > 1 && Vector3.Forward.AngleTo(local) <= Mathf.DegToRad(ConeDegrees);
        if (Locked)
            Locked = inCone && range <= LockRange * 1.15f;
        else if (inCone && range <= LockRange && Swept(local))
            Locked = true;
    }

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
            Vector3 toTarget = GlobalBasis.Inverse() * (Target.GlobalPosition - GlobalPosition);
            return local.AngleTo(toTarget) <= half;
        }
        return Vector3.Forward.AngleTo(local) <= Mathf.DegToRad(ConeDegrees) && Swept(local);
    }

    /// <summary>The fan beam passed over this direction (emitter axes) in the last step.</summary>
    private bool Swept(Vector3 local)
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
