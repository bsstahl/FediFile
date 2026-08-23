#pragma warning disable CA1707
using System.Text;
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

    private static ActivityPubNote CreateNote() =>
        new(
            "https://example.social/notes/1",
            "https://example.social/users/alice",
            "A note",
            "<p>Hello from the Fediverse.</p>",
            "text/html",
            DateTimeOffset.UtcNow,
            [],
            System.Text.Json.JsonDocument.Parse("{}"));

    [Fact]
    public async Task ReturnCachedNoteContent_WhenActorIsSynchronized()
    {
        var note = CreateNote();
#pragma warning disable CA2012
        _activityPubClient.GetActorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CreateActor()));
        _activityPubClient.GetCollectionAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new ActivityPubCollectionView(
                "https://example.social/outbox",
                "Outbox",
                [note.Id],
                System.Text.Json.JsonDocument.Parse("{}"))));
        _activityPubClient.GetNoteAsync(note.Id, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(note));
#pragma warning restore CA2012

        await target.SynchronizeActorAsync("@alice@example.social", CancellationToken.None);

        var entries = await target.ListDirectoryAsync(new FediPath(@"\@alice@example.social\Notes"), CancellationToken.None);
        var content = await target.OpenReadAsync(new FediPath(@"\@alice@example.social\Notes\A note.html"), CancellationToken.None);
        using var reader = new StreamReader(content.Stream, Encoding.UTF8);

        Assert.Single(entries);
        Assert.Equal("A note.html", entries[0].Name);
        Assert.Equal("<p>Hello from the Fediverse.</p>", await reader.ReadToEndAsync());
    }
}
#pragma warning restore CA1707
