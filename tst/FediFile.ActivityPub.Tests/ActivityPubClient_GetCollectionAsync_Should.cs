#pragma warning disable CA1707
using System.Net;
using System.Net.Http;
using FediFile.ActivityPub;
using Xunit;

namespace FediFile.ActivityPub.Tests;

public sealed class ActivityPubClient_GetCollectionAsync_Should
{
    [Fact]
    public async Task ReturnEmbeddedObjectIds_WhenCollectionContainsEmbeddedObjects()
    {
        using var handler = new JsonHttpMessageHandler("""
            {
              "id": "https://example.social/users/alice/outbox",
              "type": "OrderedCollectionPage",
              "orderedItems": [
                {
                  "id": "https://example.social/notes/1",
                  "type": "Note",
                  "content": "<p>Hello</p>"
                }
              ]
            }
            """);
          using var httpClient = new HttpClient(handler);
        var target = new ActivityPubClient(httpClient);

        var collection = await target.GetCollectionAsync(
            new Uri("https://example.social/users/alice/outbox"),
            CancellationToken.None);

        Assert.Single(collection.OrderedItems);
        Assert.Equal("https://example.social/notes/1", collection.OrderedItems[0]);
    }

    private sealed class JsonHttpMessageHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/activity+json")
            });
    }
}
#pragma warning restore CA1707
