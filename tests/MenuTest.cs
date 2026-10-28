using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Cockpit;
using Xpace.Menu;

namespace Xpace.Tests;

/// <summary>
/// The main menu and its settings: Settings opens the screen with every option filled; a binding's button takes the
/// next key, and that key leaves the action that had it; Esc leaves a binding as it was; changes reach
/// <see cref="GameSettings"/> at once and Back saves them; Defaults brings the default keys back. The player's own
/// settings file is put back at the end.
///
/// Run: Godot --headless --path . res://tests/menu_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class MenuTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        byte[] saved = FileAccess.FileExists(GameSettings.Path) ? FileAccess.GetFileAsBytes(GameSettings.Path) : null;
        GameSettings s = GameSettings.Current;
        Check("the settings are an autoload", s != null);

        var menu = GD.Load<PackedScene>("res://scenes/menu/main_menu.tscn").Instantiate<MainMenu>();
        AddChild(menu);
        await Frames(5);
        var settings = menu.GetNode<SettingsMenu>("%Settings");
        Check("the menu shows, the settings don't", menu.GetNode<Control>("%Menu").Visible && !settings.Visible);
        menu.GetNode<Button>("%SettingsButton").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var tabs = settings.GetNode<TabContainer>("Center/Panel/Box/Tabs");
        tabs.CurrentTab = 1;
        await Frames(10);
        Check("Settings opens the settings screen", settings.Visible && !menu.GetNode<Control>("%Menu").Visible);
        var resolution = settings.GetNode<OptionButton>("%Resolution");
        Check($"every option is filled ({resolution.ItemCount} resolutions, {settings.GetNode<OptionButton>("%Window").ItemCount} window modes, "
              + $"{settings.GetNode<OptionButton>("%Antialiasing").ItemCount} antialiasing)",
              resolution.ItemCount >= 1 && resolution.Selected >= 0 && settings.GetNode<OptionButton>("%Window").ItemCount == 3
              && settings.GetNode<OptionButton>("%Antialiasing").ItemCount == 6);
        var bindings = settings.GetNode<GridContainer>("%Bindings");
        int actions = ControlSets.All.Count();
        Check($"every action has its binding, under its set's heading ({bindings.GetChildCount() / 2 - ControlSets.Sets.Length} of {actions})",
              bindings.GetChildCount() == 2 * (actions + ControlSets.Sets.Length));

        // Rebinding
        tabs.CurrentTab = 3;
        await Frames(10);
        await Rebind(bindings, FlightInput.ThrottleUp, Key.K);
        Check($"a binding's button takes the next key (throttle up: {GameSettings.Describe(GameSettings.Binding(FlightInput.ThrottleUp))})",
              GameSettings.Binding(FlightInput.ThrottleUp) is InputEventKey { PhysicalKeycode: Key.K });
        await Rebind(bindings, FlightInput.ThrottleDown, Key.K);
        Check("the key leaves the action that had it",
              GameSettings.Binding(FlightInput.ThrottleDown) is InputEventKey { PhysicalKeycode: Key.K } && GameSettings.Binding(FlightInput.ThrottleUp) == null);
        await Rebind(bindings, FlightInput.ThrottleDown, Key.Escape);
        Check("Esc leaves a binding as it was", GameSettings.Binding(FlightInput.ThrottleDown) is InputEventKey { PhysicalKeycode: Key.K });
        Check("the gamepad keeps its binding",
              InputMap.ActionGetEvents(FlightInput.ThrottleUp).Count == 1 && InputMap.ActionGetEvents(FlightInput.ThrottleUp)[0] is InputEventJoypadMotion);

        // Changes reach the settings, Back saves them
        tabs.CurrentTab = 2;
        await Frames(10);
        bool invert = s.InvertMouseY;
        settings.GetNode<CheckButton>("%InvertY").ButtonPressed = !invert;
        settings.GetNode<HSlider>("%MouseSensitivity").Value = 1.5;
        Check("changes reach the settings at once", s.InvertMouseY == !invert && Mathf.IsEqualApprox(s.MouseSensitivity, 1.5f));
        settings.GetNode<Button>("%Back").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var file = new ConfigFile();
        Check("Back saves them and goes back to the menu",
              file.Load(GameSettings.Path) == Error.Ok && file.GetValue("controls", "invert_mouse_y").AsBool() == !invert
              && file.GetValue("bindings", FlightInput.ThrottleDown).AsString() == $"key:{(long)Key.K}"
              && !settings.Visible && menu.GetNode<Control>("%Menu").Visible);

        // Defaults
        menu.GetNode<Button>("%SettingsButton").EmitSignal(BaseButton.SignalName.Pressed);
        settings.GetNode<Button>("%Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        Check("Defaults brings the default keys back",
              GameSettings.Binding(FlightInput.ThrottleUp) is InputEventKey { PhysicalKeycode: Key.W }
              && GameSettings.Binding(FlightInput.ThrottleDown) is InputEventKey { PhysicalKeycode: Key.S }
              && !s.InvertMouseY && Mathf.IsEqualApprox(s.MouseSensitivity, 1));

        // The player's own settings back
        if (saved != null)
            FileAccess.Open(GameSettings.Path, FileAccess.ModeFlags.Write).StoreBuffer(saved);
        else
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(GameSettings.Path));
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Rebind(GridContainer bindings, string action, Key key)
    {
        string name = ControlSets.All.First(a => a.Action == action).Name;
        int row = 0;
        while (bindings.GetChild(2 * row) is not Label { Text: var text } || text != name)
            row++;
        bindings.GetChild<Button>(2 * row + 1).EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(10);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        await Frames(2);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
