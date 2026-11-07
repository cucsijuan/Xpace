using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// A measurement, not a check: the universe with its people runs for a while with the player sitting at the start,
/// and what comes of the meetings out of sight is counted: who went for whom, what was read, what was fired on, what
/// was lost and looted.
///
/// Run: XPACE_FIGHT_MINUTES=60 Godot --headless --fixed-fps 60 --path . res://tests/fights_test.tscn
/// </summary>
public partial class FightsTest : Node
{
    public override async void _Ready()
    {
        double minutes = double.TryParse(OS.GetEnvironment("XPACE_FIGHT_MINUTES"), out double m) ? m : 30;
        Universe.Current.Forget();
        Universe.Current.Population = GD.Load<PopulationSpec>("res://assets/people/population.tres");
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        foreach (string name in new[] { "Drone1", "Drone2", "Raider" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        var view = main.GetNode<CargoView>("CargoView");
        await Seconds(3);
        UniverseState state = Universe.Current.State;
        int shipsAtStart = state.CargoShips.Count, people = state.People.Count;
        var loose = new HashSet<string>();
        var owners = new Dictionary<string, string>();
        var last = new Dictionary<string, string>();
        int turns = 0, reads = 0, shotsSeen = 0;
        var shooters = new HashSet<Node3D>();
        void Shot(Node3D by, Vector3 at)
        {
            shotsSeen++;
            shooters.Add(by);
        }
        Combat.Fired += Shot;
        int lost = 0;
        Combat.Destroyed += (ship, by, at) =>
        {
            lost++;
            GD.Print($"       destroyed {ship?.Name} by {by?.Name}");
        };
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int step = 0; step < minutes * 3600; step++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (step % 60 != 0)
                continue;
            loose.RemoveWhere(id => !state.CargoShips.ContainsKey(id));
            foreach (CargoShipRecord ship in state.CargoShips.Values)
                if (ship.Phase != CargoShipRecord.Loose)
                    loose.Remove(ship.Id);
                else if (loose.Add(ship.Id))
                    turns++;
            foreach (string gone in owners.Keys.Except(state.CargoShips.Keys).ToList())
            {
                GD.Print($"{step / 3600.0,6:0.0} min  {gone} of {owners[gone]} is gone: last {last.GetValueOrDefault(gone)}");
                owners.Remove(gone);
            }
            foreach (CargoShipRecord ship in state.CargoShips.Values)
            {
                owners[ship.Id] = state.People.GetValueOrDefault(ship.Owner)?.Livelihood ?? ship.Owner;
                if (view.Shown.TryGetValue(ship.Id, out ShipBody body) && IsInstanceValid(body) && ship.Phase == CargoShipRecord.Loose)
                {
                    if (step % 3600 == 0)
                        GD.Print($"   {step / 60,4} s {ship.Id} {body.Find<FighterAi>()?.Doing} hull {body.Find<ShipHull>()?.Fraction:0.00} speed {body.LinearVelocity.Length():0} station {StationRadio.ProtectingAt(Sectors.WorldOf(body), body.GlobalPosition)?.Id} sector {main.PlaceOf(body)}");
                    last[ship.Id] = $"{body.Find<FighterAi>()?.Doing} hull {body.Find<ShipHull>()?.Fraction:0.00} speed {body.LinearVelocity.Length():0} heat {body.Device<EngineDevice>()?.CoreTemperature:0} tonnes {ship.Tonnes}";
                }
            }
            if (step % (60 * 300) == 0)
            {
                reads = state.People.Values.Sum(p => p.Memories.Count(e => e.What == Memories.NothingAboard));
                int hits = state.People.Values.Sum(p => p.Memories.Count(e => e.What is Memories.HitMe or Memories.HitMine or Memories.KilledMine));
                GD.Print($"{step / 3600.0,6:0} min  went for a ship {turns,3}  now loose {loose.Count}  empty reads {reads,3}  shots {shotsSeen,6}  shooters {shooters.Count,2}  lost {lost,2}  records {state.CargoShips.Count}/{shipsAtStart}  people {state.People.Count}/{people}  remembered hits {hits}  loot {state.CargoShips.Values.Sum(s => s.Loot.Values.Sum())} t  sectors {string.Join(",", view.Shown.Count)} shown  real {(System.Diagnostics.Stopwatch.GetTimestamp() - started) / (double)System.Diagnostics.Stopwatch.Frequency,5:0} s");
            }
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(0);
    }

    private async Task Seconds(double s)
    {
        for (int i = 0; i < s * 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
