namespace ShowUp.Core.Catalog;

public enum WorkoutKind
{
    Cardio,
    Strength,
    Abs,
    Yoga,
    Stretch,
    Recovery,
    Rest,
    FitTest,
    Mixed,
}

public enum VerificationLevel
{
    Official,
    Community,

    /// <summary>Built by the user in the schedule builder; never in the bundled catalog.</summary>
    Custom,
}
