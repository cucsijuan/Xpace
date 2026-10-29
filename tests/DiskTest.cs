using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;

namespace Xpace.Tests;

/// <summary>
/// The pilot's diskette between games and the insurer's copy: what a
/// program writes is kept when the drive stops; the next game's computer
/// boots it; the insurer's new ship boots the factory diskette with no
/// backup, and the last backup sent with one (not what was written after).
/// A new factory diskette (or copies kept before they were marked with
/// theirs) sets both aside, and the ship starts from the new one.
/// Run: Godot --headless --path . res://tests/disk_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class DiskTest : Node
{
    private const string Kept = "user://test/ship.img", Backup = "user://test/insurer.img";
    private int _failures;

    public override async void _Ready()
    {
        Clean();

        ShipComputer a = Computer("A");
        Check("COS boots", RunUntil(a, () => a.ScreenText().Contains("A:\\>"), 30));
        Check("nothing kept before anything is written", !Godot.FileAccess.FileExists(Kept));
        Type(a, "COPY HELLO.ASM NOTE.ASM\n");
        Check("a program writes the diskette", RunUntil(a, () => a.ScreenText().Contains("copied"), 20));
        RunUntil(a, () => !a.DriveLight, 10);
        a._Process(0.02);
        Check("what it wrote is kept when the drive stops", Godot.FileAccess.FileExists(Kept));
        a.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        ShipComputer b = Computer("B");
        RunUntil(b, () => b.ScreenText().Contains("A:\\>"), 30);
        Check("the next game's computer boots the kept diskette", Dir(b).Contains("NOTE     ASM"));

        Check("there is no backup yet", !b.HasBackup);
        b.Rearm();
        RunUntil(b, () => b.ScreenText().Contains("A:\\>"), 30);
        Check("with no backup the insurer's ship boots the factory diskette", !Dir(b).Contains("NOTE     ASM"));

        Type(b, "COPY HELLO.ASM SAVED.ASM\n");
        RunUntil(b, () => b.ScreenText().Contains("copied"), 20);
        RunUntil(b, () => !b.DriveLight, 10);
        Check("the backup is sent", b.SendBackup() && b.HasBackup);
        Type(b, "COPY HELLO.ASM LATER.ASM\n");
        RunUntil(b, () => b.ScreenText().Contains("copied"), 20);
        RunUntil(b, () => !b.DriveLight, 10);
        b.Rearm();
        RunUntil(b, () => b.ScreenText().Contains("A:\\>"), 30);
        string dir = Dir(b);
        Check("the insurer's ship boots the last backup", dir.Contains("SAVED    ASM") && !dir.Contains("LATER    ASM"));
        b.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        ShipComputer c = Computer("C");
        RunUntil(c, () => c.ScreenText().Contains("A:\\>"), 30);
        Check("and that is what is kept for the next game", Dir(c).Contains("SAVED    ASM") && !Dir(c).Contains("LATER    ASM"));
        c.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // A game with another factory diskette; the backup as an older game left it, unmarked
        using (var mark = Godot.FileAccess.Open(Kept + ".factory", Godot.FileAccess.ModeFlags.Write))
            mark.StoreString("0123456789ABCDEF");
        DirAccess.RemoveAbsolute(Backup + ".factory");
        ShipComputer d = Computer("D");
        RunUntil(d, () => d.ScreenText().Contains("A:\\>"), 30);
        Check("a new factory diskette replaces the kept one", !Dir(d).Contains("SAVED    ASM"));
        Check("which is set aside, not lost", Godot.FileAccess.FileExists("user://test/ship.old.img")
              && !Godot.FileAccess.FileExists(Kept));
        Check("an unmarked backup is set aside too", !d.HasBackup && Godot.FileAccess.FileExists("user://test/insurer.old.img"));
        Check("a backup sent now is marked", d.SendBackup()
              && Godot.FileAccess.FileExists(Backup + ".factory"));
        d.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        ShipComputer e = Computer("E");
        Check("a marked backup from this factory diskette stays", e.HasBackup);
        e.QueueFree();

        Clean();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static void Clean()
    {
        foreach (string path in new[] { Kept, Backup, "user://test/ship.old.img", "user://test/insurer.old.img" })
        {
            DirAccess.RemoveAbsolute(path);
            DirAccess.RemoveAbsolute(path + ".factory");
        }
    }

    private ShipComputer Computer(string name)
    {
        var computer = new ShipComputer { Name = name, KeptDiskPath = Kept, BackupDiskPath = Backup };
        AddChild(computer);
        computer.SetProcess(false);
        return computer;
    }

    private static string Dir(ShipComputer computer)
    {
        Type(computer, "CLS\n");
        Type(computer, "DIR\n");
        RunUntil(computer, () => computer.ScreenText().Contains("bytes free"), 10);
        return computer.ScreenText();
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }

    private static bool RunUntil(ShipComputer computer, Func<bool> done, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.02)
        {
            computer._Process(0.02);
            if (done())
                return true;
        }
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
                '.' => "Period",
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
}
