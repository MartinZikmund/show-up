using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using ShowUp.Core.Supabase;

namespace ShowUp.Droid.Auth;

/// <summary>
/// AES-GCM with a non-exportable Android Keystore key. Layout: IV length, IV, ciphertext. A restored backup on a new
/// device has no key, so it fails to decrypt and the user simply signs in again.
/// </summary>
public sealed class AndroidKeystoreProtector : ITokenProtector
{
    private const string Alias = "showup_supabase_session";
    private const string Transformation = "AES/GCM/NoPadding";

    public byte[] Protect(byte[] data)
    {
        try
        {
            Cipher cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
            byte[] iv = cipher.GetIV()!;
            byte[] encrypted = cipher.DoFinal(data)!;
            return [(byte)iv.Length, .. iv, .. encrypted];
        }
        catch (Java.Lang.Exception ex)
        {
            throw new System.Security.Cryptography.CryptographicException("Keystore encryption failed.", ex);
        }
    }

    public byte[] Unprotect(byte[] data)
    {
        try
        {
            int ivLength = data[0];
            Cipher cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(128, data, 1, ivLength));
            return cipher.DoFinal(data, 1 + ivLength, data.Length - 1 - ivLength)!;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IndexOutOfRangeException)
        {
            throw new System.Security.Cryptography.CryptographicException("Keystore decryption failed.", ex);
        }
    }

    private static IKey GetOrCreateKey()
    {
        KeyStore store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.GetKey(Alias, null) is IKey existing)
        {
            return existing;
        }

        KeyGenerator generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        generator.Init(new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .SetKeySize(256)!
            .Build());
        return generator.GenerateKey()!;
    }
}
