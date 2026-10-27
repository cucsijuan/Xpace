using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The ship's resident programs on IRQ 5, on a full fighter with no pilot:
///
///   - AUTOEXEC.BAT leaves FLIGHT, FIRECTL, RWR and POWER chained, the last
///     one installed first;
///   - FIRECTL OFF takes it out from the middle of the chain, the others
///     keep running (FLIGHT, at the bottom, still drives the thrusters);
///   - FIRECTL goes back on top, FLIGHT OFF comes out from the bottom;
///   - ASM.COM in COS assembles FIRECTL.ASM to the same bytes as the
///     diskette's FIRECTL.COM, and the new one runs;
///   - the example program of the ship's hardware manual (COOL.ASM, put on
///     the diskette before it boots) assembles, goes on top of the chain,
///     throws a heat sink when the ship runs hot, and comes off again;
///   - after a power cycle (the insurer's new computer) the programs find
///     their screens again.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/chain_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ChainTest : Node3D
{
    private ShipBody _ship;
    private RadarDevice _radar;
    private HeatDevice _heat;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        _ship.GetNode("Pilot").Free();
        var computer = _ship.GetNode<Computer.ShipComputer>("ShipComputer");
        computer.PowerOnAtStart = false;
        AddChild(_ship);
        _radar = _ship.GetNode<RadarDevice>("Radar");
        _heat = _ship.GetNode<HeatDevice>("Heat");
        byte[] disk = computer.DiskImage();
        FatDisk.Write(disk, "COOL.ASM", Godot.FileAccess.GetFileAsBytes("res://tests/fixtures/COOL.ASM"));
        computer.InsertDisk(disk);
        computer.PowerOn();

        Check("AUTOEXEC.BAT ends with POWER resident", await Until(() => Screen().Contains("Power manager resident"), 60));
        await Seconds(1);
        Check($"the chain is POWER, RWR, FIRECTL, FLIGHT ({Chain()})", Chain() == "XPPW11 XPRW15 XPFC12 XPFL16");
        Check("FLIGHT drives the thrusters", !_ship.DirectMode);

        await Command("FIRECTL OFF");
        Check("FIRECTL OFF from the middle of the chain", await Until(() => Screen().Contains("Fire control removed"), 10));
        Check($"the chain is POWER, RWR, FLIGHT ({Chain()})", Chain() == "XPPW11 XPRW15 XPFL16");
        await Seconds(1);
        Check("the radar is off", _radar.Mode == RadarMode.Off);
        Check("FLIGHT still drives the thrusters", !_ship.DirectMode);

        await Command("FIRECTL");
        Check("FIRECTL again", await Until(() => Screen().Contains("Fire control resident"), 10));
        await Seconds(1);
        Check($"it is on top ({Chain()})", Chain() == "XPFC12 XPPW11 XPRW15 XPFL16");
        Check("the radar searches again", _radar.Mode == RadarMode.Search);

        await Command("FLIGHT OFF");
        Check("FLIGHT OFF from the bottom", await Until(() => Screen().Contains("Flight control removed"), 10));
        Check($"the chain is FIRECTL, POWER, RWR ({Chain()})", Chain() == "XPFC12 XPPW11 XPRW15");
        await Seconds(1);
        Check("the thrusters fall back to direct", _ship.DirectMode);
        await Command("FLIGHT");
        Check("FLIGHT again", await Until(() => Screen().Contains("Flight control resident"), 10));
        await Seconds(1);
        Check("and drives the thrusters", !_ship.DirectMode);

        byte[] stock = FatDisk.Read(_ship.Computer.DiskImage(), "FIRECTL.COM");
        double t0 = _ship.Computer.EmulatedSeconds;
        Check("ASM assembles FIRECTL.ASM in flight", await Assemble("FIRECTL.ASM NEW.COM", "NEW.COM"));
        GD.Print($"     ({_ship.Computer.EmulatedSeconds - t0:0.0} s)");
        byte[] built = FatDisk.Read(_ship.Computer.DiskImage(), "NEW.COM");
        Check($"to the same bytes as FIRECTL.COM ({built?.Length} and {stock?.Length})",
              built != null && stock != null && built.AsSpan().SequenceEqual(stock));
        await Command("FIRECTL OFF");
        await Until(() => Screen().Contains("Fire control removed"), 10);
        await Command("NEW");
        Check("and it runs", await Until(() => Chain().StartsWith("XPFC12 "), 10));

        Check("ASM assembles the manual's COOL.ASM", await Assemble("COOL.ASM", "COOL.COM"));
        string before = Chain();
        await Command("COOL");
        Check("COOL goes on top of the chain", await Until(() => Chain() == "XPCOOL " + before, 10));
        int sinks = _heat.SinksLeft;
        await Seconds(1);
        Check($"no heat sink while the ship is cool ({_heat.Level * 100:0}%)", _heat.SinksLeft == sinks);
        _heat.Add(_heat.Megajoules);
        Check("one thrown when it runs hot", await Until(() => _heat.SinksLeft == sinks - 1, 2));
        await Seconds(2);
        Check("only one while it works", _heat.SinksLeft == sinks - 1);
        await Command("COOL OFF");
        Check($"COOL OFF ({Chain()})", await Until(() => Chain() == before, 10));

        var display = _ship.GetNode<DisplayDevice>("Display");
        Check("FIRECTL draws its page on the WEAPON screen", display.Text("WEAPON").Contains("FIRECTL"));
        computer.Rearm();
        Check("the insurer's new computer boots", await Until(() => Screen().Contains("Power manager resident"), 60));
        Check("and FIRECTL finds its screens again (the table is written after the power cycle wiped it)",
              await Until(() => display.Text("WEAPON").Contains("FIRECTL"), 5));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private string Screen() => _ship.Computer.ScreenText();

    /// <summary>The signatures along the IRQ 5 chain, from the vector, while they are the ship's programs.</summary>
    private unsafe string Chain()
    {
        byte* m = _ship.Computer.Memory;
        var names = new List<string>();
        int vector = (8 + 5) * 4;
        int offset = m[vector] | m[vector + 1] << 8, segment = m[vector + 2] | m[vector + 3] << 8;
        while (names.Count < 8)
        {
            int at = segment * 16;
            if (m[at + 0x103] != 'X' || m[at + 0x104] != 'P')
                break;
            names.Add(Encoding.ASCII.GetString(new ReadOnlySpan<byte>(m + at + 0x103, 6)));
            offset = m[at + 0x109] | m[at + 0x10A] << 8;
            segment = m[at + 0x10B] | m[at + 0x10C] << 8;
        }
        return string.Join(" ", names);
    }

    /// <summary>ASM in COS, until the output is on the diskette and the prompt is back.</summary>
    private async Task<bool> Assemble(string arguments, string output)
    {
        await Command("ASM " + arguments);
        return await Until(() => FatDisk.Read(_ship.Computer.DiskImage(), output) != null && !_ship.Computer.DriveLight
                                 && Screen().TrimEnd().EndsWith("A:\\>"), 180);
    }

    private async Task Command(string line)
    {
        _ship.Computer.ReleaseKeys();
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
        GD.Print(Screen());
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
