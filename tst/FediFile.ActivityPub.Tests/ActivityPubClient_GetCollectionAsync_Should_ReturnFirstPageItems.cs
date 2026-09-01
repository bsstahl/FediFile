#pragma warning disable CA1707
using System.Net;
using System.Net.Http;
using FediFile.ActivityPub;
using Xunit;

namespace FediFile.ActivityPub.Tests;

public sealed class ActivityPubClient_GetCollectionAsync_Should_ReturnFirstPageItems
{
    [Fact]
    public async Task ReturnItemsFromFirstPage_WhenCollectionContainsFirstPageLink()
    {
        using var handler = new JsonHttpMessageHandler(
            """
            {
              "id": "https://example.social/users/alice/outbox",
              "type": "OrderedCollection",
              "first": "https://example.social/users/alice/outbox?page=true"
            }
            """,
            """
            {
              "id": "https://example.social/users/alice/outbox?page=true",
              "type": "OrderedCollectionPage",
              "orderedItems": ["https://example.social/notes/1"]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var target = new ActivityPubClient(httpClient);

        var collection = await target.GetCollectionAsync(
            new Uri("https://example.social/users/alice/outbox"),
            CancellationToken.None);

        Assert.Equal(["https://example.social/notes/1"], collection.OrderedItems);
    }

        [Fact]
        public async Task ReturnItemsFromAllPages_WhenCollectionIsPaginated()
        {
                using var handler = new JsonHttpMessageHandler(
                        """
                        {
                            "id": "https://example.social/users/alice/following",
                            "type": "OrderedCollection",
                            "first": "https://example.social/users/alice/following?page=1"
                        }
                        """,
                        """
                        {
                            "id": "https://example.social/users/alice/following?page=1",
                            "type": "OrderedCollectionPage",
                            "orderedItems": ["https://example.social/users/a"],
                            "next": "https://example.social/users/alice/following?page=2"
                        }
                        """,
                        """
                        {
                            "id": "https://example.social/users/alice/following?page=2",
                            "type": "OrderedCollectionPage",
                            "orderedItems": ["https://example.social/users/b"]
                        }
                        """);
                using var httpClient = new HttpClient(handler);
                var target = new ActivityPubClient(httpClient);

                var collection = await target.GetCollectionAsync(
                        new Uri("https://example.social/users/alice/following"),
                        CancellationToken.None);

                Assert.Equal(
                        ["https://example.social/users/a", "https://example.social/users/b"],
                        collection.OrderedItems);
        }

    private sealed class JsonHttpMessageHandler(params string[] responses) : HttpMessageHandler
    {
        private int _responseIndex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responses[Math.Min(_responseIndex++, responses.Length - 1)],
                    System.Text.Encoding.UTF8,
                    "application/activity+json")
            });
    }
}
#pragma warning restore CA1707
