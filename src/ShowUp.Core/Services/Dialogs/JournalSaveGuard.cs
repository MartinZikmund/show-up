using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;

namespace ShowUp.Services.Dialogs;

/// <summary>Runs a notes or results save and tells the user when it fails, instead of dropping their edit silently.</summary>
public sealed class JournalSaveGuard(IDialogService dialogs, IStringLocalizer localizer, ILogger<JournalSaveGuard> logger)
{
    public async Task<bool> RunAsync(Func<Task> save)
    {
        try
        {
            await save();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Saving notes or results failed.");
            await dialogs.ShowAsync(localizer["JournalSaveFailedTitle"], localizer["JournalSaveFailedMessage"]);
            return false;
        }
    }
}
