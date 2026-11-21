using AwesomeAssertions;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Tests.Supabase;

[TestClass]
public sealed class SupabaseOptionsTests
{
    [TestMethod]
    [DataRow("https://abc.supabase.co", "sb_publishable_x", true)]
    [DataRow("https://abc.supabase.co/", "sb_publishable_x", true)]
    [DataRow("", "", false)]
    [DataRow("https://abc.supabase.co", "", false)]
    [DataRow("", "sb_publishable_x", false)]
    [DataRow("not a url", "sb_publishable_x", false)]
    [DataRow("http://abc.supabase.co", "sb_publishable_x", false)]
    [DataRow("http://localhost:54321", "sb_publishable_x", true)]
    [DataRow("http://127.0.0.1:54321", "sb_publishable_x", true)]
    public void IsConfigured_Values_RequiresBothAndAValidUrl(string url, string key, bool expected) =>
        new SupabaseOptions { Url = url, PublishableKey = key }.IsConfigured.Should().Be(expected);

    [TestMethod]
    public void Url_TrailingSlash_IsTrimmed() =>
        new SupabaseOptions { Url = " https://abc.supabase.co/ " }.Url.Should().Be("https://abc.supabase.co");
}
