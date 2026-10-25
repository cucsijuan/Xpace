using System;
using System.Threading.Tasks;
using Godot;

namespace Xpace.Net;

/// <summary>
/// Online play that outlives a change of scene: an autoload holding the <see cref="EosNet"/> connection the main menu
/// opens, so the game scene's <see cref="NetLauncher"/> can start its session on it. Every physics frame it services
/// the lobby and the transport (while the session runs, the session polls it too: no harm).
/// </summary>
public partial class OnlinePlay : Node
{
    public static OnlinePlay Current { get; private set; }

    /// <summary>The transport, and this machine's peer on it, once in a game (hosting, or numbered by the host).</summary>
    public StarTransport Transport { get; private set; }

    public byte? Peer => Transport?.LocalPeer;

    /// <summary>This build can play online (the EOS SDK was there when it was built).</summary>
    public static bool Available =>
#if XPACE_EOS
        true;
#else
        false;
#endif

#if XPACE_EOS
    private EosNet _eos;
#endif

    public override void _Ready() => Current = this;

    /// <summary>
    /// Joins the first online game with room whose host answers, or hosts one; done once this machine has its peer.
    /// <paramref name="status"/> hears each step; failures throw, with the reason.
    /// </summary>
    public async Task FindOrHostAsync(string name, bool newDevice = false, Action<string> status = null)
    {
#if XPACE_EOS
        Leave();
        if (_eos == null)
        {
            _eos = new EosNet();                // one for the whole run: the SDK starts once a process
            _eos.Log += what => GD.Print(what);
        }
        try
        {
            Transport = await _eos.FindOrHostAsync(name, newDevice, status);
        }
        catch
        {
            Leave();
            throw;
        }
#else
        await Task.CompletedTask;
        throw new InvalidOperationException("this build has no online play (the EOS SDK was not there when it was built)");
#endif
    }

    /// <summary>Leaves the online game (and the lobby), if in one; still signed in, for the next.</summary>
    public void Leave()
    {
#if XPACE_EOS
        _eos?.LeaveGame();
#endif
        Transport = null;
    }

    public override void _PhysicsProcess(double delta)
    {
#if XPACE_EOS
        _eos?.Update();
#endif
    }

    public override void _ExitTree()
    {
#if XPACE_EOS
        _eos?.Dispose();
        _eos = null;
#endif
        Transport = null;
    }
}
