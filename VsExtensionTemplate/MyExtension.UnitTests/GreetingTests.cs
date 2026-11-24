using MyExtension.Core;

namespace MyExtension.UnitTests;

public class GreetingTests
{
    [Fact]
    public void GreetsByName() =>
        Assert.Equal("Hello, Ada, from My Extension!", Greeting.For(" Ada "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FallsBackWithoutAName(string? name) =>
        Assert.Equal("Hello from My Extension!", Greeting.For(name));
}
