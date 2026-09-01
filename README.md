# FediFile

FediFile is a starter design for a Windows virtual filesystem that mounts Fediverse data as a drive letter and Explorer namespace.

## What is in this repository

1. A full technical specification in `docs\technical-specification.md`
2. A .NET 10 solution scaffold for ActivityPub, store, filesystem, shell, and host layers
3. Example interfaces and starter implementations for the key integration points

## Projects

| Project | Purpose |
| --- | --- |
| `FediFile.ActivityPub` | ActivityPub discovery, fetch, update, delete, and HTTP signatures |
| `FediFile.Store` | Local cache abstraction and filesystem-to-ActivityPub mapping |
| `FediFile.WinFsp` | Virtual filesystem facade and WinFsp/Dokan adapter shape |
| `FediFile.Shell` | Shell Namespace Extension contracts and stream stubs |
| `FediFile.Host` | Starter console host |

## Build

```powershell
dotnet build .\FediFile.slnx
```

## Run

```powershell
dotnet run --project .\src\FediFile.Host -- list @barry@mastodon.social
dotnet run --project .\src\FediFile.Host -- cat @barry@mastodon.social "\@barry@mastodon.social\Notes\note.html"
dotnet run --project .\src\FediFile.Host -- mount F: @barry@mastodon.social
```

The `list` command synchronizes an actor and lists its Following directory by default. Startup fetches the complete available Following collection, including all pagination pages, but does not fetch statuses or other collections. Following entries are actor directories. Opening any actor directory expands that actor's root folders on demand. Listing `Inbox`, `Outbox`, `Media`, or `Notes` then fetches only that folder's content. Pass an explicit path to list another collection, such as `\@barry@mastodon.social\Followers`, `\@barry@mastodon.social\Outbox`, or `\@barry@mastodon.social\Notes`. The `cat` command synchronizes an actor and writes a note's HTML content to standard output.

The `mount` command synchronizes an actor and mounts the read-only view as a drive visible in Windows Explorer. WinFsp must be installed on Windows, and the process must remain running while the drive is in use. Press `Ctrl+C` to unmount it.

## Status

This repository is a foundation, not a finished filesystem. The current WinFsp mount is read-only; write operations, persistent caching, and Explorer shell registration remain future work. Relationship collections may be empty when a remote server does not expose them to anonymous clients.
