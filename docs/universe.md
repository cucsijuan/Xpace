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
    stations/{id}.json       one for each station
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

## Stations' names

A station or gate is known for good by its `StationId` (on its `Radio`; up to 8 characters; none: its callsign): records, ships' routes, kiosks and other machines all go by it. `Title` and `Callsign` are what it starts with. In play `Universe.Rename` gives it a new name and callsign, and `Universe.Transfer` a new owner, with the ships that work for it; nothing else changes. Hosts tell guests what stations are called now.

## Owners and money

Every station and gate has an owner: an organisation or a pilot. An organisation is set up in a resource (`Organisation`, a .tres in assets/organisations, listed in its catalog.tres: name, kind, personality, parent, starting credits, `Permanent`), and a `Personality` is a resource too (assets/personalities). To add one, make a .tres, put it in the catalog, and set a station's `OwnedBy` (on its `Radio`, in the sector's scene).

What it is now is its `OrganisationRecord`, made from the resource the first time it is needed: money and parent. An owner is an id in `StationRecord.Owner`, which can change hands; a pilot as owner is `pilot:` and their id, and both are an `IAccount` (`Universe.Account`, `StationRadio.Cash`).

What pilots pay a station goes to its owner, tolls, fines and bounties too; what it pays comes from its owner, and with no money left it buys nothing. Its people pay for what they use up (`MarketGood.Uses`, at the market's price): that is where money comes from. `StationMarket.UpkeepPerHour` is where it goes. A station with no owner trades as if its money had no end (tests' stations). Today a station's money moves only when someone deals with it (`Market.Advance`).

## Cargo ships

The universe works by itself while anyone plays, on the host (`CargoShips`, every `Universe.WorkEverySeconds`): every station is brought up to date, and every cargo ship does its job, seen or not. A cargo ship is a record (`CargoShipRecord`), not a scene.

What a ship is, is a `ShipModel` (a .tres in assets/ship/models: hull, engine, generator, its scene, and how its crew works: `RestockBelow`, `TakeShare`, `WaitSeconds`, `HandlingSeconds`). A station's `Fleet` (on its `Market`) says how many work for it when a universe starts; they belong to its owner.

A ship looks at what its home uses up (a good whose `Market.Net` is negative), takes the one it is shortest of, goes to the cheapest station that makes it, pays that market's prices, and brings it back, paying every gate's toll on the way. It uses its owner's money, by the same rules as a pilot: no money, no trip. Its voyage takes what `Voyage` reckons from its hull, generator, engine, cargo, the medium and the speed limits.

**Prices follow costs.** An owner won't sell what its station makes under what that costs: the inputs at the prices they have there, its share of the upkeep, and the margin its personality asks (`Personality.Margin`). So when chips go up, electronics go up, and then missiles. A ship won't fetch what costs more than it is worth at its home's market, so a price that nobody can pay leaves the stock to fill up, the factory rests, it stops buying, and its inputs get cheaper: the chain finds its level by itself.

To do this without the sectors loaded, `StationBook` reads every station, gate and mass from the sectors' scenes once, at the start. `tests/cargo_ships_test` runs 200 hours of it with no scene at all. 

**Pads.** Coming near a station (`ShipModel.AskAtMeters`) a ship asks it for a pad: the free one nearest to it that it fits, whichever hangar that is. With none free it holds there and asks again. It goes in and lands on that pad, stands on it while its crew works, and leaves from it. All of that is in its record (`Dock`, `Pad`, `Phase`; `StationRecord.CargoPads` keeps who has which), so it happens the same whether anyone is there or not, and pilots and cargo ships never get the same pad.

**Seen.** Where a sector is loaded (a player is there), `CargoView` (in main.tscn) shows the real ship of every record there, from the model's `Scene`, held where the record says (`CargoRun`, an `IShipHold`): parked on its pad, holding off a station, or on its way. It is a true ship to radar, weapons and other players. Its station opens the door for it (`StationRadio.OpenFor`). A line that would cross a station goes over it. The record is the truth; the scene only shows it.

**Detail levels.** A ship nobody deals with is at level 1 (`ShipBody.Dormant`): its computer, devices and AI rest, and its holder alone moves it. Radar and rounds still find it. Locked on by any radar, or hit, it goes to level 0 and its computer runs, until `CargoView.RestAfterSeconds` after the last time. Measured (headless, physics time): about 1 ms a frame for a ship at level 0, 0.04 ms at level 1. It applies to held ships: cargo ships. Ships that fly by themselves (miners, raiders, police) stay at level 0 for now.

**Talk.** A cargo ship and the station say what they do on the radio, heard by the ships in the station's zone (`StationRadio.Traffic`): texts and speeches in its Traffic group, pilots' voices in `TrafficVoices`. After a ship leaves a pad, `ShipModel.PadRestSeconds` pass before it is given to anyone. 

**Losses.** A ship destroyed is gone: its record, what it carried, and its pad is free again (`CargoShips.Lose`; its wreck stays `CargoView.WreckSeconds`). Fleets are given once (`StationRecord.Mustered`); after that an owner short of what `Fleet` says orders a ship (`ShipOrderRecord`) from the nearest yard that builds it (`StationMarket.Shipyard`). The yard starts when it has the materials (`ShipModel.Recipe`), the pieces and the buyer the money: the materials at its market's prices with its owner's margin and its fee (`ShipModel.BuildFee`), nothing of that to the same owner. The buyer chooses what is fitted (`FleetLine.Fitting`; none: what the hull comes with), and each piece is bought from whoever sells it, so what arms makers sell depends on what buyers choose. It takes `ShipModel.BuildHours`, comes off on a free pad, and flies to its home.

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

There are four stations. In the first sector Miltec - Alpha (machinery, hydrogen, electronics and arms) and Quartz (chips). A gate
away, Kestrel (a small station with three hangars: mines and smelts ore into iron and metals, mines silicon and copper) and
the Drydock (a big one: water, food, medicines, and the shipyard). Stations were at work before any pilot
came (`Market.HoursBeforeStart`) and go on working unvisited, so prices differ from a new universe's first minute.

**The first chain: ore into iron into missiles.** Kestrel mines ore and smelts iron; the first station's market buys it,
uses a little itself, and its factory makes missiles of it and of electronics, which it makes of Quartz's chips and of copper. Quartz
makes its chips of Kestrel's silicon and copper (`StationMarket.Outfits`, an `OutfitLine` per model with the
stock wanted; the factory is a `FactoryLine`, see Recipes below). The factory rests with the stock full and stops with no iron.
The price (`Outfit.Price`, on the model's spec) moves with the stock as a good's does. Commodities are traded at the market's kiosk,
in tonnes, into the hold; missiles at the **arms kiosk** (`scenes/stations/props/arms_kiosk.tscn`, `ArmsWindow`), by
the piece, straight onto the ship. Docked only; Kestrel's dealer has nothing to sell.

Chaff, flares and the cannon's rounds go the same way: the first station's factory makes them of its own metals
(`ChaffSpec`, `FlareSpec` and `AmmoSpec` are outfits, like `MissileSpec`), and its dealer sells them straight into the
ship: chaff and flares by the piece into the dispenser, rounds by the box (`Outfit.Lot`) into the turret's magazine.
The laser needs none: it costs power and heat instead.

A player's missiles, chaff and flares are in their record (`ShipRecord.Magazine`, by model; a model not listed has
what the hull gives: `ShipSpec.MissileRack` missiles of each of its `FactoryMissiles`, on the rails of its pylons, the chaff and flares of the dispenser it comes
with, `ShipSpec.Dispenser`, and the rounds of the magazine of the turret it has fitted; so a new ship comes loaded). Docking brings none of them back: the insurer's new ship is
the only free load. In a session the host owns the record: a guest asks through it (topic `arms`) and the host counts a
guest's launch when its missile appears there, its chaff and flares when their drops arrive, and its cannon rounds
when their fire events do (the record goes back to the guest at most twice a second).

**Recipes.** What anything is made of is a `Recipe` (a .tres in assets/recipes, for goods and outfits alike): what one
run makes, and a list of components with amounts. Adding a component to a recipe is one edit, and it holds at every
station that makes the thing. How fast a station runs it is a `FactoryLine` in its market's `Factories`. A factory
stops without its inputs and rests while the market has all it wants of what it makes, so a chain runs itself: no ore,
no iron; no iron, no missiles. A recipe with no inputs is a mine's, the only way anything comes into being. What a
station's people use up is `MarketGood.Uses`. Stations work in steps of at most an hour (`Market.Advance`), so what one
factory makes reaches the next in time. To add a good: a .tres in assets/goods, in catalog.tres, a line in the markets
that deal in it, and a recipe with a factory where it is made.

**Miners.** A `ShipSpawner` by Kestrel's belt keeps two miners (scenes/ships/ai_miner.tscn: the AI fighter, `Peaceful`,
lasers only) at work: a `MinerJob` flies to a rock, stops off it and fires its ordinary laser, cutting
`TonnesPerCycle` every `CycleSeconds` while the pulses reach the rock, then takes a full hold to the nearest station
that deals in the ore (`StationMarket.Deliver`). A stand-in until mining is a loop of its own; players can't mine yet,
and miners work only while the sector runs. They come from a spawner so that they work in a session: the host brings
them in and guests see them (ai_miner.tscn is one of `SessionNode.Spawnables`). `tests/miner_test`.

Lasers, turrets and missile pylons (`LaserSpec`, `TurretSpec`, `PylonSpec`, outfits too) are pieces for hardpoints: the first station's factory
makes them of metals, slowly, and the dealer sells them into the pilot's **locker at that station** (and buys a
spare one back from that locker, as the market buys a tonne)
(`ItemRecord.Piece`, Data = the model's resource path), not onto the ship. The kiosk's **Fitting** tab fits them
(`FittingDesk`, topic `fitting`, served by the station's market on the host): with the ship docked there, a piece taken
off a hardpoint goes to the locker, a piece fitted from the locker takes the hardpoint's place (what was on it goes to
the locker). A hardpoint takes a piece of its size or smaller and of the kinds it accepts; a ship has one turret. The
pilot's record keeps the fit (`ShipRecord.Fittings`), `PilotShip` mounts it and restarts the ship's computer, so
FIRECTL finds the weapons it has now. The tab shows the ship's exterior turning on a stand (`ShipSpec.Exterior`,
`TurnTable`; drag to turn it), and a piece fitted appears there in a puff of dust (`scenes/fx/fit_puff.tscn`). This
is for now: a retrofit hangar with the ship in it comes later.

Three places hold a pilot's things, apart: what they carry on them (`PilotRecord.Items`), their locker at each station
(`StationRecord.Lockers`), and their ship (`ShipRecord`: the hold's cargo and the magazine), which goes when it blows
up.

**Measuring the balance:** `tests/trade_balance_test.tscn` reads the goods, markets, hulls, generator, tolls and
distances from the game's data and prints what trading pays: prices at both ends, the best run as the universe ages,
careers against hours played. Change the data and run it again; the targets it fails on are constants at the top of
`tests/TradeBalanceTest.cs`. `tests/trade_run_test.tscn` is one run in the real game, from dock to dock.

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

## What actors know

Nobody reads a far station's stock. Pilots and organisations (`IAccount.Known`) keep a dated `StationNews` for each
station they have heard of (`scripts/World/MarketNews.cs`); newer news replaces older, never the other way round.

- **Seeing it:** a cargo ship landing at a station tells its owner, and a pilot at a market kiosk learns that market.
- **A commercial beacon** (`CommercialBeacon`, `scenes/beacons/commercial_beacon.tscn`, kind `B` on the radio) reports the
  stations within `ReachJumps` gates, as they are then. A pilot asks it within radio reach (`RadioRequest.News`; a
  guest's request goes to the host like docking). A cargo ship passing within `HeardMeters` tells its owner on arrival.
- **Deciding:** `CargoShips.Plan` uses the owner's news for other owners' stations, and the truth only for its own.
  News older than `ShipModel.NewsHours` counts as none. `CargoShips.Load` checks the real price on arrival.

Shipyards still buy a ship's fitting from whoever has it without news: that is a purchase order, not yet a decision
made on what the buyer knows.

## What owners do about damage

`CargoView` tells a ship's owner when it is hit (at most every `MaydayEverySeconds`) or lost: `Reactions.Happened` keeps an
`Incident` on the `OrganisationRecord` and adds to `Bounties` (pilot id to credits) if a pilot did it.
`CargoShips.Plan` leaves out sellers on a way the owner fears (`Reactions.Avoids`). `Reactions.Collect` pays a bounty
when one pilot destroys another. Pilots who own ships get none of this: they decide for themselves.

A ship on a record can leave it and come back: `CargoShipRecord.Loose` is a real ship flown by its pilot, and
`CargoShips.Resume` puts it back on a way from a point in space (`StationBook.Origin`). `IEngagement` says whom a
fighter may fight: a station's security or an `EscortDuty`.

