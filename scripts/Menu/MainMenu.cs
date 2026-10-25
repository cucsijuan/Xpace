using System;
using System.Linq;
using Godot;
using Xpace.Net;

namespace Xpace.Menu;

/// <summary>
/// The game's first screen: single player, multiplayer (online: the first game with room, or a new one hosted
/// here), settings and quit, the view going slowly round a ship (the Pivot node). Started with network arguments
/// (--host, --join, --online), it goes straight to the game, where <see cref="NetLauncher"/> reads them.
/// </summary>
public partial class MainMenu : Node3D
{
    [Export(PropertyHint.File, "*.tscn")] public string GameScene = "res://scenes/main.tscn";

    /// <summary>How fast the view goes round, degrees a second.</summary>
    [Export] public float TurnRate = 1.5f;

    /// <summary>The name other players see.</summary>
    public static string PlayerName =>
        OS.GetEnvironment("XPACE_NAME") is { Length: > 0 } n ? n : GameSettings.Current?.PlayerName is { Length: > 0 } p ? p : "Pilot";

    private Node3D _pivot;
    private Control _menu;
    private Label _status;
    private SettingsMenu _settings;
    private Button[] _buttons;

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Any(a => a.Split('=')[0] is "--host" or "--join" or "--online"))
        {
            CallDeferred(MethodName.Play);
            return;
        }
        _pivot = GetNodeOrNull<Node3D>("Pivot");
        _menu = GetNode<Control>("%Menu");
        _status = GetNode<Label>("%Status");
        _settings = GetNode<SettingsMenu>("%Settings");
        var single = GetNode<Button>("%SinglePlayer");
        var multi = GetNode<Button>("%Multiplayer");
        var settings = GetNode<Button>("%SettingsButton");
        var quit = GetNode<Button>("%Quit");
        _buttons = new[] { single, multi, settings, quit };
        single.Pressed += Play;
        multi.Pressed += PlayOnline;
        settings.Pressed += () =>
        {
            _menu.Hide();
            _settings.Open();
        };
        _settings.Closed += () =>
        {
            _menu.Show();
            settings.GrabFocus();
        };
        quit.Pressed += () => GetTree().Quit();
        if (!OnlinePlay.Available)
        {
            multi.Disabled = true;
            multi.TooltipText = "This build has no online play";
        }
        _status.Text = "";
        Input.MouseMode = Input.MouseModeEnum.Visible;
        single.GrabFocus();
    }

    public override void _Process(double delta)
    {
        _pivot?.RotateY(Mathf.DegToRad(TurnRate) * (float)delta);
    }

    private void Play() => GetTree().ChangeSceneToFile(GameScene);

    private async void PlayOnline()
    {
        foreach (Button b in _buttons)
            b.Disabled = true;
        try
        {
            await OnlinePlay.Current.FindOrHostAsync(PlayerName, OS.GetCmdlineUserArgs().Contains("--eos-new-device"),
                what => _status.Text = what + "...");
            Play();
        }
        catch (Exception e)
        {
            _status.Text = $"Couldn't play online: {e.Message}";
            foreach (Button b in _buttons)
                b.Disabled = false;
            _buttons[1].Disabled = !OnlinePlay.Available;
        }
    }
}
