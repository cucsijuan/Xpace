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
    private static bool _checkedUpdates;

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
        CheckForUpdates();
    }

    public override void _Process(double delta)
    {
        _pivot?.RotateY(Mathf.DegToRad(TurnRate) * (float)delta);
    }

    /// <summary>Once a run, in an exported build: offers a newer release, unless the player skipped that one.</summary>
    private async void CheckForUpdates()
    {
        if (_checkedUpdates || !GameSettings.Current.CheckForUpdates || !Updater.Checks)
            return;
        _checkedUpdates = true;
        if (await Updater.NewerAsync() is { } release && release.Tag != GameSettings.Current.SkippedVersion && IsInsideTree())
            ShowUpdate(release);
    }

    /// <summary>The update dialog: what's new, then Update (or the download page when it can't update itself), Later or Skip.</summary>
    private void ShowUpdate(ReleaseInfo release)
    {
        var overlay = new Control();
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(shade);
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(centre);
        var panel = new PanelContainer();
        centre.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);
        var title = new Label { Text = $"Xpace {release.Tag} is available", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        box.AddChild(title);
        box.AddChild(new Label { Text = $"You have {Updater.GameVersion}.", Modulate = new Color(1, 1, 1, 0.6f) });
        var notes = new RichTextLabel { Text = PlainNotes(release.Notes), FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(600, 0) };
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(620, 300) };
        scroll.AddChild(notes);
        box.AddChild(scroll);
        var status = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        status.AddThemeColorOverride("font_color", new Color(1, 0.65f, 0.2f));
        box.AddChild(status);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        box.AddChild(buttons);

        void Close()
        {
            overlay.QueueFree();
            _buttons[0].GrabFocus();
        }
        Button Answer(string text, Action action)
        {
            var button = new Button { Text = text, CustomMinimumSize = new Vector2(160, 0) };
            button.Pressed += action;
            buttons.AddChild(button);
            return button;
        }
        Answer("Skip this version", () =>
        {
            GameSettings.Current.SkippedVersion = release.Tag;
            GameSettings.Current.Save();
            Close();
        });
        Answer("Later", Close);
        Button main;
        if (release.ZipUrl == null || !Updater.CanUpdate || !Updater.InstallFolderWritable())
        {
            status.Text = "This copy of the game can't update itself here: download it from the release page.";
            main = Answer("Open download page", () =>
            {
                OS.ShellOpen(release.PageUrl);
                Close();
            });
        }
        else
        {
            main = Answer("Update", async () =>
            {
                foreach (Button b in buttons.GetChildren().OfType<Button>())
                    b.Disabled = true;
                try
                {
                    string zip = await Updater.DownloadAsync(release, p => Callable.From(() => status.Text = $"Downloading... {p:P0}").CallDeferred());
                    status.Text = "Installing... the game starts again by itself.";
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Updater.InstallAndRestart(zip);
                    GetTree().Quit();
                }
                catch (Exception e)
                {
                    GD.PushWarning($"Update failed: {e}");
                    status.Text = $"The update failed: {e.Message}";
                    foreach (Node child in buttons.GetChildren())
                        child.QueueFree();
                    Answer("Open download page", () =>
                    {
                        OS.ShellOpen(release.PageUrl);
                        Close();
                    }).GrabFocus();
                    Answer("Close", Close);
                }
            });
        }
        GetNode("Ui/Root").AddChild(overlay);
        main.GrabFocus();
    }

    /// <summary>Release notes without Markdown marks (headings, bold, code).</summary>
    private static string PlainNotes(string markdown) =>
        string.Join("\n", markdown.Replace("\r", "").Split('\n')
                .Select(line => line.StartsWith('#') ? line.TrimStart('#', ' ').ToUpperInvariant() : line))
            .Replace("**", "").Replace("*", "").Replace("`", "");

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
