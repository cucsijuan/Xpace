using System;
using System.Text.Json;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.OnFoot;

/// <summary>
/// A station's registry terminal, as its kiosk shows it (scenes/ui/registry_window.tscn): the station's records, and a
/// Copy that takes the terminal's time (<see cref="RegistryView.CopySeconds"/>) with the pilot standing at it; closed
/// before, nothing is copied. Requests go to the universe's owner (<see cref="StationRegistry"/>). Esc or Close shuts it.
/// </summary>
public partial class RegistryWindow : CanvasLayer, IKioskWindow
{
    public string Station { get; set; }

    public event Action Closed;

    /// <summary>The terminal as last shown (null until the first answer).</summary>
    public RegistryView View { get; private set; }

    /// <summary>Seconds into the copy (below 0: none going).</summary>
    public double Copying { get; private set; } = -1;

    private Label _title, _status, _message;
    private ProgressBar _bar;
    private Button _copy;

    public override void _Ready()
    {
        Layer = 5;
        var green = new Color(0.35f, 1f, 0.45f);
        var root = new Control { Theme = GD.Load<Theme>("res://assets/ui/menu_theme.tres") };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var shade = new ColorRect { Color = new Color(0, 0.01f, 0.02f, 0.6f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(shade);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(520, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0.04f, 0.02f, 0.95f), BorderColor = green * 0.6f,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2, ContentMarginLeft = 18, ContentMarginRight = 18,
            ContentMarginTop = 14, ContentMarginBottom = 14 });
        center.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        _title = Line(box, green, 20);
        _status = Line(box, green * 0.8f, 15);
        _bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14) };
        box.AddChild(_bar);
        _message = Line(box, Colors.White, 15);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        box.AddChild(buttons);
        _copy = new Button { Text = "Copy registry", FocusMode = Control.FocusModeEnum.None };
        _copy.Pressed += () => Ask(RegistryRequest.Start);
        buttons.AddChild(_copy);
        var close = new Button { Text = "Close", FocusMode = Control.FocusModeEnum.None };
        close.Pressed += Close;
        buttons.AddChild(close);
        _title.Text = $"{Station} REGISTRY";
        _status.Text = "Connecting...";
        if (Universe.Current is Universe universe)
        {
            universe.Changed += Changed;
            TreeExiting += () => universe.Changed -= Changed;
        }
        Ask(RegistryRequest.Look);
    }

    private static Label Line(Container box, Color colour, int size)
    {
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = colour };
        label.AddThemeFontSizeOverride("font_size", size);
        box.AddChild(label);
        return label;
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    public override void _Process(double delta)
    {
        if (Copying < 0 || View == null)
            return;
        Copying += delta;
        _bar.Value = Math.Min(1, Copying / View.CopySeconds);
        _status.Text = $"COPYING RECORDS... {(int)(_bar.Value * 100)}%";
        if (Copying >= View.CopySeconds)
        {
            Copying = -1;
            Ask(RegistryRequest.Finish);
        }
    }

    public void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }

    public void Ask(string what) =>
        Universe.Current?.Ask(StationRegistry.Topic, JsonSerializer.Serialize(new RegistryRequest { Station = Station, Do = what }));

    private void Changed(string topic)
    {
        if (topic != StationRegistry.Topic || Universe.Current?.Document(topic) is not string text)
            return;
        RegistryView view;
        try
        {
            view = JsonSerializer.Deserialize<RegistryView>(text);
        }
        catch (JsonException)
        {
            return;
        }
        if (view?.Station != Station)
            return;
        View = view;
        _title.Text = $"{view.Title} REGISTRY";
        if (view.Copying >= 0 && Copying < 0)
            Copying = view.Copying;
        _status.Text = Copying >= 0 ? "COPYING RECORDS..." : view.Present ? "TERMINAL READY" : "NO SHIP IN THE HANGAR";
        _message.Text = view.Message;
        _copy.Disabled = Copying >= 0 || !view.Present;
        if (Copying < 0)
            _bar.Value = 0;
    }
}
