using System.Security.Cryptography;
using System.Text;

namespace ShowUp.Core.Supabase;

/// <summary>
/// A file holding the secret, optionally encrypted by an <see cref="ITokenProtector"/>. On Unix the file is created
/// owner-only (0600). A file that cannot be decrypted (other user, other machine) reads as empty.
/// </summary>
public sealed class FileTokenStore(string path, ITokenProtector? protector = null) : ITokenStore
{
    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, ct);
            if (protector is not null)
            {
                bytes = protector.Unprotect(bytes);
            }

            return bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task WriteAsync(string value, CancellationToken ct = default)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (protector is not null)
        {
            bytes = protector.Protect(bytes);
        }

        if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
        }

        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        FileStreamOptions options = new()
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            await using (FileStream stream = new(temp, options))
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                await stream.WriteAsync(bytes, ct);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
