using McpProbe.Cli;

namespace McpProbe.Tests;

public sealed class CommandLineSplitterTests
{
    [Theory]
    [InlineData("dotnet run", new[] { "dotnet", "run" })]
    [InlineData("  dotnet   run  ", new[] { "dotnet", "run" })]
    [InlineData("dotnet run --project \"My Server/app.csproj\"", new[] { "dotnet", "run", "--project", "My Server/app.csproj" })]
    [InlineData("node 'server one.js' --flag", new[] { "node", "server one.js", "--flag" })]
    [InlineData("echo \"a \\\"quoted\\\" word\"", new[] { "echo", "a \"quoted\" word" })]
    [InlineData("echo \"\"", new[] { "echo", "" })]
    [InlineData("C:\\tools\\server.exe --x", new[] { "C:\\tools\\server.exe", "--x" })]
    [InlineData("pre\"fix\"ed", new[] { "prefixed" })]
    [InlineData("", new string[0])]
    public void Split(string commandLine, string[] expected)
    {
        Assert.Equal(expected, CommandLineSplitter.Split(commandLine));
    }

    [Fact]
    public void Split_RejectsUnclosedQuote()
    {
        Assert.Throws<FormatException>(() => CommandLineSplitter.Split("dotnet \"run"));
    }
}
