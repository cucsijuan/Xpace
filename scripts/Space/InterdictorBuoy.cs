using Godot;
using Xpace.Net;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>
/// A fixed interdictor: the same beam as a ship's (<see cref="InterdictorSpec"/>), with no lock and no pilot: any
/// cruising ship within its range is caught in it. A hazard to place on a route. Each machine works it on the ships it
/// owns, so it needs nothing from the session; out of sight, the universe works it on the ships on records.
/// </summary>
public partial class InterdictorBuoy : Node3D
{
    [Export] public InterdictorSpec Model { get; set; }

    private double _time;

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        if (Model == null)
            return;
        byte band = Model.Band(Xpace.World.Universe.Current?.State.Clock ?? _time);
        foreach (Node node in Sectors.InGroup(this, Radio.Group))
        {
            if (Combat.ShipOf(node) is not ShipBody ship || NetBody.IsPuppet(ship) || ship.Device<CruiseDevice>() is not { Cruising: true } cruise)
                continue;
            float density = Model.Density(GlobalPosition.DistanceSquaredTo(ship.GlobalPosition));
            if (density > 0)
                cruise.Thicken(density, band);
        }
    }
}
