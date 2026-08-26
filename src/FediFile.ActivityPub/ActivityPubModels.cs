using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FediFile.ActivityPub;

public sealed record WebFingerResult(string Subject, IReadOnlyDictionary<string, string> Links);

public sealed record ActivityPubActor(
    string Id,
    string PreferredUsername,
    string Handle,
    Uri Inbox,
    Uri Outbox,
    Uri Followers,
    Uri Following,
    Uri? Featured,
    Uri? Icon,
    JsonDocument RawDocument);

public sealed record ActivityPubCollectionView(
    string Id,
    string Name,
    IReadOnlyList<string> OrderedItems,
    JsonDocument RawDocument);

public sealed record ActivityPubNote(
    string Id,
    string AttributedTo,
    string? Name,
    string Content,
    string MediaType,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<ActivityPubAttachment> Attachments,
    JsonDocument RawDocument);

public sealed record ActivityPubAttachment(
    string Id,
    string Name,
    Uri Url,
    string MediaType,
    long? Size,
    JsonDocument RawDocument);

public sealed record ActivityPubDeleteRequest(string ObjectId, string ActorId, Uri Inbox);

public sealed record ActivityPubUpdateRequest(
    string ObjectId,
    string ActorId,
    string? Name,
    string Content,
    string MediaType,
    IReadOnlyList<ActivityPubAttachment> Attachments);

public interface IHttpSignatureProvider
{
    ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

public interface IActivityPubClient
{
    ValueTask<WebFingerResult> ResolveActorAsync(string handle, CancellationToken cancellationToken);
    ValueTask<ActivityPubActor> GetActorAsync(string handle, CancellationToken cancellationToken);
    ValueTask<ActivityPubCollectionView> GetCollectionAsync(Uri collectionUri, CancellationToken cancellationToken);
    ValueTask<ActivityPubNote> GetNoteAsync(string objectId, CancellationToken cancellationToken);
    ValueTask<Stream> OpenMediaReadAsync(Uri mediaUri, CancellationToken cancellationToken);
    ValueTask<ActivityPubNote> CreateNoteAsync(ActivityPubUpdateRequest request, CancellationToken cancellationToken);
    ValueTask<ActivityPubNote> UpdateNoteAsync(ActivityPubUpdateRequest request, CancellationToken cancellationToken);
    ValueTask DeleteObjectAsync(ActivityPubDeleteRequest request, CancellationToken cancellationToken);
}

public sealed class RsaHttpSignatureProvider : IHttpSignatureProvider
{
    private readonly RSA _privateKey;
    private readonly string _keyId;

    public RsaHttpSignatureProvider(string keyId, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        ArgumentNullException.ThrowIfNull(privateKey);

        _keyId = keyId;
        _privateKey = privateKey;
    }

    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dateValue = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        request.Headers.Date = DateTimeOffset.Parse(
            dateValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        if (request.RequestUri is null)
        {
            throw new InvalidOperationException("The HTTP request is missing a request URI.");
        }

        request.Headers.Host = request.RequestUri.Host;

        var pathAndQuery = request.RequestUri.PathAndQuery;
        var methodName = request.Method.Method.ToUpperInvariant();

        var signingString = new StringBuilder()
            .Append("(request-target): ")
            .Append(methodName)
            .Append(' ')
            .Append(pathAndQuery)
            .Append('\n')
            .Append("host: ")
            .Append(request.Headers.Host)
            .Append('\n')
            .Append("date: ")
            .Append(dateValue)
            .ToString();

        var signatureBytes = _privateKey.SignData(
            Encoding.UTF8.GetBytes(signingString),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var signatureHeader =
            $"keyId=\"{_keyId}\",algorithm=\"rsa-sha256\",headers=\"(request-target) host date\",signature=\"{Convert.ToBase64String(signatureBytes)}\"";

        request.Headers.TryAddWithoutValidation("Signature", signatureHeader);

        return ValueTask.CompletedTask;
    }
}

public sealed class ActivityPubClient : IActivityPubClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IHttpSignatureProvider? _signatureProvider;

    public ActivityPubClient(HttpClient httpClient, IHttpSignatureProvider? signatureProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _signatureProvider = signatureProvider;
    }

    public async ValueTask<WebFingerResult> ResolveActorAsync(string handle, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        var normalized = handle.TrimStart('@');
        var parts = normalized.Split('@', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new ArgumentException("Expected handle in the form @user@host.", nameof(handle));
        }

        var webFingerUri = new Uri($"https://{parts[1]}/.well-known/webfinger?resource=acct:{parts[0]}@{parts[1]}");
        using var request = new HttpRequestMessage(HttpMethod.Get, webFingerUri);
        using var document = await SendForJsonAsync(request, cancellationToken).ConfigureAwait(false);

        var subject = document.RootElement.GetProperty("subject").GetString() ?? $"acct:{normalized}";
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in document.RootElement.GetProperty("links").EnumerateArray())
        {
            var rel = link.GetProperty("rel").GetString();
            var href = link.TryGetProperty("href", out var hrefValue) ? hrefValue.GetString() : null;

            if (!string.IsNullOrWhiteSpace(rel) && !string.IsNullOrWhiteSpace(href))
            {
                links[rel!] = href!;
            }
        }

        return new WebFingerResult(subject, links);
    }

    public async ValueTask<ActivityPubActor> GetActorAsync(string handle, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        var webFinger = await ResolveActorAsync(handle, cancellationToken).ConfigureAwait(false);
        if (!webFinger.Links.TryGetValue("self", out var actorUrl))
        {
            throw new InvalidOperationException($"No self link was returned for actor {handle}.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, actorUrl);
        using var document = await SendForJsonAsync(request, cancellationToken).ConfigureAwait(false);
        return ParseActor(handle, document);
    }

    public async ValueTask<ActivityPubCollectionView> GetCollectionAsync(Uri collectionUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collectionUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, collectionUri);
        using var document = await SendForJsonAsync(request, cancellationToken).ConfigureAwait(false);
        var collection = ParseCollection(document);
        if (collection.OrderedItems.Count > 0
            || !document.RootElement.TryGetProperty("first", out var firstValue)
            || !Uri.TryCreate(firstValue.GetString(), UriKind.Absolute, out var firstPageUri))
        {
            return collection;
        }

        using var firstPageRequest = new HttpRequestMessage(HttpMethod.Get, firstPageUri);
        using var firstPageDocument = await SendForJsonAsync(firstPageRequest, cancellationToken).ConfigureAwait(false);
        return ParseCollection(firstPageDocument);
    }

    public async ValueTask<ActivityPubNote> GetNoteAsync(string objectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);

        using var request = new HttpRequestMessage(HttpMethod.Get, objectId);
        using var document = await SendForJsonAsync(request, cancellationToken).ConfigureAwait(false);
        return ParseNote(document);
    }

    public async ValueTask<Stream> OpenMediaReadAsync(Uri mediaUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, mediaUri);
        await ApplySignatureIfNeededAsync(request, cancellationToken).ConfigureAwait(false);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new MemoryStream(contentBytes, writable: false);
    }

    public async ValueTask<ActivityPubNote> CreateNoteAsync(ActivityPubUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = await BuildOutboundActivityAsync("Create", request, cancellationToken).ConfigureAwait(false);
        using var document = await SendForJsonAsync(message, cancellationToken).ConfigureAwait(false);
        return ParseNote(document);
    }

    public async ValueTask<ActivityPubNote> UpdateNoteAsync(ActivityPubUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = await BuildOutboundActivityAsync("Update", request, cancellationToken).ConfigureAwait(false);
        using var document = await SendForJsonAsync(message, cancellationToken).ConfigureAwait(false);
        return ParseNote(document);
    }

    public async ValueTask DeleteObjectAsync(ActivityPubDeleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new
        {
            @context = "https://www.w3.org/ns/activitystreams",
            type = "Delete",
            actor = request.ActorId,
            @object = request.ObjectId
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, request.Inbox)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };

        await ApplySignatureIfNeededAsync(message, cancellationToken).ConfigureAwait(false);
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static ActivityPubActor ParseActor(string handle, JsonDocument document)
    {
        var root = document.RootElement;
        return new ActivityPubActor(
            root.GetProperty("id").GetString() ?? handle,
            root.TryGetProperty("preferredUsername", out var username) ? username.GetString() ?? handle : handle,
            handle,
            new Uri(root.GetProperty("inbox").GetString()!),
            new Uri(root.GetProperty("outbox").GetString()!),
            new Uri(root.GetProperty("followers").GetString()!),
            new Uri(root.GetProperty("following").GetString()!),
            root.TryGetProperty("featured", out var featured) && Uri.TryCreate(featured.GetString(), UriKind.Absolute, out var featuredUri) ? featuredUri : null,
            root.TryGetProperty("icon", out var iconElement)
                && iconElement.TryGetProperty("url", out var iconUrl)
                && Uri.TryCreate(iconUrl.GetString(), UriKind.Absolute, out var iconUri)
                ? iconUri
                : null,
            JsonDocument.Parse(document.RootElement.GetRawText()));
    }

    private static ActivityPubCollectionView ParseCollection(JsonDocument document)
    {
        var root = document.RootElement;
        var items = new List<string>();

        if (root.TryGetProperty("orderedItems", out var orderedItems))
        {
            foreach (var item in orderedItems.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    items.Add(item.GetString()!);
                }
                else if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var idValue)
                    && idValue.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(idValue.GetString()))
                {
                    items.Add(idValue.GetString()!);
                }
            }
        }

        return new ActivityPubCollectionView(
            root.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
            root.TryGetProperty("name", out var name) ? name.GetString() ?? "collection" : "collection",
            items,
            JsonDocument.Parse(root.GetRawText()));
    }

    private static ActivityPubNote ParseNote(JsonDocument document)
    {
        var root = document.RootElement;
        if (root.TryGetProperty("object", out var objectElement)
            && objectElement.ValueKind == JsonValueKind.Object)
        {
            using var nestedDocument = JsonDocument.Parse(objectElement.GetRawText());
            return ParseNote(nestedDocument);
        }

        var attachments = new List<ActivityPubAttachment>();

        if (root.TryGetProperty("attachment", out var attachmentElement) && attachmentElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in attachmentElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("url", out var urlValue) || !Uri.TryCreate(urlValue.GetString(), UriKind.Absolute, out var url))
                {
                    continue;
                }

                attachments.Add(new ActivityPubAttachment(
                    entry.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? url.ToString() : url.ToString(),
                    entry.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? Path.GetFileName(url.LocalPath) : Path.GetFileName(url.LocalPath),
                    url,
                    entry.TryGetProperty("mediaType", out var mediaTypeValue) ? mediaTypeValue.GetString() ?? "application/octet-stream" : "application/octet-stream",
                    entry.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var size) ? size : null,
                    JsonDocument.Parse(entry.GetRawText())));
            }
        }

        return new ActivityPubNote(
            root.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
            root.TryGetProperty("attributedTo", out var attributedTo) ? attributedTo.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("name", out var name) ? name.GetString() : null,
            root.TryGetProperty("content", out var content) ? content.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("mediaType", out var mediaType) ? mediaType.GetString() ?? "text/html" : "text/html",
            root.TryGetProperty("updated", out var updated) && updated.ValueKind == JsonValueKind.String
                ? DateTimeOffset.Parse(updated.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                : null,
            attachments,
            JsonDocument.Parse(root.GetRawText()));
    }

    private async ValueTask ApplySignatureIfNeededAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/activity+json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/ld+json"));

        if (_signatureProvider is not null)
        {
            await _signatureProvider.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JsonDocument> SendForJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await ApplySignatureIfNeededAsync(request, cancellationToken).ConfigureAwait(false);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<HttpRequestMessage> BuildOutboundActivityAsync(
        string activityType,
        ActivityPubUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new
        {
            @context = "https://www.w3.org/ns/activitystreams",
            type = activityType,
            actor = request.ActorId,
            @object = new
            {
                id = request.ObjectId,
                type = "Note",
                attributedTo = request.ActorId,
                name = request.Name,
                content = request.Content,
                mediaType = request.MediaType,
                attachment = request.Attachments.Select(attachment => new
                {
                    id = attachment.Id,
                    type = "Document",
                    name = attachment.Name,
                    url = attachment.Url,
                    mediaType = attachment.MediaType
                }).ToArray()
            }
        };

        var actor = await GetNoteAsync(request.ObjectId, cancellationToken).ConfigureAwait(false);
        var message = new HttpRequestMessage(HttpMethod.Post, actor.AttributedTo)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };

        await ApplySignatureIfNeededAsync(message, cancellationToken).ConfigureAwait(false);
        return message;
    }
}
