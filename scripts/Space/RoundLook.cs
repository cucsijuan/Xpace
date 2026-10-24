using Godot;

namespace Xpace.Space;

/// <summary>
/// How a kind of round looks in flight: a glowing streak along its path.
/// <see cref="Projectiles"/> keeps one per kind (0 the cannon's tracer, 1 a
/// laser pulse...); the kind travels with every round fired in a session,
/// so the other machines draw it the same.
/// </summary>
[GlobalClass]
public partial class RoundLook : Resource
{
    [Export] public Color Colour = new(1f, 0.75f, 0.35f);
    [Export] public Color Glow = new(1f, 0.6f, 0.2f);
    [Export] public float GlowEnergy = 3f;
    [Export] public float Length = 40f;                 // m
    [Export] public float MinWidth = 0.15f;             // m
    /// <summary>Width per metre of distance from the camera: about the same few pixels near or far.</summary>
    [Export] public float WidthPerMetre = 0.0025f;
}
