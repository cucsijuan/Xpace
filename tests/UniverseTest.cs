using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// The universe's state and who owns it:
///
///   - a new pilot starts with the starting credits and can't spend what they haven't got;
///   - the state goes to a folder of documents (one for each pilot and station) and comes back the same; a crash
///     can't leave half a document; a universe saved by a newer game is refused;
///   - alone, this machine's pilot is in the state, and hears when their record changes;
///   - in a session the guest says who flies its ship, the host makes or finds that pilot's record and sends it
///     back, and sends it again when it changes; documents longer than a packet arrive whole; the guest's played
///     time runs on the host while it is there.
///
/// Run: Godot --headless --path . res://tests/universe_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class UniverseTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Records();
        Store();
        await Alone();
        await InASession();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Records()
    {
        var state = new UniverseState();
        PilotRecord pilot = state.Pilot("p1", "Ann");
        Check($"a new pilot has the starting credits ({pilot.Credits})", pilot.Credits == PilotRecord.StartingCredits);
        Check("and can't spend more than that", !pilot.Spend(PilotRecord.StartingCredits + 1) && pilot.Credits == PilotRecord.StartingCredits);
        Check("nor less than nothing", !pilot.Spend(-5));
        Check("but can spend what they have", pilot.Spend(300) && pilot.Credits == PilotRecord.StartingCredits - 300);
        Check("the same id is the same pilot, renamed if the name changed", state.Pilot("p1", "Anne") == pilot && pilot.Name == "Anne");
        Check("a station holds nothing against a new pilot", state.Station("HAVEN").CrimesOf("p1").Clean);
    }

    private void Store()
    {
        string folder = ProjectSettings.GlobalizePath("user://test_universe");
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        var state = new UniverseState { Clock = 1234.5 };
        PilotRecord pilot = state.Pilot("p1", "Ann");
        pilot.Credits = 4321;
        pilot.PlayedSeconds = 99;
        state.Station("HAVEN").CrimesOf("p1").Fines = 200;
        state.Pilot("odd/id", "Bob");
        UniverseStore.Save(state, folder);
        Check("saved as a folder: the universe, a document for each pilot and each station",
              File.Exists(Path.Combine(folder, "universe.json")) && File.Exists(Path.Combine(folder, "pilots", "p1.json")) &&
              File.Exists(Path.Combine(folder, "stations", "HAVEN.json")));
        Check("an id that is no file name is written as one", File.Exists(Path.Combine(folder, "pilots", "odd%2fid.json")));
        Check("no document left half written", !Directory.EnumerateFiles(folder, "*.new", SearchOption.AllDirectories).Any());
        Check("each document says its format", File.ReadAllText(Path.Combine(folder, "pilots", "p1.json")).Contains("\"Version\": 1"));

        UniverseState back = UniverseStore.Load(folder);
        PilotRecord again = back.Pilots.GetValueOrDefault("p1");
        Check($"it comes back the same (clock {back.Clock}, credits {again?.Credits}, played {again?.PlayedSeconds})",
              back.Clock == 1234.5 && again is { Name: "Ann", Credits: 4321, PlayedSeconds: 99 } && back.Pilots.ContainsKey("odd/id"));
        Check("with what the station holds against the pilot", back.Stations.GetValueOrDefault("HAVEN")?.CrimesOf("p1").Fines == 200);
        Check("an empty folder is a new universe", UniverseStore.Load(Path.Combine(folder, "nothing")).Pilots.Count == 0);

        File.WriteAllText(Path.Combine(folder, "universe.json"), "{\"Version\": 99, \"Clock\": 1}");
        bool refused = false;
        try
        {
            UniverseStore.Load(folder);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }
        Check("a universe saved by a newer game is refused", refused);
        Directory.Delete(folder, true);
    }

    private async Task Alone()
    {
        Universe universe = Universe.Current;
        string folder = "user://test_universe_alone";
        string path = ProjectSettings.GlobalizePath(folder);
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        universe.Open(folder);
        universe.LocalPilotId = "me";
        universe.LocalPilotName = "Me";
        string heard = null;
        universe.Changed += topic => heard = topic;
        PilotRecord me = universe.Me;
        Check($"alone, this machine's pilot is in the state ({me?.Name}, {me?.Credits})",
              me is { Id: "me", Name: "Me" } && universe.State.Pilots.ContainsKey("me") && !universe.IsGuest);
        me.Credits += 50;
        universe.Touch(me);
        Check("and hears when their record changes", heard == Universe.PilotTopic);
        universe.SaveAfterSeconds = 0.1;
        await Frames(20);
        Check("the change is saved a moment later", File.Exists(Path.Combine(path, "pilots", "me.json")) &&
              File.ReadAllText(Path.Combine(path, "pilots", "me.json")).Contains($"\"Credits\": {PilotRecord.StartingCredits + 50}"));
        universe.Forget();
        Check("a universe in memory only has no folder", universe.Folder == null && universe.Me.Credits == PilotRecord.StartingCredits);
        Directory.Delete(path, true);
    }

    private async Task InASession()
    {
        Universe host = Universe.Current;
        host.Forget();
        host.LocalPilotId = "host";
        var guest = new Universe { Name = "GuestUniverse", LocalPilotId = "guest", LocalPilotName = "Guest" };
        AddChild(guest);
        var net = new SimulatedNetwork { Latency = 0.05, Jitter = 0.01, Loss = 0.05 };
        var hostSession = new Session(Session.HostPeer, net.Join(Session.HostPeer));
        var guestSession = new Session(1, net.Join(1));
        host.Attach(hostSession);
        guest.Attach(guestSession);
        host.Answer("echo", (pilot, text) => $"{pilot}:{new string('x', 2500)}");

        uint tick = 0;
        async Task Run(double seconds)
        {
            for (int i = 0; i < seconds * 60; i++)
            {
                net.Now = ++tick / 60.0;
                hostSession.Tick(tick);
                guestSession.Tick(tick);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
        }

        Check("a guest has no record of its own until the host sends it", guest.IsGuest && guest.Me == null);
        await Run(3);
        Check($"the guest said who flies its ship: the host made the pilot ({host.PilotOfPeer(1)})",
              host.PilotOfPeer(1) == "guest" && host.State.Pilots.GetValueOrDefault("guest")?.Name == "Guest");
        Check($"and sent the record back ({guest.Me?.Credits})", guest.Me is { Id: "guest", Credits: PilotRecord.StartingCredits });

        PilotRecord theirs = host.State.Pilot("guest");
        theirs.Spend(250);
        host.Touch(theirs);
        await Run(1);
        Check($"a change on the host reaches the guest ({guest.Me?.Credits})", guest.Me?.Credits == PilotRecord.StartingCredits - 250);
        Check($"the guest's played time runs on the host while it is there ({theirs.PlayedSeconds:0.0} s)",
              theirs.PlayedSeconds > 3 && host.State.Clock >= theirs.PlayedSeconds - 0.1);
        Check("the host's pilot, with no ship in play, has played none", host.State.Pilot("host").PlayedSeconds == 0);

        guest.Ask("echo", "anything");
        await Run(1);
        string echo = guest.Document("echo");
        Check($"a document longer than a packet arrives whole ({echo?.Length} characters), answered for the asking pilot",
              echo != null && echo.Length == 2506 && echo.StartsWith("guest:"));

        host.Detach();
        guest.Detach();
        guest.QueueFree();
        host.Forget();
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
