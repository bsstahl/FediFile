#pragma warning disable CA1707
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FediFile.ActivityPub.Tests;

public sealed class HttpTrafficLoggingHandler_SendAsync_Should
{
    [Fact]
    public async Task LogRequestAndResponsePayloadsWithoutHeaders_WhenTraceIsEnabled()
    {
        var logger = new RecordingLogger();
        using var innerHandler = new StubHttpMessageHandler("response-payload");
        using var handler = new HttpTrafficLoggingHandler(logger)
        {
            InnerHandler = innerHandler
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://example.social/inbox"))
        {
            Content = new StringContent("request-payload")
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer request-secret");

        using var response = await client.SendAsync(request);
        response.Headers.TryAddWithoutValidation("X-Test-Credential", "response-secret");

        Assert.Contains(logger.Messages, message => message.Contains("request-payload", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("response-payload", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("request-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("response-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogFailureMetadata_WhenRequestFails()
    {
        var logger = new RecordingLogger();
        using var innerHandler = new ThrowingHttpMessageHandler();
        using var handler = new HttpTrafficLoggingHandler(logger)
        {
            InnerHandler = innerHandler
        };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("https://example.social/users/alice")));

        Assert.Contains(logger.Messages, message => message.Contains("HTTP request failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogRequestAndResponseMetadata_WhenRequestSucceeds()
    {
        var logger = new RecordingLogger();
        using var innerHandler = new StubHttpMessageHandler();
        using var handler = new HttpTrafficLoggingHandler(logger)
        {
            InnerHandler = innerHandler
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(
            new Uri("https://example.social/users/alice?resource=acct%3Aalice%40example.social"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(logger.Messages, message => message.Contains("HTTP request started", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("HTTP response received", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("acct%3Aalice%40example.social", StringComparison.Ordinal));
    }

    private sealed class StubHttpMessageHandler(string responsePayload = "") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responsePayload)
            });
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Test failure.");
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
#pragma warning restore CA1707
