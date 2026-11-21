using ShowUp.Core.Storage;

namespace ShowUp.Core.ViewModels;

/// <summary>A logged bonus workout with its "Today · 10 min" line.</summary>
public sealed record BonusItemViewModel(BonusWorkoutEntity Entity, string Title, string Detail);
