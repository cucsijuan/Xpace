using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.World;

namespace Xpace.Space;

/// <summary>How a station's security sees a ship.</summary>
public enum Standing
{
    /// <summary>Nothing against it.</summary>
    Clean,
    /// <summary>It fired in the zone and was told to stop.</summary>
    Warned,
    /// <summary>The police go after it.</summary>
    Hostile,
}

/// <summary>
/// A station's security, beside its radio (<see cref="StationRadio"/>) in
/// the station's scene. It knows nothing of who is good or bad: only what it
/// sees ships do (<see cref="Combat"/>) inside the radio's laws' zone:
///
///   a weapon going off   the first time, a warning by radio
///                        (<see cref="WarningText"/>); going on firing more
///                        than <see cref="WarnGraceSeconds"/> after it, the
///                        ship is hostile and fined
///                        (<see cref="EngageText"/>, <see cref="FiringFine"/>).
///                        A ship hit by another in the last
///                        <see cref="DefenceSeconds"/> fires back freely;
///   a ship damaged       one the security had nothing against: its attacker
///                        is hostile at once, with a bounty on it
///                        (<see cref="AssaultText"/>, <see cref="DamageBounty"/>);
///   a ship destroyed     one the security had nothing against: its
///                        destroyer is hostile at once, with a big bounty and
///                        infamy (<see cref="DestroyedText"/>,
///                        <see cref="KillBounty"/>, <see cref="KillInfamy"/>);
///                        destroying a warned or hostile ship is no crime;
///   going too fast       inside the speed limit's radius, faster than the
///                        limit (and <see cref="SpeedTolerance"/>) for
///                        <see cref="SpeedingSeconds"/>: a fine, told by
///                        radio (<see cref="FineText"/>) and counted in
///                        <see cref="Fines"/>; a player owes it to the
///                        station (<see cref="SpeedingFine"/>, in the
///                        universe's record of what the station holds
///                        against the pilot) until it is paid.
///
/// What players owe goes on their record with the station, in the universe
/// (<see cref="CrimeRecord"/>, with <see cref="Justice"/>'s rules), but only
/// for a ship it can tell who flies: one whose radio is on. A pilot with a
/// bounty here is held on the pad until it is paid (<see cref="Holds"/>); a
/// notorious one is refused docking and attacked on sight
/// (<see cref="NotoriousText"/>).
///
/// Hostile ships are fair game for its police: up to
/// <see cref="PoliceCount"/> of <see cref="PoliceScene"/> launch from the
/// station's docks (the door opens, they fly out), and with no hostile ship
/// near for <see cref="StandDownSeconds"/> they fly back in. A warning is
/// forgotten after <see cref="ForgetSeconds"/> without firing, a hostility
/// after as long out of the zone; a ship destroyed starts clean. Its own
/// police may fire.
///
/// Its police also read the holds of ships in the zone (<see cref="Scans"/>):
/// a ship there for <see cref="ScanAfterSeconds"/> and not read in the last
/// <see cref="RescanSeconds"/> is looked over, as a pirate looks a prey over:
/// a police ship comes near, locks it and keeps the lock for as long as its
/// scanner takes, and the ship is told (<see cref="ScanText"/>). Nothing makes
/// the reading certain: a lock broken (a jammer, chaff, the station between
/// them, or simply distance) starts it over, and a ship out of the zone is no
/// longer looked for, nor one cleared to dock here, on its way in. Stolen goods found aboard a ship it can tell: a fine for
/// each tonne not fined before (<see cref="StolenFinePerTonne"/>; goods the law forbids, <see cref="IllegalFinePerTonne"/>); aboard one
/// it cannot tell (its radio off): hostile (<see cref="SmugglerText"/>). A
/// station whose owner fences stolen goods reads nobody's hold.
///
/// A ship a hangar's watch (<see cref="HangarWatch"/>) noticed, one it could not tell, that goes into the hangar all
/// the same is hostile (<see cref="TrespassText"/>); the station keeps that door shut meanwhile. One it never
/// noticed is in, and nobody knows.
///
/// In a session only the host's security acts, on every machine's ships
/// alike (<see cref="SessionNode"/> tells it what they do).
/// </summary>
public partial class StationSecurity : Node, IEngagement
{
    [Export] public double WarnGraceSeconds = 4;
    [Export] public double DefenceSeconds = 20;
    [Export] public double ForgetSeconds = 120;
    [Export] public float SpeedTolerance = 1.1f;
    [Export] public double SpeedingSeconds = 3;
    [Export] public double FineEverySeconds = 60;
    /// <summary>What speeding costs (credits).</summary>
    [Export] public long SpeedingFine = 200;
    /// <summary>What going on firing after the warning costs.</summary>
    [Export] public long FiringFine = 500;
    /// <summary>The bounty for damaging a ship that had done nothing.</summary>
    [Export] public long DamageBounty = 1000;
    /// <summary>The bounty for destroying one, and the infamy it brings (<see cref="Justice.Notorious"/> is 1).</summary>
    [Export] public long KillBounty = 5000;
    [Export] public double KillInfamy = 2;
    /// <summary>A gate's: going through it without clearance; again while still owing fines here, a bounty.</summary>
    [Export] public long UnpaidTransitFine = 1000;
    [Export] public long UnpaidTransitBounty = 3000;

    [ExportGroup("Police")]
    [Export] public PackedScene PoliceScene;
    [Export] public int PoliceCount = 2;
    /// <summary>How far past the zone the police still go after a hostile ship.</summary>
    [Export] public float PursuitMargin = 1000f;
    [Export] public double StandDownSeconds = 30;
    [Export] public double LaunchEverySeconds = 4;

    [ExportGroup("Scans")]
    /// <summary>Its police read the holds of ships in the zone.</summary>
    [Export] public bool Scans = true;
    /// <summary>How long a ship is in the zone before the police come to look at it.</summary>
    [Export] public double ScanAfterSeconds = 10;
    /// <summary>A ship read is left alone this long.</summary>
    [Export] public double RescanSeconds = 600;
    /// <summary>What each tonne of stolen goods found costs (credits).</summary>
    [Export] public long StolenFinePerTonne = 200;
    /// <summary>What each tonne of goods the law forbids (<see cref="Good.Illegal"/>) costs.</summary>
    [Export] public long IllegalFinePerTonne = 300;
    /// <summary>
    /// Its hangars' watches read the hulls of ships this near (metres; 0: they don't): a station with tight security,
    /// where a false callsign is found out at the door (<see cref="HangarWatch.RegistrationRange"/>).
    /// </summary>
    [Export] public float RegistrationRange;
    /// <summary>The bounty on a pilot found sending a false callsign.</summary>
    [Export] public long ForgeryBounty = 4000;

    [ExportGroup("Texts")]
    [Export] public string WarningText = "Weapons fire in the protected zone. Cease fire or you will be engaged.";
    /// <summary>{0} the fine.</summary>
    [Export] public string UnpaidTransitText = "Transit without clearance. You have been fined {0} credits.";
    /// <summary>{0} the bounty.</summary>
    [Export] public string UnpaidAgainText = "Transit without clearance again. A bounty of {0} credits is on you.";
    /// <summary>{0} the fine.</summary>
    [Export] public string EngageText = "You were warned. You have been fined {0} credits. Police are engaging you.";
    /// <summary>{0} the ship attacked, {1} the bounty.</summary>
    [Export] public string AssaultText = "You attacked {0}. A bounty of {1} credits is on you. Police are engaging you.";
    /// <summary>{0} the ship destroyed, {1} the bounty.</summary>
    [Export] public string DestroyedText = "You destroyed {0}. A bounty of {1} credits is on you. Police are engaging you.";
    [Export] public string NotoriousText = "You are not welcome here. Police are engaging you.";
    /// <summary>{0} the speed (m/s), {1} the limit, {2} the fine.</summary>
    [Export] public string FineText = "Speeding at {0} m/s, limit {1} m/s. You have been fined {2} credits.";
    [Export] public string ScanText = "Cargo scan in progress. Hold your course.";
    /// <summary>{0} the stolen tonnes, {1} the fine.</summary>
    [Export] public string StolenText = "Stolen goods aboard, {0} t. You have been fined {1} credits.";
    /// <summary>{0} the forbidden tonnes, {1} the fine.</summary>
    [Export] public string IllegalText = "Contraband aboard, {0} t. You have been fined {1} credits.";
    /// <summary>{0} the false callsign, {1} the bounty.</summary>
    [Export] public string UnmaskedText = "Callsign {0} is false. A bounty of {1} credits is on you. Police are engaging you.";
    [Export] public string SmugglerText = "Unidentified ship carrying stolen goods. Police are engaging you.";
    /// <summary>{0} the hangar.</summary>
    [Export] public string TrespassText = "Unidentified ship in hangar {0}. Police are engaging you.";

    /// <summary>The station operator's lines for each text (see <see cref="StationRadio.Say"/>).</summary>
    [ExportGroup("Speech")]
    [Export] public string WarningSpeech = "warning";
    [Export] public string EngageSpeech = "engage";
    [Export] public string DestroyedSpeech = "destroyed";
    [Export] public string FineSpeech = "fined";
    [Export] public string UnpaidTransitSpeech = "unpaid_transit";
    [Export] public string UnpaidAgainSpeech = "unpaid_again";
    [Export] public string AssaultSpeech = "assault";
    [Export] public string NotoriousSpeech = "notorious";
    [Export] public string ScanSpeech = "";
    [Export] public string StolenSpeech = "";
    [Export] public string SmugglerSpeech = "";
    [Export] public string IllegalSpeech = "";
    [Export] public string UnmaskedSpeech = "";
    [Export] public string TrespassSpeech = "";

    private sealed class Record
    {
        public Standing Standing;
        public double Since, LastSeen;
    }

    public StationRadio Radio { get; private set; }

    /// <summary>Fines given, by ship (players also owe them: <see cref="Fine"/>).</summary>
    public IReadOnlyDictionary<Node3D, int> Fines => _fines;

    private readonly Dictionary<Node3D, Record> _records = new();
    private readonly Dictionary<Node3D, (Node3D By, double At)> _lastHit = new();
    private readonly Dictionary<Node3D, int> _fines = new();
    private readonly Dictionary<Node3D, (double Fast, double Fined)> _speeding = new();
    private readonly Dictionary<(Node3D By, Node3D Victim), double> _assaults = new();
    private readonly List<(Node3D Ship, FighterAi Pilot, HangarDock From)> _police = new();
    private readonly HashSet<Node3D> _returning = new();
    private readonly Dictionary<Node3D, double> _inZone = new(), _scanned = new();
    private readonly Dictionary<Node3D, (int Stolen, int Illegal)> _finedTonnes = new();
    private readonly List<Node3D> _suspects = new();
    private readonly HashSet<Node3D> _toldScan = new();
    private readonly Dictionary<Node3D, (Node3D Of, ScanReading Before)> _reading = new();
    private double _now, _quiet, _sinceLaunch = double.MaxValue, _sinceLook;
    private int _launched;

    public override void _Ready()
    {
        foreach (Node n in GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
            Radio ??= n as StationRadio;
    }

    public override void _EnterTree()
    {
        Combat.Fired += OnFired;
        Combat.Damaged += OnDamaged;
        Combat.Destroyed += OnDestroyed;
    }

    public override void _ExitTree()
    {
        Combat.Fired -= OnFired;
        Combat.Damaged -= OnDamaged;
        Combat.Destroyed -= OnDestroyed;
    }

    public Standing StandingOf(Node3D ship) => ship != null && _records.TryGetValue(ship, out Record r) ? r.Standing : Standing.Clean;

    /// <summary>Traffic control does not deal with it: hostile now, or its pilot is notorious here.</summary>
    public bool Refuses(Node3D ship) => StandingOf(ship) == Standing.Hostile || Notorious(ship);

    /// <summary>Its pilot is wanted here: held on the pad until the bounty is paid (or the ship impounded for it).</summary>
    public bool Detains(Node3D ship) =>
        Jurisdiction(ship) is (PilotRecord pilot, CrimeRecord record) && Justice.Wanted(record) &&
        !Universe.Current.State.Station(Radio.Id).Impounded.ContainsKey(pilot.Id);

    /// <summary>Its pilot's infamy here is too high: refused and attacked on sight.</summary>
    public bool Notorious(Node3D ship) => Jurisdiction(ship) is (PilotRecord pilot, CrimeRecord record) && Justice.IsNotorious(record, pilot);

    /// <summary>The pilot flying the ship and their record here, if the ship can be told (its radio on) and a player flies it.</summary>
    public (PilotRecord Pilot, CrimeRecord Record)? Jurisdiction(Node3D ship)
    {
        if (Radio == null || ship == null || Universe.Current is not Universe universe || universe.IsGuest ||
            !Identified(ship) || universe.PilotOf(ship) is not PilotRecord pilot)
            return null;
        // A false callsign is a ship of its own here, with its own record; a callsign on no record, or one found out, nobody
        var (kind, forged) = universe.IdentityOf(ship);
        if (kind == Identity.Unknown || forged?.Burned == true)
            return null;
        return (pilot, universe.State.Station(Radio.Id).CrimesOf(forged?.Pilot ?? pilot.Id));
    }

    /// <summary>
    /// A ship sending a false callsign is found out (its hull read, by a scan or a hangar's registration reader): the
    /// callsign is no good any more anywhere, the ship is hostile, and the pilot behind it has a bounty here.
    /// </summary>
    public void Unmask(Node3D ship, ForgedRecord forged)
    {
        if (!Acts || forged == null || forged.Burned)
            return;
        forged.Burned = true;
        if (Universe.Current.PilotOf(ship) is PilotRecord pilot)
        {
            Universe.Current.State.Station(Radio.Id).CrimesOf(pilot.Id).Bounty += ForgeryBounty;
            Universe.Current.Touch(pilot);
        }
        else if (ship is ShipBody { Actor: { Length: > 0 } actor })
            Reactions.Unpaid(Radio.Cash, actor, ForgeryBounty);    // a free person: a price on their head with the owner
        Engage(ship, string.Format(UnmaskedText, forged.Callsign, ForgeryBounty), UnmaskedSpeech, force: true);
    }

    private static bool Identified(Node3D ship) =>
        ship.FindChildren("*", "", true, false).OfType<IRadio>().Any(r => r.RadioOn);

    /// <summary>The ships its police go after.</summary>
    public IEnumerable<Node3D> Hostiles => _records.Where(kv => kv.Value.Standing == Standing.Hostile && Valid(kv.Key)).Select(kv => kv.Key);

    /// <summary>The ships its police are to look over now: their holds to be read.</summary>
    public IReadOnlyList<Node3D> Suspects => _suspects;

    /// <summary>When its police last read that ship's hold, if they have.</summary>
    public double? ScannedAt(Node3D ship) => ship != null && _scanned.TryGetValue(ship, out double at) ? at : null;

    // Its police's orders: the hostile ships, and the ships to look over
    IEnumerable<Node3D> IEngagement.Hostiles => Hostiles.Concat(_suspects.Where(Valid)).Distinct();

    bool IEngagement.Holds(Node3D ship) => ship != null && _suspects.Contains(ship) && StandingOf(ship) != Standing.Hostile;

    public bool IsPolice(Node3D ship) => _police.Any(p => p.Ship == ship);

    private bool Acts => Radio?.Laws != null && !(SessionNode.Instance is SessionNode s && s.Active && !s.Session.IsHost);

    private void OnFired(Node3D ship, Vector3 at)
    {
        if (!Acts || !Radio.Protects(at) || IsPolice(ship) || Defending(ship))
            return;
        Record r = RecordOf(ship);
        r.LastSeen = _now;
        if (r.Standing == Standing.Clean)
        {
            r.Standing = Standing.Warned;
            r.Since = _now;
            Say(ship, WarningText, WarningSpeech);
        }
        else if (r.Standing == Standing.Warned && _now - r.Since > WarnGraceSeconds)
        {
            Owe(ship, FiringFine, 0, 0);
            Engage(ship, string.Format(EngageText, FiringFine), EngageSpeech);
        }
    }

    private void OnDamaged(Node3D victim, Node3D by)
    {
        bool crime = Acts && by != null && by != victim && Radio.Protects(victim.GlobalPosition) && !IsPolice(by) && !Defending(by) &&
                     StandingOf(victim) == Standing.Clean &&
                     !(_assaults.TryGetValue((by, victim), out double at) && _now - at < DefenceSeconds);
        _lastHit[victim] = (by, _now);
        if (!crime)
            return;
        _assaults[(by, victim)] = _now;
        Owe(by, 0, DamageBounty, 0);
        Engage(by, string.Format(AssaultText, Callsign(victim), DamageBounty), AssaultSpeech, force: true);
    }

    private void OnDestroyed(Node3D victim, Node3D by, Vector3 at)
    {
        bool crime = Acts && by != null && Radio.Protects(at) && !IsPolice(by) && StandingOf(victim) == Standing.Clean
                     && !(_lastHit.TryGetValue(by, out var hit) && hit.By == victim && _now - hit.At < DefenceSeconds);
        _records.Remove(victim);                    // a ship destroyed starts clean
        _lastHit.Remove(victim);
        if (crime)
        {
            Owe(by, 0, KillBounty, KillInfamy);
            Engage(by, string.Format(DestroyedText, Callsign(victim), KillBounty), DestroyedSpeech, force: true);
        }
    }

    // Hit by another ship lately: firing back is no crime
    private bool Defending(Node3D ship) => _lastHit.TryGetValue(ship, out var hit) && _now - hit.At < DefenceSeconds && hit.By != ship;

    // Hostile from now; told so, unless it already was (a new crime is told anyway)
    private void Engage(Node3D ship, string text, string speech, bool force = false)
    {
        Record r = RecordOf(ship);
        if (r.Standing == Standing.Hostile && !force)
            return;
        r.Standing = Standing.Hostile;
        r.Since = r.LastSeen = _now;
        Say(ship, text, speech);
    }

    private Record RecordOf(Node3D ship)
    {
        if (!_records.TryGetValue(ship, out Record r))
            _records[ship] = r = new Record { LastSeen = _now };
        return r;
    }

    private void Say(Node3D ship, string text, string speech) =>
        Radio.Say(ship.FindChildren("*", "", true, false).OfType<IRadioReceiver>().FirstOrDefault(), text, speech);

    private static string Callsign(Node3D ship) =>
        ship.FindChildren("*", "", true, false).OfType<IRadio>().FirstOrDefault()?.Callsign ?? Space.Radio.Callsign("", ship);

    private static bool Valid(Node3D ship) => IsInstanceValid(ship) && !ship.IsQueuedForDeletion();

    public override void _PhysicsProcess(double delta)
    {
        _now += delta;
        _sinceLaunch += delta;
        if (!Acts)
            return;
        Forget();
        _sinceLook += delta;
        if (_sinceLook >= 0.5)
        {
            Speeding(_sinceLook);
            OnSight();
            LookFor();
            Trespass();
            _sinceLook = 0;
        }
        Police(delta);
    }

    private void Forget()
    {
        foreach (Node3D ship in _records.Keys.ToList())
        {
            Record r = _records[ship];
            if (!Valid(ship))
            {
                _records.Remove(ship);
                continue;
            }
            if (Radio.Protects(ship.GlobalPosition) && r.Standing == Standing.Hostile)
                r.LastSeen = _now;
            if (_now - r.LastSeen > ForgetSeconds)
                _records.Remove(ship);
        }
        foreach (Node3D ship in _lastHit.Keys.Where(s => !Valid(s)).ToList())
            _lastHit.Remove(ship);
        foreach (var pair in _assaults.Keys.Where(p => !Valid(p.By) || !Valid(p.Victim)).ToList())
            _assaults.Remove(pair);
        foreach (Node3D ship in _scanned.Keys.Where(s => !Valid(s) || _now - _scanned[s] > RescanSeconds).ToList())
            _scanned.Remove(ship);
        foreach (Node3D ship in _finedTonnes.Keys.Where(s => !Valid(s)).ToList())
            _finedTonnes.Remove(ship);
    }

    private void Speeding(double dt)
    {
        StationLaws laws = Radio.Laws;
        foreach (Node node in Sectors.InGroup(this, Space.Radio.Group))
        {
            if (Combat.ShipOf(node) is not Node3D ship || !Combat.IsAlive(ship) || IsPolice(ship) || Velocity(ship) is not Vector3 velocity)
                continue;
            var (fast, fined) = _speeding.TryGetValue(ship, out var s) ? s : (0, double.MinValue);
            float speed = velocity.Length();
            bool over = ship.GlobalPosition.DistanceTo(Radio.GlobalPosition) <= laws.SpeedRadius && speed > laws.SpeedLimit * SpeedTolerance;
            fast = over ? fast + dt : 0;
            if (fast >= SpeedingSeconds && _now - fined > FineEverySeconds)
            {
                fined = _now;
                _fines[ship] = (_fines.TryGetValue(ship, out int n) ? n : 0) + 1;
                Owe(ship, SpeedingFine, 0, 0);
                Say(ship, string.Format(FineText, Mathf.RoundToInt(speed), Mathf.RoundToInt(laws.SpeedLimit), SpeedingFine), FineSpeech);
            }
            _speeding[ship] = (fast, fined);
        }
        foreach (Node3D ship in _speeding.Keys.Where(s => !Valid(s)).ToList())
            _speeding.Remove(ship);
    }

    /// <summary>
    /// A ship went through the gate without clearance: a fine; again while its pilot still owes
    /// fines here, a bounty and its police after it.
    /// </summary>
    public void UnpaidTransit(Node3D ship)
    {
        if (!Acts || ship == null)
            return;
        if (Jurisdiction(ship) is (_, CrimeRecord record) && record.Fines > 0)
        {
            Owe(ship, 0, UnpaidTransitBounty, 0);
            Engage(ship, string.Format(UnpaidAgainText, UnpaidTransitBounty), UnpaidAgainSpeech, force: true);
        }
        else
        {
            Owe(ship, UnpaidTransitFine, 0, 0);
            Say(ship, string.Format(UnpaidTransitText, UnpaidTransitFine), UnpaidTransitSpeech);
        }
    }

    /// <summary>The player flying <paramref name="ship"/> (if it can be told) owes the station more, or is more infamous here.</summary>
    private void Owe(Node3D ship, long fine, long bounty, double infamy)
    {
        if (Jurisdiction(ship) is not (PilotRecord pilot, CrimeRecord record))
            return;
        record.Fines += fine;
        record.Bounty += bounty;
        if (infamy > 0)
            Justice.AddInfamy(record, pilot, infamy);
        Universe.Current.Touch(pilot);
    }

    // Notorious pilots it can tell, in the zone: attacked on sight
    private void OnSight()
    {
        foreach (Node node in Sectors.InGroup(this, Space.Radio.Group))
            if (Combat.ShipOf(node) is Node3D ship && Combat.IsAlive(ship) && !IsPolice(ship) &&
                Radio.Protects(ship.GlobalPosition) && StandingOf(ship) != Standing.Hostile && Notorious(ship))
                Engage(ship, NotoriousText, NotoriousSpeech);
    }

    // ---------- hangars ----------

    // A ship a hangar's watch noticed outside and that went in all the same: hostile
    private void Trespass()
    {
        foreach (HangarDock dock in Radio.Docks)
        {
            if (dock.Watch is not HangarWatch watch)
                continue;
            foreach (Node3D ship in watch.Noticed.ToList())
                if (Valid(ship) && Combat.IsAlive(ship) && dock.Holds(ship) && !Radio.Expects(ship) && !IsPolice(ship) &&
                    StandingOf(ship) != Standing.Hostile)
                    Engage(ship, string.Format(TrespassText, dock.Number), TrespassSpeech);
        }
    }

    // ---------- cargo scans ----------

    /// <summary>The station's owner deals in stolen goods: whose they were is no business of its police.</summary>
    private bool Fences => Universe.Current is Universe universe && universe.Book?.Find(Radio.Id) is StationBook.Station here &&
                           People.Fences(here, universe.Population, universe.State, universe.Account);

    // Ships in the zone a while, not read lately, flying (not in a hangar, not cleared in: the station knows where those
    // are going and won't send police through their door). Whoever flies them, a pilot or a crew on its way, alike
    private void LookFor()
    {
        _suspects.Clear();
        bool scans = Scans && PoliceScene != null && !Fences;
        var seen = new HashSet<Node3D>();
        foreach (Node node in Sectors.InGroup(this, Space.Radio.Group))
        {
            if (Combat.ShipOf(node) is not Node3D ship || !Combat.IsAlive(ship) || IsPolice(ship) || !seen.Add(ship))
                continue;
            if (!Radio.Protects(ship.GlobalPosition) || Radio.Inside(ship) || Radio.Expects(ship))
            {
                _inZone.Remove(ship);
                continue;
            }
            _inZone.TryAdd(ship, _now);
            if (scans && _now - _inZone[ship] >= ScanAfterSeconds && !_scanned.ContainsKey(ship) && StandingOf(ship) != Standing.Hostile)
                _suspects.Add(ship);
        }
        foreach (Node3D ship in _inZone.Keys.Where(s => !seen.Contains(s)).ToList())
            _inZone.Remove(ship);
        _toldScan.RemoveWhere(s => !_suspects.Contains(s));
    }

    // A police ship's lock on a ship to look over: its scanner on it, and the reading, once it is done
    private void Scan(ShipBody police)
    {
        if (police.Device<ScannerDevice>() is not ScannerDevice scanner)
            return;
        if (_reading.TryGetValue(police, out var reading) && scanner.ResultOf == reading.Of && scanner.Result != null &&
            !ReferenceEquals(scanner.Result, reading.Before))
        {
            _reading.Remove(police);
            if (Valid(reading.Of))
                Scanned(reading.Of, scanner.Result);
            return;
        }
        if (scanner.Scanning || police.Device<RadarDevice>()?.Tracked is not Node tracked || Combat.ShipOf(tracked) is not Node3D ship ||
            !_suspects.Contains(ship))
            return;
        scanner.Start();
        _reading[police] = (ship, scanner.Result);
        if (_toldScan.Add(ship))
            Say(ship, ScanText, ScanSpeech);
    }

    /// <summary>What its police read of a ship's hold.</summary>
    private void Scanned(Node3D ship, ScanReading reading)
    {
        _scanned[ship] = _now;
        _suspects.Remove(ship);
        if (Universe.Current?.IdentityOf(ship) is (Identity.Forged, ForgedRecord forged))
            Unmask(ship, forged);                       // the hull isn't what the radio says
        int illegal = reading.Cargo.Where(kv => GoodsCatalog.Find(kv.Key)?.Illegal == true).Sum(kv => kv.Value);
        var (stolenFined, illegalFined) = _finedTonnes.GetValueOrDefault(ship);
        _finedTonnes[ship] = (reading.StolenTonnes, illegal);
        if (reading.StolenTonnes <= 0 && illegal <= 0)
            return;
        if (!Identified(ship))
        {
            Engage(ship, SmugglerText, SmugglerSpeech, force: true);
            return;
        }
        int stolen = System.Math.Max(0, reading.StolenTonnes - stolenFined), banned = System.Math.Max(0, illegal - illegalFined);
        if (stolen + banned == 0)
            return;
        long fine = stolen * StolenFinePerTonne + banned * IllegalFinePerTonne;
        if (Jurisdiction(ship) != null)
            Owe(ship, fine, 0, 0);
        else if (ship is ShipBody { Actor: { Length: > 0 } actor })
            Reactions.Unpaid(Radio.Cash, actor, fine);  // a free person: a price on their head with the owner
        if (stolen > 0)
            Say(ship, string.Format(StolenText, reading.StolenTonnes, stolen * StolenFinePerTonne), StolenSpeech);
        if (banned > 0)
            Say(ship, string.Format(IllegalText, illegal, banned * IllegalFinePerTonne), IllegalSpeech);
    }

    private static Vector3? Velocity(Node3D ship) => ship switch
    {
        ShipBody b => b.LinearVelocity,
        RemoteShip r => r.Velocity,
        _ => null,
    };

    // ---------- the police ----------

    private void Police(double delta)
    {
        _police.RemoveAll(p => !Valid(p.Ship));
        foreach (var p in _police.ToList())
            if (p.Ship is ShipBody { Wrecked: true })
            {
                _police.Remove(p);
                Node3D wreck = p.Ship;
                GetTree().CreateTimer(10).Timeout += () => { if (Valid(wreck)) wreck.QueueFree(); };
            }
        float reach = Radio.Laws.ZoneRadius + PursuitMargin;
        Node3D[] wanted = ((IEngagement)this).Hostiles.Where(h => Combat.IsAlive(h) && h.GlobalPosition.DistanceTo(Radio.GlobalPosition) < reach).ToArray();
        _quiet = wanted.Length > 0 ? 0 : _quiet + delta;
        foreach (var (ship, _, _) in _police)
            if (ship is ShipBody police && !_returning.Contains(ship))
                Scan(police);
        if (wanted.Length > 0 && _police.Count < PoliceCount && _sinceLaunch > LaunchEverySeconds && PoliceScene != null)
            Launch(wanted[0]);
        foreach (var (ship, pilot, from) in _police)
        {
            if (pilot == null)
                continue;
            if (_quiet < StandDownSeconds)
            {
                if (_returning.Remove(ship))
                    pilot.Route.Clear();        // trouble again: back to work
                continue;
            }
            if (_returning.Add(ship))
            {
                Radio.Hold(from, ship, 180);
                pilot.Route.Clear();
                pilot.Route.Enqueue(from.ApproachPoint);
                pilot.Route.Enqueue(from.InsidePoint);
            }
            else if (pilot.Route.Count == 0 || from.Holds(ship))
            {
                _returning.Remove(ship);
                ship.QueueFree();               // back in its hangar
            }
        }
    }

    /// <summary>A police ship out of the dock nearest the trouble.</summary>
    private void Launch(Node3D trouble)
    {
        HangarDock dock = Radio.Docks.Where(d => d.ClearedFor == null)
            .OrderBy(d => d.GlobalPosition.DistanceTo(trouble.GlobalPosition)).FirstOrDefault();
        if (dock == null || PoliceScene.Instantiate() is not Node3D ship)
            return;
        _sinceLaunch = 0;
        ship.Name = $"Police{++_launched}";
        if (ship is ShipBody cruiser)
            cruiser.Base = Radio.Id;                    // what it fires comes out of the station's own stores
        var parts = ship.FindChildren("*", "", true, false).Prepend(ship).ToList();
        foreach (NetBody body in parts.OfType<NetBody>())
            body.Spawned = true;
        foreach (ShipHull hull in parts.OfType<ShipHull>())
            hull.Insured = false;
        FighterAi pilot = parts.OfType<FighterAi>().FirstOrDefault();
        if (pilot != null)
        {
            pilot.Police = this;
            pilot.Route.Enqueue(dock.ApproachPoint);   // out of the hangar first
        }
        Node world = Radio.GetParent()?.GetParent() ?? GetTree().CurrentScene;
        world.AddChild(ship);
        // In the hangar, its nose to the door
        Basis outward = dock.GlobalBasis.Rotated(dock.GlobalBasis.Y.Normalized(), Mathf.Pi);
        ship.GlobalTransform = new Transform3D(outward.Orthonormalized(), dock.InsidePoint);
        if (pilot != null)
            pilot.Anchor = Radio.GlobalPosition;
        Radio.Hold(dock, ship, 30);
        _police.Add((ship, pilot, dock));
    }
}
