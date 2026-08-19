using FediFile.ActivityPub;
using FediFile.Store;
using FediFile.WinFsp;

var mountPoint = args.Length > 0 ? args[0] : "F:";
var actorHandle = args.Length > 1 ? args[1] : "@demo@example.social";

using var httpClient = new HttpClient
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

#pragma warning disable CA1303
Console.WriteLine(HostMessages.StarterHostInitialized);
Console.WriteLine($"Requested mount point: {mountPoint}");
Console.WriteLine($"Seed actor: {actorHandle}");
Console.WriteLine(HostMessages.WinFspNextStep);
#pragma warning restore CA1303

internal static class HostMessages
{
    public const string StarterHostInitialized = "FediFile starter host initialized.";
    public const string WinFspNextStep = "Next step: replace WinFspAdapter with a concrete WinFsp or Dokan mount host and wire shell registration.";
}
