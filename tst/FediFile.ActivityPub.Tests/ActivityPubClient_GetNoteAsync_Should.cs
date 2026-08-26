#pragma warning disable CA1707
using System.Net;
using System.Net.Http;
using FediFile.ActivityPub;
using Xunit;

namespace FediFile.ActivityPub.Tests;

public sealed class ActivityPubClient_GetNoteAsync_Should
{
    [Fact]
    public async Task ReturnNestedNote_WhenResponseIsCreateActivity()
    {
        using var handler = new JsonHttpMessageHandler(
            """
            {
              "type": "Create",
              "actor": "https://example.social/users/alice",
              "object": {
                "id": "https://example.social/notes/1",
                "type": "Note",
                "attributedTo": "https://example.social/users/alice",
                "name": "A note",
                "content": "<p>Hello from the Fediverse.</p>",
                "mediaType": "text/html"
              }
            }
            """);
        using var httpClient = new HttpClient(handler);
        var target = new ActivityPubClient(httpClient);

        var note = await target.GetNoteAsync(
            "https://example.social/notes/1/activity",
            CancellationToken.None);

        Assert.Equal("https://example.social/notes/1", note.Id);
        Assert.Equal("<p>Hello from the Fediverse.</p>", note.Content);
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
