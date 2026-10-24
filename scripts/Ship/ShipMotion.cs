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
/// body under thrust and torque, with no gravity, drag or contacts.
///
/// One step is a pure function of the state and the commands, so a step
/// can be re-run (to replay a ship from a saved state) and gives the same
/// bits on every machine: it uses only + - * / and square roots, never
/// sines or cosines from the system's maths library.
///
/// Semi-implicit Euler, like the engine's rigid bodies: the velocity first,
/// then the position with the new velocity, and the orientation turned by
/// exactly the step's angle. As in the engine, the body has no gyroscopic
/// term: with no torque it keeps turning about the same axis at the same rate.
/// </summary>
public static class ShipMotion
{
    /// <param name="force">Newtons, along the ship's own axes.</param>
    /// <param name="torque">Newton metres, about the ship's own axes.</param>
    /// <param name="inertia">Moments of inertia about the ship's own X, Y, Z (kg m²).</param>
    public static ShipState Step(ShipState s, Vector3 force, Vector3 torque, float mass, Vector3 inertia, float dt)
    {
        Quaternion q = s.Orientation;
        s.Velocity += q * (force / mass) * dt;
        s.AngularVelocity += q * (torque / inertia) * dt;
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
