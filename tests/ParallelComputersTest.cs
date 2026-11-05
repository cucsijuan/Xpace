using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;

namespace Xpace.Tests;

/// <summary>
/// Computers run side by side on the processor's cores end the same as run one after the other: two sets of machines
/// get the same ticks and the same registers, one set in turn and the other in parallel, and every machine's memory must
/// come out the same byte for byte. Each pair gets registers of its own, so machines that touched each other would show.
/// Run: Godot --headless --path . res://tests/parallel_computers_test.tscn
/// </summary>
public partial class ParallelComputersTest : Node
{
    private const int Pairs = 8, Ticks = 60 * 30;

    public override async void _Ready()
    {
        var inTurn = new List<ShipComputer>();
        var sideBySide = new List<ShipComputer>();
        for (int i = 0; i < Pairs; i++)
        {
            inTurn.Add(Machine());
            sideBySide.Add(Machine());
        }
        for (int t = 0; t < Ticks; t++)
        {
            for (int i = 0; i < Pairs; i++)
            {
                Feed(inTurn[i], i, t);
                Feed(sideBySide[i], i, t);
            }
            foreach (ShipComputer c in inTurn)
                c.Advance(1.0 / 60);
            Parallel.For(0, Pairs, i => sideBySide[i].Advance(1.0 / 60));
        }

        int failures = 0;
        for (int i = 0; i < Pairs; i++)
        {
            bool same = inTurn[i].EmulatedSeconds == sideBySide[i].EmulatedSeconds && Same(inTurn[i], sideBySide[i]);
            GD.Print($"{(same ? "ok  " : "FAIL")} pair {i} after {inTurn[i].EmulatedSeconds:0.0} emulated seconds");
            failures += same ? 0 : 1;
        }
        bool apart = !Same(inTurn[0], inTurn[1]);
        GD.Print($"{(apart ? "ok  " : "FAIL")} the pairs did different things");
        failures += apart ? 0 : 1;

        GD.Print(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private ShipComputer Machine()
    {
        var computer = new ShipComputer { RunsItself = false };
        AddChild(computer);
        computer.SetProcess(false);
        return computer;
    }

    private static void Feed(ShipComputer computer, int pair, int tick)
    {
        computer.Bus[1, 2] = (byte)(pair * 31 + tick);
        computer.Bus.RaiseIrq();
        if (tick < 600 || tick > 900 + pair * 90)
            return;                                     // COS is up by then; each pair types a different number of letters
        if (tick % 90 == 60)
            computer.Key("KeyD", true);
        if (tick % 90 == 66)
            computer.Key("KeyD", false);
    }

    private static unsafe bool Same(ShipComputer a, ShipComputer b) =>
        new ReadOnlySpan<byte>(a.Memory, 1 << 20).SequenceEqual(new ReadOnlySpan<byte>(b.Memory, 1 << 20));
}
