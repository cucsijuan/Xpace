using System.Linq;
using Godot;
using Xpace.Ship;

namespace Xpace.Tools;

/// <summary>
/// Writes on each hull what the parts of its exterior come to: mass and inertia (<see cref="ShipPart.Totals"/>). Hulls
/// whose exterior has no parts (one model, numbers by hand) are left as they are. Run the scene from the editor after
/// changing a hull's parts, or:
///
///   godot --headless --path . res://tools/ship_mass/ship_mass_tool.tscn
/// </summary>
public partial class ShipMassTool : Node
{
    [Export(PropertyHint.Dir)] public string Hulls = "res://assets/ship/hulls";

    public override void _Ready()
    {
        foreach (string file in DirAccess.GetFilesAt(Hulls).Where(f => f.EndsWith(".tres")))
        {
            string path = $"{Hulls}/{file}";
            if (Xpace.World.ResourceCache.Load<Resource>(path) is not ShipSpec hull || hull.Exterior?.Instantiate() is not Node3D exterior)
                continue;
            ShipPart.Sum sum = ShipPart.Totals(exterior);
            exterior.Free();
            if (sum.Parts == 0)
                continue;
            hull.Mass = sum.Mass;
            hull.Inertia = sum.Inertia;
            Error saved = ResourceSaver.Save(hull, path);
            GD.Print($"{file}: {sum.Parts} parts, {sum.Mass / 1000:0.0} t, inertia {sum.Inertia}, centre of mass {sum.Centre}{(saved == Error.Ok ? "" : $": not saved ({saved})")}");
        }
        GetTree().Quit();
    }
}
