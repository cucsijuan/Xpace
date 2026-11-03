using Godot;

namespace Xpace.Space;

/// <summary>
/// A burst of particles that plays once and goes (a puff of dust where a piece is fitted): put it on a
/// CPUParticles3D set to one shot; it starts when it enters the tree and frees itself when the last particle is gone.
/// </summary>
public partial class OneShotFx : CpuParticles3D
{
    public override void _Ready()
    {
        OneShot = true;
        Emitting = true;
        Finished += QueueFree;
    }
}
