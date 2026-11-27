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
    /// <remarks>
    /// Uses dedicated threads and a minimum number of reads instead of a time window: on a busy CI machine the
    /// thread pool can be starved by the writers and other tests, so pool-based readers may not run at all.
    /// </remarks>
    [Fact]
    public async Task ConcurrentWritersAndReaders_NeverSeeAPartialSnapshot()
    {
        const int MinReadsPerReader = 100;
        const int MinWrites = 20;
        var path = _dir.File("mcp-tools.json");
        var tools = Enumerable.Range(0, 200).Select(i => Tool($"tool_{i:D3}", """{"type":"object","properties":{"city":{"type":"string","description":"City."}}}""")).ToArray();
        var surfaceA = Surface(tools);
        var surfaceB = Surface([.. tools, Tool("tool_extra")]); // A -> B is non-breaking, so readers pass either way
        File.WriteAllText(path, surfaceA.ToJson());
        var update = new SnapshotOptions { Update = true };
        var verify = new SnapshotOptions { Update = false, CreateMissing = false };

        using var start = new ManualResetEventSlim();
        using var readersDone = new CancellationTokenSource();
        var writes = 0;
        var writers = Enumerable.Range(0, 4).Select(w => StartThread(() =>
        {
            start.Wait();
            for (var i = 0; !readersDone.IsCancellationRequested; i++)
            {
                ToolSnapshot.Verify((i + w) % 2 == 0 ? surfaceA : surfaceB, path, update);
                Interlocked.Increment(ref writes);
            }
        })).ToList();
        var readers = Enumerable.Range(0, 4).Select(_ => StartThread(() =>
        {
            start.Wait();
            // Keep reading until the writers have replaced the file a few times, however the threads get scheduled.
            for (var i = 0; i < MinReadsPerReader || Volatile.Read(ref writes) < MinWrites; i++)
            {
                ToolSnapshot.Verify(surfaceB, path, verify);
            }
        })).ToList();

        start.Set();
        try
        {
            await Task.WhenAll(readers).WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        }
        finally
        {
            readersDone.Cancel();
            await Task.WhenAll(writers);
        }

        Assert.True(writes >= MinWrites);
    }

    private static Task StartThread(Action action) =>
        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
