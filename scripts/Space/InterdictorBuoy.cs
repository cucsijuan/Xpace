using Godot;
using Xpace.Net;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>
/// A fixed interdictor (its beam as <see cref="InterdictorDevice"/>'s): any cruising ship within <see cref="Range"/>
/// is caught in it, with no lock and no pilot. A hazard to place on a route. Each machine works it on the ships it
/// owns, so it needs nothing from the session.
/// </summary>
public partial class InterdictorBuoy : Node3D
{
    /// <summary>Where the density it adds is 1 (m).</summary>
    [Export] public float Reach = 150000f;
    /// <summary>Beyond this it doesn't reach a ship (m).</summary>
    [Export] public float Range = 60000f;
    [ExportGroup("Its frequency's program (0-255 band)")]
    [Export] public float Centre = 128f;
    [Export] public float Wander = 70f;
    [Export] public float Quiver = 15f;
    [Export] public float WanderRate = 0.12f;

    private double _time;

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        byte band = InterdictorDevice.BandAt(_time, Centre, Wander, Quiver, WanderRate);
        foreach (Node node in Sectors.InGroup(this, Radio.Group))
        {
            if (Combat.ShipOf(node) is not ShipBody ship || NetBody.IsPuppet(ship) || ship.Device<CruiseDevice>() is not { On: true } cruise)
                continue;
            float d2 = GlobalPosition.DistanceSquaredTo(ship.GlobalPosition);
            if (d2 <= Range * Range)
                cruise.Thicken(Reach * Reach / Mathf.Max(d2, 1), band);
        }
    }
}
