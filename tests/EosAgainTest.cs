using System;
using Godot;
using Xpace.Net;

namespace Xpace.Tests;

/// <summary>
/// Online play twice in one run of the game, as a player going back to the main menu and choosing Multiplayer again:
/// the first time hosts (or joins), leaving lets the lobby go, and the second time signs in on the same platform and
/// gets in again (the EOS SDK can't start twice in a process). Needs the product's keys and the internet; without
/// keys it says so and passes.
///
/// Run: Godot --headless --path . res://tests/eos_again_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class EosAgainTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        if (!OnlinePlay.Available)
        {
            GD.Print("SKIPPED: this build has no online play");
            GetTree().Quit(0);
            return;
        }
        OnlinePlay online = OnlinePlay.Current;
        for (int round = 1; round <= 2; round++)
        {
            try
            {
                await online.FindOrHostAsync($"Again{round}", status: what => GD.Print($"  {what}"));
                Check($"round {round}: in a game (peer {online.Peer})", online.Peer.HasValue);
            }
            catch (Exception e) when (e.Message.Contains("keys are missing"))
            {
                GD.Print("SKIPPED: no product keys");
                GetTree().Quit(0);
                return;
            }
            catch (Exception e)
            {
                Check($"round {round}: in a game ({e.Message})", false);
            }
            online.Leave();
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        }
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
