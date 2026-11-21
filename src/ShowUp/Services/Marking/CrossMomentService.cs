using ShowUp.Core.Marking;
using ShowUp.Infrastructure;

namespace ShowUp.Services.Marking;

/// <summary>Per-window <see cref="ICrossMoment"/>: forwards to the overlay hosted in that window's shell.</summary>
internal sealed class CrossMomentService(IWindowShellProvider shellProvider) : ICrossMoment
{
    public Task ShowAsync(CrossMomentInfo info) => shellProvider.Shell.CrossMoment.ShowAsync(info);
}
