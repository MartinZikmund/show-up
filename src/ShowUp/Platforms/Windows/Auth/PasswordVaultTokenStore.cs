using ShowUp.Core.Supabase;
using Windows.Security.Credentials;

namespace ShowUp.Platforms.Windows.Auth;

/// <summary>
/// Windows Credential Locker (packaged app). A credential holds about 1280 characters, less than a session, so the
/// value is split into numbered credentials under one resource.
/// </summary>
public sealed class PasswordVaultTokenStore : ITokenStore
{
    private const string Resource = "ShowUp.Supabase";
    private const int ChunkSize = 400;

    private readonly PasswordVault _vault = new();

    public Task<string?> ReadAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PasswordCredential> found;
        try
        {
            found = _vault.FindAllByResource(Resource);
        }
        catch (Exception)
        {
            // "Element not found" is how the vault says it is empty.
            return Task.FromResult<string?>(null);
        }

        System.Text.StringBuilder value = new();
        foreach (PasswordCredential credential in found.OrderBy(c => int.Parse(c.UserName)))
        {
            credential.RetrievePassword();
            value.Append(credential.Password);
        }

        return Task.FromResult<string?>(value.Length == 0 ? null : value.ToString());
    }

    public Task WriteAsync(string value, CancellationToken ct = default)
    {
        Remove();
        for (int i = 0, index = 0; i < value.Length; i += ChunkSize, index++)
        {
            _vault.Add(new PasswordCredential(Resource, index.ToString(), value.Substring(i, Math.Min(ChunkSize, value.Length - i))));
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        Remove();
        return Task.CompletedTask;
    }

    private void Remove()
    {
        try
        {
            foreach (PasswordCredential credential in _vault.FindAllByResource(Resource).ToList())
            {
                _vault.Remove(credential);
            }
        }
        catch (Exception)
        {
            // Nothing stored.
        }
    }
}
