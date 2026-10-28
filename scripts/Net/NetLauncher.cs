using System;
using System.Linq;
using Godot;

namespace Xpace.Net;

/// <summary>
/// Starts the scene's session: on the online game the main menu found or
/// made (<see cref="OnlinePlay"/>), or from the command line, over
/// <see cref="EnetTransport"/> (a LAN, or an open port) or online (Epic
/// Online Services: the first game with room, or a new one). Place it beside
/// a <see cref="SessionNode"/>; with neither the game plays alone.
///
///   godot --path . -- --host [port]
///   godot --path . -- --join address[:port]
///   godot --path . -- --online
///
/// <c>--eos-new-device</c> signs in with a new device account: for testing
/// several players on one computer.
/// The window's title says which machine it is.
/// </summary>
public partial class NetLauncher : Node
{
    [Export] public NodePath SessionPath = "../SessionNode";

    private EnetTransport _transport;
    private SessionNode _session;
    private string _title;

    public override void _Ready()
    {
        _session = GetNodeOrNull<SessionNode>(SessionPath);
        _title = GetWindow().Title;
        if (OnlinePlay.Current?.Peer is byte peer)
        {
            Online(peer);
            return;
        }
        string[] args = OS.GetCmdlineUserArgs();
        bool newDevice = args.Contains("--eos-new-device");
        for (int i = 0; i < args.Length; i++)
        {
            string[] kv = args[i].Split('=', 2);
            string value = kv.Length > 1 ? kv[1] : i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : null;
            try
            {
                if (kv[0] is "--host" or "--online")
                    World.Universe.Current?.Open();     // the universe this machine plays in if it hosts
                if (kv[0] == "--host")
                    Host(value == null ? EnetTransport.DefaultPort : int.Parse(value));
                else if (kv[0] == "--join")
                    Join(value ?? "127.0.0.1");
                else if (kv[0] == "--online")
                    GoOnline(newDevice);
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException)
            {
                GD.PushError($"network: {e.Message}");
            }
        }
    }

    private void Host(int port)
    {
        _transport = EnetTransport.Host(port);
        _transport.PeerJoined += peer => Say($"player {peer} joined");
        _transport.PeerLeft += peer => Say($"player {peer} left");
        _session?.Start(Session.HostPeer, _transport);
        Title($"host :{port}");
        Say($"hosting on port {port}");
    }

    private void Join(string where)
    {
        int port = EnetTransport.DefaultPort;
        int colon = where.LastIndexOf(':');
        if (colon > 0)
        {
            port = int.Parse(where[(colon + 1)..]);
            where = where[..colon];
        }
        _transport = EnetTransport.Join(where, port);
        _transport.PeerJoined += peer =>
        {
            _session?.Start(peer, _transport);
            Title($"player {peer}");
            Say($"joined {where}:{port} as player {peer}");
        };
        _transport.PeerLeft += _ => { Title("host gone"); Say("the host is gone"); };
        Title($"joining {where}:{port}");
    }

    /// <summary>From the command line: find or host an online game, as the menu does.</summary>
    private async void GoOnline(bool newDevice)
    {
        if (OnlinePlay.Current is not OnlinePlay online)
            return;
        try
        {
            await online.FindOrHostAsync(MainMenuName(), newDevice, what => Title(what.ToLowerInvariant()));
        }
        catch (Exception e)
        {
            Title("offline");
            GD.PushError($"network: {e.Message}");
            return;
        }
        Online(online.Peer.Value);
    }

    private void Online(byte peer)
    {
        StarTransport t = OnlinePlay.Current.Transport;
        t.PeerJoined += p => Say($"player {p} joined");
        t.PeerLeft += p => Say(p == Session.HostPeer ? t.Refusal ?? "the host is gone" : $"player {p} left");
        _session?.Start(peer, t);
        Title(peer == Session.HostPeer ? "host, online" : $"player {peer}, online");
        Say(peer == Session.HostPeer ? "hosting online" : $"joined online as player {peer}");
    }

    private static string MainMenuName() => Menu.MainMenu.PlayerName;

    public override void _PhysicsProcess(double delta)
    {
        // Until the session runs, someone has to hear the host's welcome
        if (_session?.Active != true)
            _transport?.Service();
    }

    public override void _ExitTree() => _transport?.Dispose();

    private void Title(string what) => GetWindow().Title = $"{_title} ({what})";

    private static void Say(string what) => GD.Print($"network: {what}");
}
