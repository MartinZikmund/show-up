using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Messages;

/// <summary>
/// Invalidates the caching services before anyone hears <see cref="DataChangedMessage"/>, so a page that reloads on it
/// never reads a stale cache, whatever order the messenger calls its recipients in.
/// </summary>
public sealed class DataChangeAnnouncer(IActiveEnrollmentService enrollments, ICatalogService catalog, IMessenger messenger)
{
    public void Announce()
    {
        catalog.Invalidate();
        enrollments.Invalidate();
        messenger.Send(new DataChangedMessage());
    }
}
