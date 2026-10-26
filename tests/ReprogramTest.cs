using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The pilot reprograms the fire control in flight, and the turret hits more:
///
///   - the stock FIRECTL against a drone jinking at 2 g, at 500 m, 1 km and
///     2 km, many bursts each (one burst's hit rate goes from a few percent
///     to most of it);
///   - the pilot's change to FIRECTL.ASM (tests/fixtures/firectl_abg.edit:
///     an alpha-beta-gamma filter, aiming where the target will be if it
///     keeps turning as it does) is on the diskette as ABG.ASM; ASM in COS
///     assembles it while the stock program flies on, FIRECTL OFF takes the
///     stock one out of the chain and ABG goes in its place;
///   - the same bursts again: more hits close in, no fewer far out.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/reprogram_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ReprogramTest : Node3D
{
    private ShipBody _ship;
    private RadarDevice _radar;
    private TurretDevice _turret;
    private DisplayDevice _display;
    private Drone _drone;
    private PilotControls _controls;
    private int _failures;
    private const int Trials = 8;
    private static readonly float[] Ranges = { 500, 1000, 2000 };

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = new ShipBody();
        var computer = new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false };
        _ship.AddChild(computer);
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _display = new DisplayDevice { Screens = DisplayScreens() };
        _ship.AddChild(_display);
        _turret = new TurretDevice { Name = "Turret", Position = new Vector3(0.9f, 0.8f, -3f) };
        _ship.AddChild(_turret);
        _radar = new RadarDevice { Name = "Radar", Position = new Vector3(0, 0.5f, -5f), TrackOriginPath = "../Turret" };
        _ship.AddChild(_radar);
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);
        _drone = new Drone { Straight = true, Speed = 120, Health = 1e9f, Position = new Vector3(0, 0, -3000) };
        AddChild(_drone);

        // The pilot's edit of the stock source, saved as ABG.ASM
        byte[] disk = computer.DiskImage();
        string stock = Encoding.ASCII.GetString(FatDisk.Read(disk, "FIRECTL.ASM"));
        string edited = Edit(stock, Godot.FileAccess.GetFileAsString("res://tests/fixtures/firectl_abg.edit"));
        Check("the edit applies to the diskette's FIRECTL.ASM", edited != null);
        FatDisk.Write(disk, "ABG.ASM", Encoding.ASCII.GetBytes(edited ?? ""));
        computer.InsertDisk(disk);
        computer.PowerOn();
        Check("the stock fire control is running", await Until(() => _display.Text("WEAPON").Contains("FIRECTL"), 40));

        GD.Print("     stock FIRECTL");
        float[] before = await Rates();

        await Command("ASM ABG.ASM");
        Check("ASM assembles the edited program in flight",
              await Until(() => FatDisk.Read(computer.DiskImage(), "ABG.COM") != null && !computer.DriveLight
                                && computer.ScreenText().TrimEnd().EndsWith("A:\\>"), 180));
        await Command("FIRECTL OFF");
        Check("the stock one comes out of the chain", await Until(() => computer.ScreenText().Contains("Fire control removed"), 10));
        await Command("ABG");
        Check("the new one goes in", await Until(() => computer.ScreenText().Contains("Fire control resident"), 10));
        await Seconds(1);

        GD.Print("     ABG (alpha-beta-gamma close in)");
        float[] after = await Rates();

        GD.Print("     range   stock    ABG");
        for (int i = 0; i < Ranges.Length; i++)
            GD.Print($"     {Ranges[i],5:0} m  {before[i],6:P0}  {after[i],6:P0}");
        float closeBefore = (before[0] + before[1]) / 2, closeAfter = (after[0] + after[1]) / 2;
        Check($"close in, the new program hits more ({closeBefore:P0} -> {closeAfter:P0} at 500 m and 1 km)",
              closeAfter >= closeBefore * 1.3f && closeAfter - closeBefore >= 0.05f);
        Check($"and no worse far out ({before[2]:P0} -> {after[2]:P0} at 2 km)", after[2] >= before[2] - 0.03f);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>The edit's "replace" blocks applied to the source, each found once; null if one is not.</summary>
    private static string Edit(string source, string edit)
    {
        foreach (Match m in Regex.Matches(edit.Replace("\r\n", "\n"), @"@@@ replace\n(.*?)@@@ with\n(.*?)@@@ end", RegexOptions.Singleline))
        {
            string old = m.Groups[1].Value;
            int at = source.IndexOf(old, StringComparison.Ordinal);
            if (at < 0 || source.IndexOf(old, at + 1, StringComparison.Ordinal) >= 0)
            {
                GD.Print($"     not found once: {old.Split('\n')[0]}");
                return null;
            }
            source = source[..at] + m.Groups[2].Value + source[(at + old.Length)..];
        }
        return source;
    }

    /// <summary>The average hit rate at each range, the drone jinking at 2 g.</summary>
    private async Task<float[]> Rates()
    {
        var rates = new float[Ranges.Length];
        for (int i = 0; i < Ranges.Length; i++)
        {
            float sum = 0;
            for (int trial = 0; trial < Trials; trial++)
                sum += await Trial(Ranges[i]);
            rates[i] = sum / Trials;
            GD.Print($"     {Ranges[i],5:0} m  average of {Trials}   {rates[i],6:P0}");
        }
        return rates;
    }

    /// <summary>As accuracy_test's: the drone crossing at a range; lock it, let the filter settle, fire 3 s, count the hits.</summary>
    private async Task<float> Trial(float range)
    {
        _drone.Evades = true;
        _drone.GlobalPosition = _ship.GlobalPosition + new Vector3(-0.1f * range, 0.35f * range, -range);
        _drone.LookAt(_drone.GlobalPosition + Vector3.Right, Vector3.Up);
        _drone.Fly(Vector3.Right * _drone.Speed);
        await Seconds(1.2);
        await Lock();
        await Seconds(1);
        int rounds0 = _turret.RoundsFired, hits0 = _drone.Hits;
        _controls.Trigger = true;
        await Seconds(3);
        _controls.Trigger = false;
        int rounds = _turret.RoundsFired - rounds0;
        await Seconds(range / _turret.MuzzleSpeed + 0.5);
        int hits = _drone.Hits - hits0;
        float rate = rounds == 0 ? 0 : (float)hits / rounds;
        GD.Print($"     {range,5:0} m  {hits,4} / {rounds,-4}  {rate,6:P0}");
        await Seconds(1);
        return rate;
    }

    private async Task Lock()
    {
        if (_radar.Locked != null)
        {
            _controls.TargetAhead = true;
            await Seconds(0.1);
            _controls.TargetAhead = false;
            await Seconds(0.1);
        }
        _controls.TargetAhead = true;
        await Seconds(0.1);
        _controls.TargetAhead = false;
        await Seconds(0.2);
    }

    private async Task Command(string line)
    {
        foreach (char c in line + "\n")
        {
            string code = c switch
            {
                '\n' => "Enter",
                ' ' => "Space",
                '.' => "Period",
                >= 'A' and <= 'Z' => "Key" + c,
                _ => throw new ArgumentException($"no key for '{c}'"),
            };
            _ship.Computer.Key(code, true);
            await Seconds(0.05);
            _ship.Computer.Key(code, false);
            await Seconds(0.05);
        }
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        GD.Print(_ship.Computer.ScreenText());
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }

    private static Godot.Collections.Array<DisplayScreen> DisplayScreens() => new()
    {
        GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/radar.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/weapon.tres"),
    };
}
