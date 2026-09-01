using FediFile.ActivityPub;
using FediFile.Host;
using FediFile.Store;
using FediFile.WinFsp;
using Microsoft.Extensions.Logging;

var options = HostCommandLine.Parse(args);
var command = options.Command;
var actorHandle = options.ActorHandle;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Information).AddSimpleConsole();
});
using var httpTransport = new HttpClientHandler();
using var loggingHandler = new HttpTrafficLoggingHandler(
    loggerFactory.CreateLogger<HttpTrafficLoggingHandler>())
{
    InnerHandler = httpTransport
};
using var httpClient = new HttpClient(loggingHandler)
{
    Timeout = TimeSpan.FromSeconds(30)
};

var activityPubClient = new ActivityPubClient(httpClient);
var cache = new MemoryFediCache();
var store = new FediStore(activityPubClient, cache);

await store.SynchronizeActorAsync(actorHandle, CancellationToken.None).ConfigureAwait(false);

var fileSystem = new FediFileSystem(
    store,
    new FediMutationContext(
        actorHandle,
        $"https://example.social/users/{actorHandle.TrimStart('@').Split('@')[0]}",
        new Uri("https://example.social/inbox")));

var adapter = new WinFspAdapter(fileSystem);

if (command is "LIST" or "CAT")
{
    var path = options.Path ?? HostPath.GetDefaultFollowingPath(actorHandle);

    if (command == "LIST")
    {
        var entries = await fileSystem.ReadDirectoryAsync(path, CancellationToken.None).ConfigureAwait(false);
        foreach (var entry in entries)
        {
#pragma warning disable CA1303
            Console.WriteLine(entry.IsDirectory ? $"<DIR> {entry.Name}" : entry.Name);
#pragma warning restore CA1303
        }
    }
    else
    {
        using var content = await fileSystem.OpenReadAsync(path, CancellationToken.None).ConfigureAwait(false);
        using var output = Console.OpenStandardOutput();
        await content.CopyToAsync(output, CancellationToken.None).ConfigureAwait(false);
    }

    return;
}

using var mountHost = new WinFspMountHost(store);
mountHost.Mount(options.MountPoint);

#pragma warning disable CA1303
Console.WriteLine(HostMessages.StarterHostInitialized);
Console.WriteLine($"Mounted FediFile at {mountHost.MountPoint}");
Console.WriteLine($"Seed actor: {actorHandle}");
Console.WriteLine(HostMessages.MountStopMessage);
#pragma warning restore CA1303

var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.TrySetResult();
};
await shutdown.Task.ConfigureAwait(false);

internal static class HostMessages
{
    public const string StarterHostInitialized = "FediFile starter host initialized.";
    public const string MountStopMessage = "Press Ctrl+C to unmount and exit.";
}
