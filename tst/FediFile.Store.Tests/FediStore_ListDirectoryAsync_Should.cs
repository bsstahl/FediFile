#pragma warning disable CA1707
using FediFile.ActivityPub;
using NSubstitute;
using Xunit;

namespace FediFile.Store.Tests;

public sealed class FediStore_ListDirectoryAsync_Should
{
    [Fact]
    public async Task LoadContentCollectionsOnDemand_WhenAnyActorFolderIsOpened()
    {
        var actor = CreateActor("actor");
        var note = new ActivityPubNote(
            "https://example.social/notes/1",
            actor.Id,
            "A note",
            "<p>Content</p>",
            "text/html",
            DateTimeOffset.UtcNow,
            [new ActivityPubAttachment(
                "https://example.social/media/1",
                "image.png",
                new Uri("https://example.social/media/1"),
                "image/png",
                10,
                System.Text.Json.JsonDocument.Parse("{}"))],
            System.Text.Json.JsonDocument.Parse("{}"));
        var storeClient = Substitute.For<IActivityPubClient>();
        var cache = new MemoryFediCache();
        var target = new FediStore(storeClient, cache);
        var actorPath = new FediPath(@"\@actor@example.social");
        await cache.UpsertAsync(
            new FediNode(actor.Id, "actor", FediNodeKind.Actor, actorPath, null, DateTimeOffset.UtcNow, null, true, "root", actor.Handle),
            CancellationToken.None);
#pragma warning disable CA2012
        storeClient.GetActorByIdAsync(actor.Id, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(actor));
        storeClient.GetCollectionAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CreateCollection("content", note.Id)));
        storeClient.GetNoteAsync(note.Id, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(note));
#pragma warning restore CA2012

        await target.ListDirectoryAsync(actorPath, CancellationToken.None);

        foreach (var collectionName in new[] { "Inbox", "Outbox", "Media", "Notes" })
        {
            var entries = await target.ListDirectoryAsync(new FediPath($@"{actorPath.FullPath}\{collectionName}"), CancellationToken.None);
            Assert.NotEmpty(entries);
        }
    }

    [Fact]
    public async Task LoadNestedFollowersOnDemand_WhenFollowerFollowersFolderIsOpened()
    {
        var actor = CreateActor("follower");
        var nestedFollower = CreateActor("nested");
        var storeClient = Substitute.For<IActivityPubClient>();
        var cache = new MemoryFediCache();
        var target = new FediStore(storeClient, cache);
        var actorPath = new FediPath(@"\@follower@example.social");
        var followersPath = new FediPath(@"\@follower@example.social\Followers");
        await cache.UpsertAsync(
            new FediNode(
                actor.Id,
                "follower",
                FediNodeKind.Actor,
                actorPath,
                null,
                DateTimeOffset.UtcNow,
                null,
                true,
                "root",
                actor.Handle),
            CancellationToken.None);
#pragma warning disable CA2012
        storeClient.GetActorByIdAsync(actor.Id, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(actor));
        storeClient.GetCollectionAsync(actor.Followers, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CreateCollection("followers", nestedFollower.Id)));
        storeClient.GetActorByIdAsync(nestedFollower.Id, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(nestedFollower));
        storeClient.GetCollectionAsync(nestedFollower.Followers, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CreateCollection("nested-followers", "https://example.social/users/final")));
#pragma warning restore CA2012

        await target.ListDirectoryAsync(actorPath, CancellationToken.None);
        var followers = await target.ListDirectoryAsync(followersPath, CancellationToken.None);
        var nestedActorPath = new FediPath($@"{followersPath.FullPath}\nested");
        var nestedRoot = await target.ListDirectoryAsync(nestedActorPath, CancellationToken.None);
        var nestedFollowers = await target.ListDirectoryAsync(new FediPath($@"{nestedActorPath.FullPath}\Followers"), CancellationToken.None);

        Assert.Single(followers);
        Assert.Equal("nested", followers[0].Name);
        Assert.Equal(6, nestedRoot.Count);
        Assert.Equal("final", nestedFollowers.Single().Name);
    }

    [Fact]
    public async Task LoadActorRootFoldersOnDemand_WhenFollowingActorIsOpened()
    {
        var actor = new ActivityPubActor(
            "https://example.social/users/follower",
            "follower",
            "@follower@example.social",
            new Uri("https://example.social/users/follower/inbox"),
            new Uri("https://example.social/users/follower/outbox"),
            new Uri("https://example.social/users/follower/followers"),
            new Uri("https://example.social/users/follower/following"),
            null,
            null,
            System.Text.Json.JsonDocument.Parse("{}"));
        var storeClient = Substitute.For<IActivityPubClient>();
        var cache = new MemoryFediCache();
        var target = new FediStore(storeClient, cache);
        var actorPath = new FediPath(@"\@follower@example.social");
        await cache.UpsertAsync(
            new FediNode(
                actor.Id,
                actorPath.Segments[0],
                FediNodeKind.Actor,
                actorPath,
                null,
                DateTimeOffset.UtcNow,
                null,
                true,
                null,
                actor.Id),
            CancellationToken.None);
#pragma warning disable CA2012
        storeClient.GetActorByIdAsync(actor.Id, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(actor));
#pragma warning restore CA2012

        var entries = await target.ListDirectoryAsync(actorPath, CancellationToken.None);

        Assert.Equal(6, entries.Count);
        Assert.Contains(entries, entry => entry.Name == "Followers");
        Assert.Contains(entries, entry => entry.Name == "Following");
    }

    private static ActivityPubActor CreateActor(string name) =>
        new(
            $"https://example.social/users/{name}",
            name,
            $"@{name}@example.social",
            new Uri($"https://example.social/users/{name}/inbox"),
            new Uri($"https://example.social/users/{name}/outbox"),
            new Uri($"https://example.social/users/{name}/followers"),
            new Uri($"https://example.social/users/{name}/following"),
            null,
            null,
            System.Text.Json.JsonDocument.Parse("{}"));

    private static ActivityPubCollectionView CreateCollection(string name, params string[] items) =>
        new(
            $"https://example.social/{name}",
            name,
            items,
            System.Text.Json.JsonDocument.Parse("{}"));
}
#pragma warning restore CA1707
