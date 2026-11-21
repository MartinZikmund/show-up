namespace ShowUp.Core.Supabase;

/// <summary>Encrypts bytes at rest for <see cref="FileTokenStore"/> (DPAPI on Windows, a Keystore key on Android).</summary>
public interface ITokenProtector
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}
