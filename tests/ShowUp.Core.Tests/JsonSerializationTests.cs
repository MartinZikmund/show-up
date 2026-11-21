using System.Text.Json;
using ShowUp.Core.Models;
using AwesomeAssertions;

namespace ShowUp.Core.Tests;

[TestClass]
public class JsonSerializationTests
{
    [TestMethod]
    public void IsReflectionEnabledByDefault_TestHost_IsFalse()
    {
        JsonSerializer.IsReflectionEnabledByDefault.Should().BeFalse();
    }

    [TestMethod]
    public void Serialize_ReflectionOverload_ThrowsInvalidOperationException()
    {
        Action act = () => JsonSerializer.Serialize(new ExampleModel());

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void RoundTrip_SourceGeneratedContext_PreservesMembers()
    {
        ExampleModel model = new() { Id = 42, Name = "Example" };

        string json = JsonSerializer.Serialize(model, ShowUpJsonContext.Default.ExampleModel);
        ExampleModel? result = JsonSerializer.Deserialize(json, ShowUpJsonContext.Default.ExampleModel);

        result.Should().Be(model);
    }
}
