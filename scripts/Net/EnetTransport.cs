using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Net;

/// <summary>
/// The session over UDP, through ENet (Godot's <see cref="ENetConnection"/>,
/// without its high-level multiplayer): for playing on a LAN or over the
/// internet with an open port, until the EOS transport. Snapshots go
/// unsequenced (the session orders them itself), events reliable. ENet's
/// throttle, which drops unreliable packets when the round trip wobbles,
/// is off: the session copes with loss itself.
///
/// Star-shaped like the session: guests connect to the host, which numbers
/// them 1 to <see cref="MaxGuests"/> and tells each its number. Until then a
/// guest has no peers (<see cref="LocalPeer"/> is null).
/// </summary>
public sealed class EnetTransport : INetTransport, IDisposable
{
    public const int DefaultPort = 24680;
    public const int MaxGuests = 15;

    /// <summary>Bumped whenever the packets change: other versions are turned away.</summary>
    public const byte Version = 1;

    private const int Unreliable = 0, Reliable = 1, Control = 2, Channels = 3;
    private const byte Welcome = 1;

    public bool IsHost { get; }

    /// <summary>This machine's peer: 0 on the host, the host's number on a guest once it has it.</summary>
    public byte? LocalPeer { get; private set; }

    public IReadOnlyList<byte> Peers => _peerList;

    /// <summary>A guest came (on the host), or the host numbered this guest (on a guest).</summary>
    public event Action<byte> PeerJoined;

    /// <summary>A guest left (on the host), or the host is gone (on a guest: peer 0).</summary>
    public event Action<byte> PeerLeft;

    private readonly ENetConnection _connection = new();
    private readonly Dictionary<byte, ENetPacketPeer> _peers = new();
    private readonly Dictionary<ulong, byte> _ids = new();               // by the ENet peer's instance id
    private readonly List<byte> _peerList = new();
    private readonly Queue<(byte, byte[])> _arrived = new();
    private ENetPacketPeer _host;

    private EnetTransport(bool host) { IsHost = host; }

    /// <summary>Hosts a session on <paramref name="port"/>, on every address of this machine.</summary>
    public static EnetTransport Host(int port = DefaultPort)
    {
        var t = new EnetTransport(true) { LocalPeer = Session.HostPeer };
        Error e = t._connection.CreateHostBound("*", port, MaxGuests, Channels);
        if (e != Error.Ok)
            throw new InvalidOperationException($"cannot host on port {port}: {e}");
        return t;
    }

    /// <summary>Joins the session hosted at <paramref name="address"/>.</summary>
    public static EnetTransport Join(string address, int port = DefaultPort)
    {
        var t = new EnetTransport(false);
        Error e = t._connection.CreateHost(1, Channels);
        if (e != Error.Ok)
            throw new InvalidOperationException($"cannot open a socket: {e}");
        t._host = t._connection.ConnectToHost(address, port, Channels, Version);
        if (t._host == null)
            throw new InvalidOperationException($"cannot reach {address}:{port}");
        return t;
    }

    public void Send(byte peer, byte[] packet, bool reliable)
    {
        if (_peers.TryGetValue(peer, out ENetPacketPeer p))
            p.Send(reliable ? Reliable : Unreliable, packet,
                   (int)(reliable ? ENetPacketPeer.FlagReliable : ENetPacketPeer.FlagUnsequenced));
    }

    public bool Poll(out byte peer, out byte[] packet)
    {
        if (_arrived.Count == 0)
            Service();
        if (_arrived.Count == 0)
        {
            peer = 0;
            packet = null;
            return false;
        }
        (peer, packet) = _arrived.Dequeue();
        return true;
    }

    public void Flush() => _connection.Flush();

    /// <summary>Takes in what the network brought: comings, goings and packets (queued for <see cref="Poll"/>).</summary>
    public void Service()
    {
        while (true)
        {
            Godot.Collections.Array ev = _connection.Service();
            var type = (ENetConnection.EventType)(int)ev[0];
            if (type == ENetConnection.EventType.None)
                return;
            var from = ev[1].As<ENetPacketPeer>();
            switch (type)
            {
                case ENetConnection.EventType.Connect:
                    from.SetTimeout(32, 3000, 6000);
                    from.ThrottleConfigure(5000, 2, 0);         // never thin out the snapshots: the session copes with loss itself
                    if (IsHost && (int)ev[2] != Version)
                        from.PeerDisconnect();      // another version of the game
                    else if (IsHost)
                        Came(from);
                    break;
                case ENetConnection.EventType.Disconnect:
                    Went(from);
                    break;
                case ENetConnection.EventType.Receive:
                    byte[] data = from.GetPacket();
                    if ((int)ev[3] == Control)
                        Told(data);
                    else if (_ids.TryGetValue(from.GetInstanceId(), out byte id))
                        _arrived.Enqueue((id, data));
                    break;
                case ENetConnection.EventType.Error:
                    if (!IsHost)
                        Went(_host);
                    return;
            }
        }
    }

    /// <summary>The host: a guest connected; give it the lowest free number.</summary>
    private void Came(ENetPacketPeer guest)
    {
        byte id = 1;
        while (_peers.ContainsKey(id))
            id++;
        if (id > MaxGuests)
        {
            guest.PeerDisconnect();
            return;
        }
        _peers[id] = guest;
        _ids[guest.GetInstanceId()] = id;
        _peerList.Add(id);
        guest.Send(Control, new[] { Welcome, Version, id }, (int)ENetPacketPeer.FlagReliable);
        PeerJoined?.Invoke(id);
    }

    /// <summary>A guest: the host's word.</summary>
    private void Told(byte[] data)
    {
        if (IsHost || data.Length != 3 || data[0] != Welcome || LocalPeer.HasValue)
            return;
        if (data[1] != Version)
        {
            GD.PushError($"the host speaks version {data[1]}, this is {Version}");
            _host.PeerDisconnect();
            return;
        }
        LocalPeer = data[2];
        _peers[Session.HostPeer] = _host;
        _ids[_host.GetInstanceId()] = Session.HostPeer;
        _peerList.Add(Session.HostPeer);
        PeerJoined?.Invoke(data[2]);
    }

    private void Went(ENetPacketPeer who)
    {
        if (who == null || !_ids.Remove(who.GetInstanceId(), out byte id))
        {
            if (!IsHost && who == _host)
                PeerLeft?.Invoke(Session.HostPeer);         // never got in
            return;
        }
        _peers.Remove(id);
        _peerList.Remove(id);
        PeerLeft?.Invoke(id);
    }

    public void Dispose()
    {
        foreach (ENetPacketPeer p in _peers.Values)
            p.PeerDisconnectNow();
        _connection.Destroy();
        foreach (ENetPacketPeer p in _peers.Values)     // let go of the engine's objects now, not at the next collection
            p.Dispose();
        _host?.Dispose();
        _connection.Dispose();
        _peers.Clear();
        _peerList.Clear();
    }
}
