using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.World;

namespace Xpace.Space;

/// <summary>
/// Shows the universe's cargo ships (<see cref="CargoShipRecord"/>) where someone can see them: on the universe's
/// owner, for every loaded sector, the real ship of each record that is there: on its pad, or on its way
/// (<see cref="CargoRun"/>). Other machines get them as any ship the game brings in, so its scene must be one of the
/// <see cref="SessionNode.Spawnables"/>. A record stays the truth: the ships shown come and go with it.
/// </summary>
public partial class CargoView : Node
{
    /// <summary>How often it looks at the records, seconds.</summary>
    [Export] public double LookEverySeconds = 1;
    /// <summary>How long a door stays open for a ship after it last asked, seconds.</summary>
    [Export] public double DoorSeconds = 10;

    /// <summary>How long after anyone last dealt with a ship its computer goes on running, seconds.</summary>
    [Export] public double RestAfterSeconds = 60;

    /// <summary>How long a destroyed ship's wreck stays, seconds.</summary>
    [Export] public double WreckSeconds = 60;

    /// <summary>What a ship under fire calls on the radio ({0}: its callsign).</summary>
    [Export] public string MaydayText = "Mayday, mayday, {0} under fire.";

    /// <summary>A ship under fire calls and tells its owner no oftener than this, seconds.</summary>
    [Export] public double MaydayEverySeconds = 30;

    /// <summary>How near a pilot an escort has its computer up, ready to fight (m).</summary>
    [Export] public float AlertMeters = 30000f;

    /// <summary>How long after its last enemy is gone a ship goes back on its way, seconds.</summary>
    [Export] public double StandDownSeconds = 20;

    /// <summary>How far from who attacked it a cargo ship counts itself clear of them (m).</summary>
    [Export] public float ClearMeters = 15000f;

    /// <summary>How far a cargo ship under attack will run for the shelter of a station's zone (m).</summary>
    [Export] public float ShelterMeters = 60000f;

    /// <summary>How far a pirate sees cargo drifting (m).</summary>
    [Export] public float PodMeters = 5000f;

    /// <summary>How often it looks for who comes across whom on the ways, seconds.</summary>
    [Export] public double MeetEverySeconds = 5;

    /// <summary>How many fights it runs at once where nobody is: each one is a sector running.</summary>
    [Export] public int MostFights = 2;

    /// <summary>The ships shown now, by the record's id.</summary>
    public IReadOnlyDictionary<string, ShipBody> Shown => _shown;

    private readonly Dictionary<string, ShipBody> _shown = new();
    private readonly Dictionary<string, double> _dealt = new();
    private readonly Dictionary<ShipBody, double> _wrecks = new();
    private readonly HashSet<ShipBody> _struck = new();
    private readonly Dictionary<ShipBody, Node3D> _killers = new();
    private readonly Dictionary<ShipBody, double> _called = new();
    private readonly Dictionary<string, EscortDuty> _duties = new();
    private double _wait, _sinceMeet;
    private readonly Dictionary<ShipBody, double> _thought = new();
    private readonly System.Random _nerve = new(1);
    private readonly HashSet<string> _fights = new();
    private readonly HashSet<byte> _heldForFights = new();

    public override void _PhysicsProcess(double delta)
    {
        _wait -= delta;
        if (_wait > 0 || Universe.Current is not Universe universe || universe.IsGuest || universe.Book == null || SectorHost.Current is not SectorHost host)
            return;
        _wait = LookEverySeconds;
        foreach (CargoShipRecord record in universe.State.CargoShips.Values.ToList())
            Look(record, universe, host);
        Meet(universe, host);
        foreach (ShipBody wreck in _wrecks.Keys.ToList())
            if (!IsInstanceValid(wreck))
                _wrecks.Remove(wreck);
            else if ((_wrecks[wreck] -= LookEverySeconds) <= 0)
            {
                wreck.QueueFree();
                _wrecks.Remove(wreck);
            }
    }

    // People who come across what they are after go for it: where nobody is, the sector is run for the fight, with
    // real ships and nothing drawn. Players are found where they are, and seen by the stations they talk to
    private void Meet(Universe universe, SectorHost host)
    {
        UniverseState state = universe.State;
        if (universe.Population == null)
            return;
        if ((_sinceMeet += LookEverySeconds) >= MeetEverySeconds)
        {
            _sinceMeet = 0;
            foreach ((CargoShipRecord attacker, CargoShipRecord victim) in People.Meetings(state, universe.Book, universe.Population.MeetMeters))
                if (_fights.Count < MostFights || _fights.Contains(attacker.Id))
                    Attack(attacker, victim, universe, host);
        }
        foreach (string pilot in state.Pilots.Keys)
            if (universe.ShipOf(pilot) is Node3D ship && (ship is not ShipBody flown || (flown.Device<RadioDevice>() as IRadio)?.RadioOn == true) && StationRadio.ProtectingAt(Sectors.WorldOf(ship), ship.GlobalPosition) != null)
                People.Seen(state, pilot, universe.Book.Space.Keys.FirstOrDefault(id => host.Root(id) == FloatingOrigin.Of(ship)) ?? "", ToSector(ship));
        foreach ((string id, ShipBody ship) in _shown.ToList())
            if (IsInstanceValid(ship) && state.CargoShips.TryGetValue(id, out CargoShipRecord record) && state.People.GetValueOrDefault(record.Owner) is PersonRecord { Quarry.Length: > 0 } person
                && record.Phase is CargoShipRecord.Flying or CargoShipRecord.Holding && universe.ShipOf(person.Quarry) is Node3D quarry && Sectors.WorldOf(quarry) == Sectors.WorldOf(ship)
                && quarry.GlobalPosition.DistanceTo(ship.GlobalPosition) <= AlertMeters)
                Turn(record, ship, quarry);
        foreach (string id in _fights.ToList())
            if (!state.CargoShips.TryGetValue(id, out CargoShipRecord fighter) || fighter.Phase != CargoShipRecord.Loose)
                _fights.Remove(id);
        foreach (byte place in _heldForFights.ToList())
            if (_fights.Count == 0)
            {
                _heldForFights.Remove(place);
                host.Release(place);
            }
    }

    private static Vector3 ToSector(Node3D ship)
    {
        SectorPoint at = FloatingOrigin.Of(ship).Origin + ship.Position;
        return new Vector3((float)at.X, (float)at.Y, (float)at.Z);
    }

    private void Attack(CargoShipRecord attacker, CargoShipRecord victim, Universe universe, SectorHost host)
    {
        if (!ResourceLoader.Exists(attacker.Model) || Xpace.World.ResourceCache.Load<ShipModel>(attacker.Model) is not ShipModel model || universe.Book.FixOf(attacker, model, universe.State.Clock) is not StationBook.Fix fix)
            return;
        if (host.Root(fix.Sector) == null)
        {
            byte place = host.PlaceOf(fix.Sector);
            _heldForFights.Add(place);
            if (!host.Prepare(place))
                return;                                 // being made on another thread; the two are still there next look
        }
        Look(attacker, universe, host);
        Look(victim, universe, host);
        if (_shown.TryGetValue(attacker.Id, out ShipBody ship) && _shown.TryGetValue(victim.Id, out ShipBody prey))
            Turn(attacker, ship, prey);
    }

    // Off its way, its pilot flying it, after that ship
    private void Turn(CargoShipRecord record, ShipBody ship, Node3D at)
    {
        if (!_duties.TryGetValue(record.Id, out EscortDuty duty))
            return;
        duty.Add(at);
        _fights.Add(record.Id);
        record.Phase = CargoShipRecord.Loose;
        _dealt[record.Id] = double.MaxValue;
        ship.Wake();
        ship.Unshow();
        ship.Holder = null;
    }

    private void Look(CargoShipRecord record, Universe universe, SectorHost host)
    {
        _shown.TryGetValue(record.Id, out ShipBody ship);
        if (ship != null && !IsInstanceValid(ship))
        {
            _shown.Remove(record.Id);
            ship = null;
        }
        // Destroyed where it can be seen, it is gone from the universe: the wreck stays a while
        if (ship?.Find<ShipHull>()?.Wrecked == true)
        {
            _killers.Remove(ship, out Node3D killer);
            Tell(record, ship, killer, lost: true, universe);
            Spill(record, ship);
            CargoShips.Lose(universe.State, record.Id);
            _shown.Remove(record.Id);
            _wrecks[ship] = WreckSeconds;
            universe.Touch();
            return;
        }
        ShipModel model = ResourceLoader.Exists(record.Model) ? Xpace.World.ResourceCache.Load<ShipModel>(record.Model) : null;
        if (model == null)
            return;
        if (ship?.Find<ShipHull>() is ShipHull left)
        {
            if (record.Hull >= 1 && left.Fraction < 1 || record.Hull < 1 && left.Fraction <= record.Hull)
                record.Hull = left.Fraction;            // hit where it can be seen
            else if (left.Fraction < record.Hull)
                left.Mend(record.Hull);                 // mended at a station while shown
        }
        if (record.Phase == CargoShipRecord.Loose)
        {
            Fighting(record, model, ship, universe, host);
            return;
        }
        StationBook.Fix? fix = universe.Book.FixOf(record, model, universe.State.Clock);
        FloatingOrigin root = fix is StationBook.Fix f ? host.Root(f.Sector) : null;
        if (ship != null && (root == null || ship.GetParent() != root))
        {
            if (ship.Holder is CargoRun)
                ship.QueueFree();
            _shown.Remove(record.Id);
            ship = null;
        }
        if (ship == null && root != null && model.Scene != null)
            ship = Bring(record, model, fix.Value, root);
        if (ship != null)
        {
            Doors(record, ship, model, universe);
            if (model.Escort && PilotNear(ship, universe))
                _dealt[record.Id] = Now;
            Detail(record.Id, ship);
        }
    }

    private bool PilotNear(ShipBody ship, Universe universe)
    {
        foreach (IRadio radio in Radio.All(ship))
            if (Combat.ShipOf(radio as Node) is Node3D other && other != ship && universe.PilotIdOf(other) != null && other.GlobalPosition.DistanceTo(ship.GlobalPosition) <= AlertMeters)
                return true;
        return false;
    }

    // An escort off its way, flown by its pilot: it goes back on it from where it is once its enemies are gone. If its sector
    // went, it goes on from where it was last seen
    private void Fighting(CargoShipRecord record, ShipModel model, ShipBody ship, Universe universe, SectorHost host)
    {
        if (ship != null)
        {
            FloatingOrigin root = FloatingOrigin.Of(ship);
            SectorPoint at = root.Origin + ship.Position;
            record.FromSector = universe.Book.Space.Keys.FirstOrDefault(id => host.Root(id) == root) ?? record.FromSector;
            (record.FromX, record.FromY, record.FromZ) = (at.X, at.Y, at.Z);
            if (_duties.TryGetValue(record.Id, out EscortDuty duty))
            {
                if (!model.Escort)
                {
                    if (model.Fights && ship.Find<ShipHull>()?.Fraction < CrewOf(record, universe.State).RunBelowHull)
                        duty.Fights = false;
                    if (duty.Fights)
                        Forget(ship, duty);
                    else
                        Run(ship, duty);
                    if (universe.State.People.GetValueOrDefault(record.Owner)?.Livelihood == nameof(Livelihood.Pirate))
                        Plunder(record, model, ship, duty, universe);
                }
                if (!duty.Over(LookEverySeconds, StandDownSeconds))
                    return;
            }
        }
        var from = new Vector3((float)record.FromX, (float)record.FromY, (float)record.FromZ);
        CargoShips.Resume(record, model, record.FromSector, from, universe.State, universe.Book, universe.Account);
        if (ship == null || universe.Book.FixOf(record, model, universe.State.Clock) is not StationBook.Fix fix)
            return;
        if (ship.Find<FighterAi>() is FighterAi pilot)
        {
            pilot.Route.Clear();
            pilot.WantsCruise = false;
        }
        ship.Holder = new CargoRun(record, model, fix, FloatingOrigin.Of(ship));
    }

    // A cargo ship under attack and alone runs: for a station's zone if one is near and not past who attacked it, else
    // straight away from them, on its generator if it can. Far enough off it is clear of them
    private void Run(ShipBody ship, EscortDuty duty)
    {
        if (ship.Find<FighterAi>() is not FighterAi pilot)
            return;
        Vector3 here = ship.GlobalPosition;
        Forget(ship, duty);
        Node3D nearest = duty.Attackers.OrderBy(a => a.GlobalPosition.DistanceSquaredTo(here)).FirstOrDefault();
        pilot.Route.Clear();
        pilot.WantsCruise = nearest != null;
        if (nearest == null)
            return;
        Vector3 away = (here - nearest.GlobalPosition).Normalized();
        World3D world = Sectors.WorldOf(ship);
        StationRadio shelter = StationRadio.All.Where(s => s.World == world && s.GlobalPosition.DistanceTo(here) <= ShelterMeters && (s.GlobalPosition - here).Dot(away) > 0)
            .OrderBy(s => s.GlobalPosition.DistanceSquaredTo(here)).FirstOrDefault();
        pilot.RouteSpeed = ship.Spec.TopSpeed(ship.Spec.MainThrust);
        pilot.Route.Enqueue(shelter?.GlobalPosition ?? here + away * ClearMeters * 2);
    }

    // What a pirate came for is the cargo: with some drifting near it stops fighting and goes for that, and with
    // nothing in what it attacked there is nothing to stay for
    private void Plunder(CargoShipRecord record, ShipModel model, ShipBody ship, EscortDuty duty, Universe universe)
    {
        if (ship.Find<FighterAi>() is not FighterAi pilot)
            return;
        Vector3 here = ship.GlobalPosition;
        int room = (int)model.Hull.CargoCapacity - record.Loot.Values.Sum();
        var near = new List<Node>();
        Spatial.Grid(ship, Spatial.Pods).Within(here, PodMeters, near);
        CargoPod pod = near.OfType<CargoPod>().Where(p => IsInstanceValid(p) && p.Tonnes > 0).OrderBy(p => p.GlobalPosition.DistanceSquaredTo(here)).FirstOrDefault();
        if (pod != null && room > 0)
        {
            foreach (Node3D other in duty.Attackers.ToList())
                if (_shown.ContainsValue(other as ShipBody))
                    duty.Forget(other);
            pilot.Route.Clear();
            pilot.WantsCruise = false;
            pilot.RouteSpeed = 120f;
            pilot.Route.Enqueue(pod.GlobalPosition);
            if (pod.Clear && pod.GlobalPosition.DistanceTo(here) <= pod.ScoopMeters)
            {
                string good = pod.Good;
                record.Loot[good] = record.Loot.GetValueOrDefault(good) + pod.Take(room);
            }
            return;
        }
        foreach (Node3D other in duty.Attackers.ToList())
            if (_shown.FirstOrDefault(kv => kv.Value == other).Key is string id && universe.State.CargoShips.GetValueOrDefault(id)?.Tonnes == 0)
                duty.Forget(other);
    }

    // Who got too far off is none of its business any more
    private void Forget(ShipBody ship, EscortDuty duty)
    {
        Vector3 here = ship.GlobalPosition;
        foreach (Node3D other in duty.Attackers.ToList())
            if (other.GlobalPosition.DistanceTo(here) > ClearMeters)
                duty.Forget(other);
    }

    // Who guards the ship turns on its attacker, or the ship itself runs if it has no one: off its way, a pilot flying it
    private void Scramble(CargoShipRecord record, Node3D by, UniverseState state)
    {
        if (by == null)
            return;
        bool guarded = state.CargoShips.Values.Any(s => s.Ward == record.Id);
        foreach (CargoShipRecord guard in state.CargoShips.Values.Where(s => s.Ward == record.Id || s == record && !guarded).ToList())
            if (_duties.TryGetValue(guard.Id, out EscortDuty duty) && _shown.TryGetValue(guard.Id, out ShipBody ship) && GodotObject.IsInstanceValid(ship))
            {
                duty.Add(by);
                if (guard.Phase is not (CargoShipRecord.Flying or CargoShipRecord.Holding))
                    continue;
                guard.Phase = CargoShipRecord.Loose;
                _dealt[guard.Id] = double.MaxValue;
                ship.Wake();
                ship.Unshow();
                ship.Holder = null;
            }
    }

    // The game's own time, not the clock on the wall: a battle that runs faster or slower than real time rests its ships the same
    private static double Now => Engine.GetPhysicsFrames() / (double)Engine.PhysicsTicksPerSecond;

    // Level 0 while someone deals with it: it is hit, or a radar is locked on it; level 1 again a while after
    private void Detail(string id, ShipBody ship)
    {
        if (Dealt(ship))
            _dealt[id] = Now;
        if (!_dealt.TryGetValue(id, out double at))
            return;
        if (Now - at <= RestAfterSeconds)
            ship.Wake();
        else
        {
            ship.Rest();
            _dealt.Remove(id);
        }
    }

    private bool Dealt(ShipBody ship)
    {
        if (_struck.Remove(ship))
            return true;
        foreach (Node node in Sectors.InGroup(ship, "radar_emitters"))
            if ((node as INetRadar ?? node.GetParent() as INetRadar)?.Tracked is Node tracked && Combat.ShipOf(tracked) == ship)
                return true;
        return false;
    }

    public override void _EnterTree()
    {
        Combat.Damaged += Struck;
        Combat.Destroyed += Destroyed;
    }

    public override void _ExitTree()
    {
        Combat.Damaged -= Struck;
        Combat.Destroyed -= Destroyed;
    }

    private void Struck(Node3D victim, Node3D by)
    {
        if (victim is not ShipBody ship || !_shown.ContainsValue(ship))
            return;
        _struck.Add(ship);
        double now = Now;
        if (Universe.Current is not Universe universe)
            return;
        string id = _shown.First(kv => kv.Value == ship).Key;
        if (!(_called.TryGetValue(ship, out double at) && now - at < MaydayEverySeconds) && universe.State.CargoShips.TryGetValue(id, out CargoShipRecord record))
        {
            _called[ship] = now;
            Tell(record, ship, by, lost: false, universe);
            Mayday(ship, record.Pilot != "" ? record.Pilot : CargoShips.CallsignOf(id));
            Scramble(record, by, universe.State);
        }
        if (universe.State.CargoShips.TryGetValue(id, out CargoShipRecord hit) && ResourceLoader.Exists(hit.Model) && Xpace.World.ResourceCache.Load<ShipModel>(hit.Model) is ShipModel { Fights: false, Escort: false })
            Think(hit, CrewOf(hit, universe.State), ship, by, universe.State);
    }

    // A crew that can't fight may give up what it carries, in the hope that is all they came for. It holds on longer for
    // what is worth more, near a protected zone or with help coming, and in the end it has to let go
    private void Think(CargoShipRecord record, Personality crew, ShipBody ship, Node3D by, UniverseState state)
    {
        double worth = record.Tonnes * (GoodsCatalog.Find(record.Good)?.BasePrice ?? 0) + record.Loot.Sum(l => l.Value * (GoodsCatalog.Find(l.Key)?.BasePrice ?? 0));
        if (worth <= 0 || ship.Find<ShipHull>() is not ShipHull hull)
            return;
        if (hull.Fraction > crew.JettisonLastHull)
        {
            if (hull.Fraction >= crew.JettisonAt(worth, ToZone(ship), HelpComing(record, by, ship, state)))
                return;
            if (_thought.TryGetValue(ship, out double at) && Now - at < crew.JettisonThinkSeconds)
                return;
            _thought[ship] = Now;
            if (_nerve.NextDouble() >= crew.JettisonChance)
                return;
        }
        Spill(record, ship);
    }

    // Who is aboard: a person is their own crew; a company's ships have people of its own kind, until crews have names
    private static Personality CrewOf(CargoShipRecord record, UniverseState state) =>
        state.People.GetValueOrDefault(record.Owner)?.Personality ?? OrganisationCatalog.Find(record.Owner)?.Personality ?? Ordinary;

    private static readonly Personality Ordinary = new();

    // How far the nearest protected zone is
    private static float ToZone(ShipBody ship)
    {
        World3D world = Sectors.WorldOf(ship);
        Vector3 here = ship.GlobalPosition;
        float nearest = float.MaxValue;
        foreach (StationRadio station in StationRadio.All)
            if (station.World == world && station.Laws != null)
                nearest = Mathf.Min(nearest, Mathf.Max(0, station.GlobalPosition.DistanceTo(here) - station.Laws.ZoneRadius));
        return nearest;
    }

    // Its escort, or anyone with orders against who attacked it: another ship's, or a station's police
    private bool HelpComing(CargoShipRecord record, Node3D by, ShipBody ship, UniverseState state)
    {
        if (state.CargoShips.Values.Any(s => s.Ward == record.Id) || _duties.Any(d => d.Key != record.Id && d.Value.Fights && d.Value.Attackers.Contains(by)))
            return true;
        World3D world = Sectors.WorldOf(ship);
        return StationRadio.All.Any(s => s.World == world && s.GetParent()?.GetNodeOrNull<StationSecurity>("Security")?.Hostiles.Contains(by) == true);
    }

    // What a ship carries goes out into space
    private static void Spill(CargoShipRecord record, ShipBody ship)
    {
        var goods = new Dictionary<string, int>(record.Loot);
        if (record.Tonnes > 0 && record.Good != "")
            goods[record.Good] = goods.GetValueOrDefault(record.Good) + record.Tonnes;
        if (goods.Count > 0)
        {
            CargoPod.Spill(ship, ship.LinearVelocity, goods);
            record.Tonnes = 0;
            record.Loot.Clear();
        }
    }

    private void Destroyed(Node3D victim, Node3D by, Vector3 at)
    {
        if (victim is ShipBody ship && _shown.ContainsValue(ship))
            _killers[ship] = by;
        if (Universe.Current is not Universe { IsGuest: false } universe || victim == null || by == null || victim == by)
            return;
        if (universe.ActorOf(victim) is string killed && universe.AccountOfActor(universe.ActorOf(by)) is IAccount killer && Reactions.Collect(universe.State, killed, killer) > 0)
            universe.Touch(killer as PilotRecord);
    }

    // The owner hears what was done, and where the record says it was
    private static void Tell(CargoShipRecord record, ShipBody ship, Node3D by, bool lost, Universe universe)
    {
        if (!ResourceLoader.Exists(record.Model) || Xpace.World.ResourceCache.Load<ShipModel>(record.Model) is not ShipModel model || universe.Book.FixOf(record, model, universe.State.Clock) is not StationBook.Fix fix)
            return;
        string actor = by != null && GodotObject.IsInstanceValid(by) ? universe.ActorOf(by) : "";
        Reactions.Happened(universe.Account(record.Owner) as OrganisationRecord, OrganisationCatalog.Find(record.Owner)?.Personality, model, fix.Sector, fix.At, actor, universe.State.Clock, lost);
        if (universe.State.People.GetValueOrDefault(record.Owner) is PersonRecord person)
            Memories.Note(person, actor, Memories.HitMe, -1, universe.State.Clock, fix.Sector, person.Personality?.MemoryHours ?? 48);
        universe.Touch();
    }

    // Every radio that can hear the ship hears the call
    private void Mayday(ShipBody ship, string sign)
    {
        if (ship.FindChild("Radio", true, false) is not RadioDevice radio)
            return;
        foreach (IRadio r in Radio.All(ship))
            if (r is IRadioReceiver listener && !ReferenceEquals(r, radio) && Radio.Hears(listener, radio))
                listener.Hear(radio, string.Format(MaydayText, sign), "");
    }

    private ShipBody Bring(CargoShipRecord record, ShipModel model, StationBook.Fix fix, FloatingOrigin root)
    {
        if (model.Scene.Instantiate() is not ShipBody ship)
            return null;
        ship.Name = $"Cargo_{record.Id}";
        if (ship.FindChild("Radio", true, false) is RadioDevice radio)
            radio.Callsign = CargoShips.CallsignOf(record.Id);
        var parts = ship.FindChildren("*", "", true, false).ToList();
        foreach (NetBody body in parts.OfType<NetBody>())
            body.Spawned = true;
        foreach (ShipHull hull in parts.OfType<ShipHull>())
            hull.Insured = false;
        if (ship.FindChildren("*", "", true, false).OfType<CruiseDevice>().FirstOrDefault() is CruiseDevice cruise)
            cruise.Spec = model.Generator;
        _duties[record.Id] = new EscortDuty { Fights = model.Escort || model.Fights };
        if (Universe.Current.State.People.TryGetValue(record.Owner, out PersonRecord person))
        {
            ship.Actor = person.Id;
            if (ship.FindChild("Radio", true, false) is RadioDevice own)
                own.Callsign = person.Last.ToUpperInvariant();
            foreach (Datalink link in parts.OfType<Datalink>())
                link.Channel = person.Id;
        }
        foreach (FighterAi pilot in parts.OfType<FighterAi>())
            pilot.Engagement = _duties[record.Id];
        var run = new CargoRun(record, model, fix, root);
        ship.Transform = run.Pose(fix);
        ship.Holder = run;
        foreach ((string place, string piece) in record.Fittings)
            if (ResourceLoader.Exists(piece))
                ship.Loadout[place] = Xpace.World.ResourceCache.Load<Resource>(piece);
        if (ship.FindChild("ShipComputer", true, false) is Xpace.Computer.ShipComputer computer)
            computer.PowerOnAtStart = false;
        ship.RecordId = record.Id;
        root.AddChild(ship);
        foreach (LauncherDevice launcher in parts.OfType<LauncherDevice>())
            launcher.Used(record.Spent);
        foreach (DispenserDevice dispenser in parts.OfType<DispenserDevice>())
            dispenser.Used(record.Spent);
        foreach (ShipHull hull in parts.OfType<ShipHull>())
            hull.Mend(record.Hull);
        ship.Rest();
        _shown[record.Id] = ship;
        return ship;
    }

    // Its station opens the door for it when it is cleared to leave, or to land, until it is through
    private void Doors(CargoShipRecord record, ShipBody ship, ShipModel model, Universe universe)
    {
        double since = universe.State.Clock - record.Left;
        if (record.Phase == CargoShipRecord.Landing)
            StationRadio.Called(record.Going)?.OpenFor(record.Dock, ship, DoorSeconds);
        else if (record.Phase == CargoShipRecord.Flying && record.LeftDock != 0 && since < model.DoorSeconds + DoorSeconds * 3)
            StationRadio.Called(record.From)?.OpenFor(record.LeftDock, ship, DoorSeconds);
    }
}

/// <summary>Holds a cargo ship where its record says: on its pad, holding off a station, or on its way, turning towards it at the speed its numbers give.</summary>
public sealed class CargoRun : IShipHold
{
    /// <summary>How fast it turns to a new heading, radians a second.</summary>
    private const float TurnRate = 0.5f;

    private readonly CargoShipRecord _record;
    private readonly ShipModel _model;
    private readonly FloatingOrigin _root;
    private Quaternion _facing;

    public CargoRun(CargoShipRecord record, ShipModel model, StationBook.Fix fix, FloatingOrigin root)
    {
        _record = record;
        _model = model;
        _root = root;
        _facing = Facing(fix.Heading);
    }

    private static Quaternion Facing(Vector3 heading) =>
        Basis.LookingAt(heading.LengthSquared() > 0 ? heading : Vector3.Forward, Mathf.Abs(heading.Y) > 0.99f ? Vector3.Forward : Vector3.Up).GetRotationQuaternion();

    private Vector3 Local(Vector3 at) => _root.Origin.To(new SectorPoint(at.X, at.Y, at.Z));

    public Transform3D Pose(StationBook.Fix fix) => new(new Basis(_facing), Local(fix.At));

    public ShipState Hold(ShipBody ship, double delta)
    {
        if (ship.Find<ShipHull>()?.Wrecked == true)
        {
            ship.Holder = null;
            return ship.State;
        }
        UniverseState state = Universe.Current.State;
        StationBook.Fix? where = Universe.Current.Book.FixOf(_record, _model, state.Clock);
        if (where is not StationBook.Fix fix)
            return ship.State;
        CruiseDevice cruise = ship.Device<CruiseDevice>();
        if (ship.Dormant)
            cruise?.Fade((float)delta);
        float speed = fix.Speed;
        _record.ShownAt = state.Clock;
        if (_record.Phase == CargoShipRecord.Flying)
            speed = Interdiction.Step(_record, _model, Universe.Current.Book.Space[fix.Sector], fix.At, fix.Speed, cruise?.BeamDensity ?? 0, cruise?.BeamBand ?? 0, (float)delta);
        float angle = _facing.AngleTo(Facing(fix.Heading));
        if (angle > 0.0001f)
            _facing = _facing.Slerp(Facing(fix.Heading), Mathf.Min(1, TurnRate * (float)delta / angle)).Normalized();
        bool cruising = speed > _model.Hull.TopSpeed(_model.Engine?.RatedThrust ?? _model.Hull.MainThrust) * 1.05f;
        ship.Show(speed > 0 ? 0.6f : 0, cruising ? 1 : 0);
        if (cruise != null)
            cruise.Riding = cruising;
        return new ShipState { Position = Local(fix.At), Orientation = _facing, Velocity = fix.Heading * speed };
    }
}
