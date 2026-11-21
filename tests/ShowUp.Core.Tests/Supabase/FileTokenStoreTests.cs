using AwesomeAssertions;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Tests.Supabase;

[TestClass]
public sealed class FileTokenStoreTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "showup-tokens-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string FilePath => Path.Combine(_dir, "session.bin");

    [TestMethod]
    public async Task ReadAsync_NothingSaved_ReturnsNull() =>
        (await new FileTokenStore(FilePath).ReadAsync()).Should().BeNull();

    [TestMethod]
    public async Task WriteAsync_ThenRead_RoundTrips()
    {
        FileTokenStore store = new(FilePath);

        await store.WriteAsync("secret-value");

        (await store.ReadAsync()).Should().Be("secret-value");
        File.Exists(FilePath + ".tmp").Should().BeFalse();
    }

    [TestMethod]
    public async Task WriteAsync_Twice_ReplacesTheValue()
    {
        FileTokenStore store = new(FilePath);
        await store.WriteAsync("one");

        await store.WriteAsync("two");

        (await store.ReadAsync()).Should().Be("two");
    }

    [TestMethod]
    public async Task WriteAsync_WithProtector_DoesNotStorePlaintext()
    {
        FileTokenStore store = new(FilePath, new XorProtector());

        await store.WriteAsync("secret-value");

        System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(FilePath)).Should().NotContain("secret");
        (await store.ReadAsync()).Should().Be("secret-value");
    }

    [TestMethod]
    public async Task ReadAsync_ProtectorCannotDecrypt_ReturnsNull()
    {
        await new FileTokenStore(FilePath).WriteAsync("secret-value");

        (await new FileTokenStore(FilePath, new ThrowingProtector()).ReadAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task ClearAsync_RemovesTheFile()
    {
        FileTokenStore store = new(FilePath);
        await store.WriteAsync("x");

        await store.ClearAsync();

        File.Exists(FilePath).Should().BeFalse();
    }

    [TestMethod]
    public async Task WriteAsync_OnUnix_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix permissions only.");
        }

        await new FileTokenStore(FilePath).WriteAsync("x");

        File.GetUnixFileMode(FilePath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private sealed class XorProtector : ITokenProtector
    {
        public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();

        public byte[] Unprotect(byte[] data) => Protect(data);
    }

    private sealed class ThrowingProtector : ITokenProtector
    {
        public byte[] Protect(byte[] data) => data;

        public byte[] Unprotect(byte[] data) => throw new System.Security.Cryptography.CryptographicException("wrong user");
    }
}
