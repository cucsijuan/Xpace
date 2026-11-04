using System.Collections.Generic;
using Godot;
using Xpace.Menu;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>
/// The game's shell: it loads sectors, each into a world of its own (a
/// SubViewport with its own World3D, shown through a container filling the
/// window), puts the player's ship into the sector it starts in, and moves
/// it whole from one sector to another (<see cref="Jump"/>). Only the sector
/// the player is in is drawn and heard.
///
/// A session's host also runs the sectors its guests are in
/// (<see cref="Hold"/>): loaded the same way, nothing drawn or heard and what
/// they show left out (<see cref="SectorVisuals"/>), let go when nobody is
/// left in them (<see cref="Release"/>, and <see cref="Keep"/> when the player
/// leaves one). A sector's number in the map is its place in a session.
/// </summary>
public partial class SectorHost : Node
{
    [Export] public SectorMap Map;
    /// <summary>The player's ship.</summary>
    [Export] public PackedScene ShipScene;
    /// <summary>The Id of the sector to start in (empty: the map's start).</summary>
    [Export] public string StartSector = "";

    public static SectorHost Current { get; private set; }

    /// <summary>The player's ship.</summary>
    public ShipBody Ship { get; private set; }
    /// <summary>The sector the player is in.</summary>
    public Sector Here => _here?.Info;
    /// <summary>The root of the sector the player is in.</summary>
    public FloatingOrigin Sector => _here?.Origin;
    /// <summary>The world of the sector the player is in.</summary>
    public SubViewport View => _here?.Port;

    private sealed class Running
    {
        public Sector Info;
        public SubViewportContainer Frame;
        public SubViewport Port;
        public FloatingOrigin Origin;
        public SectorVisuals Visuals;
    }

    /// <summary>A sector was loaded (its root), before anything in it has run a step.</summary>
    public event System.Action<FloatingOrigin> Loaded;

    /// <summary>The player's ship went from one sector to another.</summary>
    public event System.Action<Sector, Sector> Jumped;

    /// <summary>Whether the sector the player leaves stays loaded (a session's host, with others there); null: never.</summary>
    public System.Func<string, bool> Keep;

    private readonly Dictionary<string, Running> _loaded = new();
    private Running _here;

    public override void _EnterTree() => Current = this;

    public override void _ExitTree()
    {
        if (Current == this)
            Current = null;
        if (GameSettings.Current is GameSettings s)
            s.Changed -= ApplySettings;
    }

    public override void _Ready()
    {
        Sector start = (StartSector != "" ? Map?.Find(StartSector) : null) ?? Map?.First;
        if (start == null || ShipScene == null)
        {
            GD.PushError("SectorHost: no sector to start in, or no ship");
            return;
        }
        Ship = ShipScene.Instantiate<ShipBody>();
        Ship.Name = "Ship";
        _here = Load(start, Ship);
        Xpace.World.Universe.Current?.Run(Map);
        Show(_here);
        if (GameSettings.Current is GameSettings s)
            s.Changed += ApplySettings;
    }

    /// <summary>
    /// Takes the player's ship to another sector, at a place in it (that sector's
    /// scene frame); the sector left behind is let go.
    /// </summary>
    public void Jump(string sectorId, Transform3D at)
    {
        Sector info = Map?.Find(sectorId);
        if (info == null || Ship == null)
        {
            GD.PushError($"SectorHost: no sector '{sectorId}'");
            return;
        }
        Running from = _here;
        if (!_loaded.TryGetValue(sectorId, out Running to))
            to = Load(info, null);
        if (to == from)
            return;
        from.Origin.Ship = null;
        Sectors.Move(Ship, to.Origin, at);
        to.Origin.Ship = Ship;
        _here = to;
        Show(to);
        Jumped?.Invoke(from.Info, to.Info);
        if (Keep?.Invoke(from.Info.Id) != true)
            Unload(from);
    }

    /// <summary>A sector's number in the map (its place in a session).</summary>
    public byte PlaceOf(string sectorId)
    {
        for (int i = 0; Map != null && i < Map.Sectors.Count; i++)
            if (Map.Sectors[i]?.Id == sectorId)
                return (byte)i;
        return 0;
    }

    /// <summary>The number in the map of the sector a node is in (0 if it is in none).</summary>
    public byte PlaceOf(Node node)
    {
        foreach (Running l in _loaded.Values)
            if (node != null && (node == l.Origin || l.Origin.IsAncestorOf(node)))
                return PlaceOf(l.Info.Id);
        return 0;
    }

    /// <summary>The root of the sector of that number, if it is loaded.</summary>
    public FloatingOrigin RootAt(byte place) => place < (Map?.Sectors.Count ?? 0) ? Root(Map.Sectors[place].Id) : null;

    /// <summary>The numbers of the sectors loaded.</summary>
    public IEnumerable<byte> LoadedPlaces
    {
        get
        {
            foreach (string id in _loaded.Keys)
                yield return PlaceOf(id);
        }
    }

    /// <summary>Runs the sector of that number too (without the player: nothing drawn); its root.</summary>
    public FloatingOrigin Hold(byte place)
    {
        if (RootAt(place) is FloatingOrigin root)
            return root;
        if (Map == null || place >= Map.Sectors.Count || Map.Sectors[place] == null)
            return null;
        Running l = Load(Map.Sectors[place], null);
        Show(_here);
        return l.Origin;
    }

    /// <summary>Lets go of the sector of that number, unless the player is in it.</summary>
    public void Release(byte place)
    {
        if (place < (Map?.Sectors.Count ?? 0) && _loaded.TryGetValue(Map.Sectors[place].Id, out Running l) && l != _here)
            Unload(l);
    }

    /// <summary>What the sector of that Id shows is left out (it is loaded and the player is elsewhere).</summary>
    public SectorVisuals VisualsOf(string sectorId) => _loaded.TryGetValue(sectorId, out Running l) ? l.Visuals : null;

    /// <summary>A node's sector is the one on the screen (with no sectors at all, it is).</summary>
    public static bool Seen(Node node)
    {
        if (Current == null || Current._here == null || node == null)
            return true;
        return node == Current._here.Origin || Current._here.Origin.IsAncestorOf(node);
    }

    /// <summary>The root of the sector of that Id if it is loaded, or null.</summary>
    public FloatingOrigin Root(string sectorId) => _loaded.TryGetValue(sectorId, out Running l) ? l.Origin : null;

    // A sector in a world of its own; the ship, if one comes with it, is in it before it starts
    private Running Load(Sector info, ShipBody ship)
    {
        var origin = GD.Load<PackedScene>(info.Scene).Instantiate<FloatingOrigin>();
        origin.Name = info.Id;
        if (ship != null)
        {
            Node3D start = origin.GetNodeOrNull<Node3D>("PlayerStart");
            ship.Transform = start?.Transform ?? Transform3D.Identity;
            origin.AddChild(ship);
            origin.MoveChild(ship, start?.GetIndex() + 1 ?? 0);    // ready before the sector's session starts with it
            origin.Ship = ship;
        }
        var port = new SubViewport { Name = "World", OwnWorld3D = true };
        port.AddChild(origin);
        var frame = new SubViewportContainer { Name = info.Id, Stretch = true, Visible = false };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        frame.AddChild(port);
        var loaded = new Running { Info = info, Frame = frame, Port = port, Origin = origin, Visuals = new SectorVisuals(origin) };
        _loaded[info.Id] = loaded;
        AddChild(frame);
        MoveChild(frame, 0);                        // under the menus
        GameSettings.Current?.ApplyTo(port);
        Loaded?.Invoke(origin);
        return loaded;
    }

    private void Unload(Running l)
    {
        _loaded.Remove(l.Info.Id);
        l.Visuals.Forget();
        l.Frame.QueueFree();
    }

    // Only the player's sector is drawn, heard and given the keys and mouse
    private void Show(Running here)
    {
        foreach (Running l in _loaded.Values)
        {
            bool shown = l == here;
            l.Frame.Visible = shown;
            l.Port.RenderTargetUpdateMode = shown ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
            l.Port.AudioListenerEnable3D = shown;
            if (shown)
                l.Visuals.Show();
            else
                l.Visuals.Hide();
        }
    }

    private void ApplySettings()
    {
        foreach (Running l in _loaded.Values)
            GameSettings.Current.ApplyTo(l.Port);
    }
}
