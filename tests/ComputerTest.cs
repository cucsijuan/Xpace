using System;
using System.Diagnostics;
using Godot;
using Xpace.Computer;
using Xpace.Native;

namespace Xpace.Tests;

/// <summary>
/// The ship computer end to end, and how many of them a core can run.
/// Run: Godot --headless --path . res://tests/computer_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ComputerTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        var computer = new ShipComputer { Name = "ShipComputer" };
        AddChild(computer);
        // Run it here, faster than the game does, instead of in its _Process
        computer.SetProcess(false);

        Check("COS boots to the prompt", RunUntil(computer, () => computer.ScreenText().Contains("A:\\>"), 30));
        Type(computer, "DIR\n");
        Check("DIR lists the diskette", RunUntil(computer, () => computer.ScreenText().Contains("BASIC    COM"), 10));

        byte[] saved = computer.SaveState();
        string before = computer.ScreenText();
        Type(computer, "CLS\n");
        RunUntil(computer, () => !computer.ScreenText().Contains("BASIC    COM"), 5);
        Check("the save loads back", computer.LoadState(saved));
        Check("the screen is back as it was saved", computer.ScreenText() == before);
        Type(computer, "VER\n");
        Check("the loaded computer keeps running", RunUntil(computer, () => computer.ScreenText().Contains("Version"), 10));

        Benchmark();

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }

    /// <summary>Runs the computer in 20 ms steps until the condition holds, up to a limit of emulated seconds.</summary>
    private static bool RunUntil(ShipComputer computer, Func<bool> done, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.02)
        {
            computer._Process(0.02);
            if (done())
                return true;
        }
        GD.Print(computer.ScreenText());
        return false;
    }

    private static void Type(ShipComputer computer, string text)
    {
        foreach (char c in text)
        {
            string code = c switch
            {
                '\n' => "Enter",
                ' ' => "Space",
                >= 'A' and <= 'Z' => "Key" + c,
                >= '0' and <= '9' => "Digit" + c,
                _ => throw new ArgumentException($"no key for '{c}'"),
            };
            computer.Key(code, true);
            computer._Process(0.03);
            computer.Key(code, false);
            computer._Process(0.03);
        }
    }

    /// <summary>
    /// Raw speed of one machine running flat out, in instructions per second,
    /// and how many 4.77 MHz machines that is on one core.
    /// </summary>
    private static void Benchmark()
    {
        var computer = new ShipComputer { InstructionsPerSecond = 400_000_000 };
        computer._Ready();
        computer._Process(0.01);
        // A loop that never halts (COS at its prompt would wait in HLT):
        //     mov ax, 1
        // l:  add bx, ax / mov [200h], bx / mov cx, [200h] / shl cx, 1 / inc bx / mul bx / jmp l
        computer.LoadCom(new byte[] {
            0xB8, 0x01, 0x00,
            0x01, 0xC3, 0x89, 0x1E, 0x00, 0x02, 0x8B, 0x0E, 0x00, 0x02, 0xD1, 0xE1, 0x43, 0xF7, 0xE3, 0xEB, 0xEF });
        var watch = Stopwatch.StartNew();
        double emulated0 = computer.EmulatedSeconds;
        while (watch.Elapsed.TotalSeconds < 2)
            computer._Process(0.001);
        double ips = (computer.EmulatedSeconds - emulated0) * 400_000_000 / watch.Elapsed.TotalSeconds;
        GD.Print($"bench: {ips / 1e6:0.0} million instructions per second on one core, " +
                 $"about {ips / 400_000:0} machines at 4.77 MHz");
        computer._ExitTree();
        computer.Free();
    }
}
