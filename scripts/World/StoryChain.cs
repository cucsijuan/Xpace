using Godot;

namespace Xpace.World;

/// <summary>
/// A story told by contracts (assets/story, listed on the sector map): its steps, each offered to the pilot once the
/// one before is done (<see cref="PilotRecord.Story"/>). Only the pilot it is for sees it.
/// </summary>
[GlobalClass]
public partial class StoryChain : Resource
{
    [Export] public string Id = "";
    [Export] public string Title = "";
    [Export] public StoryStep[] Steps = System.Array.Empty<StoryStep>();
    /// <summary>
    /// What the story settles for each pilot the first time a step needs it, "name=kind:argument", and its steps' texts
    /// name as {name}: callsign:&lt;owner&gt; one of that owner's freighters' callsigns, new:&lt;seed&gt; a callsign nobody
    /// flies. Kept in <see cref="PilotRecord.Facts"/>, so a step offered again says the same.
    /// </summary>
    [Export] public string[] Facts = System.Array.Empty<string>();
}
