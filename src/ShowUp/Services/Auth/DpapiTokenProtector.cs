#if !HAS_UNO || __DESKTOP__
using System.Security.Cryptography;
using ShowUp.Core.Supabase;

namespace ShowUp.Services.Auth;

/// <summary>Windows DPAPI: only this Windows user on this machine can decrypt the file.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DpapiTokenProtector : ITokenProtector
{
    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}
#endif
