using System.Linq;
using Godot;
using Xpace.World;

namespace Xpace.Ship;

/// <summary>
/// Makes a player's ship the one their record says (<see cref="ShipRecord.Hull"/>): its hull's spec, and with it its
/// racks, its mass and its cockpit's screens, and what is mounted on its hardpoints (<see cref="ShipRecord.Fittings"/>);
/// a refit restarts the ship's computer, as the fitters would, so that its programs find the weapons it has now. A pilot whose ship is impounded flies the insurer's courtesy ship in its
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

    /// <summary>The hull and the fitting the record says, if the ship has others.</summary>
    public void Fit()
    {
        if (_ship == null || Universe.Current?.RecordOf(_ship)?.Ship is not ShipRecord record || record.Hull is not string hull)
            return;
        if (_ship.Spec?.ResourcePath != hull && ResourceLoader.Exists(hull) && GD.Load<ShipSpec>(hull) is ShipSpec spec)
            _ship.Spec = spec;
        string wanted = $"{_ship.Spec?.ResourcePath}|{string.Join(";", record.Fittings.OrderBy(f => f.Key, System.StringComparer.Ordinal))}";
        if (wanted == _fitted)
            return;
        bool refit = _fitted != null;
        _fitted = wanted;
        var fitted = Fittings.Of(_ship.Spec, record);
        if (ShipBody.Key(fitted) == _ship.Fitted)
            return;
        _ship.Fit(fitted);
        if (refit)
        {
            _ship.Computer?.Reset();                // its programs find the weapons when they start
            Refits++;
        }
    }

    private string _fitted;

    /// <summary>How many times the ship was refitted here and its computer restarted.</summary>
    public int Refits { get; private set; }
}
