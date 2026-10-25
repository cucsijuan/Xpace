using System;
using Godot;
using Xpace.Ship;

namespace Xpace.Tests;

/// <summary>
/// The ship's own integrator: the motion it gives, that a replay from a
/// saved state gives the same bits, and that it flies like the engine's
/// rigid body it replaced.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/motion_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class MotionTest : Node
{
    private const float Dt = 1f / 60;

    /// <summary>The final state's hash on the reference machine (Linux x64); other machines must give the same.</summary>
    private const ulong ReferenceHash = 0x99f1a2177f552c65;

    // Fixed numbers, not the hull's current tuning: the reference hash is of these
    private static readonly ShipSpec Spec = new()
    {
        MainThrust = 1_000_000f, ReverseThrust = 500_000f, LateralThrust = 500_000f, VerticalThrust = 500_000f,
        PitchAcceleration = 1.5f, RollAcceleration = 3.0f, YawAcceleration = 0.8f,
    };
    private int _failures;

    private RigidBody3D _body;
    private ShipState _ours;
    private int _step;
    private float _worstPosition, _worstAngle;

    public override void _Ready()
    {
        Pure();

        // The engine's rigid body, pushed by the same commands, side by side
        _body = new RigidBody3D
        {
            Mass = Spec.Mass, Inertia = Spec.Inertia, GravityScale = 0, CanSleep = false,
            LinearDampMode = RigidBody3D.DampMode.Replace, LinearDamp = 0,
            AngularDampMode = RigidBody3D.DampMode.Replace, AngularDamp = 0,
        };
        _body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        AddChild(_body);
        _ours = new ShipState { Orientation = Quaternion.Identity };
    }

    private void Pure()
    {
        var rest = new ShipState { Orientation = Quaternion.Identity };

        // Full forward thrust for 2 s from rest
        ShipState s = rest;
        for (int i = 0; i < 120; i++)
            s = ShipMotion.Step(s, new Vector3(0, 0, -Spec.MainThrust), Vector3.Zero, Spec.Mass, Spec.Inertia, Dt);
        float a = Spec.MainThrust / Spec.Mass;
        Check($"2 s of full thrust: {s.Velocity.Length():0.0} m/s and {-s.Position.Z:0.0} m forward ({a * 2:0.0}, {a * 2 * 2 / 2:0.0})",
              Near(-s.Velocity.Z, a * 2, 0.01f) && Near(-s.Position.Z, a * 2 * 2 / 2, a * 2 * Dt) && s.Position.X == 0);

        // Full pitch torque for 1 s, then let go: the turn keeps its rate
        s = rest;
        var pitch = new Vector3(Spec.PitchAcceleration * Spec.Inertia.X, 0, 0);
        for (int i = 0; i < 60; i++)
            s = ShipMotion.Step(s, Vector3.Zero, pitch, Spec.Mass, Spec.Inertia, Dt);
        float rate = s.AngularVelocity.X;
        for (int i = 0; i < 60; i++)
            s = ShipMotion.Step(s, Vector3.Zero, Vector3.Zero, Spec.Mass, Spec.Inertia, Dt);
        float angle = s.Orientation.GetAngle();
        float expected = Spec.PitchAcceleration / 2 + rate;     // half a second's worth, then a full second
        Check($"1 s of full pitch: {rate:0.000} rad/s, kept; {Mathf.RadToDeg(angle):0.0} deg turned ({Mathf.RadToDeg(expected):0.0})",
              Near(rate, Spec.PitchAcceleration, 1e-4f) && s.AngularVelocity.X == rate && Near(angle, expected, 0.02f));

        // A spin about a skew axis for a minute: same axis, same rate, a unit quaternion
        s = rest;
        s.AngularVelocity = new Vector3(0.7f, -1.1f, 2.3f);
        for (int i = 0; i < 3600; i++)
            s = ShipMotion.Step(s, Vector3.Zero, Vector3.Zero, Spec.Mass, Spec.Inertia, Dt);
        float along = MathF.Abs(s.Orientation.GetAxis().Dot(s.AngularVelocity.Normalized()));
        Check($"a minute's spin keeps its axis ({along:0.000000}) and stays a rotation ({s.Orientation.Length():0.000000})",
              along > 0.99999f && Near(s.Orientation.Length(), 1, 1e-5f));

        // Replay: from a state saved halfway, the same commands give the same bits
        ShipState saved = default, first = rest;
        for (int i = 0; i < 600; i++)
        {
            if (i == 300)
                saved = first;
            first = Commanded(first, i);
        }
        ShipState replay = saved;
        for (int i = 300; i < 600; i++)
            replay = Commanded(replay, i);
        Check("a replay from a saved state gives the same bits", Hash(replay) == Hash(first));
        ulong hash = Hash(first);
        Check($"the same bits as the reference machine ({hash:x16})", hash == ReferenceHash);
    }

    public override void _PhysicsProcess(double delta)
    {
        // The body has had as many engine steps as we have had our own
        float position = _body.GlobalPosition.DistanceTo(_ours.Position);
        float angle = _body.GlobalBasis.GetRotationQuaternion().AngleTo(_ours.Orientation);
        _worstPosition = MathF.Max(_worstPosition, position);
        _worstAngle = MathF.Max(_worstAngle, angle);

        if (_step == 600)
        {
            Check($"10 s of manoeuvres fly like the engine's rigid body ({_worstPosition * 100:0.0} cm, {Mathf.RadToDeg(_worstAngle):0.000} deg at worst)",
                  _worstPosition < 0.05f && Mathf.RadToDeg(_worstAngle) < 0.1f);
            GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
            GetTree().Quit(_failures == 0 ? 0 : 1);
            _step++;
            return;
        }
        if (_step > 600)
            return;

        (Vector3 force, Vector3 torque) = Commands(_step);
        _body.ApplyCentralForce(_body.GlobalBasis * force);
        _body.ApplyTorque(_body.GlobalBasis * torque);
        _ours = ShipMotion.Step(_ours, force, torque, Spec.Mass, Spec.Inertia, Dt);
        _step++;
    }

    private static ShipState Commanded(ShipState s, int step)
    {
        (Vector3 force, Vector3 torque) = Commands(step);
        return ShipMotion.Step(s, force, torque, Spec.Mass, Spec.Inertia, Dt);
    }

    /// <summary>Stick and thrusters changing every half second, the same on every run.</summary>
    private static (Vector3 force, Vector3 torque) Commands(int step)
    {
        uint seed = (uint)(step / 30) * 2654435761u + 12345;
        float Next()
        {
            seed = seed * 1664525 + 1013904223;
            return (seed >> 8) / (float)(1 << 23) - 1;         // -1..1
        }
        var force = new Vector3(Next() * Spec.LateralThrust, Next() * Spec.VerticalThrust, -Next() * Spec.MainThrust);
        var torque = new Vector3(Next() * Spec.PitchAcceleration * Spec.Inertia.X,
                                 Next() * Spec.YawAcceleration * Spec.Inertia.Y,
                                 Next() * Spec.RollAcceleration * Spec.Inertia.Z);
        return (force, torque);
    }

    private static ulong Hash(ShipState s)
    {
        ulong h = 14695981039346656037;
        foreach (float f in new[] { s.Position.X, s.Position.Y, s.Position.Z,
                                    s.Orientation.X, s.Orientation.Y, s.Orientation.Z, s.Orientation.W,
                                    s.Velocity.X, s.Velocity.Y, s.Velocity.Z,
                                    s.AngularVelocity.X, s.AngularVelocity.Y, s.AngularVelocity.Z })
            h = (h ^ BitConverter.SingleToUInt32Bits(f)) * 1099511628211;
        return h;
    }

    private static bool Near(float x, float target, float tolerance) => MathF.Abs(x - target) <= tolerance;

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
