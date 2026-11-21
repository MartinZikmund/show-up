using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Services.Dialogs;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public class JournalSaveGuardTests
{
    private readonly RecordingDialogService _dialogs = new();

    private JournalSaveGuard CreateGuard() => new(_dialogs, new PassThroughLocalizer(), NullLogger<JournalSaveGuard>.Instance);

    [TestMethod]
    public async Task Run_SaveSucceeds_ShowsNothing()
    {
        var saved = await CreateGuard().RunAsync(() => Task.CompletedTask);

        saved.Should().BeTrue();
        _dialogs.Shown.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Run_SaveThrows_ShowsTheErrorDialog()
    {
        var saved = await CreateGuard().RunAsync(() => throw new InvalidOperationException("Enrollment ended."));

        saved.Should().BeFalse();
        _dialogs.Shown.Should().ContainSingle().Which.Should().Be(("JournalSaveFailedTitle", "JournalSaveFailedMessage"));
    }

    [TestMethod]
    public async Task Run_StorageFails_ShowsTheErrorDialog()
    {
        var saved = await CreateGuard().RunAsync(() => Task.FromException(new IOException("disk full")));

        saved.Should().BeFalse();
        _dialogs.Shown.Should().HaveCount(1);
    }
}
