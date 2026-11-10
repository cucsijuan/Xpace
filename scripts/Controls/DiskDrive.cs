using System;
using System.Linq;
using Godot;
using Xpace.Cockpit;
using Xpace.Computer;
using Xpace.World;

namespace Xpace.Controls;

/// <summary>
/// A diskette drive of the ship's computer (scenes/controls/drives/disk_drive.tscn), as a screen is one of its monitors:
/// put anywhere under the ship, it is drive <see cref="Unit"/> (0 A:, 1 B:) of the ship's computer. Its light is the
/// drive's motor; <see cref="Part"/> is the diskette, shown while one is in.
///
/// A: holds the ship's own diskette, for good. B: is the pilot's: clicked (or the Diskette key), it shows the
/// diskettes the pilot carries (<see cref="PilotRecord.Items"/>); one put in goes into the drive (its image:
/// disks/&lt;Data&gt;.img, the insurer's backup the factory's diskette), Eject takes it out. A program on it is copied
/// onto the ship's own with COS (COPY B:SPOOF.COM A:), and the ship has it from then on.
/// </summary>
public partial class DiskDrive : InteractiveControl
{
    /// <summary>0: drive A:, the ship's own diskette; 1: drive B:, the pilot's.</summary>
    [Export(PropertyHint.Range, "0,1")] public int Unit = 1;
    /// <summary>The drive's light: lit while its motor runs.</summary>
    [Export] public Light3D Light;

    /// <summary>The name of the diskette in it (empty: none).</summary>
    public string Inside { get; private set; } = "";

    public ShipComputer Computer => (Host as Xpace.Ship.ShipBody)?.Computer;

    private CanvasLayer _panel;

    public override void _Ready()
    {
        base._Ready();
        if (Unit == 0)
            Inside = "Ship";
        ShowDisk();
    }

    public override void _Process(double delta)
    {
        if (Light != null)
            Light.Visible = Computer != null && (Unit == 0 ? Computer.DriveLight : Computer.DriveBLight);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Unit != 1 || !e.IsActionPressed(FlightInput.Diskette) || Computer == null || Xpace.Net.NetBody.IsPuppet(Host))
            return;
        GetViewport().SetInputAsHandled();
        Press();
    }

    public override void Press()
    {
        if (Unit != 1 || Computer == null)
            return;
        if (_panel != null)
            Close();
        else
            Open();
    }

    /// <summary>Puts a diskette of the pilot's in the drive (null: takes out the one there). Drive B: only.</summary>
    public bool Insert(ItemRecord item)
    {
        if (Unit != 1 || Computer == null)
            return false;
        if (item == null)
        {
            Inside = "";
            ShowDisk();
            return Computer.InsertDiskB(Array.Empty<byte>());
        }
        string path = $"res://disks/{(item.Data == "factory" ? "cos" : item.Data)}.img";
        byte[] image = FileAccess.FileExists(path) ? FileAccess.GetFileAsBytes(path) : Array.Empty<byte>();
        if (image.Length == 0 || !Computer.InsertDiskB(image))
            return false;
        Inside = item.Name;
        ShowDisk();
        return true;
    }

    private void ShowDisk()
    {
        if (Part != null)
            Part.Visible = Inside != "";
    }

    private void Open()
    {
        _panel = new CanvasLayer { Layer = 5 };
        var root = new PanelContainer { Theme = GD.Load<Theme>("res://assets/ui/menu_theme.tres"), Position = new Vector2(40, 200) };
        var box = new VBoxContainer();
        root.AddChild(box);
        box.AddChild(new Label { Text = Inside == "" ? "DRIVE B:  empty" : $"DRIVE B:  {Inside}" });
        var diskettes = Universe.Current?.Me?.Items.Where(i => i.Kind == ItemRecord.Diskette).ToList() ?? new();
        if (diskettes.Count == 0)
            box.AddChild(new Label { Text = "You carry no diskettes.", Modulate = new Color(1, 1, 1, 0.6f) });
        foreach (ItemRecord item in diskettes)
        {
            var b = new Button { Text = $"Put in: {item.Name}", FocusMode = Control.FocusModeEnum.None };
            b.Pressed += () => { Insert(item); Close(); };
            box.AddChild(b);
        }
        var eject = new Button { Text = "Eject", Disabled = Inside == "", FocusMode = Control.FocusModeEnum.None };
        eject.Pressed += () => { Insert(null); Close(); };
        box.AddChild(eject);
        var close = new Button { Text = "Close", FocusMode = Control.FocusModeEnum.None };
        close.Pressed += Close;
        box.AddChild(close);
        _panel.AddChild(root);
        AddChild(_panel);
    }

    private void Close()
    {
        _panel?.QueueFree();
        _panel = null;
    }
}
