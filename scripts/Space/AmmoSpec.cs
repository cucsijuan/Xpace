using Godot;

namespace Xpace.Space;

/// <summary>
/// A model of cannon round (a .tres per model, in assets/weapons/ammo): what a turret's magazine is filled with,
/// bought at an arms dealer by the round, a box at a time (<see cref="Outfit.Lot"/>). Which turret takes it is the
/// turret's spec (<see cref="Xpace.Ship.TurretSpec.Ammunition"/>).
/// </summary>
[GlobalClass]
public partial class AmmoSpec : Outfit
{
    public AmmoSpec()
    {
        Model = "RND";
        Price = 2;
        Lot = 100;
    }

    public override string About => "Cannon rounds: a turret fires only what its magazine holds";

    public override string Details => $"Sold in boxes of {Lot}";
}
