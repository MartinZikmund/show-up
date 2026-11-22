namespace ShowUp.Core.Catalog;

/// <summary>One published workout or program as the server stores it: the id columns plus the raw catalog JSON.</summary>
public sealed partial record RemoteProgram(string Id, int Revision, string Json);

public sealed record RemoteCatalog(int Version, IReadOnlyList<string> Workouts, IReadOnlyList<RemoteProgram> Programs);

/// <summary>Where newer catalog versions are published.</summary>
public interface ICatalogRemote
{
    /// <summary>False when there is no server in this build; nothing is requested then.</summary>
    bool IsAvailable { get; }

    /// <summary>The newest published version, or null when none is published yet.</summary>
    Task<int?> GetLatestVersionAsync(CancellationToken ct = default);

    /// <summary>Every workout and program row published up to <paramref name="version"/>.</summary>
    Task<RemoteCatalog> DownloadAsync(int version, CancellationToken ct = default);
}
