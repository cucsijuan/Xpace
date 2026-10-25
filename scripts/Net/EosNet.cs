#if XPACE_EOS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GodotEos;

namespace Xpace.Net;

/// <summary>
/// Playing over the internet through Epic Online Services (the shared GodotEos library): players sign in with this
/// computer's device account, the host makes a lobby, guests find it and join it, and the session goes over
/// peer-to-peer connections (direct, or through Epic's relays) with a <see cref="StarTransport"/>. Only the lobby's
/// members can connect to the host, and a guest only takes packets from the host; a member who leaves the lobby
/// leaves the session.
///
/// The product's keys are not in the repository: tools/eos_keys.py writes them into EosProduct.g.cs, or they come from
/// the environment (EOS_PRODUCT_ID and the rest, see <see cref="EosKeys.FromEnvironment"/>).
/// </summary>
public sealed partial class EosNet : IDisposable
{
    private const string SocketName = "XPACE";
    private const string AttrHost = "HOST";

    private readonly EosPlatform _platform;
    private readonly EosP2P _p2p;
    private readonly EosLobbies _lobbies;
    private EosLobby _lobby;

    /// <summary>The transport, once hosting or joined.</summary>
    public StarTransport Transport { get; private set; }

    /// <summary>The lobby's id (for joining that one), once hosting.</summary>
    public string LobbyId => _lobby?.Id;

    /// <summary>The SDK's and the service's diagnostics (on the SDK's thread).</summary>
    public event Action<string> Log;

    /// <summary>The lobby's bucket: only games of the same network version find each other.</summary>
    public static string Bucket => $"xpace:{StarTransport.Version}";

    public static EosKeys Keys
    {
        get
        {
            EosKeys keys = null;
            Load(ref keys);
            return keys ?? EosKeys.FromEnvironment();
        }
    }

    static partial void Load(ref EosKeys keys);

    public EosNet()
    {
        _platform = new EosPlatform(Keys, new EosPlatformOptions
        {
            ProductName = "Xpace",
            ProductVersion = ProjectSettings.GetSetting("application/config/version", "").AsString() is { Length: > 0 } v ? v : "0.1",
            CacheDirectory = ProjectSettings.GlobalizePath("user://eos"),
            VerboseLog = OS.GetEnvironment("XPACE_EOS_LOG") == "1",
        });
        _platform.Log += what => Log?.Invoke(what);
        _p2p = new EosP2P(_platform, SocketName);
        _lobbies = new EosLobbies(_platform);
    }

    /// <summary>
    /// Signs in and makes a lobby of up to <paramref name="players"/>; the transport is the host's. One EosNet serves
    /// the whole run of the game, a game after another (<see cref="LeaveGame"/> between them).
    /// </summary>
    public async Task<StarTransport> HostAsync(string name, int players = StarTransport.MaxGuests + 1, bool newDevice = false)
    {
        await _platform.SignInWithDeviceAsync(name, newDevice);
        _lobby = await _lobbies.CreateAsync(Bucket, players, new Dictionary<string, string> { [AttrHost] = name });
        _p2p.Accepts = _lobby.Has;
        return Transport = StarTransport.Host(new Link(_p2p), _lobby.Has);
    }

    /// <summary>Signs in, finds a lobby (that one, or the first with room) and joins its host.</summary>
    public async Task<StarTransport> JoinAsync(string name, string lobbyId = null, bool newDevice = false)
    {
        await _platform.SignInWithDeviceAsync(name, newDevice);
        IReadOnlyList<EosLobbyInfo> found = await _lobbies.SearchAsync(Bucket);
        EosLobbyInfo lobby = found.FirstOrDefault(l => lobbyId == null || l.Id == lobbyId)
            ?? throw new InvalidOperationException(lobbyId == null ? "no game to join" : $"no game {lobbyId}");
        return await Join(lobby);
    }

    /// <summary>How long a guest waits for a lobby's host to answer before trying another game.</summary>
    public const double HostAnswerSeconds = 12;

    /// <summary>
    /// Searches before hosting, a couple of seconds apart: a lobby just made takes a few seconds to show in searches,
    /// and two players choosing Multiplayer together would otherwise both host.
    /// </summary>
    public const int Searches = 3;
    public const double SearchGapSeconds = 2;

    /// <summary>
    /// After hosting, how long before looking once more for another game made at the same time: of two such, the one
    /// with the smaller lobby id stays and the other's host joins it.
    /// </summary>
    public const double SettleSeconds = 5;

    /// <summary>
    /// Signs in and joins the first game with room whose host answers; with none, hosts one. A lobby can outlive its
    /// host for a while (the host's game crashed): one whose host doesn't answer is left, and the next tried. Done once
    /// this machine has its peer. <paramref name="status"/> hears each step. (Two players starting at the same moment
    /// may both host: each then has a game of their own.)
    /// </summary>
    public async Task<StarTransport> FindOrHostAsync(string name, bool newDevice = false, Action<string> status = null)
    {
        status?.Invoke("Signing in");
        await _platform.SignInWithDeviceAsync(name, newDevice);
        var tried = new HashSet<string>();
        for (int search = 0; search < Searches; search++)
        {
            if (search > 0)
                await Task.Delay(TimeSpan.FromSeconds(SearchGapSeconds + Random.Shared.NextDouble()));
            status?.Invoke(search == 0 ? "Looking for a game" : "Looking for a game again");
            StarTransport t = await TryLobbies(await Search(), tried, status);
            if (t != null)
                return t;
        }
        status?.Invoke("No game found: hosting one");
        StarTransport hosted = await HostAsync(name);
        status?.Invoke("Hosting: checking for other new games");
        await Task.Delay(TimeSpan.FromSeconds(SettleSeconds));
        string mine = LobbyId;
        List<EosLobbyInfo> older = (await Search())
            .Where(l => l.Id != mine && l.Members <= 1 && string.CompareOrdinal(l.Id, mine) < 0).ToList();
        if (older.Count == 0 || hosted.Peers.Count > 0)
            return hosted;                      // ours stays (or someone is in it already)
        Log?.Invoke($"Online: another game was made at the same time ({older[0].Id}); joining it instead");
        await Abandon();
        StarTransport joined = await TryLobbies(older, tried, status);
        if (joined != null)
            return joined;
        status?.Invoke("Hosting a game");
        return await HostAsync(name);
    }

    private async Task<IReadOnlyList<EosLobbyInfo>> Search()
    {
        IReadOnlyList<EosLobbyInfo> found = await _lobbies.SearchAsync(Bucket);
        Log?.Invoke($"Online: {found.Count} game(s) found{(found.Count > 0 ? ": " + string.Join(", ", found.Select(l => $"{l.Id} ({l.Members}/{l.MaxMembers})")) : "")}");
        return found;
    }

    /// <summary>The lobbies found, not tried yet, in turn: the transport of the first whose host lets this player in.</summary>
    private async Task<StarTransport> TryLobbies(IReadOnlyList<EosLobbyInfo> found, HashSet<string> tried, Action<string> status)
    {
        foreach (EosLobbyInfo lobby in found)
        {
            if (!tried.Add(lobby.Id))
                continue;
            status?.Invoke($"Joining {lobby.Attributes.GetValueOrDefault(AttrHost, "a game")}");
            try
            {
                StarTransport t = await Join(lobby, HostAnswerSeconds);
                await Welcomed(t);
                return t;
            }
            catch (Exception e)
            {
                Log?.Invoke($"Online: lobby {lobby.Id}: {e.Message}");
                await Abandon();
            }
        }
        return null;
    }

    private async Task<StarTransport> Join(EosLobbyInfo lobby, double timeout = StarTransport.JoinTimeout)
    {
        _lobby = await _lobbies.JoinAsync(lobby);
        string host = _lobby.Owner;
        _p2p.Accepts = peer => peer == host;
        return Transport = StarTransport.Join(new Link(_p2p), host, timeout: timeout);
    }

    /// <summary>Until the host numbers this guest (the transport is serviced meanwhile: <see cref="Update"/>).</summary>
    private static Task Welcomed(StarTransport t)
    {
        var welcomed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        t.PeerJoined += _ => welcomed.TrySetResult(true);
        t.PeerLeft += _ => welcomed.TrySetException(new InvalidOperationException(t.Refusal ?? "the host is gone"));
        return welcomed.Task;
    }

    /// <summary>Out of the game (its connections closed, its lobby left or closed); signed in still, for the next.</summary>
    public Task LeaveGame() => Abandon();

    /// <summary>Out of a game that didn't work: its connection closed and its lobby left.</summary>
    private async Task Abandon()
    {
        Transport?.Dispose();
        Transport = null;
        _p2p.Accepts = _ => false;
        EosLobby lobby = _lobby;
        _lobby = null;
        if (lobby != null)
            await lobby.LeaveAsync();
    }

    /// <summary>
    /// Every frame: the transport takes in what came (a guest waiting for its host's word has nobody else to do it);
    /// members who left the lobby leave the session; on a guest, a closed lobby means the host is gone.
    /// </summary>
    public void Update()
    {
        Transport?.Service();
        if (_lobby == null || Transport == null)
            return;
        while (_lobby.TryTakeChange(out string member, out bool joined))
            if (!joined && Transport.IsHost)
                Transport.Drop(member);
        if (_lobby.IsClosed && !Transport.IsHost)
            Transport.Drop(_lobby.Owner);
    }

    public void Dispose()
    {
        Transport?.Dispose();
        _lobby?.LeaveAsync();
        _p2p.Dispose();
        _platform.Dispose();
    }

    private sealed class Link : IPeerLink
    {
        private readonly EosP2P _p2p;

        public Link(EosP2P p2p) { _p2p = p2p; }

        public void Send(string to, byte channel, byte[] data, bool reliable) => _p2p.Send(to, channel, data, reliable);

        public bool Receive(out string from, out byte channel, out byte[] data) => _p2p.TryReceive(out from, out channel, out data);

        public bool TakeClosed(out string peer) => _p2p.TryTakeClosed(out peer);

        public void Close(string peer) => _p2p.Close(peer);
    }
}
#endif
