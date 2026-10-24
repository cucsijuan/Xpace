using System;
using Godot;

namespace Xpace.Net;

/// <summary>
/// Starts a session from the command line, over <see cref="EnetTransport"/>.
/// Place it beside a <see cref="SessionNode"/>; with no arguments the game
/// plays alone.
///
///   godot --path . -- --host [port]
///   godot --path . -- --join address[:port]
///
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
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string[] kv = args[i].Split('=', 2);
            string value = kv.Length > 1 ? kv[1] : i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : null;
            try
            {
                if (kv[0] == "--host")
                    Host(value == null ? EnetTransport.DefaultPort : int.Parse(value));
                else if (kv[0] == "--join")
                    Join(value ?? "127.0.0.1");
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

    public override void _PhysicsProcess(double delta)
    {
        // Until the session runs, someone has to hear the host's welcome
        if (_transport != null && _session?.Active != true)
            _transport.Service();
    }

    public override void _ExitTree() => _transport?.Dispose();

    private void Title(string what) => GetWindow().Title = $"{_title} ({what})";

    private static void Say(string what) => GD.Print($"network: {what}");
}
