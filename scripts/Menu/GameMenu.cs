using System;
using Godot;
using Xpace.Computer;
using Xpace.Net;

namespace Xpace.Menu;

/// <summary>
/// The menu Esc opens while playing (scenes/menu/game_menu.tscn): resume, settings, a backup of the ship's diskette
/// for the insurer (the new ship after a loss boots it), back to the main menu, quit.
/// Playing alone it pauses the game; in a session it can't (the others play on), and the ship flies on with nobody
/// at the controls but the keys let go. Esc again closes it (or the settings, if open).
/// </summary>
public partial class GameMenu : CanvasLayer
{
    [Export(PropertyHint.File, "*.tscn")] public string MainMenuScene = "res://scenes/menu/main_menu.tscn";

    public static bool IsOpen => _current?.Visible == true;

    /// <summary>The menu opened (the game may be paused: what shows over it should hide now).</summary>
    public static event Action Opened;

    /// <summary>The menu closed: the game takes the mouse back.</summary>
    public static event Action Closed;

    private static GameMenu _current;
    private Control _menu;
    private SettingsMenu _settings;
    private Label _note;

    public override void _Ready()
    {
        _current = this;
        ProcessMode = ProcessModeEnum.Always;
        _menu = GetNode<Control>("%Menu");
        _settings = GetNode<SettingsMenu>("%Settings");
        _note = GetNode<Label>("%Note");
        GetNode<Button>("%Resume").Pressed += Close;
        GetNode<Button>("%SettingsButton").Pressed += () =>
        {
            _menu.Hide();
            _settings.Open();
        };
        _settings.Closed += () =>
        {
            _menu.Show();
            GetNode<Button>("%SettingsButton").GrabFocus();
        };
        GetNode<Button>("%Backup").Pressed += SendBackup;
        GetNode<Button>("%MainMenu").Pressed += ToMainMenu;
        GetNode<Button>("%Quit").Pressed += () => GetTree().Quit();
        Hide();
    }

    public override void _ExitTree()
    {
        if (_current == this)
            _current = null;
        if (GetTree().Paused)
            GetTree().Paused = false;
    }

    /// <summary>Opens the menu (if the scene has one).</summary>
    public static void Open() => _current?.Show(true);

    private void Show(bool open)
    {
        if (open)
        {
            bool alone = SessionNode.Instance?.Active != true;
            GetTree().Paused = alone;
            _note.Text = alone ? "Paused" : "Online: the game goes on";
            GetNode<Button>("%Backup").Visible = ShipComputer.Kept?.BackupDiskPath is { Length: > 0 };
            _menu.Show();
            _settings.Hide();
            Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetNode<Button>("%Resume").GrabFocus();
            Opened?.Invoke();
        }
        else
        {
            Visible = false;
            GetTree().Paused = false;
            Closed?.Invoke();
        }
    }

    private void Close() => Show(false);

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            return;
        GetViewport().SetInputAsHandled();
        if (_settings.Visible)
            _settings.Back();
        else
            Close();
    }

    private void SendBackup()
    {
        bool sent = ShipComputer.Kept?.SendBackup() == true;
        _note.Text = sent ? "Backup sent: the insurer's next ship boots it" : "The backup could not be sent";
    }

    private void ToMainMenu()
    {
        GetTree().Paused = false;
        OnlinePlay.Current?.Leave();
        GetTree().ChangeSceneToFile(MainMenuScene);
    }
}
