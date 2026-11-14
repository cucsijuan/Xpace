using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Debug;
using Xpace.Menu;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// The debug menu: the game menu offers it in a debug build; its law zones switch draws every station's and gate's
/// protected zone and speed limit's radius as spheres of the laws' sizes, and takes them away again.
///
/// Run: Godot --headless --path . res://tests/debug_menu_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class DebugMenuTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        await Frames(30);

        GameMenu.Open();
        await Frames(2);
        var menu = main.GetNode<GameMenu>("GameMenu");
        var button = menu.GetNode<Button>("%DebugButton");
        Check("a debug build: the game menu offers the debug page", DebugOptions.Available && button.Visible);
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var page = menu.GetNode<DebugMenu>("%Debug");
        var lawZones = page.GetNode<CheckBox>("%LawZones");
        Check("it opens, the law zones switch off", page.Visible && !lawZones.ButtonPressed);
        lawZones.ButtonPressed = true;
        await Frames(3);
        Check("ticking it turns the law zones on", DebugOptions.LawZones);

        var radios = StationRadio.All.Where(r => IsInstanceValid(r) && r.IsInsideTree() && r.Laws != null).ToList();
        bool all = radios.Count > 0 && radios.All(r =>
        {
            var zones = r.GetNodeOrNull<Node3D>("DebugLawZones");
            var shown = zones?.GetNode<Node3D>("Protected/Sphere");
            var speed = zones?.GetNode<Node3D>("Speed");
            return shown != null && Mathf.IsEqualApprox(shown.Scale.X, r.Laws.ZoneRadius)
                && speed.Visible == r.Laws.SpeedRadius > 0
                && (r.Laws.SpeedRadius <= 0 || Mathf.IsEqualApprox(speed.GetNode<Node3D>("Sphere").Scale.X, r.Laws.SpeedRadius));
        });
        Check($"every station and gate shows its zones, the laws' sizes ({radios.Count}: {string.Join(", ", radios.Select(r => $"{r.Sign} {r.Laws.ZoneRadius:0}/{r.Laws.SpeedRadius:0}"))})", all);

        page.GetNode<Button>("%Back").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check("back to the game menu", !page.Visible && menu.GetNode<Control>("%Menu").Visible);
        DebugOptions.LawZones = false;
        await Frames(3);
        Check("switched off, the spheres are gone", radios.All(r => r.GetNodeOrNull("DebugLawZones") == null));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Paused = false;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
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
