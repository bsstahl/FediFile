# FediFile

FediFile is a starter design for a Windows virtual filesystem that mounts Fediverse data as a drive letter and Explorer namespace.

## What is in this repository

1. A full technical specification in `docs\technical-specification.md`
2. A .NET 8 solution scaffold for ActivityPub, store, filesystem, shell, and host layers
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
dotnet run --project .\FediFile.Host -- F: @barry@mastodon.social
```

## Status

This repository is a foundation, not a finished filesystem. The code intentionally focuses on architecture, contracts, and mapping strategy so a concrete WinFsp mount host and Explorer integration can be implemented on top.
