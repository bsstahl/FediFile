#pragma warning disable CA1707
using FediFile.ActivityPub;
using NSubstitute;
using Xunit;

namespace FediFile.Store.Tests;

public sealed class FediStore_SynchronizeActorAsync_Should
{
    private readonly IActivityPubClient _activityPubClient;
    private readonly IFediCache _cache;
    private readonly FediStore target;

    public FediStore_SynchronizeActorAsync_Should()
    {
        _activityPubClient = Substitute.For<IActivityPubClient>();
        _cache = new MemoryFediCache();
        target = new FediStore(_activityPubClient, _cache);
    }

    private static ActivityPubActor CreateActor() =>
        new(
            "https://example.social/users/alice",
            "alice",
            "@alice@example.social",
            new Uri("https://example.social/inbox"),
            new Uri("https://example.social/outbox"),
            new Uri("https://example.social/followers"),
            new Uri("https://example.social/following"),
            null,
            null,
            System.Text.Json.JsonDocument.Parse("{}"));

    [Fact]
    public async Task PopulateFollowing_WhenActorIsSynchronized()
    {
#pragma warning disable CA2012
        _activityPubClient.GetActorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CreateActor()));
        _activityPubClient.GetCollectionAsync(Arg.Is<Uri>(uri => uri.AbsoluteUri.EndsWith("/following", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new ActivityPubCollectionView(
            "https://example.social/following",
            "Following",
                ["https://example.social/users/following"],
                System.Text.Json.JsonDocument.Parse("{}"))));
#pragma warning restore CA2012

        await target.SynchronizeActorAsync("@alice@example.social", CancellationToken.None);

        var entries = await target.ListDirectoryAsync(new FediPath(@"\@alice@example.social\Following"), CancellationToken.None);

        Assert.Single(entries);
        Assert.Equal("following", entries[0].Name);
    }

    [Fact]
    public async Task PopulateFollowingOnly_WhenRelationshipCollectionsAreAvailable()
    {
        var actor = CreateActor();
#pragma warning disable CA2012
        _activityPubClient.GetActorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(actor));
        _activityPubClient.GetCollectionAsync(Arg.Is<Uri>(uri => uri.AbsoluteUri.EndsWith("/following", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CreateCollection("following", "https://example.social/users/following")));
#pragma warning restore CA2012

        await target.SynchronizeActorAsync("@alice@example.social", CancellationToken.None);

        var following = await target.ListDirectoryAsync(new FediPath(@"\@alice@example.social\Following"), CancellationToken.None);
        var followers = await target.ListDirectoryAsync(new FediPath(@"\@alice@example.social\Followers"), CancellationToken.None);

        Assert.Equal("following", following.Single().Name);
        Assert.True(following.Single().IsDirectory);
        Assert.Empty(followers);
    }

    [Fact]
    public async Task LoadFollowersOnDemand_WhenFollowersFolderIsListed()
    {
        var actor = CreateActor();
#pragma warning disable CA2012
        _activityPubClient.GetActorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(actor));
        _activityPubClient.GetCollectionAsync(Arg.Is<Uri>(uri => uri.AbsoluteUri.EndsWith("/following", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CreateCollection("following")));
        _activityPubClient.GetCollectionAsync(Arg.Is<Uri>(uri => uri.AbsoluteUri.EndsWith("/followers", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CreateCollection("followers", "https://example.social/users/follower")));
#pragma warning restore CA2012

        await target.SynchronizeActorAsync("@alice@example.social", CancellationToken.None);

        var entries = await target.ListDirectoryAsync(new FediPath(@"\@alice@example.social\Followers"), CancellationToken.None);

        Assert.Single(entries);
        Assert.Equal("follower", entries[0].Name);
        Assert.True(entries[0].IsDirectory);
    }

    private static ActivityPubCollectionView CreateCollection(string name, params string[] items) =>
        new(
            $"https://example.social/{name}",
            name,
            items,
            System.Text.Json.JsonDocument.Parse("{}"));
}
#pragma warning restore CA1707
