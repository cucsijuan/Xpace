using Godot;
using Xpace.World;

namespace Xpace.Ship;

/// <summary>
/// Makes a player's ship the one their record says (<see cref="ShipRecord.Hull"/>): its hull's spec, and with it its
/// racks, its mass and its cockpit's screens. A pilot whose ship is impounded flies the insurer's courtesy ship in its
/// place, and their own again once it is released. Put it under the player's <see cref="ShipBody"/>.
/// </summary>
public partial class PilotShip : Node
{
    private ShipBody _ship;

    public override void _Ready()
    {
        _ship = GetParentOrNull<ShipBody>();
        if (Universe.Current is Universe universe)
        {
            universe.Changed += Changed;
            TreeExiting += () => universe.Changed -= Changed;
        }
    }

    private void Changed(string topic)
    {
        if (topic == Universe.PilotTopic)
            Fit();
    }

    public override void _PhysicsProcess(double delta) => Fit();

    /// <summary>The hull the record says, if the ship has another.</summary>
    public void Fit()
    {
        if (_ship == null || Universe.Current?.RecordOf(_ship)?.Ship.Hull is not string hull || _ship.Spec?.ResourcePath == hull)
            return;
        if (ResourceLoader.Exists(hull) && GD.Load<ShipSpec>(hull) is ShipSpec spec)
            _ship.Spec = spec;
    }
}
