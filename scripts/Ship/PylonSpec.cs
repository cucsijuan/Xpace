using Godot;

namespace Xpace.Ship;

/// <summary>
/// A model of missile pylon (a .tres per model, in assets/weapons/pylons): rails on a hardpoint that takes missiles,
/// each rail holding one missile of any model. The ship's launcher fires from them (<see cref="LauncherDevice"/>), and
/// how many missiles a ship carries is how many rails its pylons have. Bought at an arms dealer, it goes to the
/// pilot's locker there, to be fitted.
/// </summary>
[GlobalClass]
public partial class PylonSpec : Xpace.Space.Outfit
{
    public PylonSpec()
    {
        Model = "PYL";
        Price = 600;
    }

    /// <summary>The hardpoint size it needs.</summary>
    [Export] public int Size = 1;

    /// <summary>
    /// What it looks like: a scene with a Marker3D for each rail, named Rail1, Rail2... (a missile hangs centred on
    /// it, its nose along -Z); as many rails as it has markers.
    /// </summary>
    [Export] public PackedScene Scene;

    /// <summary>The editor only: a missile's look hung on every rail while a hardpoint shows it as its preview.</summary>
    [Export] public PackedScene PreviewLoad;

    /// <summary>How many rails it has (as its scene has them).</summary>
    [Export] public int Rails = 4;

    public override string About => "Missile pylon: rails for the ship's launcher, one missile of any model on each";

    public override string Details => $"Size {Size}, {Rails} rails";
}
