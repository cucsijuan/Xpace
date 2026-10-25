using System;
using Godot;

namespace Xpace.Ship;

/// <summary>Where a ship is and how it moves, in world axes.</summary>
public struct ShipState
{
    public Vector3 Position;
    public Quaternion Orientation;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;
}

/// <summary>
/// The ship's own integrator, in place of the physics engine's: a rigid
/// body under thrust and torque, with no gravity or contacts. The only drag
/// is the veil's (GDD 6.5): the field the ship's generator raises holds on
/// to the sector's thin medium, at rest in the sector's frame, and pulls
/// back with k v² (a top speed where it matches the thrust), harder across
/// the hull than along its nose (so the path follows the nose, at a cost in
/// speed, as in air); it drags on turning too, about each of the ship's
/// axes (a top turn rate).
///
/// One step is a pure function of the state and the commands, so a step
/// can be re-run (to replay a ship from a saved state) and gives the same
/// bits on every machine: it uses only + - * / and square roots, never
/// sines or cosines from the system's maths library.
///
/// Semi-implicit Euler, like the engine's rigid bodies: the velocity first
/// (the thrust, then the veil's drag along each of the ship's axes, taken
/// implicitly: v / (1 + k |v| dt), steady at any speed), then the position with the new velocity, and the
/// orientation turned by exactly the step's angle. As in the engine, the body has no gyroscopic
/// term: with no torque it keeps turning about the same axis at the same rate.
/// </summary>
public static class ShipMotion
{
    /// <param name="force">Newtons, along the ship's own axes.</param>
    /// <param name="torque">Newton metres, about the ship's own axes.</param>
    /// <param name="inertia">Moments of inertia about the ship's own X, Y, Z (kg m²).</param>
    /// <param name="drag">The veil's k along the ship's X (across), Y (up and down), Z (the nose), per metre: a deceleration of k |v| v that way (0: none, as in empty space).</param>
    /// <param name="spinDrag">The veil against turning about the ship's X, Y, Z, per radian: k w² about each (0: none).</param>
    public static ShipState Step(ShipState s, Vector3 force, Vector3 torque, float mass, Vector3 inertia, float dt,
                                 Vector3 drag = default, Vector3 spinDrag = default)
    {
        Quaternion q = s.Orientation;
        s.Velocity += q * (force / mass) * dt;
        if (drag != Vector3.Zero)
        {
            float speed = s.Velocity.Length();
            Vector3 v = q.Inverse() * s.Velocity;
            v.X /= 1 + drag.X * speed * dt;
            v.Y /= 1 + drag.Y * speed * dt;
            v.Z /= 1 + drag.Z * speed * dt;
            s.Velocity = q * v;
        }
        s.AngularVelocity += q * (torque / inertia) * dt;
        if (spinDrag != Vector3.Zero)
        {
            // About the ship's own axes, each taken implicitly as the speed is
            Vector3 w = q.Inverse() * s.AngularVelocity;
            w.X /= 1 + spinDrag.X * MathF.Abs(w.X) * dt;
            w.Y /= 1 + spinDrag.Y * MathF.Abs(w.Y) * dt;
            w.Z /= 1 + spinDrag.Z * MathF.Abs(w.Z) * dt;
            s.AngularVelocity = q * w;
        }
        s.Position += s.Velocity * dt;

        // The step's turn exactly, w dt about w: the quaternion (sin(x) axis, cos(x))
        // for the half angle x, by series (exact to 1e-6 even at x = 1, 120 rad/s)
        Vector3 h = s.AngularVelocity * (dt * 0.5f);
        float x2 = h.LengthSquared();
        float sinc = 1 - x2 / 6 * (1 - x2 / 20 * (1 - x2 / 42 * (1 - x2 / 72)));
        float cos = 1 - x2 / 2 * (1 - x2 / 12 * (1 - x2 / 30 * (1 - x2 / 56)));
        var turn = new Quaternion(h.X * sinc, h.Y * sinc, h.Z * sinc, cos);
        s.Orientation = (turn * q).Normalized();
        return s;
    }
}
