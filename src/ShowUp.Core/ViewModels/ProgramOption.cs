namespace ShowUp.Core.ViewModels;

/// <summary>An active program in a page's switcher.</summary>
public sealed partial record ProgramOption(Guid Id, string Name)
{
    public string AutomationId => $"ProgramOption_{Id:N}";
}
