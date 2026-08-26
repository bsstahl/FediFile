# FediFile technical specification

## 1. Purpose

FediFile is a Windows-compatible virtual filesystem that projects Fediverse resources into a mountable drive letter such as `F:\`. It treats ActivityPub as a distributed backing store and maps actors, collections, notes, and media attachments into directories and files that can be browsed with Win32 APIs and Windows File Explorer.

The system is intentionally designed as a demonstration platform. It highlights how the Fediverse can behave as a public, mutable, distributed object store, while remaining explicit about the mismatch between ActivityPub semantics and conventional filesystem expectations.

## 2. Goals and non-goals

### Goals

1. Mount as a real user-selectable Windows drive letter by using WinFsp or Dokan.
2. Expose a browseable hierarchy for ActivityPub actors, collections, notes, and attachments.
3. Support read, create, update, delete, rename, and copy-style flows wherever they can be mapped onto ActivityPub operations.
4. Cache remote objects locally for Explorer responsiveness and offline inspection.
5. Authenticate with HTTP Signatures when remote instances require signed requests.
6. Provide both filesystem integration and Explorer-specific namespace integration.
7. Stay modular enough to evolve from a demo into a more complete Windows integration sample.
8. Implement all services in C# on .NET 10 with analyzers enabled and warnings treated as errors.
9. Keep the core ActivityPub, store, and filesystem semantics portable so a future FUSE adapter can support Unix-like systems.

## 2.1 Platform and quality baseline

1. All services and components are implemented in C#.
2. The target runtime is .NET 10 across the entire solution.
3. .NET analyzers are enabled for all projects.
4. Analyzer and compiler warnings are treated as build errors.
5. Code style rules are enforced in build to keep quality gates consistent across contributors.

### Non-goals

1. End-to-end encryption.
2. Perfect POSIX or NTFS semantic fidelity.
3. Full trust of remote data.
4. Guaranteed cross-instance support for every mutation flow.
5. Strong consistency across federated servers.

## 3. User-visible model

### Root layout

```text
F:\
 ├── @barry@mastodon.social\
 │   ├── Inbox\
 │   ├── Outbox\
 │   ├── Followers\
 │   ├── Following\
 │   ├── Media\
 │   └── Notes\
 └── @alice@example.net\
     ├── Inbox\
     ├── Outbox\
     ├── Followers\
     ├── Following\
     ├── Media\
     └── Notes\
```

### Object mapping

| ActivityPub concept | Filesystem representation | Notes |
| --- | --- | --- |
| Actor | Directory | Named as `@user@host` |
| Collection | Subdirectory | Inbox, Outbox, Followers, Following, Media, Notes |
| Note | Virtual file | `.json` or `.html` projection; default demo can render HTML |
| MediaAttachment | Regular file | Backed by remote media URL stream |
| Delete activity | Delete file/folder intent | May be logical tombstone first, then remote propagation |
| Update activity | Rename or write | Depends on object type and remote instance behavior |

## 4. Functional requirements

### 4.1 Mounting

1. User chooses mount point such as `F:`.
2. FediFile host starts the WinFsp dispatcher for the read-only filesystem view.
3. Dispatcher exposes the root directory and serves Win32 filesystem requests.
4. Unmount must be graceful and flush pending writes.

### 4.2 Read operations

1. Opening an actor directory triggers cache lookup, then optional remote actor/collection refresh.
2. Opening a Note file returns either:
   1. raw `application/activity+json`,
   2. normalized JSON,
   3. rendered HTML projection.
3. Opening a MediaAttachment streams remote bytes via HTTP with local read-through caching.

### 4.3 Write operations

1. Creating a new file under `Notes\` creates a Note via `Create`.
2. Saving an existing Note file sends an `Update`.
3. Delete maps to `Delete`.
4. Rename maps to an `Update` that changes the user-facing title or logical collection membership.
5. Move/copy between collections maps to collection reassignment or re-publication when supported.

### 4.4 Shell integration

1. Shell Namespace Extension exposes the same hierarchy without requiring a mounted drive.
2. Notes and media provide `IStream`.
3. Explorer can enumerate items, show metadata, and later support thumbnails and preview handlers.

### 4.5 Future FUSE integration

1. A future FUSE adapter must expose the shared filesystem semantics on supported Unix-like systems.
2. The FUSE adapter must remain separate from Windows-specific WinFsp, Dokan, and Shell integration code.
3. Platform-neutral ActivityPub and store components must be reusable by both Windows and FUSE hosts.
4. FUSE support is a planned requirement and is not part of the initial Windows implementation milestone.

## 5. Constraints and semantic mismatches

1. ActivityPub identifiers are URLs, not stable NTFS file IDs.
2. Federation is eventually consistent, not transactional.
3. Rename is not a native first-class ActivityPub primitive; it is represented as object mutation.
4. Remote deletion may tombstone objects rather than physically remove bytes.
5. Attachments may be remote-CDN backed and immutable even when the Note is mutable.
6. Explorer and Win32 callers may expect byte-range reads, timestamps, attributes, and cheap random access; the implementation must synthesize or cache these values.

## 6. Architecture

### 6.1 High-level layers

```text
+---------------------------------------------------------------+
| Windows Explorer / Win32 apps / Shell consumers              |
+-------------------------+-------------------------------------+
                          |
              +-----------+-----------+
              |                       |
              v                       v
+---------------------------+   +-------------------------------+
| FediFileFS (WinFsp/Dokan) |   | FediShell (Namespace Ext.)    |
| Drive letter mount        |   | Explorer-native object view   |
+-------------+-------------+   +---------------+---------------+
              |                                 |
              +---------------+-----------------+
                              v
                    +---------------------+
                    | FediStore           |
                    | Path/object mapper  |
                    | Cache coordinator   |
                    | Mutation policy     |
                    +----------+----------+
                               |
             +-----------------+------------------+
             |                                    |
             v                                    v
+---------------------------+        +---------------------------+
| Local cache               |        | ActivityPub client        |
| SQLite metadata           |        | WebFinger                 |
| blob/media cache          |        | object fetch              |
| sync journal              |        | inbox/outbox mutation     |
+-------------+-------------+        | HTTP signatures           |
              |                      +-------------+-------------+
              |                                    |
              +-------------------+----------------+
                                  v
                       +----------------------------+
                       | Fediverse servers          |
                       | Actors / Notes / Media     |
                       +----------------------------+
```

### 6.2 Component responsibilities

#### FediStore

1. Converts Windows-style paths into logical actor/object references.
2. Maintains local metadata cache and lookup indexes.
3. Decides when to satisfy reads from cache versus remote.
4. Translates filesystem mutations into ActivityPub operations.
5. Emits change events for filesystem and shell layers.

#### FediFileFS

1. Adapts WinFsp or Dokan callbacks to `IFediStore`.
2. Synthesizes file attributes, timestamps, and directory listings.
3. Buffers writes and commits them as Note updates.
4. Handles mount lifecycle, path normalization, and access masks.

#### FediShell

1. Implements `IShellFolder`, `IShellItem`, `IEnumIDList`, and `IStream`.
2. Enables Explorer browsing independent of drive mapping.
3. Supplies metadata and later thumbnails/property handlers.

#### ActivityPub client

1. Performs WebFinger lookup.
2. Fetches actors, collections, notes, and attachments.
3. Creates signed requests when required.
4. Normalizes JSON into internal record types.

#### Local cache

1. SQLite stores actors, collections, notes, attachments, paths, ETags, last sync times, and mutation queue.
2. Blob cache stores rendered note content and downloaded media chunks.
3. Background sync updates stale objects and replays pending writes.

## 7. Detailed data model

### 7.1 Canonical internal entities

| Entity | Key fields |
| --- | --- |
| ActorRecord | `actor_id`, `handle`, `host`, `inbox_url`, `outbox_url`, `etag`, `last_seen_utc` |
| CollectionRecord | `collection_id`, `actor_id`, `name`, `source_url`, `last_synced_utc` |
| NoteRecord | `note_id`, `actor_id`, `collection_id`, `title`, `content_html`, `content_json`, `updated_utc`, `etag` |
| AttachmentRecord | `attachment_id`, `note_id`, `file_name`, `media_url`, `media_type`, `size_bytes`, `etag` |
| PathIndex | `path`, `node_id`, `node_kind`, `parent_path` |
| SyncJob | `job_id`, `job_type`, `target_id`, `attempt_count`, `state`, `next_retry_utc` |

### 7.2 Suggested SQLite schema

```sql
CREATE TABLE actors (
    actor_id TEXT PRIMARY KEY,
    handle TEXT NOT NULL UNIQUE,
    host TEXT NOT NULL,
    inbox_url TEXT NOT NULL,
    outbox_url TEXT NOT NULL,
    followers_url TEXT,
    following_url TEXT,
    featured_url TEXT,
    icon_url TEXT,
    etag TEXT,
    last_seen_utc TEXT NOT NULL
);

CREATE TABLE collections (
    collection_id TEXT PRIMARY KEY,
    actor_id TEXT NOT NULL REFERENCES actors(actor_id),
    name TEXT NOT NULL,
    source_url TEXT NOT NULL,
    last_synced_utc TEXT NOT NULL
);

CREATE TABLE notes (
    note_id TEXT PRIMARY KEY,
    actor_id TEXT NOT NULL REFERENCES actors(actor_id),
    collection_id TEXT,
    title TEXT,
    content_html TEXT NOT NULL,
    content_json TEXT NOT NULL,
    media_type TEXT NOT NULL,
    updated_utc TEXT,
    etag TEXT
);

CREATE TABLE attachments (
    attachment_id TEXT PRIMARY KEY,
    note_id TEXT NOT NULL REFERENCES notes(note_id),
    file_name TEXT NOT NULL,
    media_url TEXT NOT NULL,
    media_type TEXT NOT NULL,
    size_bytes INTEGER,
    etag TEXT
);

CREATE TABLE path_index (
    path TEXT PRIMARY KEY,
    node_id TEXT NOT NULL,
    node_kind TEXT NOT NULL,
    parent_path TEXT
);

CREATE TABLE sync_jobs (
    job_id TEXT PRIMARY KEY,
    job_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    state TEXT NOT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    next_retry_utc TEXT,
    created_utc TEXT NOT NULL
);
```

## 8. Filesystem behavior mapping

### 8.1 Path resolution

1. `\` returns cached actor roots.
2. `\@user@host` resolves actor metadata.
3. `\@user@host\Notes` resolves note collection.
4. `\@user@host\Notes\foo.html` resolves note record by path index.
5. `\@user@host\Media\image.jpg` resolves attachment record.

### 8.2 Callback mapping

| Filesystem callback | FediStore action |
| --- | --- |
| `GetFileInfo` | lookup node metadata from cache |
| `ReadDirectory` | enumerate cached child nodes; trigger async refresh if stale |
| `Open/CreateFile` | open note/media stream or stage write buffer |
| `ReadFile` | fetch content stream |
| `WriteFile` | write into local buffer/cache |
| `Cleanup/Close` | commit buffered note update if dirty |
| `DeleteFile/DeleteDirectory` | stage and send Delete |
| `MoveFile` | rename or collection reassignment |

### 8.3 Notes as files

Offer two projections:

1. `Notes\foo.html` for rendered content.
2. `Notes\foo.activity.json` for raw object inspection.

The starter implementation can begin with `.html` only, with later optional alternate data streams or sibling files for raw JSON.

## 9. Shell Namespace Extension design

### 9.1 Core COM interfaces

1. `IShellFolder` for hierarchy enumeration and binding.
2. `IShellItem` for item identity and display names.
3. `IEnumIDList` for folder enumeration.
4. `IStream` for note/media content access.
5. Future: `IThumbnailProvider`, `IPropertyStore`, `IPreviewHandler`.

### 9.2 Explorer integration strategy

1. Register a CLSID under `HKCU\Software\Classes\CLSID\{...}` for per-user install.
2. Add namespace root under Explorer `Desktop\Namespace`.
3. Map PIDLs to internal `FediPath` tokens.
4. Use cached metadata to keep enumeration responsive.

## 10. Authentication and security

### 10.1 HTTP signatures

1. Store actor key material in Windows DPAPI-protected storage or Windows Credential Manager.
2. Sign outbound POST requests for create, update, delete, and any GET endpoints that require auth.
3. Include `(request-target)`, `host`, `date`, and optionally `digest`.

### 10.2 Trust model

1. Remote data is untrusted and must be treated as hostile input.
2. Sanitize file names derived from titles or URLs.
3. Limit rendered HTML surface; prefer safe HTML projection or plain text fallback.
4. Never auto-execute remote content.

## 11. Sync and cache behavior

### 11.1 Read path

```text
Explorer/Win32 read
  -> FediFileFS callback
  -> FediStore path lookup
  -> Cache hit? yes -> return immediately
  -> Cache miss/stale -> ActivityPub fetch
  -> Normalize + persist metadata
  -> Return stream
```

### 11.2 Write path

```text
App save
  -> WinFsp write buffer
  -> Close/Cleanup triggers commit
  -> FediStore validates path + actor context
  -> ActivityPub Create/Update/Delete
  -> Cache update
  -> Background fan-out / retry queue if remote failed
```

### 11.3 Invalidation policy

1. Per-collection TTL for directory listings.
2. Per-object ETag or `updated` comparison when available.
3. Tombstone retention to avoid deleted entries instantly reappearing.
4. Background poller for outbox/inbox updates.

## 12. Module breakdown and interfaces

### 12.1 Solution modules

| Project | Purpose |
| --- | --- |
| `FediFile.ActivityPub` | Protocol client, signing, object parsing |
| `FediFile.Store` | Cache abstractions, path mapping, mutation policy |
| `FediFile.WinFsp` | Filesystem abstraction and WinFsp/Dokan adapter |
| `FediFile.Shell` | Shell Namespace Extension contracts and adapter |
| `FediFile.Host` | Local demo host, dependency wiring, CLI |

### 12.2 Key interfaces

1. `IActivityPubClient`
2. `IHttpSignatureProvider`
3. `IFediCache`
4. `IFediStore`
5. `IFediFileSystem`
6. `IFediShellFolder`
7. `IFediShellItem`

## 13. Step-by-step data flows

### 13.1 Enumerating an actor

1. User opens `F:\@barry@mastodon.social\Notes`.
2. `ReadDirectory` reaches `FediFileFS`.
3. `FediStore` checks `PathIndex`.
4. If stale, `ActivityPubClient` fetches actor outbox/notes.
5. Store updates SQLite and path index.
6. Directory entries are returned to Explorer.

### 13.2 Reading a note

1. User opens `hello-world.html`.
2. `CreateFile`/`ReadFile` resolves note id from path.
3. Store checks cached projection.
4. If needed, client fetches note JSON.
5. Store renders/sanitizes HTML and returns stream.

### 13.3 Updating a note

1. User edits a note-backed file and saves.
2. WinFsp write callback stores bytes in a memory/temp buffer.
3. On close, FediStore maps file to actor/note context.
4. ActivityPub `Update` is posted with HTTP Signature when required.
5. Cache record and timestamps are refreshed.

### 13.4 Deleting a note

1. User deletes a file.
2. Store resolves object id and actor context.
3. ActivityPub `Delete` is posted.
4. Local item becomes tombstoned or removed.
5. Background sync reconciles downstream remote state later.

## 14. Starter implementation choices

### 14.1 Language

C# on .NET 10 is required for the starter because:

1. COM and Windows Explorer integration are straightforward.
2. WinFsp and Dokan .NET interop are practical.
3. SQLite, HTTP, JSON, and hosting support are mature.

### 14.2 WinFsp vs Dokan

Use WinFsp first.

1. Strong Windows filesystem focus.
2. Good compatibility with user-mode filesystems.
3. Clear conceptual match for a virtual drive demo.

Dokan remains a viable alternative if a preferred managed wrapper is already available in the target environment.

## 15. Proposed repository structure

```text
FediFile.slnx
README.md
docs/
  technical-specification.md
FediFile.ActivityPub/
  ActivityPubModels.cs
FediFile.Store/
  FediStore.cs
FediFile.WinFsp/
  FediFileSystem.cs
FediFile.Shell/
  ShellExtensionContracts.cs
FediFile.Host/
  Program.cs
samples/
src/
```

Recommended expansion after the first pass:

```text
src/
  FediFile.ActivityPub/
    Discovery/
    Signing/
    Serialization/
  FediFile.Store/
    Cache/
    Sync/
    Mapping/
  FediFile.WinFsp/
    WinFsp/
    Dokan/
  FediFile.Shell/
    Namespace/
    Streams/
    Registration/
  FediFile.Host/
tests/
  FediFile.ActivityPub.Tests/
  FediFile.Store.Tests/
  FediFile.WinFsp.Tests/
```

## 16. Starter implementation notes

The scaffold in this repository provides:

1. `ActivityPubClient` with WebFinger, actor, collection, note, media, create, update, and delete stubs.
2. `FediStore` with in-memory caching, path mapping, synchronization, and note mutation flow.
3. `FediFileSystem` and `WinFspAdapter` showing how callbacks map into store operations.
4. Shell COM interface definitions and basic stub implementations for folder enumeration and `IStream`.
5. A console host that seeds one actor and demonstrates composition.

The scaffold intentionally stops short of binding a real WinFsp package or writing registry entries for the shell extension, because those choices depend on the exact WinFsp/Dokan and COM registration strategy you want to adopt.

## 17. Incremental roadmap

### Phase 1: Read-only filesystem

1. Mount a drive letter with WinFsp.
2. Resolve actors by handle.
3. Enumerate Inbox/Outbox/Notes/Media.
4. Read notes and attachments.
5. Persist metadata in SQLite rather than memory.

### Phase 2: Writable Notes

1. Add buffered file writes for note-backed files.
2. Implement `Create` and `Update` flows against authenticated actor outbox/inbox endpoints.
3. Add optimistic concurrency metadata and retry queue.

### Phase 3: Media upload

1. Support creating binary files in `Media\`.
2. Upload media to supported endpoints.
3. Link uploaded attachments into Note updates.
4. Cache thumbnails and MIME metadata.

### Phase 4: Collections and Trails

1. Add explicit collection membership views.
2. Represent reply trails, threads, and references as folders or link-like entries.
3. Implement move/copy semantics between collections when possible.
4. Add sync journaling and change propagation UI.

### Phase 5: Full Explorer integration

1. Register Shell Namespace Extension.
2. Provide thumbnails, property sheets, and preview handlers.
3. Add context menu verbs such as Refresh, Open in Browser, Federate, Pin Actor.
4. Improve metadata columns like actor, visibility, updated time, media type, and object URL.

## 18. Build and run guide

### Prerequisites

1. Windows 11 or Windows 10.
2. .NET 10 SDK.
3. WinFsp SDK/runtime or Dokan SDK/runtime.
4. SQLite native package or managed provider once persistence is wired in.
5. A Fediverse test actor and keypair for authenticated writes.

### Build

```powershell
dotnet build .\FediFile.slnx
```

### Run starter host

```powershell
dotnet run --project .\FediFile.Host -- F: @barry@mastodon.social
```

### Next integration steps

1. Add a WinFsp NuGet package or native interop layer.
2. Replace `WinFspAdapter` with concrete callback registration.
3. Swap `MemoryFediCache` for SQLite-backed `IFediCache`.
4. Add Explorer registration helpers for the namespace extension.

## 19. Recommended next engineering tasks

1. Introduce a `FediFile.Persistence` project with SQLite schema migrations.
2. Add a real mount host with WinFsp callbacks and integration tests.
3. Split note projections into raw JSON and sanitized HTML streams.
4. Add a per-actor configuration file for keys, endpoints, and mount policy.
5. Add a `tests/` solution slice before deeper implementation.
