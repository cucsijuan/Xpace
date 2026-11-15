using System.IO;
using Godot;
using GodotCharacters;

namespace Xpace.OnFoot;

/// <summary>
/// The crew's bodies in the terms of the character library (external/godot-characters): what its import makes, and
/// the look and moves a body wears. The model is Synty's Sci-Fi Space characters, every body, head and helmet on one
/// skeleton (<c>tools/import_synty.cmake</c> takes it out of the pack's Unity package into
/// <c>.raw-assets/characters</c>); a person is the few meshes of it they wear. The moves are Synty's Animation Base
/// Locomotion (<c>.raw-assets/anim_base_locomotion</c>), and sitting is the Universal Animation Library's
/// (<c>assets/animations/ual</c>, imported by Godot onto the humanoid profile).
/// </summary>
public static class Crew
{
    /// <summary>Where the import puts what it makes (ignored by git: Synty's licence).</summary>
    public const string Output = "res://assets/synty/characters";

    /// <summary>The clip a seated body holds (<see cref="Body.Hold"/>).</summary>
    public const string Sit = "sit";

    private const string Model = "crew";
    private const string Set = "masculine";
    private const string Hair = "crew_male_hair";
    private const string Paint = "res://assets/materials/synty_atlas.tres";
    private const string Sitting = "res://assets/animations/ual/AnimationLibrary_Godot_Standard.glb";
    private const string Moves = "anim_base_locomotion/Animations/Polygon/Masculine";

    /// <summary>The meshes of the model the crew wear.</summary>
    private static readonly string[] Parts = { "SM_Chr_Crew_Male_01" };

    public static ImportPlan Plan()
    {
        string raw = Path.Combine(ProjectSettings.GlobalizePath("res://"), ".raw-assets");
        string Clip(string file) => Path.Combine(raw, Moves, file + ".fbx");
        string Travelling(string folder, string name) => Clip($"{folder}/{name}_RootMotion_Masc");
        return new ImportPlan
        {
            Output = Output,
            Models = { new(Model, Path.Combine(raw, "characters", "SM_Chr_Characters.fbx")) },
            Attachments = { new(Hair, Path.Combine(raw, "characters", "SM_Chr_Attach_Crew_Male_Hair_01.fbx")) },
            AnimationSets =
            {
                new(Set, new ImportPlan.Clip[]
                {
                    new("idle", Clip("Idle/A_Idle_Standing_Masc"), Loop: true),
                    new("walk", Clip("Locomotion/Walk/A_Walk_F_Masc"), Loop: true, Travelling("Locomotion/Walk", "A_Walk_F")),
                    new("run", Clip("Locomotion/Run/A_Run_F_Masc"), Loop: true, Travelling("Locomotion/Run", "A_Run_F")),
                    new("sprint", Clip("Locomotion/Sprint/A_Sprint_F_Masc"), Loop: true, Travelling("Locomotion/Sprint", "A_Sprint_F")),
                    new("jump", Clip("InAir/A_Jump_Running_Masc"), Loop: false),
                    new("fall", Clip("InAir/A_InAir_FallShort_Masc"), Loop: true),
                    new("land", Clip("InAir/A_Land_IdleSoft_Masc"), Loop: false),
                    new(Sit, Sitting, Loop: true, Take: "Sitting_Idle"),
                }),
            },
        };
    }

    public static Body.Look Look => new(
        CharacterPaths.Model(Output, Model),
        Parts,
        new[] { new Body.Mount(CharacterPaths.Attachment(Output, Hair), "Head", Transform3D.Identity) },
        Paint);

    public static Body.Moves Moving => new(
        CharacterPaths.Animations(Output, Set), "idle", new[] { "walk", "run", "sprint" }, "jump", "fall", "land");
}
