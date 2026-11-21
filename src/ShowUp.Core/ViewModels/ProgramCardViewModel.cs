using ShowUp.Core.Catalog;

namespace ShowUp.Core.ViewModels;

/// <summary>One program in the browse grid.</summary>
public sealed partial record ProgramCardViewModel(
    string Id,
    string Name,
    string Family,
    int TotalDays,
    int TrackCount,
    string AccentColor,
    string? HeroImage,
    VerificationLevel Verification,
    string SummaryText,
    string VerificationText,
    string VerificationTooltip)
{
    public bool IsCommunityVerified => Verification == VerificationLevel.Community;

    public string AutomationId => $"ProgramCard_{Id}";

    public string AutomationName => $"{Name}, {SummaryText}, {VerificationText}";

    public bool IsCustom => Verification == VerificationLevel.Custom;

    public string VerificationGlyph => Verification switch
    {
        VerificationLevel.Community => "\uE716",
        VerificationLevel.Custom => "\uE70F",
        _ => "\uE930",
    };
}
