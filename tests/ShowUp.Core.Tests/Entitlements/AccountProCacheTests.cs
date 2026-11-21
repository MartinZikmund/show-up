using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Entitlements;

namespace ShowUp.Core.Tests.Entitlements;

[TestClass]
public sealed class AccountProCacheTests
{
    private readonly DictionaryPreferences _preferences = new();
    private readonly AccountProCache _cache;

    public AccountProCacheTests()
    {
        _cache = new(_preferences);
    }

    [TestMethod]
    public void Anonymous_KeepsTheOriginalKeys()
    {
        _cache.LastKnownPro = true;

        _preferences.Values.Should().ContainKey("LastKnownPro");
    }

    [TestMethod]
    public void Accounts_DoNotShareProState()
    {
        _cache.UseAccount("alice");
        _cache.LastKnownPro = true;
        var expires = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _cache.LastKnownProExpires = expires;

        _cache.UseAccount("bob");

        _cache.LastKnownPro.Should().BeFalse();
        _cache.LastKnownProExpires.Should().BeNull();
        _cache.UseAccount("alice");
        _cache.LastKnownPro.Should().BeTrue("switching back finds Alice's own state");
        _cache.LastKnownProExpires.Should().Be(expires);
    }

    [TestMethod]
    public void SigningOut_ReturnsToTheAnonymousState()
    {
        _cache.UseAccount("alice");
        _cache.LastKnownPro = true;

        _cache.UseAccount(null);

        _cache.LastKnownPro.Should().BeFalse();
    }

    [TestMethod]
    public void UseAccount_NewAccount_ReadsTheAnonymousStateUntilTheStoreWrites()
    {
        var expires = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _cache.LastKnownPro = true;
        _cache.LastKnownProExpires = expires;

        _cache.UseAccount("alice");

        _cache.LastKnownPro.Should().BeTrue("a Pro buyer signing in must not flash Free while the store answers");
        _cache.LastKnownProExpires.Should().Be(expires);
        _cache.LastKnownPro = false;
        _cache.LastKnownPro.Should().BeFalse();
        _cache.LastKnownProExpires.Should().Be(expires, "the seed was copied whole before the first write");
        _cache.UseAccount(null);
        _cache.LastKnownPro.Should().BeTrue("the anonymous state itself is untouched");
    }

    [TestMethod]
    public void UseAccount_KnownAccount_IsNotSeeded()
    {
        _cache.UseAccount("alice");
        _cache.LastKnownPro = false;
        _cache.UseAccount(null);
        _cache.LastKnownPro = true;

        _cache.UseAccount("alice");

        _cache.LastKnownPro.Should().BeFalse();
    }

    [TestMethod]
    public void Keys_HashTheUserIdInsteadOfStoringIt()
    {
        _cache.UseAccount("alice@example.com");
        _cache.LastKnownPro = true;

        _preferences.Values.Keys.Should().OnlyContain(k => !k.Contains("alice") && (k.StartsWith("LastKnownPro.") || k.StartsWith("LastKnownProExpires.")));
        AccountProCache.KeySuffix("alice@example.com").Should().NotBe(AccountProCache.KeySuffix("bob@example.com"));
        AccountProCache.KeySuffix("alice@example.com").Should().Be(AccountProCache.KeySuffix("alice@example.com"), "stable across launches");
    }

    private sealed class DictionaryPreferences : IPreferences
    {
        public Dictionary<string, object> Values { get; } = [];

        public T Get<T>(string key, T defaultValue) => Values.TryGetValue(key, out var v) ? (T)v : defaultValue;

        public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
        {
            if (Values.TryGetValue(key, out var v))
            {
                value = (T)v;
                return true;
            }

            value = default;
            return false;
        }

        public void Set<T>(string key, T value) => Values[key] = value!;

        public T GetComplex<T>(string key, T defaultValue) => Get(key, defaultValue);

        public bool TryGetComplex<T>(string key, [MaybeNullWhen(false)] out T value) => TryGet(key, out value);

        public void SetComplex<T>(string key, T value) => Set(key, value);

        public bool ContainsKey(string key) => Values.ContainsKey(key);

        public void Remove(string key) => Values.Remove(key);

        public void Clear() => Values.Clear();
    }
}
