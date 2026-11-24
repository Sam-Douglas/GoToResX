namespace MyExtension.Core;

public static class Greeting
{
    public static string For(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Hello from My Extension!" : $"Hello, {name!.Trim()}, from My Extension!";
}
