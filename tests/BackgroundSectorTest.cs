using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// A sector the host runs only for its guests: what it shows is left out, what
/// is added to it too, and no explosions go off in it; its solids and radios go
/// on as ever. When the player arrives, everything it shows is back; the sector
/// the player leaves, if kept, is left out in turn.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/background_sector_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class BackgroundSectorTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        await Frames(10);
        Check("the sector the player is in shows everything", !main.VisualsOf("sector_01").Hidden && Rendered(main.Sector) > 100);

        FloatingOrigin held = main.Hold(1);
        await Frames(5);
        SectorVisuals visuals = main.VisualsOf("sector_02");
        Check($"a sector held for a guest shows nothing ({visuals.Count} things left out)", visuals.Hidden && visuals.Count > 100 && Rendered(held) == 0);

        var added = new MeshInstance3D { Mesh = new BoxMesh() };
        held.AddChild(added);
        await Frames(2);
        Check("nor what is added to it", !added.GetBase().IsValid);
        int children = held.GetChildCount();
        Explosion.Spawn(held, Vector3.Zero, 30);
        Check("no explosions go off in it", held.GetChildCount() == children);
        children = main.Sector.GetChildCount();
        Explosion.Spawn(main.Sector, new Vector3(0, 0, -5000), 30);
        Check("they do where the player is", main.Sector.GetChildCount() > children);

        World3D world = held.GetNode<Node3D>("Kestrel").GetWorld3D();
        Node3D kestrel = held.GetNode<Node3D>("Kestrel");
        Check("its solids are there all the same", Solid.Blocked(world, kestrel.GlobalPosition + new Vector3(0, 0, 2000), kestrel.GlobalPosition - new Vector3(0, 0, 2000)));

        // The player goes there: the sector left is kept, as a host's is with guests in it
        main.Keep = _ => true;
        FloatingOrigin first = main.Sector;
        main.Jump("sector_02", new Transform3D(Basis.Identity, new Vector3(0, 0, -300)));
        await Frames(5);
        Check($"the player arrives: everything is back ({Rendered(held)})", !visuals.Hidden && visuals.Count == 0 && Rendered(held) > 100);
        Check("added or not", added.GetBase().IsValid);
        Check("the sector left is left out in turn", main.VisualsOf("sector_01").Hidden && Rendered(first) == 0);

        main.Keep = null;
        main.Jump("sector_01", new Transform3D(Basis.Identity, new Vector3(0, 0, 400)));
        await Frames(5);
        Check("and back again", !main.VisualsOf("sector_01").Hidden && Rendered(first) > 100 && main.VisualsOf("sector_02") == null);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static int Rendered(Node root)
    {
        int n = root is VisualInstance3D v && v.GetBase().IsValid ? 1 : 0;
        foreach (Node child in root.GetChildren())
            n += Rendered(child);
        return n;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
