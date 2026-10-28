using System;
using System.Linq;
using Godot;
using Xpace.Cockpit;

namespace Xpace.Menu;

/// <summary>How the window shows the game.</summary>
public enum WindowKind
{
    Windowed,
    Borderless,         // a window the size of the screen, without borders
    Fullscreen,         // the screen to the game alone
}

public enum Antialiasing
{
    Off,
    Fxaa,
    Msaa2,
    Msaa4,
    Msaa8,
    Taa,
}

/// <summary>How much of the card an effect may take: off, or better looking and dearer.</summary>
public enum Quality
{
    Off,
    Low,
    Medium,
    High,
}

/// <summary>
/// The player's settings: video (resolution, window, vertical sync, antialiasing, the dearer effects) and controls (mouse, key bindings),
/// kept in user://settings.cfg. An autoload: it registers the flight actions, loads the settings and applies them when
/// the game starts; the settings menu changes them, applies and saves.
///
/// In windowed mode the resolution is the window's size; in the full-screen modes the window covers the screen and
/// the resolution is the one the 3D view is drawn at, scaled to the screen.
/// </summary>
public partial class GameSettings : Node
{
    public const string Path = "user://settings.cfg";

    /// <summary>
    /// The bindings' version: settings saved by an older one keep their keys
    /// except for the actions whose default moved since (version 2: the
    /// arrows went from pitch and roll to the power distributor).
    /// </summary>
    private const int BindingsVersion = 2;
    private static readonly string[] MovedInVersion2 = { FlightInput.PitchUp, FlightInput.PitchDown, FlightInput.RollLeft, FlightInput.RollRight };

    /// <summary>Resolutions offered (those that fit the screen, and the screen's own).</summary>
    public static readonly Vector2I[] Resolutions =
    {
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3440, 1440), new(3840, 2160),
    };

    public static GameSettings Current { get; private set; }

    private static bool Headless => DisplayServer.GetName() == "headless";

    /// <summary>The screen's size (a common one with no screen, in tests).</summary>
    public static Vector2I Screen => Headless ? new Vector2I(1920, 1080) : DisplayServer.ScreenGetSize();

    public Vector2I Resolution = new(1600, 900);
    public WindowKind Window = WindowKind.Windowed;
    public bool VSync = true;
    public Antialiasing Antialiasing = Antialiasing.Msaa4;

    /// <summary>The effects a card may struggle with (a scene's <see cref="QualityEnvironment"/> follows them).</summary>
    public Quality Shadows = Quality.High, AmbientOcclusion = Quality.Medium, IndirectLight = Quality.Medium;
    public bool Reflections = true, Glow = true;

    /// <summary>The name other players see.</summary>
    public string PlayerName = "Pilot";

    public bool InvertMouseY;

    /// <summary>The mouse's speed as a stick, and turning the head: 1 as the cockpit is made.</summary>
    public float MouseSensitivity = 1, LookSensitivity = 1;

    /// <summary>The radio's voices (the cockpit radio, on the "Voices" bus), 0 to 1.</summary>
    public float VoiceVolume = 0.6f;

    /// <summary>The audio bus the radio's voices play on.</summary>
    public const string VoicesBus = "Voices";

    /// <summary>Look for a newer release when the game starts.</summary>
    public bool CheckForUpdates = true;

    /// <summary>A release the player chose to skip (not offered again; a later one is).</summary>
    public string SkippedVersion = "";

    /// <summary>The settings changed (the menu applied them).</summary>
    public event Action Changed;

    public override void _Ready()
    {
        Current = this;
        ControlSets.Register();
        Load();
        Apply();
    }

    /// <summary>Puts the settings to work.</summary>
    public void Apply()
    {
        int voices = AudioServer.GetBusIndex(VoicesBus);
        if (voices >= 0)
            AudioServer.SetBusVolumeDb(voices, Mathf.LinearToDb(Mathf.Clamp(VoiceVolume, 0, 1)));
        if (!Headless)
        {
            Vector2I screen = Screen;
            switch (Window)
            {
                case WindowKind.Windowed:
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                    DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                    Vector2I size = new(Math.Min(Resolution.X, screen.X), Math.Min(Resolution.Y, screen.Y));
                    DisplayServer.WindowSetSize(size);
                    DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition() + (screen - size) / 2);
                    break;
                case WindowKind.Borderless:
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                    break;
                case WindowKind.Fullscreen:
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                    break;
            }
            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
            Viewport root = GetTree().Root;
            root.Scaling3DScale = Window == WindowKind.Windowed ? 1 : Mathf.Clamp((float)Resolution.Y / screen.Y, 0.25f, 2f);
            root.Msaa3D = Antialiasing switch
            {
                Antialiasing.Msaa2 => Viewport.Msaa.Msaa2X,
                Antialiasing.Msaa4 => Viewport.Msaa.Msaa4X,
                Antialiasing.Msaa8 => Viewport.Msaa.Msaa8X,
                _ => Viewport.Msaa.Disabled,
            };
            root.ScreenSpaceAA = Antialiasing == Antialiasing.Fxaa ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
            root.UseTaa = Antialiasing == Antialiasing.Taa;
            ApplyEffects();
        }
        Changed?.Invoke();
    }

    /// <summary>The effects' quality, for every scene (turning them on and off is each scene's QualityEnvironment).</summary>
    private void ApplyEffects()
    {
        RenderingServer.DirectionalShadowAtlasSetSize(Shadows switch { Quality.High => 8192, Quality.Medium => 4096, _ => 2048 }, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(Shadows switch
        {
            Quality.High => RenderingServer.ShadowQuality.SoftMedium,
            Quality.Medium => RenderingServer.ShadowQuality.SoftLow,
            _ => RenderingServer.ShadowQuality.Hard,
        });
        // Ships and stations are tens to hundreds of metres: the occlusion fades out far beyond Godot's 300 m.
        RenderingServer.EnvironmentSetSsaoQuality(AmbientOcclusion switch
        {
            Quality.High => RenderingServer.EnvironmentSsaoQuality.High,
            Quality.Medium => RenderingServer.EnvironmentSsaoQuality.Medium,
            _ => RenderingServer.EnvironmentSsaoQuality.Low,
        }, AmbientOcclusion != Quality.High, 0.5f, 2, 400, 2500);
        RenderingServer.EnvironmentSetSsilQuality(IndirectLight switch
        {
            Quality.High => RenderingServer.EnvironmentSsilQuality.High,
            Quality.Medium => RenderingServer.EnvironmentSsilQuality.Medium,
            _ => RenderingServer.EnvironmentSsilQuality.Low,
        }, IndirectLight != Quality.High, 0.5f, 4, 400, 2500);
    }

    /// <summary>The flight actions back to their default keys.</summary>
    public static void DefaultBindings()
    {
        foreach (var (action, _) in ControlSets.All)
            if (InputMap.HasAction(action))
                InputMap.EraseAction(action);
        ControlSets.Register();
    }

    /// <summary>The key or mouse button bound to <paramref name="action"/> (the first that is not a game controller's).</summary>
    public static InputEvent Binding(string action) =>
        InputMap.HasAction(action) ? InputMap.ActionGetEvents(action).FirstOrDefault(e => e is InputEventKey or InputEventMouseButton) : null;

    /// <summary>Binds <paramref name="action"/> to <paramref name="e"/> instead of its key or mouse button (controllers keep theirs).</summary>
    public static void Bind(string action, InputEvent e)
    {
        if (Binding(action) is InputEvent old)
            InputMap.ActionEraseEvent(action, old);
        InputMap.ActionAddEvent(action, e);
    }

    /// <summary>A binding's name, as the menu shows it.</summary>
    public static string Describe(InputEvent e) => e switch
    {
        InputEventKey key => OS.GetKeycodeString(Headless ? key.PhysicalKeycode : DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode)),
        InputEventMouseButton { ButtonIndex: MouseButton.Left } => "Left mouse",
        InputEventMouseButton { ButtonIndex: MouseButton.Right } => "Right mouse",
        InputEventMouseButton { ButtonIndex: MouseButton.Middle } => "Middle mouse",
        InputEventMouseButton button => $"Mouse {(int)button.ButtonIndex}",
        _ => "-",
    };

    public void Save()
    {
        var file = new ConfigFile();
        file.SetValue("player", "name", PlayerName);
        file.SetValue("video", "resolution", Resolution);
        file.SetValue("video", "window", Window.ToString());
        file.SetValue("video", "vsync", VSync);
        file.SetValue("video", "antialiasing", Antialiasing.ToString());
        file.SetValue("video", "shadows", Shadows.ToString());
        file.SetValue("video", "ambient_occlusion", AmbientOcclusion.ToString());
        file.SetValue("video", "indirect_light", IndirectLight.ToString());
        file.SetValue("video", "reflections", Reflections);
        file.SetValue("video", "glow", Glow);
        file.SetValue("controls", "invert_mouse_y", InvertMouseY);
        file.SetValue("controls", "mouse_sensitivity", MouseSensitivity);
        file.SetValue("controls", "look_sensitivity", LookSensitivity);
        file.SetValue("audio", "voices", VoiceVolume);
        file.SetValue("updates", "check", CheckForUpdates);
        file.SetValue("updates", "skipped", SkippedVersion);
        file.SetValue("bindings", "version", BindingsVersion);
        foreach (var (action, _) in ControlSets.All)
            file.SetValue("bindings", action, Binding(action) switch
            {
                InputEventKey key => $"key:{(long)key.PhysicalKeycode}",
                InputEventMouseButton button => $"mouse:{(long)button.ButtonIndex}",
                _ => "",
            });
        file.Save(Path);
    }

    private void Load()
    {
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok)
            return;
        PlayerName = file.GetValue("player", "name", PlayerName).AsString();
        Resolution = file.GetValue("video", "resolution", Resolution).AsVector2I();
        Enum.TryParse(file.GetValue("video", "window", Window.ToString()).AsString(), out Window);
        VSync = file.GetValue("video", "vsync", VSync).AsBool();
        Enum.TryParse(file.GetValue("video", "antialiasing", Antialiasing.ToString()).AsString(), out Antialiasing);
        Enum.TryParse(file.GetValue("video", "shadows", Shadows.ToString()).AsString(), out Shadows);
        Enum.TryParse(file.GetValue("video", "ambient_occlusion", AmbientOcclusion.ToString()).AsString(), out AmbientOcclusion);
        Enum.TryParse(file.GetValue("video", "indirect_light", IndirectLight.ToString()).AsString(), out IndirectLight);
        Reflections = file.GetValue("video", "reflections", Reflections).AsBool();
        Glow = file.GetValue("video", "glow", Glow).AsBool();
        InvertMouseY = file.GetValue("controls", "invert_mouse_y", InvertMouseY).AsBool();
        MouseSensitivity = file.GetValue("controls", "mouse_sensitivity", MouseSensitivity).AsSingle();
        LookSensitivity = file.GetValue("controls", "look_sensitivity", LookSensitivity).AsSingle();
        VoiceVolume = file.GetValue("audio", "voices", VoiceVolume).AsSingle();
        CheckForUpdates = file.GetValue("updates", "check", CheckForUpdates).AsBool();
        SkippedVersion = file.GetValue("updates", "skipped", SkippedVersion).AsString();
        int version = file.GetValue("bindings", "version", 1).AsInt32();
        foreach (var (action, _) in ControlSets.All)
        {
            if (version < 2 && MovedInVersion2.Contains(action))
                continue;
            string[] saved = file.GetValue("bindings", action, "").AsString().Split(':');
            if (saved.Length != 2 || !long.TryParse(saved[1], out long code))
                continue;
            if (saved[0] == "key")
                Bind(action, new InputEventKey { PhysicalKeycode = (Key)code });
            else if (saved[0] == "mouse")
                Bind(action, new InputEventMouseButton { ButtonIndex = (MouseButton)code });
        }
    }
}
