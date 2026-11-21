using ShowUp.Core.ViewModels;

namespace ShowUp.Controls;

/// <summary>Gives each program switcher item container its <see cref="ProgramOption.AutomationId"/>, so UI tests can pick one.</summary>
internal static class ProgramSwitcherAutomation
{
    /// <summary>Call from <c>DropDownOpened</c>: the containers are realized right after it, so tag them on the next tick.</summary>
    public static void TagContainers(ComboBox box) => box.DispatcherQueue.TryEnqueue(() => TagNow(box));

    private static void TagNow(ComboBox box)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.ContainerFromIndex(i) is ComboBoxItem container && box.Items[i] is ProgramOption option)
            {
                AutomationProperties.SetAutomationId(container, option.AutomationId);
                AutomationProperties.SetName(container, option.Name);
            }
        }
    }
}
