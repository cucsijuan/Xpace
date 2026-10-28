using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Xpace.Menu;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.World;

/// <summary>
/// The universe as this machine has it (an autoload). The machine that owns it, the host or a player alone, holds
/// its whole <see cref="State"/>, changes it and keeps it on disk (<see cref="Open"/>, saved a few seconds after a
/// change and on the way out). A guest owns none of it: it keeps what the host sends it of its own pilot
/// (<see cref="Me"/>) and of what it looks at (<see cref="Document"/>), and asks the host for anything it wants done
/// (<see cref="Ask"/>), as it asks a station to dock.
///
/// Pilots are players, known by an id of their own (<see cref="LocalPilotId"/>): a guest says who flies its ship
/// when it comes (<see cref="Session.Introduce"/>), so its pilot's record is the same whichever ship or peer number
/// it has. Which pilot flies a ship: <see cref="PilotOf"/>.
///
/// Its clock (<see cref="UniverseState.Clock"/>) and each pilot's played time run while they play.
/// </summary>
public partial class Universe : Node
{
    public const string DefaultFolder = "user://universe";

    /// <summary>The topic of the document that carries a guest's own pilot's record.</summary>
    public const string PilotTopic = "pilot";

    /// <summary>A change is saved this long after it; the clocks alone, this often.</summary>
    [Export] public double SaveAfterSeconds = 5, SaveEverySeconds = 60;

    /// <summary>A guest repeats who flies its ship this often until the host answers.</summary>
    [Export] public double IntroduceEverySeconds = 2;

    public static Universe Current { get; private set; }

    /// <summary>The universe this machine owns (on a guest it waits unused: the host's is the one that counts).</summary>
    public UniverseState State { get; private set; } = new();

    /// <summary>Where it is kept; null: only in memory (a test, a scene run on its own).</summary>
    public string Folder { get; private set; }

    /// <summary>This machine's player, to every universe (XPACE_PILOT names another: several players on one computer).</summary>
    public string LocalPilotId
    {
        get => _localId ??= OS.GetEnvironment("XPACE_PILOT") is { Length: > 0 } id ? id
                            : GameSettings.Current?.PilotId is { Length: > 0 } p ? p : "local";
        set => _localId = value;
    }

    public string LocalPilotName
    {
        get => _localName ?? MainMenu.PlayerName;
        set => _localName = value;
    }

    private string _localId, _localName;

    /// <summary>This machine plays in another's universe.</summary>
    public bool IsGuest => _session is { IsHost: false };

    /// <summary>This machine's pilot: the record itself, or on a guest the host's copy of it (null until it comes).</summary>
    public PilotRecord Me => IsGuest ? _mine : State.Pilot(LocalPilotId, LocalPilotName);

    /// <summary>This machine's pilot's record changed, or a document came (its topic; <see cref="PilotTopic"/> for the pilot).</summary>
    public event Action<string> Changed;

    private Session _session;
    private PilotRecord _mine;
    private readonly Dictionary<byte, string> _peers = new();              // the host: who flies each guest's ship
    private readonly Dictionary<Node3D, string> _flown = new();            // ships whose pilot was named
    private readonly HashSet<Node3D> _boarded = new();                     // this machine's pilot's ships in play
    private readonly Dictionary<string, string> _documents = new();        // a guest: what the host sent, by topic
    private readonly Dictionary<string, Func<string, string, string>> _answers = new();
    private bool _dirty, _touched;
    private double _sinceChange, _sinceSave, _sinceIntroduced;

    public override void _EnterTree()
    {
        Current ??= this;
        Combat.Destroyed += Destroyed;
    }

    public override void _ExitTree()
    {
        Combat.Destroyed -= Destroyed;
        Save();
        if (Current == this)
            Current = null;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            Save();
    }

    /// <summary>Plays in the universe kept in <paramref name="folder"/> (made there if there is none).</summary>
    public void Open(string folder = DefaultFolder)
    {
        Save();
        string path = ProjectSettings.GlobalizePath(folder);
        try
        {
            State = UniverseStore.Load(path);
            Folder = path;
            _sinceSave = 0;
        }
        catch (Exception e) when (e is System.IO.IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            GD.PushError($"universe: {e.Message}; playing in a new one that will not be saved");
            State = new UniverseState();
            Folder = null;
        }
        _dirty = _touched = false;
    }

    /// <summary>A new universe kept only in memory.</summary>
    public void Forget()
    {
        Save();
        State = new UniverseState();
        Folder = null;
        _dirty = _touched = false;
    }

    /// <summary>Writes the universe to its folder now, if it changed.</summary>
    public void Save()
    {
        if (!_dirty || Folder == null || IsGuest)
            return;
        try
        {
            UniverseStore.Save(State, Folder);
            _dirty = _touched = false;
            _sinceSave = 0;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushError($"universe: couldn't save to {Folder}: {e.Message}");
        }
    }

    /// <summary>
    /// The state changed (saved a moment later). With a pilot: their record changed, and they hear of it, here
    /// (<see cref="Changed"/>) or on the guest they play from.
    /// </summary>
    public void Touch(PilotRecord pilot = null)
    {
        _dirty = _touched = true;
        _sinceChange = 0;
        if (pilot == null)
            return;
        if (pilot.Id == LocalPilotId && !IsGuest)
            Changed?.Invoke(PilotTopic);
        foreach ((byte peer, string id) in _peers)
            if (id == pilot.Id)
                _session?.SendDocument(peer, PilotTopic, JsonSerializer.Serialize(pilot));
    }

    // ---------- pilots and ships ----------

    /// <summary>Names the pilot of a ship (the ones the session knows find their own: see <see cref="PilotOf"/>).</summary>
    public void Fly(Node3D ship, string pilot)
    {
        if (pilot == null)
            _flown.Remove(ship);
        else
            _flown[ship] = pilot;
    }

    /// <summary>The id of the pilot flying <paramref name="ship"/>, if a player flies it.</summary>
    public string PilotIdOf(Node3D ship)
    {
        if (ship == null || !IsInstanceValid(ship))
            return null;
        if (_flown.TryGetValue(ship, out string id))
            return id;
        if (ship is RemoteShip { NetId: var remote } && NetIds.IsPlayerShip(remote))
            return _peers.GetValueOrDefault(NetIds.Owner(remote));
        if (ship.GetChildren().OfType<NetBody>().FirstOrDefault() is { OwnedBy: NetOwner.Pilot, IsLocal: true, Puppet: false })
            return LocalPilotId;
        return null;
    }

    /// <summary>The record of the pilot flying <paramref name="ship"/> (the owner of the universe only).</summary>
    public PilotRecord PilotOf(Node3D ship) =>
        !IsGuest && PilotIdOf(ship) is string id ? State.Pilot(id, id == LocalPilotId ? LocalPilotName : null) : null;

    /// <summary>The ship <paramref name="pilot"/> flies in this scene (the owner of the universe), or null.</summary>
    public Node3D ShipOf(string pilot)
    {
        foreach ((Node3D ship, string id) in _flown)
            if (id == pilot && IsInstanceValid(ship) && ship.IsInsideTree())
                return ship;
        if (pilot == LocalPilotId && _boarded.FirstOrDefault(s => IsInstanceValid(s) && s.IsInsideTree()) is Node3D mine)
            return mine;
        foreach ((byte peer, string id) in _peers)
            if (id == pilot && SessionNode.Instance?.NodeOf(NetIds.PlayerShip(peer)) is Node3D theirs)
                return theirs;
        return null;
    }

    /// <summary>A player's ship blew up (here or on its owner's machine): its cargo went with it.</summary>
    public void Lose(Node3D ship)
    {
        if (PilotOf(ship) is PilotRecord pilot && pilot.Ship.Cargo.Count > 0)
        {
            pilot.Ship.Cargo.Clear();
            Touch(pilot);
        }
    }

    private void Destroyed(Node3D ship, Node3D by, Vector3 at) => Lose(ship);

    /// <summary>
    /// The record of the pilot flying <paramref name="ship"/> as this machine has it: the owner of the universe, any
    /// player's; a guest, only its own pilot's (the host's copy).
    /// </summary>
    public PilotRecord RecordOf(Node3D ship) =>
        IsGuest ? PilotIdOf(ship) == LocalPilotId ? Me : null : PilotOf(ship);

    /// <summary>A ship this machine's pilot flies is in play (its played time runs while one is).</summary>
    public void Board(Node3D ship) => _boarded.Add(ship);

    public void Leave(Node3D ship) => _boarded.Remove(ship);

    // ---------- the session ----------

    /// <summary>Plays through <paramref name="session"/>: a host answers its guests, a guest is told by its host.</summary>
    public void Attach(Session session)
    {
        Detach();
        _session = session;
        _sinceIntroduced = double.MaxValue;
        if (session.IsHost)
        {
            session.PilotIntroduced += Introduced;
            session.Asked += Asked;
        }
        else
        {
            _mine = null;
            session.DocumentArrived += Arrived;
        }
    }

    public void Detach()
    {
        if (_session == null)
            return;
        _session.PilotIntroduced -= Introduced;
        _session.Asked -= Asked;
        _session.DocumentArrived -= Arrived;
        _session = null;
        _peers.Clear();
        _documents.Clear();
        _mine = null;
    }

    /// <summary>The host: the pilot of a guest's ship.</summary>
    public string PilotOfPeer(byte peer) => _peers.GetValueOrDefault(peer);

    private void Introduced(byte peer, string id, string name)
    {
        _peers[peer] = id;
        PilotRecord pilot = State.Pilot(id, name);
        Touch(pilot);
    }

    /// <summary>
    /// The host: how it answers a guest's requests on <paramref name="topic"/>: given the asking pilot's id and what
    /// they ask, the document to send back on the same topic (null: none).
    /// </summary>
    public void Answer(string topic, Func<string, string, string> answer) => _answers[topic] = answer;

    private void Asked(byte peer, string topic, string text)
    {
        if (_peers.GetValueOrDefault(peer) is string pilot && _answers.TryGetValue(topic, out var answer) &&
            answer(pilot, text) is string reply)
            _session.SendDocument(peer, topic, reply);
    }

    /// <summary>
    /// Asks the owner of the universe something on <paramref name="topic"/>. On a guest it goes to the host and the
    /// answer comes later (<see cref="Changed"/>, then <see cref="Document"/>); here it is answered at once.
    /// </summary>
    public void Ask(string topic, string text)
    {
        if (IsGuest)
            _session.Ask(topic, text);
        else if (_answers.TryGetValue(topic, out var answer) && answer(LocalPilotId, text) is string reply)
        {
            _documents[topic] = reply;
            Changed?.Invoke(topic);
        }
    }

    /// <summary>The latest answer on <paramref name="topic"/> (null: none yet).</summary>
    public string Document(string topic) => _documents.GetValueOrDefault(topic);

    private void Arrived(string topic, string text)
    {
        if (topic == PilotTopic)
        {
            try
            {
                _mine = JsonSerializer.Deserialize<PilotRecord>(text);
            }
            catch (JsonException)
            {
                return;
            }
        }
        else
            _documents[topic] = text;
        Changed?.Invoke(topic);
    }

    // ---------- time ----------

    public override void _PhysicsProcess(double delta)
    {
        if (IsGuest)
        {
            _sinceIntroduced += delta;
            if (_mine == null && _sinceIntroduced >= IntroduceEverySeconds)
            {
                _sinceIntroduced = 0;
                _session.Introduce(LocalPilotId, LocalPilotName);
            }
            return;
        }
        _boarded.RemoveWhere(s => !IsInstanceValid(s) || !s.IsInsideTree());
        var playing = new HashSet<string>();
        if (_boarded.Count > 0)
            playing.Add(LocalPilotId);
        if (_session != null)
            foreach (byte peer in _session.Peers)
                if (_peers.TryGetValue(peer, out string id))
                    playing.Add(id);
        if (playing.Count > 0)
        {
            State.Clock += delta;
            foreach (string id in playing)
                State.Pilot(id).PlayedSeconds += delta;
            _dirty = true;
        }
        _sinceChange += delta;
        _sinceSave += delta;
        if (_touched && _sinceChange >= SaveAfterSeconds || _dirty && _sinceSave >= SaveEverySeconds)
            Save();
    }
}
