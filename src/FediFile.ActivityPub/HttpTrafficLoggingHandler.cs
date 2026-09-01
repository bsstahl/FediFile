using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FediFile.ActivityPub;

public sealed class HttpTrafficLoggingHandler(ILogger logger) : DelegatingHandler
{
    private static readonly Action<ILogger, string, string, Exception?> RequestStarted =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1000, nameof(RequestStarted)),
            "HTTP request started {Method} {Uri}");

    private static readonly Action<ILogger, int, string, string, double, Exception?> ResponseReceived =
        LoggerMessage.Define<int, string, string, double>(
            LogLevel.Information,
            new EventId(1001, nameof(ResponseReceived)),
            "HTTP response received {StatusCode} for {Method} {Uri} in {ElapsedMilliseconds} ms");

    private static readonly Action<ILogger, int, string, string, double, Exception?> UnsuccessfulResponseReceived =
        LoggerMessage.Define<int, string, string, double>(
            LogLevel.Warning,
            new EventId(1005, nameof(UnsuccessfulResponseReceived)),
            "HTTP response received {StatusCode} for {Method} {Uri} in {ElapsedMilliseconds} ms");

    private static readonly Action<ILogger, string, string, double, Exception?> RequestFailed =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Error,
            new EventId(1002, nameof(RequestFailed)),
            "HTTP request failed for {Method} {Uri} after {ElapsedMilliseconds} ms");

    private static readonly Action<ILogger, string, string, string, Exception?> RequestPayloadReceived =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Trace,
            new EventId(1003, nameof(RequestPayloadReceived)),
            "HTTP request payload {Method} {Uri}: {Payload}");

    private static readonly Action<ILogger, int, string, string, string, Exception?> ResponsePayloadReceived =
        LoggerMessage.Define<int, string, string, string>(
            LogLevel.Trace,
            new EventId(1004, nameof(ResponsePayloadReceived)),
            "HTTP response payload {StatusCode} {Method} {Uri}: {Payload}");

    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestUri = GetSafeUri(request.RequestUri);
        var startedAt = Stopwatch.GetTimestamp();
        RequestStarted(_logger, request.Method.Method, requestUri, null);

        try
        {
            if (_logger.IsEnabled(LogLevel.Trace) && request.Content is not null)
            {
                var (requestPayload, restoredRequestContent) =
                    await ReadAndRestoreContentAsync(request.Content).ConfigureAwait(false);
                request.Content = restoredRequestContent;
                RequestPayloadReceived(_logger, request.Method.Method, requestUri, requestPayload, null);
            }

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Trace) && response.Content is not null)
            {
                var (responsePayload, restoredResponseContent) =
                    await ReadAndRestoreContentAsync(response.Content).ConfigureAwait(false);
                response.Content = restoredResponseContent;
                ResponsePayloadReceived(
                    _logger,
                    (int)response.StatusCode,
                    request.Method.Method,
                    requestUri,
                    responsePayload,
                    null);
            }

            var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                var logResponse = response.IsSuccessStatusCode ? ResponseReceived : UnsuccessfulResponseReceived;
                logResponse(
                    _logger,
                    (int)response.StatusCode,
                    request.Method.Method,
                    requestUri,
                    elapsedMilliseconds,
                    null);
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            RequestFailed(
                _logger,
                request.Method.Method,
                requestUri,
                elapsedMilliseconds,
                exception);
            throw;
        }
    }

    private static string GetSafeUri(Uri? requestUri) =>
        requestUri is null ? "<missing>" : requestUri.GetLeftPart(UriPartial.Path);

    private static async Task<(string Payload, HttpContent Content)> ReadAndRestoreContentAsync(HttpContent content)
    {
        var bytes = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var replacement = new ByteArrayContent(bytes);
        foreach (var header in content.Headers)
        {
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return (Encoding.UTF8.GetString(bytes), replacement);
    }
}
