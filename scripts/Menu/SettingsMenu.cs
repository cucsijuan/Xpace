using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Cockpit;

namespace Xpace.Menu;

/// <summary>
/// The settings screen (scenes/menu/settings_menu.tscn): general (the pilot's name, the update check), video, controls, audio (the radio's voices), with the key
/// bindings. Every change is applied at once; Back saves them. A binding's button waits for the next key or mouse
/// button (Esc leaves it as it was); a key already bound to another action is taken from it.
/// </summary>
public partial class SettingsMenu : Control
{
    private static readonly (WindowKind Kind, string Name)[] Windows =
    {
        (WindowKind.Windowed, "Windowed"), (WindowKind.Borderless, "Borderless"), (WindowKind.Fullscreen, "Fullscreen"),
    };

    private static readonly (Antialiasing Kind, string Name)[] Smoothing =
    {
        (Antialiasing.Off, "Off"), (Antialiasing.Fxaa, "FXAA"), (Antialiasing.Msaa2, "MSAA 2x"), (Antialiasing.Msaa4, "MSAA 4x"),
        (Antialiasing.Msaa8, "MSAA 8x"), (Antialiasing.Taa, "TAA"),
    };

    private static readonly (Quality Kind, string Name)[] Qualities =
    {
        (Quality.Off, "Off"), (Quality.Low, "Low"), (Quality.Medium, "Medium"), (Quality.High, "High"),
    };

    /// <summary>Back was pressed (the settings are saved).</summary>
    public event Action Closed;

    private GameSettings _s;
    private LineEdit _name;
    private OptionButton _resolution, _window, _antialiasing, _shadows, _occlusion, _indirect;
    private CheckButton _vsync, _invertY, _checkUpdates, _reflections, _glow;
    private HSlider _mouse, _look, _voices;
    private GridContainer _bindings;
    private readonly Dictionary<string, Button> _bindButtons = new();
    private List<Vector2I> _resolutions;
    private string _rebinding;
    private bool _filling;

    public override void _Ready()
    {
        _name = GetNode<LineEdit>("%PlayerName");
        _checkUpdates = GetNode<CheckButton>("%CheckUpdates");
        _resolution = GetNode<OptionButton>("%Resolution");
        _window = GetNode<OptionButton>("%Window");
        _vsync = GetNode<CheckButton>("%VSync");
        _antialiasing = GetNode<OptionButton>("%Antialiasing");
        _shadows = GetNode<OptionButton>("%Shadows");
        _occlusion = GetNode<OptionButton>("%AmbientOcclusion");
        _indirect = GetNode<OptionButton>("%IndirectLight");
        _reflections = GetNode<CheckButton>("%Reflections");
        _glow = GetNode<CheckButton>("%Glow");
        _invertY = GetNode<CheckButton>("%InvertY");
        _mouse = GetNode<HSlider>("%MouseSensitivity");
        _look = GetNode<HSlider>("%LookSensitivity");
        _voices = GetNode<HSlider>("%VoiceVolume");
        _bindings = GetNode<GridContainer>("%Bindings");

        foreach (var (_, name) in Windows)
            _window.AddItem(name);
        foreach (var (_, name) in Smoothing)
            _antialiasing.AddItem(name);
        foreach (var (_, name) in Qualities)
        {
            _shadows.AddItem(name);
            _occlusion.AddItem(name);
            _indirect.AddItem(name);
        }
        foreach (var (set, actions) in ControlSets.Sets)
        {
            _bindings.AddChild(new Label { Text = set.ToUpperInvariant(), Modulate = new Color(1, 1, 1, 0.6f) });
            _bindings.AddChild(new Control());
            foreach (var (action, name) in actions)
                AddBinding(action, name);
        }


        _name.TextChanged += text => Change(s => s.PlayerName = text.Trim().Length > 0 ? text.Trim() : "Pilot", apply: false);
        _checkUpdates.Toggled += on => Change(s => s.CheckForUpdates = on, apply: false);
        _resolution.ItemSelected += i => Change(s => s.Resolution = _resolutions[(int)i]);
        _window.ItemSelected += i => Change(s => s.Window = Windows[(int)i].Kind);
        _vsync.Toggled += on => Change(s => s.VSync = on);
        _antialiasing.ItemSelected += i => Change(s => s.Antialiasing = Smoothing[(int)i].Kind);
        _shadows.ItemSelected += i => Change(s => s.Shadows = Qualities[(int)i].Kind);
        _occlusion.ItemSelected += i => Change(s => s.AmbientOcclusion = Qualities[(int)i].Kind);
        _indirect.ItemSelected += i => Change(s => s.IndirectLight = Qualities[(int)i].Kind);
        _reflections.Toggled += on => Change(s => s.Reflections = on);
        _glow.Toggled += on => Change(s => s.Glow = on);
        _invertY.Toggled += on => Change(s => s.InvertMouseY = on);
        _mouse.ValueChanged += v => Change(s => s.MouseSensitivity = (float)v);
        _look.ValueChanged += v => Change(s => s.LookSensitivity = (float)v);
        _voices.ValueChanged += v => Change(s => s.VoiceVolume = (float)v);
        GetNode<Button>("%Defaults").Pressed += Defaults;
        GetNode<Button>("%Back").Pressed += Close;
        Hide();
    }

    private void AddBinding(string action, string name)
    {
        _bindings.AddChild(new Label { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var button = new Button { CustomMinimumSize = new Vector2(200, 0) };
        button.Pressed += () => Rebind(action);
        _bindings.AddChild(button);
        _bindButtons[action] = button;
    }

    /// <summary>Shows the screen with the settings as they are.</summary>
    public void Open()
    {
        _s = GameSettings.Current;
        Fill();
        Show();
        GetNode<Button>("%Back").GrabFocus();
    }

    private void Fill()
    {
        _filling = true;
        _name.Text = _s.PlayerName;
        _checkUpdates.ButtonPressed = _s.CheckForUpdates;
        Vector2I screen = GameSettings.Screen;
        _resolutions = GameSettings.Resolutions.Where(r => r.X <= screen.X && r.Y <= screen.Y).Append(screen).Append(_s.Resolution)
            .Distinct().OrderBy(r => r.X * r.Y).ToList();
        _resolution.Clear();
        foreach (Vector2I r in _resolutions)
            _resolution.AddItem(r == screen ? $"{r.X} x {r.Y} (screen)" : $"{r.X} x {r.Y}");
        _resolution.Select(_resolutions.IndexOf(_s.Resolution));
        _window.Select(Array.FindIndex(Windows, w => w.Kind == _s.Window));
        _vsync.ButtonPressed = _s.VSync;
        _antialiasing.Select(Array.FindIndex(Smoothing, a => a.Kind == _s.Antialiasing));
        _shadows.Select(Array.FindIndex(Qualities, q => q.Kind == _s.Shadows));
        _occlusion.Select(Array.FindIndex(Qualities, q => q.Kind == _s.AmbientOcclusion));
        _indirect.Select(Array.FindIndex(Qualities, q => q.Kind == _s.IndirectLight));
        _reflections.ButtonPressed = _s.Reflections;
        _glow.ButtonPressed = _s.Glow;
        _invertY.ButtonPressed = _s.InvertMouseY;
        _mouse.Value = _s.MouseSensitivity;
        _look.Value = _s.LookSensitivity;
        _voices.Value = _s.VoiceVolume;
        ShowBindings();
        _filling = false;
    }

    private void ShowBindings()
    {
        foreach (var (action, button) in _bindButtons)
            button.Text = action == _rebinding ? "Press a key..." : GameSettings.Describe(GameSettings.Binding(action));
    }

    private void Change(Action<GameSettings> change, bool apply = true)
    {
        if (_filling || _s == null)
            return;
        change(_s);
        if (apply)
            _s.Apply();
    }

    private void Rebind(string action)
    {
        _rebinding = action;
        ShowBindings();
    }

    public override void _Input(InputEvent e)
    {
        if (_rebinding == null || !Visible)
            return;
        InputEvent bound = e switch
        {
            InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape } => null,
            InputEventKey { Pressed: true, Echo: false } key => new InputEventKey { PhysicalKeycode = key.PhysicalKeycode },
            InputEventMouseButton { Pressed: true } button => new InputEventMouseButton { ButtonIndex = button.ButtonIndex },
            _ => e,
        };
        if (bound == e)
            return;                                 // not a press: still waiting
        GetViewport().SetInputAsHandled();
        if (bound != null)
        {
            foreach (var (other, _) in ControlSets.SetOf(_rebinding))    // a key does one thing (in its set)
                if (other != _rebinding && GameSettings.Binding(other) is InputEvent old && Same(old, bound))
                    InputMap.ActionEraseEvent(other, old);
            GameSettings.Bind(_rebinding, bound);
        }
        _rebinding = null;
        ShowBindings();
    }

    private static bool Same(InputEvent a, InputEvent b) => (a, b) switch
    {
        (InputEventKey x, InputEventKey y) => x.PhysicalKeycode == y.PhysicalKeycode,
        (InputEventMouseButton x, InputEventMouseButton y) => x.ButtonIndex == y.ButtonIndex,
        _ => false,
    };

    private void Defaults()
    {
        string name = _s.PlayerName;
        GameSettings.DefaultBindings();
        var fresh = new GameSettings();
        _s.Resolution = fresh.Resolution;
        _s.Window = fresh.Window;
        _s.VSync = fresh.VSync;
        _s.Antialiasing = fresh.Antialiasing;
        _s.Shadows = fresh.Shadows;
        _s.AmbientOcclusion = fresh.AmbientOcclusion;
        _s.IndirectLight = fresh.IndirectLight;
        _s.Reflections = fresh.Reflections;
        _s.Glow = fresh.Glow;
        _s.InvertMouseY = fresh.InvertMouseY;
        _s.MouseSensitivity = fresh.MouseSensitivity;
        _s.LookSensitivity = fresh.LookSensitivity;
        _s.VoiceVolume = fresh.VoiceVolume;
        fresh.Free();
        _s.PlayerName = name;
        _s.Apply();
        Fill();
    }

    /// <summary>As the Back button: saves and closes.</summary>
    public void Back() => Close();

    private void Close()
    {
        _rebinding = null;
        _s?.Save();
        Hide();
        Closed?.Invoke();
    }
}
