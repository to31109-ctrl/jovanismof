// BonkLink edition. GPL-2.0; see LICENSE.
namespace MegabonkTogether.Common.Persistence;

/// <summary>What should happen to a run that is starting, given the world the host chose.</summary>
public enum ResumeDecision
{
    /// <summary>No world was chosen, so this is simply a new run.</summary>
    StartNew,
    /// <summary>The run already begins where the world left off; nothing to change.</summary>
    AlreadyAligned,
    /// <summary>The run must be pointed at another stage of the same map first.</summary>
    SwitchStage,
    /// <summary>The world is on a different map; the host has to pick that map.</summary>
    WrongMap,
    /// <summary>The stage the world was saved on is not part of this map any more.</summary>
    StageMissing,
}

/// <summary>
/// Decides, in one place and without touching the game, whether a chosen world can be
/// continued by the run that is starting.
///
/// This used to be an equality test buried in the resume path: continue only if the stage
/// being started is exactly the stage the world was saved on. A run always begins on the first
/// stage of a map, so any world left further in could never match, and the refusal was silent
/// -- the host pressed Host and got a brand new world with no explanation.
/// </summary>
public static class WorldResume
{
    /// <param name="chosenMap">Map the chosen world was saved on, or null for a new world.</param>
    /// <param name="chosenStage">Stage the chosen world was saved on.</param>
    /// <param name="startingMap">Map the run is about to generate.</param>
    /// <param name="startingStage">Stage the run is about to generate.</param>
    /// <param name="stagesOnThisMap">Every stage this map can generate.</param>
    public static ResumeDecision Decide(
        int? chosenMap,
        string chosenStage,
        int startingMap,
        string startingStage,
        IReadOnlyList<string> stagesOnThisMap)
    {
        if (chosenMap is null || string.IsNullOrEmpty(chosenStage)) return ResumeDecision.StartNew;
        if (chosenMap.Value != startingMap) return ResumeDecision.WrongMap;
        if (string.Equals(chosenStage, startingStage, StringComparison.Ordinal)) return ResumeDecision.AlreadyAligned;

        // The map is right and the stage is not; it can be continued if this map still builds
        // the stage it was saved on.
        if (stagesOnThisMap != null && stagesOnThisMap.Any(s => string.Equals(s, chosenStage, StringComparison.Ordinal)))
        {
            return ResumeDecision.SwitchStage;
        }

        return ResumeDecision.StageMissing;
    }

    /// <summary>What to tell the host when a world cannot be continued from here.</summary>
    public static string Explain(ResumeDecision decision, string worldName, string chosenStage) => decision switch
    {
        ResumeDecision.WrongMap => $"{worldName} is saved on another map.\nPick that map to continue it.",
        ResumeDecision.StageMissing => $"{worldName} is saved on {chosenStage}, which this map no longer has.",
        _ => "",
    };
}
