using McpProbe.Snapshots;
using McpProbe.Tests.TestSupport;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

public sealed class ToolSnapshotConcurrencyTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>
    /// <c>dotnet test</c> runs each target framework in its own process at the same time, so two runs can
    /// write and read the same snapshot at once (first run, or update mode). Readers must never see a partial file.
    /// </summary>
    [Fact]
    public async Task ConcurrentWritersAndReaders_NeverSeeAPartialSnapshot()
    {
        var path = _dir.File("mcp-tools.json");
        var tools = Enumerable.Range(0, 200).Select(i => Tool($"tool_{i:D3}", """{"type":"object","properties":{"city":{"type":"string","description":"City."}}}""")).ToArray();
        var surfaceA = Surface(tools);
        var surfaceB = Surface([.. tools, Tool("tool_extra")]); // A -> B is non-breaking, so readers pass either way
        File.WriteAllText(path, surfaceA.ToJson());
        var update = new SnapshotOptions { Update = true };
        var verify = new SnapshotOptions { Update = false, CreateMissing = false };

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
            {
                ToolSnapshot.Verify((i + w) % 2 == 0 ? surfaceA : surfaceB, path, update);
            }
        }));
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var reads = 0;
            while (!stop.IsCancellationRequested)
            {
                ToolSnapshot.Verify(surfaceB, path, verify);
                reads++;
            }

            return reads;
        })).ToList();

        var all = Task.WhenAll(writers.Concat<Task>(readers));
        await all;
        Assert.True((await Task.WhenAll(readers)).Sum() > 0);
    }
}
