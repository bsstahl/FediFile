using System.Collections.Concurrent;
using System.Text;
using FediFile.ActivityPub;

namespace FediFile.Store;

public enum FediNodeKind
{
    Actor,
    Collection,
    Note,
    Attachment
}

public sealed record FediPath(string FullPath)
{
    public static FediPath Root { get; } = new(@"\");

    public IReadOnlyList<string> Segments =>
        FullPath.Split(['\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override string ToString() => FullPath;
}

public sealed record FediNode(
    string Id,
    string Name,
    FediNodeKind Kind,
    FediPath Path,
    long? Size,
    DateTimeOffset LastModifiedUtc,
    string? ContentType,
    bool IsDirectory,
    string? ParentId,
    string? ActorHandle);

public sealed record FediContent(string ContentType, Stream Stream, long? Length);

public sealed record FediMutationContext(string ActorHandle, string ActorId, Uri Inbox);

public interface IFediCache
{
    ValueTask<FediNode?> TryGetNodeByPathAsync(FediPath path, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<FediNode>> GetChildrenAsync(FediPath path, CancellationToken cancellationToken);
    ValueTask UpsertAsync(FediNode node, CancellationToken cancellationToken);
    ValueTask DeleteAsync(string nodeId, CancellationToken cancellationToken);
}

public interface IFediStore
{
    ValueTask<FediNode?> TryGetNodeByPathAsync(FediPath path, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<FediNode>> ListDirectoryAsync(FediPath path, CancellationToken cancellationToken);
    ValueTask<FediContent> OpenReadAsync(FediPath path, CancellationToken cancellationToken);
    ValueTask<FediNode> WriteNoteAsync(FediPath path, string content, FediMutationContext context, CancellationToken cancellationToken);
    ValueTask DeleteAsync(FediPath path, FediMutationContext context, CancellationToken cancellationToken);
    ValueTask<FediNode> RenameAsync(FediPath sourcePath, FediPath destinationPath, FediMutationContext context, CancellationToken cancellationToken);
    ValueTask SynchronizeActorAsync(string actorHandle, CancellationToken cancellationToken);
}

public sealed class MemoryFediCache : IFediCache
{
    private readonly ConcurrentDictionary<string, FediNode> _nodesByPath = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<FediNode?> TryGetNodeByPathAsync(FediPath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        _nodesByPath.TryGetValue(path.FullPath, out var node);
        return ValueTask.FromResult(node);
    }

    public ValueTask<IReadOnlyList<FediNode>> GetChildrenAsync(FediPath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        var children = _nodesByPath.Values
            .Where(node => string.Equals(GetParentPath(node.Path.FullPath), path.FullPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<FediNode>>(children);
    }

    public ValueTask UpsertAsync(FediNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);

        _nodesByPath[node.Path.FullPath] = node;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var targets = _nodesByPath
            .Where(entry => string.Equals(entry.Value.Id, nodeId, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var target in targets)
        {
            _nodesByPath.TryRemove(target, out _);
        }

        return ValueTask.CompletedTask;
    }

    private static string GetParentPath(string path)
    {
        if (string.Equals(path, "\\", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd('\\');
        var lastSlash = trimmed.LastIndexOf('\\');
        return lastSlash <= 0 ? "\\" : trimmed[..lastSlash];
    }
}

public sealed class FediStore : IFediStore
{
    private static readonly string[] DefaultCollections = ["Inbox", "Outbox", "Followers", "Following", "Media", "Notes"];

    private readonly IActivityPubClient _activityPubClient;
    private readonly IFediCache _cache;
    private readonly ConcurrentDictionary<string, (Uri Inbox, Uri Outbox, Uri Followers, Uri Following)> _actorCollections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _loadedCollections = new(StringComparer.OrdinalIgnoreCase);

    public FediStore(IActivityPubClient activityPubClient, IFediCache cache)
    {
        ArgumentNullException.ThrowIfNull(activityPubClient);
        ArgumentNullException.ThrowIfNull(cache);

        _activityPubClient = activityPubClient;
        _cache = cache;
    }

    public ValueTask<FediNode?> TryGetNodeByPathAsync(FediPath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _cache.TryGetNodeByPathAsync(path, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<FediNode>> ListDirectoryAsync(FediPath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path == FediPath.Root)
        {
            return await _cache.GetChildrenAsync(path, cancellationToken).ConfigureAwait(false);
        }

        var node = await _cache.TryGetNodeByPathAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Node {path} is not cached.");

        if (!node.IsDirectory)
        {
            throw new IOException($"Node {path} is not a directory.");
        }

        if (node.Kind == FediNodeKind.Actor && !_actorCollections.ContainsKey(node.Id))
        {
            await ExpandActorAsync(node, cancellationToken).ConfigureAwait(false);
        }

        if (node.Kind == FediNodeKind.Collection
            && node.ParentId is not null
            && _actorCollections.TryGetValue(node.ParentId, out var actorCollections)
            && _loadedCollections.TryAdd(path.FullPath, 0))
        {
            var actorPath = new FediPath(GetParentPath(path.FullPath));
            if (string.Equals(node.Name, "Followers", StringComparison.OrdinalIgnoreCase))
            {
                await SynchronizeActorCollectionAsync(actorCollections.Followers, node.Name, actorPath, node.ActorHandle ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(node.Name, "Following", StringComparison.OrdinalIgnoreCase))
            {
                await SynchronizeActorCollectionAsync(actorCollections.Following, node.Name, actorPath, node.ActorHandle ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(node.Name, "Inbox", StringComparison.OrdinalIgnoreCase))
            {
                await SynchronizeNoteCollectionAsync(actorCollections.Inbox, node.Name, actorPath, node.ActorHandle ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(node.Name, "Outbox", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Name, "Notes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Name, "Media", StringComparison.OrdinalIgnoreCase))
            {
                await SynchronizeNoteCollectionAsync(actorCollections.Outbox, node.Name, actorPath, node.ActorHandle ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }
        }

        return await _cache.GetChildrenAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FediContent> OpenReadAsync(FediPath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        var node = await _cache.TryGetNodeByPathAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Node {path} was not found.");

        return node.Kind switch
        {
            FediNodeKind.Note => await OpenNoteAsync(node, cancellationToken).ConfigureAwait(false),
            FediNodeKind.Attachment => await OpenAttachmentAsync(node, cancellationToken).ConfigureAwait(false),
            _ => throw new IOException($"Node {path} is not readable as a file.")
        };
    }

    public async ValueTask<FediNode> WriteNoteAsync(FediPath path, string content, FediMutationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(context);

        var existing = await _cache.TryGetNodeByPathAsync(path, cancellationToken).ConfigureAwait(false);
        var request = new ActivityPubUpdateRequest(
            existing?.Id ?? $"{context.ActorId}/objects/{Guid.NewGuid():N}",
            context.ActorId,
            Path.GetFileNameWithoutExtension(path.FullPath),
            content,
            "text/html",
            Array.Empty<ActivityPubAttachment>());

        var note = existing is null
            ? await _activityPubClient.CreateNoteAsync(request, cancellationToken).ConfigureAwait(false)
            : await _activityPubClient.UpdateNoteAsync(request, cancellationToken).ConfigureAwait(false);

        var updatedNode = MapNoteNode(path, note, context.ActorHandle);
        await _cache.UpsertAsync(updatedNode, cancellationToken).ConfigureAwait(false);
        return updatedNode;
    }

    public async ValueTask DeleteAsync(FediPath path, FediMutationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(context);

        var existing = await _cache.TryGetNodeByPathAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Node {path} was not found.");

        await _activityPubClient.DeleteObjectAsync(
            new ActivityPubDeleteRequest(existing.Id, context.ActorId, context.Inbox),
            cancellationToken).ConfigureAwait(false);

        await _cache.DeleteAsync(existing.Id, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FediNode> RenameAsync(FediPath sourcePath, FediPath destinationPath, FediMutationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(context);

        var existing = await _cache.TryGetNodeByPathAsync(sourcePath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Node {sourcePath} was not found.");

        if (existing.Kind != FediNodeKind.Note)
        {
            throw new NotSupportedException("Starter implementation only supports renaming note-backed files.");
        }

        var currentContent = await OpenReadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        using var contentStream = currentContent.Stream;
        using var reader = new StreamReader(contentStream, Encoding.UTF8, leaveOpen: false);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var updated = await WriteNoteAsync(destinationPath, content, context, cancellationToken).ConfigureAwait(false);
        await _cache.DeleteAsync(existing.Id, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async ValueTask SynchronizeActorAsync(string actorHandle, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorHandle);

        var actor = await _activityPubClient.GetActorAsync(actorHandle, cancellationToken).ConfigureAwait(false);
        var actorPath = new FediPath($@"\{NormalizeHandle(actor.Handle)}");

        await _cache.UpsertAsync(
            new FediNode(
                actor.Id,
                NormalizeHandle(actor.Handle),
                FediNodeKind.Actor,
                actorPath,
                null,
                DateTimeOffset.UtcNow,
                null,
                true,
                null,
                actor.Handle),
            cancellationToken).ConfigureAwait(false);

        _actorCollections[actor.Id] = (actor.Inbox, actor.Outbox, actor.Followers, actor.Following);

        foreach (var collectionName in DefaultCollections)
        {
            var collectionPath = new FediPath($@"{actorPath.FullPath}\{collectionName}");
            await _cache.UpsertAsync(
                new FediNode(
                    $"{actor.Id}#{collectionName}",
                    collectionName,
                    FediNodeKind.Collection,
                    collectionPath,
                    null,
                    DateTimeOffset.UtcNow,
                    null,
                    true,
                    actor.Id,
                    actor.Handle),
                cancellationToken).ConfigureAwait(false);
        }

        await SynchronizeActorCollectionAsync(actor.Following, "Following", actorPath, actor.Handle, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SynchronizeActorCollectionAsync(
        Uri collectionUri,
        string collectionName,
        FediPath actorPath,
        string actorHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var collection = await _activityPubClient.GetCollectionAsync(collectionUri, cancellationToken).ConfigureAwait(false);
            if (collection is null)
            {
                return;
            }

            foreach (var objectId in collection.OrderedItems)
            {
                var actorName = BuildActorDirectoryName(objectId);
                var actorItemPath = new FediPath($@"{actorPath.FullPath}\{collectionName}\{actorName}");
                await _cache.UpsertAsync(
                    new FediNode(
                        objectId,
                        actorName,
                        FediNodeKind.Actor,
                        actorItemPath,
                        null,
                        DateTimeOffset.UtcNow,
                        null,
                        true,
                        actorPath.FullPath,
                        actorHandle),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException)
        {
            // Relationship collections may be private or unavailable to anonymous clients.
        }
    }

    private async ValueTask SynchronizeNoteCollectionAsync(
        Uri collectionUri,
        string collectionName,
        FediPath actorPath,
        string actorHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var collection = await _activityPubClient.GetCollectionAsync(collectionUri, cancellationToken).ConfigureAwait(false);
            if (collection is null)
            {
                return;
            }

            foreach (var objectId in collection.OrderedItems)
            {
                var note = await _activityPubClient.GetNoteAsync(objectId, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(collectionName, "Media", StringComparison.OrdinalIgnoreCase))
                {
                    var notePath = new FediPath($@"{actorPath.FullPath}\{collectionName}\{BuildSafeFileName(note)}.html");
                    await _cache.UpsertAsync(MapNoteNode(notePath, note, actorHandle), cancellationToken).ConfigureAwait(false);
                }

                foreach (var attachment in note.Attachments)
                {
                    var attachmentName = string.IsNullOrWhiteSpace(attachment.Name)
                        ? Path.GetFileName(attachment.Url.LocalPath)
                        : attachment.Name;
                    var attachmentPath = new FediPath($@"{actorPath.FullPath}\Media\{attachmentName}");
                    await _cache.UpsertAsync(
                        new FediNode(
                            attachment.Id,
                            attachmentName,
                            FediNodeKind.Attachment,
                            attachmentPath,
                            attachment.Size,
                            note.UpdatedAt ?? DateTimeOffset.UtcNow,
                            attachment.MediaType,
                            false,
                            note.Id,
                            actorHandle),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (HttpRequestException)
        {
            // Content collections may be private or unavailable to anonymous clients.
        }
    }

    private async ValueTask ExpandActorAsync(FediNode actorNode, CancellationToken cancellationToken)
    {
        if (_actorCollections.ContainsKey(actorNode.Id))
        {
            return;
        }

        var actor = await _activityPubClient.GetActorByIdAsync(actorNode.Id, cancellationToken).ConfigureAwait(false);
        _actorCollections[actor.Id] = (actor.Inbox, actor.Outbox, actor.Followers, actor.Following);

        foreach (var collectionName in DefaultCollections)
        {
            var collectionPath = new FediPath($@"{actorNode.Path.FullPath}\{collectionName}");
            await _cache.UpsertAsync(
                new FediNode(
                    $"{actor.Id}#{collectionName}",
                    collectionName,
                    FediNodeKind.Collection,
                    collectionPath,
                    null,
                    DateTimeOffset.UtcNow,
                    null,
                    true,
                    actor.Id,
                    actor.Handle),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static string GetParentPath(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var lastSlash = trimmed.LastIndexOf('\\');
        return lastSlash <= 0 ? "\\" : trimmed[..lastSlash];
    }

    private async ValueTask<FediContent> OpenNoteAsync(FediNode node, CancellationToken cancellationToken)
    {
        var note = await _activityPubClient.GetNoteAsync(node.Id, cancellationToken).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes(note.Content);
        return new FediContent(note.MediaType, new MemoryStream(bytes, writable: false), bytes.Length);
    }

    private async ValueTask<FediContent> OpenAttachmentAsync(FediNode node, CancellationToken cancellationToken)
    {
        if (node.ContentType is null)
        {
            throw new IOException($"Attachment {node.Path} is missing content type metadata.");
        }

        var attachment = await _activityPubClient.GetNoteAsync(node.ParentId ?? throw new IOException("Attachment is missing its parent note id."), cancellationToken).ConfigureAwait(false);
        var media = attachment.Attachments.FirstOrDefault(item => string.Equals(item.Id, node.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Attachment payload for {node.Path} was not found.");

        var stream = await _activityPubClient.OpenMediaReadAsync(media.Url, cancellationToken).ConfigureAwait(false);
        return new FediContent(node.ContentType, stream, node.Size);
    }

    private static FediNode MapNoteNode(FediPath path, ActivityPubNote note, string actorHandle) =>
        new(
            note.Id,
            Path.GetFileName(path.FullPath),
            FediNodeKind.Note,
            path,
            Encoding.UTF8.GetByteCount(note.Content),
            note.UpdatedAt ?? DateTimeOffset.UtcNow,
            note.MediaType,
            false,
            note.AttributedTo,
            actorHandle);

    private static string NormalizeHandle(string handle) => handle.StartsWith('@') ? handle : $"@{handle}";

    private static string BuildSafeFileName(ActivityPubNote note)
    {
        var candidate = !string.IsNullOrWhiteSpace(note.Name) ? note.Name : note.Id.Split('/').Last();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(candidate) ? $"note-{Guid.NewGuid():N}" : candidate;
    }

    private static string BuildActorDirectoryName(string actorId)
    {
        if (Uri.TryCreate(actorId, UriKind.Absolute, out var actorUri))
        {
            var segment = actorUri.Segments.LastOrDefault(item => !string.IsNullOrWhiteSpace(item.Trim('/')))?.Trim('/');
            if (!string.IsNullOrWhiteSpace(segment))
            {
                return Uri.UnescapeDataString(segment);
            }
        }

        return string.IsNullOrWhiteSpace(actorId) ? "actor" : actorId;
    }
}
