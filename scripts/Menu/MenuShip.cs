using System;
using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.Menu;

/// <summary>
/// The main menu's ship: it drifts with its engines dead, then pushes
/// (the plumes come up), holds and lets go, over and over, swaying a little
/// as it flies. The plumes are its <see cref="EnginePlume"/> children.
/// </summary>
public partial class MenuShip : Node3D
{
    [Export] public double CoastSeconds = 4;            // engines dead
    [Export] public double RampSeconds = 0.6;           // pushing up, and letting go
    [Export] public double BurnSeconds = 2.5;           // at full push
    [Export] public float SwayDegrees = 2.5f;
    [Export] public float SwayPeriod = 7f;              // s
    [Export] public float Acceleration = 12f;           // m/s2 at full push
    [Export] public float Drag = 0.004f;                // the veil: dv/dt = -Drag v^2
    [Export] public float StartSpeed = 25f;             // m/s

    /// <summary>The speed it would be flying at (it stays put: <see cref="MenuDust"/> goes past instead).</summary>
    public float Speed { get; private set; }

    /// <summary>Its velocity, world axes: along the nose (the model's +Z).</summary>
    public Vector3 Velocity => GlobalBasis.Z.Normalized() * Speed;

    private EnginePlume[] _plumes;
    private Basis _rest;
    private double _t;

    public override void _Ready()
    {
        _plumes = FindChildren("*", "", true, false).OfType<EnginePlume>().ToArray();
        _rest = Basis;
        Speed = StartSpeed;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        double cycle = CoastSeconds + 2 * RampSeconds + BurnSeconds;
        double c = _t % cycle - CoastSeconds;
        float push = c < 0 ? 0
            : c < RampSeconds ? (float)(c / RampSeconds)
            : c < RampSeconds + BurnSeconds ? 1
            : (float)Math.Max(0, 1 - (c - RampSeconds - BurnSeconds) / RampSeconds);
        foreach (EnginePlume plume in _plumes)
            plume.Thrust = push;
        Speed = (Speed + push * Acceleration * (float)delta) / (1 + Drag * Speed * (float)delta);

        float a = Mathf.DegToRad(SwayDegrees);
        float w = Mathf.Tau / SwayPeriod;
        float roll = a * Mathf.Sin(w * (float)_t);
        float pitch = 0.5f * a * Mathf.Sin(w * 0.7f * (float)_t + 1f);
        Basis = _rest * new Basis(Vector3.Forward, roll) * new Basis(Vector3.Right, pitch);
    }
}
