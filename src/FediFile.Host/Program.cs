using FediFile.ActivityPub;
using FediFile.Host;
using FediFile.Store;
using FediFile.WinFsp;
using Microsoft.Extensions.Logging;

var command = args.Length > 0 ? args[0].ToUpperInvariant() : "MOUNT";
var actorHandle = command is "LIST" or "CAT"
    ? args.ElementAtOrDefault(1) ?? throw new ArgumentException("An actor handle is required.")
    : args.ElementAtOrDefault(1) ?? "@demo@example.social";

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
    var path = args.ElementAtOrDefault(2) ?? HostPath.GetDefaultNotesPath(actorHandle);

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

#pragma warning disable CA1303
Console.WriteLine(HostMessages.StarterHostInitialized);
Console.WriteLine($"Requested command: {command}");
Console.WriteLine($"Seed actor: {actorHandle}");
Console.WriteLine(HostMessages.WinFspNextStep);
#pragma warning restore CA1303

internal static class HostMessages
{
    public const string StarterHostInitialized = "FediFile starter host initialized.";
    public const string WinFspNextStep = "Next step: replace WinFspAdapter with a concrete WinFsp or Dokan mount host and wire shell registration.";
}
