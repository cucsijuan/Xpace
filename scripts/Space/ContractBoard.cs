using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Xpace.World;

namespace Xpace.Space;

/// <summary>What a pilot asks a station's contract board (<see cref="Do"/>: look, take, collect, hand, abandon, report, read).</summary>
public sealed class ContractRequest
{
    public const string Look = "look", Take = "take", Collect = "collect", Hand = "hand", Abandon = "abandon", Report = "report", Read = "read";
    public string Station { get; set; } = "";
    public string Do { get; set; } = Look;
    public string Id { get; set; } = "";
    /// <summary>For <see cref="Read"/>: the guide read (<see cref="JournalEntry.HelpKey"/>); for <see cref="Report"/>: what the pilot reports.</summary>
    public string Key { get; set; } = "";
}

/// <summary>A station's contract board as a pilot sees it.</summary>
public sealed class ContractView
{
    public string Station { get; set; } = "";
    public string Title { get; set; } = "";
    public long Credits { get; set; }
    public bool Present { get; set; }
    public bool Docked { get; set; }
    public string Message { get; set; } = "";
    public List<OfferLine> Offers { get; set; } = new();
    public List<MineLine> Mine { get; set; } = new();
}

public sealed class OfferLine
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Giver { get; set; } = "";
    public string Briefing { get; set; } = "";
    public long Reward { get; set; }
    public double Hours { get; set; }
    public bool Story { get; set; }
}

public sealed class MineLine
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    public long Reward { get; set; }
    public double Left { get; set; } = -1;
    public bool CanCollect { get; set; }
    public bool CanHand { get; set; }
    /// <summary>A report to be made here: the terminal asks what was found.</summary>
    public bool CanReport { get; set; }
    public bool Story { get; set; }
}

/// <summary>
/// The universe's owner answering contract terminals (<see cref="Contracts.Topic"/>): a pilot present at the station
/// (docked, in its hangar or walking in it) may take its offers; cargo is collected and handed over with the ship docked
/// there. Reading a guide is asked here too (so a guest's journal remembers it).
/// </summary>
public static class ContractBoard
{
    public static string Desk(string pilotId, string text)
    {
        ContractRequest request;
        try
        {
            request = JsonSerializer.Deserialize<ContractRequest>(text);
        }
        catch (JsonException)
        {
            return null;
        }
        if (request == null || Universe.Current is not Universe universe)
            return null;
        PilotRecord pilot = universe.State.Pilot(pilotId);
        if (request.Do == ContractRequest.Read)
        {
            if (pilot.GuidesRead.Add(request.Key))
                universe.Touch(pilot);
            return null;
        }
        return JsonSerializer.Serialize(Serve(universe, pilot, request));
    }

    public static ContractView Serve(Universe universe, PilotRecord pilot, ContractRequest request)
    {
        UniverseState state = universe.State;
        StationRadio radio = StationRadio.Called(request.Station);
        Node3D ship = universe.ShipOf(pilot.Id);
        bool docked = radio?.DockedHere(ship) == true;
        bool present = docked || radio?.Inside(ship) == true || universe.OnFootAt(pilot.Id) == request.Station;
        string at = present ? request.Station : null, dockedAt = docked ? request.Station : null;
        string message = request.Do switch
        {
            ContractRequest.Take => Contracts.Take(state, request.Id, pilot.Id, at, universe.Account),
            ContractRequest.Collect => docked ? Contracts.Collect(state, universe.Book, request.Id, pilot.Id, pilot.Ship, universe.Room(pilot), dockedAt, universe.Account) : "Dock here to load cargo.",
            ContractRequest.Hand => docked ? Contracts.HandOver(state, universe.Book, request.Id, pilot.Id, pilot.Ship, pilot, dockedAt, universe.Account) : "Dock here to unload cargo.",
            ContractRequest.Abandon => Contracts.Abandon(state, request.Id, pilot.Id, pilot.Ship, universe.Account),
            ContractRequest.Report => Contracts.Tell(state, request.Id, pilot.Id, request.Key, at, pilot, universe.Account),
            _ => null,
        } ?? (request.Do == ContractRequest.Look ? "" : Done(request.Do));
        if (request.Do != ContractRequest.Look)
            universe.Touch(pilot);
        var view = new ContractView
        {
            Station = request.Station, Title = universe.NameOf(request.Station), Credits = pilot.Credits, Present = present, Docked = docked, Message = message,
        };
        foreach (ContractRecord c in Contracts.Offers(state, request.Station, pilot.Id, universe.Account))
            view.Offers.Add(new OfferLine
            {
                Id = c.Id, Title = c.Title, Giver = universe.Account(c.Giver)?.Name ?? c.Giver, Briefing = c.Briefing, Reward = c.Reward,
                Hours = c.Allowed / 3600, Story = c.Story != "",
            });
        foreach (ContractRecord c in Contracts.Taken(state, pilot.Id).OrderByDescending(c => c.Story != "").ThenBy(c => c.TakenAt))
            view.Mine.Add(new MineLine
            {
                Id = c.Id, Title = c.Title, Objective = Contracts.Objective(state, universe.Book, c), Reward = c.Reward, Story = c.Story != "",
                Left = c.Due > 0 ? c.Due - state.Clock : -1,
                CanCollect = docked && c.Kind == ContractRecord.Haul && c.From == request.Station && c.Loaded < c.Tonnes,
                CanHand = docked && c.Kind is not (ContractRecord.Visit or ContractRecord.Report) && c.To == request.Station && (c.Carried ? pilot.Ship.ConsignedOf(c.Good) > 0 : pilot.Ship.Clean(c.Good) > 0),
                CanReport = present && c.Kind == ContractRecord.Report && c.Station == request.Station,
            });
        return view;
    }

    private static string Done(string what) => what switch
    {
        ContractRequest.Take => "Contract taken.",
        ContractRequest.Collect => "Cargo loaded.",
        ContractRequest.Hand => "Cargo handed over.",
        ContractRequest.Abandon => "Contract given up.",
        ContractRequest.Report => "Report accepted.",
        _ => "",
    };
}
