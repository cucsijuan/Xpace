# The universe: what is kept, who owns it, money, cargo and the law

Everything the game keeps between plays is the **universe's state** (`scripts/World/UniverseState.cs`): plain data,
nothing of Godot's, keyed by ids that outlive any scene. Today it holds pilots and stations; it is meant to grow into
the whole universe (sectors, ships, factories, factions), one kind of record at a time.

## Who owns it

- The machine that owns the universe holds the whole state and is the only one that changes it: **the host**, or the
  player's own machine alone. It is the `Universe` autoload (`scripts/World/Universe.cs`).
- A **guest** owns none of it. It says who flies its ship when it joins (`Session.Introduce`: the pilot's id and name),
  and the host answers with that pilot's record, again whenever it changes. Anything the guest wants done (a trade, a
  payment, a locker) is a request to the host, as docking is: `Universe.Ask(topic, json)` goes to the host
  (`Session.Ask`), which answers with a document on the same topic (`Session.SendDocument`, cut into parts: EOS
  carries 1,170 bytes a packet). Alone or on the host the answer comes at once.
- A **pilot** is a player, known by an id of their own made once on their computer (`GameSettings.PilotId`, in
  `user://settings.cfg`; `XPACE_PILOT` names another, for several players on one computer). A guest's pilot keeps
  their record in each host's universe they play in.
- Which pilot flies a ship: `Universe.PilotIdOf(ship)`: the local player's ship (a `NetBody` owned by the pilot), a
  guest's ship (a `RemoteShip`, by its peer), or one named with `Universe.Fly` (tests).

## On disk

`UniverseStore` keeps the state as a folder of JSON documents, one for each record:

```
user://universe/
    universe.json            the format's version and the universe's clock
    pilots/{id}.json         one for each pilot
    stations/{callsign}.json one for each station
```

Each document is written whole to a new file that then replaces the old one, so a crash leaves one or the other. Each
says its format's version; a document from an older version is brought up to date as it loads (`UniverseStore.Upgrade`:
add a step there, and raise `UniverseState.CurrentVersion`, for any change an old document would be misread by; new
fields with defaults need none). A universe saved by a newer game is refused.

The main menu opens the saved universe when the game starts (`Universe.Open`), and so does the command line when it
hosts. Any other scene (a test, the main scene run on its own) plays in a universe kept only in memory. A change is
saved a few seconds after it (`Universe.Touch`), the clocks alone once a minute, and everything on the way out.

## Time and simulation

The universe has a clock (`UniverseState.Clock`) that runs while anyone plays; each pilot has their own played time
(`PilotRecord.PlayedSeconds`), which runs while they play. Records that change with time are stamped with the
universe time they were last brought up to date (`StationRecord.UpdatedAt`) and are brought to now when someone looks
at them (`Market.Advance`). That is the hook for simulating what nobody is looking at: a later simulation can advance
any record from its stamp, lazily, without the game running every station all the time.

## Money and cargo

- Pilots start with 1,000 credits and the insurer's backup diskette.
- **Cargo is only in ships**, in tonnes. A hull's internal compartments (`ShipSpec.Compartments`, each by the largest
  module size it takes) hold cargo racks (`ShipSpec.CargoRacks`; size n holds 2^n tonnes). Factory racks are set in
  the hull's spec for now; swapping them will be a retrofit hangar's job.
- A player's cargo is in their record (`ShipRecord.Cargo`), not in the scene. The ship's hold device (`CargoHold`,
  bus slot 17, 210h) shows it to the computer and puts its weight on the ship (`ShipBody.CargoMass`): a laden ship is
  slower to push and to turn, and crashes harder. Cargo goes with the ship when it blows up.
- **Character inventory and lockers:** a pilot carries up to 8 things on them (`PilotRecord.Items`: diskettes now) and
  has a locker at each station (`StationRecord.Lockers`), each its own.

## Markets

A station's market (`StationMarket`, beside its radio in the station's scene) deals in the goods listed on it
(`MarketGood`: the good, the stock the market wants, tonnes an hour it makes or uses up). The rules are `Market`'s:
stock moves with universe time between none and three times what the market wants; a tonne's price is the good's base
price at the wanted stock, dearer as it runs short and cheaper as it piles up (square root, between half and three
times the base); each tonne traded moves the price; a pilot sells at 90% of the price of the tonne added, so buying and
selling back always loses. Goods are resources in `assets/goods` (their id is the file's name) listed in
`assets/goods/catalog.tres`.

Pilots trade docked at the station, on foot at the market's kiosk in the hangar (`StationKiosk` +
`MarketWindow`); the lockers' kiosk beside it opens the locker (`StationLockers` + `LockerWindow`). Later trading moves
to a market zone inside the station and refits to a hangar of their own.

A pilot is present at a station when their ship is docked there or inside one of its hangars, or when they are on
foot there. The market and the lockers both check this. The host's universe knows where each pilot is on foot: the
local player's walker, or a guest's as the guest's machine reports it in the walker states (`Universe.OnFootAt`).

The sector has two stations with complementary markets: the first (industry: metals, machinery, hydrogen) and Kestrel
(water, food, ore, medicines), about 24 km apart.

## The law

Each station keeps a record of what it holds against each pilot (`CrimeRecord`, its jurisdiction; factions later),
with `Justice`'s rules; `StationSecurity` writes it from what it sees ships do in its zone:

| What | Cost | What follows |
|---|---|---|
| Speeding | a fine (200) | none: dock, trade and pay as anyone |
| Firing on after the warning | a fine (500) | the police engage, until the ship is out of the zone a while |
| Damaging a ship that did nothing | a bounty (1,000) | engaged; docked, held on the pad until the bounty is paid |
| Destroying one | a bounty (5,000) and infamy (2) | engaged; while infamy is 1 or more: docking refused and attacked on sight |

- Money owed never goes away by itself. Infamy wears off at 0.5 an hour of the pilot's **played** time, and paying
  doesn't take it away.
- Only a ship the station can tell is put on a pilot's record: one with its radio on. With the radio off it can't be
  told (scanning comes later); it is still engaged for what it does.
- Two ways out for a pilot who can't dock: **slip in** (radio off, through a door opened for someone else, onto any
  floor of a hangar) and pay at the market at face value; or a disreputable **factor** at another station
  (`StationMarket.Factor`, at Kestrel) settles the debt for half as much again.
- A wanted pilot with no money **surrenders**: the cargo is seized (the market buys it), and if that isn't enough the
  ship is impounded on the pad, kept free until paid; the insurer gives a free **courtesy ship**
  (`assets/ship/hulls/hull_courtesy.tres`: a small hold, old green monochrome screens) to earn it back. Paying it all at
  that station releases the ship (`PilotShip` puts the scene's ship back to the hull the record says).
