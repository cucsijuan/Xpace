using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Xpace.World;

/// <summary>
/// Everything the game keeps of the universe between plays, as plain data: no nodes, nothing of Godot's, keyed by
/// ids that outlive any scene (a pilot's id, a station's id). Whoever owns the universe (the host; alone, the
/// player's own machine) holds it and changes it; the scene's nodes read and write it through <see cref="Universe"/>.
///
/// It is the universe's whole state, so that it can grow into one: each kind of thing has its own records, each
/// record stamped with the universe time it was last brought up to date (<see cref="StationRecord.UpdatedAt"/>), so a
/// simulation can later advance what nobody is looking at from there to now, lazily, the first time someone does.
/// <see cref="UniverseStore"/> keeps it on disk as a folder of documents, one for each record.
/// </summary>
public sealed class UniverseState
{
    /// <summary>The documents' format; older ones are brought up to it as they load (<see cref="UniverseStore"/>).</summary>
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Universe time: seconds the universe has run. It runs while anyone plays in it.</summary>
    public double Clock { get; set; }

    public Dictionary<string, PilotRecord> Pilots { get; set; } = new();
    public Dictionary<string, StationRecord> Stations { get; set; } = new();
    public Dictionary<string, OrganisationRecord> Organisations { get; set; } = new();
    public Dictionary<string, CargoShipRecord> CargoShips { get; set; } = new();
    public Dictionary<string, ShipOrderRecord> ShipOrders { get; set; } = new();
    public Dictionary<string, PersonRecord> People { get; set; } = new();
    /// <summary>Where each actor was last seen, for those who look for them.</summary>
    public Dictionary<string, Sighting> Sightings { get; set; } = new();
    /// <summary>How many people have come so far (it numbers them), and whether the first ones were given their ships.</summary>
    public int PeopleEver { get; set; }
    public bool PeopleMustered { get; set; }
    /// <summary>When someone new may come for each line of the population.</summary>
    public Dictionary<int, double> NextArrival { get; set; } = new();
    /// <summary>What is on sale now (<see cref="Solvency"/>).</summary>
    public Dictionary<string, SaleRecord> Sales { get; set; } = new();

    /// <summary>A pilot's record, made the first time the pilot comes (with what a new pilot has).</summary>
    public PilotRecord Pilot(string id, string name = null)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        if (!Pilots.TryGetValue(id, out PilotRecord pilot))
        {
            Pilots[id] = pilot = new PilotRecord { Id = id, Name = name ?? id, Credits = PilotRecord.StartingCredits };
            pilot.Items.Add(ItemRecord.InsurersBackup());
        }
        else if (!string.IsNullOrEmpty(name))
            pilot.Name = name;
        return pilot;
    }

    /// <summary>An organisation's record, made the first time it is needed, as <paramref name="found"/> sets it up.</summary>
    public OrganisationRecord Organisation(string id, Func<OrganisationRecord> found)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        if (!Organisations.TryGetValue(id, out OrganisationRecord record))
            Organisations[id] = record = found();
        return record;
    }

    /// <summary>A station's record, made the first time it is needed.</summary>
    public StationRecord Station(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        if (!Stations.TryGetValue(id, out StationRecord station))
            Stations[id] = station = new StationRecord { Id = id, UpdatedAt = Clock };
        return station;
    }
}

/// <summary>A working cargo ship: it exists here, wherever it is and whether anyone sees it or not (<see cref="CargoShips"/>).</summary>
public sealed class CargoShipRecord
{
    public const string Waiting = "waiting", Fetching = "fetching", Returning = "returning", Escorting = "escorting";
    public const string Parked = "parked", Flying = "flying", Holding = "holding", Landing = "landing";
    /// <summary>Off its way, a real ship flown by its pilot: an escort fighting.</summary>
    public const string Loose = "loose";

    public string Id { get; set; }
    /// <summary>Its <see cref="ShipModel"/>, by resource path.</summary>
    public string Model { get; set; }
    /// <summary>Who owns it, as <see cref="StationRecord.Owner"/>.</summary>
    public string Owner { get; set; }
    /// <summary>The station it works for, and the one it last stood at.</summary>
    public string Home { get; set; }
    public string At { get; set; }
    /// <summary>The job in hand: <see cref="Waiting"/>, <see cref="Fetching"/> or <see cref="Returning"/>.</summary>
    public string State { get; set; } = Waiting;
    /// <summary>What it is doing now: <see cref="Parked"/>, <see cref="Flying"/>, <see cref="Holding"/> or <see cref="Landing"/>.</summary>
    public string Phase { get; set; } = Parked;
    /// <summary>Its pad: the dock and pad numbers it stands on or is cleared to (0: none).</summary>
    public int Dock { get; set; }
    public int Pad { get; set; }
    /// <summary>It has asked here and been told to hold.</summary>
    public bool AskedHold { get; set; }
    /// <summary>The station and pad it set out from on this voyage.</summary>
    public string From { get; set; } = "";
    public int LeftDock { get; set; }
    public int LeftPad { get; set; }
    /// <summary>Where it asked for a pad on this voyage, in its sector.</summary>
    public double[] Contact { get; set; } = new double[3];
    [JsonIgnore] public Godot.Vector3 ContactPoint
    {
        get => new((float)Contact[0], (float)Contact[1], (float)Contact[2]);
        set => Contact = new double[] { value.X, value.Y, value.Z };
    }
    /// <summary>Where it is going, and the universe times it left and gets there.</summary>
    public string Going { get; set; } = "";
    public double Left { get; set; }
    public double Arrives { get; set; }
    /// <summary>The ship it goes with (an escort).</summary>
    public string Ward { get; set; } = "";
    /// <summary>Whose way it flies at: the model and the load of the ship it goes with (empty: its own).</summary>
    public string PaceModel { get; set; } = "";
    public int PaceTonnes { get; set; }
    /// <summary>It set out from a point in space, not from a station (From empty): the sector, and where.</summary>
    public string FromSector { get; set; } = "";
    public double FromX { get; set; }
    public double FromY { get; set; }
    public double FromZ { get; set; }
    /// <summary>Its generator, on its way.</summary>
    public Xpace.Ship.GeneratorState Generator { get; set; } = new() { On = true };
    /// <summary>How fast it goes now if something is holding it back (below 0: as its way says).</summary>
    public float Pace { get; set; } = -1;
    /// <summary>The time its way has been worked out to, and last shown as a real ship.</summary>
    public double WorkedTo { get; set; }
    public double ShownAt { get; set; } = -10;
    [JsonIgnore] public CrewTuner Crew { get; } = new();
    /// <summary>Its way, once asked for, while it flies.</summary>
    [JsonIgnore] public List<StationBook.Leg> Legs { get; set; }
    /// <summary>When on that way, in seconds out, a fixed interdictor can reach it.</summary>
    [JsonIgnore] public (double From, double To) Beset { get; set; }
    /// <summary>When it next looks for something to do.</summary>
    public double NextLook { get; set; }
    /// <summary>What it is after or carrying, and the tonnes aboard.</summary>
    public string Good { get; set; } = "";
    public int Tonnes { get; set; }
    /// <summary>What is fitted on it, if other than what its hull comes with: hardpoint name to the piece's resource path.</summary>
    public Dictionary<string, string> Fittings { get; set; } = new();
    /// <summary>Hydrogen in its tank, tonnes (below 0: full, as a new ship comes).</summary>
    public double Fuel { get; set; } = -1;
    /// <summary>It set out without the fuel for its way: it goes on its engine, slowly.</summary>
    public bool Dry { get; set; }
    /// <summary>How much its crew reckoned worth buying on this trip (0: as much as there is room for).</summary>
    public int Planned { get; set; }
    /// <summary>Who flies it, for those who hear them, if someone in particular does.</summary>
    public string Pilot { get; set; } = "";
    /// <summary>How much of its hull is left, 0 to 1.</summary>
    public float Hull { get; set; } = 1;
    /// <summary>What it has used of what it carries, by the model's id: missiles, chaff, flares, to be bought again.</summary>
    public Dictionary<string, int> Spent { get; set; } = new();
    /// <summary>What it took out of space, by good: stolen, to sell off.</summary>
    public Dictionary<string, int> Loot { get; set; } = new();
    /// <summary>Tonnes it has brought home in all.</summary>
    public long Delivered { get; set; }
}

/// <summary>A ship ordered from a shipyard, to take the place of one lost.</summary>
public sealed class ShipOrderRecord
{
    public string Id { get; set; }
    /// <summary>Its <see cref="ShipModel"/>, by resource path.</summary>
    public string Model { get; set; }
    public string Owner { get; set; }
    /// <summary>The station it will work for, and the one building it.</summary>
    public string Home { get; set; }
    public string Yard { get; set; }
    /// <summary>What the buyer wants fitted: hardpoint name to the piece's resource path.</summary>
    public Dictionary<string, string> Fittings { get; set; } = new();
    /// <summary>The universe time it will be ready (0: not started: waiting for materials or money).</summary>
    public double Ready { get; set; }
}

/// <summary>Whoever can own things and pay for them: a pilot or an organisation, the same to a market.</summary>
public interface IAccount
{
    string Name { get; }
    long Credits { get; set; }
    /// <summary>What it asks on top of what a thing costs it to make, of that cost.</summary>
    double Margin { get; }
    /// <summary>What it knows of each station's market, by station id.</summary>
    Dictionary<string, StationNews> Known { get; }
}

/// <summary>Where someone was seen, and when.</summary>
public sealed class Sighting
{
    public string Sector { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Clock { get; set; }
    [JsonIgnore] public Godot.Vector3 At => new((float)X, (float)Y, (float)Z);
}

/// <summary>A free pilot of the universe (<see cref="People"/>): someone with a name, on their own account.</summary>
public sealed class PersonRecord : IAccount, IRemembers
{
    /// <summary>A person's id, and so their name as an owner, starts with this.</summary>
    public const string OwnerPrefix = "person-";

    public string Id { get; set; }
    public string First { get; set; } = "";
    public string Last { get; set; } = "";
    [JsonIgnore] public string Name => $"{First} {Last}";
    /// <summary>How they live, and the line of the population they belong to.</summary>
    public string Livelihood { get; set; } = "";
    public int Line { get; set; }
    /// <summary>Their <see cref="World.Personality"/>, by resource path.</summary>
    public string Character { get; set; } = "";
    [JsonIgnore] public Personality Personality => Godot.ResourceLoader.Exists(Character) ? Xpace.World.ResourceCache.Load<Personality>(Character) : null;
    public long Credits { get; set; }
    [JsonIgnore] public double Margin => 0;
    public Dictionary<string, StationNews> Known { get; set; } = new();
    public List<MemoryEntry> Memories { get; set; } = new();
    public int MemoryRoom { get; set; } = 16;
    /// <summary>When their money ran out (below 0: they have some).</summary>
    public double BrokeSince { get; set; } = -1;
    /// <summary>The actor they are after (empty: nobody).</summary>
    public string Quarry { get; set; } = "";
}

/// <summary>A company, corporation, guild or faction as it stands now, known by an id (for one set up by hand, its <see cref="Organisation"/>'s).</summary>
public sealed class OrganisationRecord : IAccount, IRemembers
{
    public string Id { get; set; }
    public string Name { get; set; }
    public long Credits { get; set; }
    public double Margin { get; set; } = 0.15;
    public Dictionary<string, StationNews> Known { get; set; } = new();
    /// <summary>What was done to its ships lately, oldest first.</summary>
    public List<Incident> Incidents { get; set; } = new();
    /// <summary>What it offers for each actor destroyed, by actor id (<see cref="Universe.ActorOf"/>).</summary>
    public Dictionary<string, long> Bounties { get; set; } = new();
    public List<MemoryEntry> Memories { get; set; } = new();
    public int MemoryRoom { get; set; } = 64;
    /// <summary>When it ran out of money (below 0: it has some).</summary>
    public double BrokeSince { get; set; } = -1;
    /// <summary>The organisation it belongs to now (empty: its own). It can join and leave.</summary>
    public string Parent { get; set; } = "";
}

/// <summary>A pilot: a player, wherever they play from, known by an id of their own (<see cref="Universe.LocalPilotId"/>).</summary>
public sealed class PilotRecord : IAccount
{
    /// <summary>A pilot as the owner of something (<see cref="StationRecord.Owner"/>): this and their id.</summary>
    public const string OwnerPrefix = "pilot:";

    [JsonIgnore] public double Margin => 0.15;

    /// <summary>What a new pilot has to start with.</summary>
    public const long StartingCredits = 1000;

    public string Id { get; set; }
    public string Name { get; set; }
    public long Credits { get; set; }
    public Dictionary<string, StationNews> Known { get; set; } = new();

    /// <summary>Seconds played in this universe: the clock that infamy wears off by.</summary>
    public double PlayedSeconds { get; set; }

    /// <summary>The ship the pilot flies.</summary>
    public ShipRecord Ship { get; set; } = new();

    /// <summary>What the pilot carries on them (at most <see cref="InventorySlots"/> things).</summary>
    public List<ItemRecord> Items { get; set; } = new();

    public const int InventorySlots = 8;

    /// <summary>Takes <paramref name="amount"/> if the pilot has it.</summary>
    public bool Spend(long amount)
    {
        if (amount < 0 || Credits < amount)
            return false;
        Credits -= amount;
        return true;
    }
}

/// <summary>Something a pilot carries on them or keeps in a station's locker: diskettes now, later weapons and more.</summary>
public sealed class ItemRecord
{
    public const string Diskette = "diskette", Piece = "piece";

    public string Kind { get; set; } = Diskette;
    public string Name { get; set; } = "";
    /// <summary>What the kind needs to know of it (a diskette: the image it holds).</summary>
    public string Data { get; set; } = "";

    /// <summary>The diskette the insurer gives every pilot: the ship's software as it came from the factory.</summary>
    public static ItemRecord InsurersBackup() => new() { Kind = Diskette, Name = "Insurer's backup", Data = "factory" };

    /// <summary>A piece of a ship's equipment off its hardpoints (a laser, a turret): Data is the model's resource path.</summary>
    public static ItemRecord PieceOf(Xpace.Space.Outfit model) => new() { Kind = Piece, Name = model.Model, Data = model.ResourcePath };
}

/// <summary>A pilot's ship: its hull and what its hold carries.</summary>
public sealed class ShipRecord
{
    /// <summary>The hull every new pilot is given.</summary>
    public const string StarterHull = "res://assets/ship/hulls/hull_fighter.tres";

    /// <summary>The insurer's free basic ship, for a pilot whose own was impounded.</summary>
    public const string CourtesyHull = "res://assets/ship/hulls/hull_courtesy.tres";

    /// <summary>The hull's spec (a <see cref="Xpace.Ship.ShipSpec"/> resource's path).</summary>
    public string Hull { get; set; } = StarterHull;

    /// <summary>Tonnes of each good aboard, by the good's id (<see cref="Good.Id"/>).</summary>
    public Dictionary<string, int> Cargo { get; set; } = new();

    [JsonIgnore] public int Tonnes
    {
        get
        {
            int tonnes = 0;
            foreach (int t in Cargo.Values)
                tonnes += t;
            return tonnes;
        }
    }

    /// <summary>
    /// Missiles, chaff and flares aboard, by the model's id (<see cref="Xpace.World.OutfitLine.Id"/>). A model not listed
    /// has what the factory gave the ship: a new ship comes loaded.
    /// </summary>
    public Dictionary<string, int> Magazine { get; set; } = new();

    /// <summary>Hydrogen in the tank, tonnes (below 0: full, as a new ship comes).</summary>
    public double Fuel { get; set; } = -1;

    /// <summary>
    /// What is mounted on its hardpoints where it differs from the factory's (<see cref="Xpace.Ship.ShipSpec.Fitting"/>):
    /// by the hardpoint's name, the model's resource path, or "" for nothing.
    /// </summary>
    public Dictionary<string, string> Fittings { get; set; } = new();

    public int Holds(string good) => Cargo.TryGetValue(good, out int t) ? t : 0;

    /// <summary>How many of a model are aboard, where a full rack is <paramref name="factory"/>.</summary>
    public int Rack(string model, int factory) => Magazine.TryGetValue(model, out int n) ? n : factory;

    /// <summary>Adds (or with a negative count takes away) tonnes of a good; none left of it, it goes from the list.</summary>
    public void Load(string good, int tonnes)
    {
        int now = Holds(good) + tonnes;
        if (now > 0)
            Cargo[good] = now;
        else
            Cargo.Remove(good);
    }
}

/// <summary>A station: what it holds against each pilot, and later its market and lockers.</summary>
public sealed class StationRecord
{
    /// <summary>What it is known by, for good, whoever owns it and whatever it is called.</summary>
    public string Id { get; set; }

    /// <summary>What it has been renamed in play, as people read it and on the radio (empty: as its scene says).</summary>
    public string Title { get; set; } = "";
    public string Callsign { get; set; } = "";

    /// <summary>The universe time this station was last brought up to date.</summary>
    public double UpdatedAt { get; set; }

    /// <summary>Who owns it: an organisation's id, or a pilot's with <see cref="PilotRecord.OwnerPrefix"/> (empty: as its scene says).</summary>
    public string Owner { get; set; } = "";

    /// <summary>
    /// The least its owner sells each thing for, by id, as of <see cref="UpdatedAt"/>: what it costs to make here, with
    /// its share of the upkeep and the owner's margin (<see cref="Market.Advance"/>). Nothing for what it doesn't make.
    /// </summary>
    public Dictionary<string, double> Floors { get; set; } = new();

    /// <summary>The pads taken by cargo ships, as "dock:pad", and by which.</summary>
    public Dictionary<string, string> CargoPads { get; set; } = new();

    /// <summary>Its fleet has come into being, once: from then on ships are built, not given.</summary>
    public bool Mustered { get; set; }
    /// <summary>Its people have run out of something they need, or its owner can't pay for it: it makes nothing, and can still sell what it has.</summary>
    public bool Idle { get; set; }
    /// <summary>When it last began to run short of something (below 0: it isn't), and the ships it has added to its fleet.</summary>
    public double ShortSince { get; set; } = -1;
    /// <summary>When its yard last sold a ship.</summary>
    public double LastSold { get; set; }
    public int ExtraShips { get; set; }
    /// <summary>Nobody wanted it: it stands idle until someone buys it.</summary>
    public bool Abandoned { get; set; }

    /// <summary>How many ships it has ordered in all.</summary>
    public int Ordered { get; set; }

    /// <summary>Pads a ship just left, as "dock:pad", and the universe time they can be given again.</summary>
    public Dictionary<string, double> PadsResting { get; set; } = new();

    /// <summary>Credits it is yet to pay or be paid, less than one: what its people use comes a little at a time.</summary>
    public double CashCarry { get; set; }

    /// <summary>Tonnes of each good its market has (by the good's id), as of <see cref="UpdatedAt"/>.</summary>
    public Dictionary<string, double> Stock { get; set; } = new();

    /// <summary>Each pilot's locker here (by the pilot's id): what they left at this station.</summary>
    public Dictionary<string, List<ItemRecord>> Lockers { get; set; } = new();

    public List<ItemRecord> LockerOf(string pilot)
    {
        if (!Lockers.TryGetValue(pilot, out List<ItemRecord> locker))
            Lockers[pilot] = locker = new List<ItemRecord>();
        return locker;
    }

    /// <summary>Ships impounded here for debts their pilots couldn't pay (by the pilot's id), kept free until paid.</summary>
    public Dictionary<string, ShipRecord> Impounded { get; set; } = new();

    /// <summary>What the station holds against each pilot (by the pilot's id): its jurisdiction.</summary>
    public Dictionary<string, CrimeRecord> Crimes { get; set; } = new();

    public CrimeRecord CrimesOf(string pilot)
    {
        if (!Crimes.TryGetValue(pilot, out CrimeRecord record))
            Crimes[pilot] = record = new CrimeRecord();
        return record;
    }
}

/// <summary>
/// What one jurisdiction holds against one pilot (<see cref="Justice"/> has the rules). Money owed never goes away by
/// itself; infamy wears off with the pilot's played time.
/// </summary>
public sealed class CrimeRecord
{
    /// <summary>Fines owed (credits): speeding, firing in the zone. Owed, not wanted.</summary>
    public long Fines { get; set; }

    /// <summary>The bounty on the pilot (credits): damaging or destroying a ship that had done nothing. Wanted till paid.</summary>
    public long Bounty { get; set; }

    /// <summary>Infamy as it was at <see cref="InfamyAsOf"/> of the pilot's played time.</summary>
    public double Infamy { get; set; }
    public double InfamyAsOf { get; set; }

    [JsonIgnore] public long Owed => Fines + Bounty;
    [JsonIgnore] public bool Clean => Fines == 0 && Bounty == 0 && Infamy <= 0;
}
