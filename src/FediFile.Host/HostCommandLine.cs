namespace FediFile.Host;

internal sealed record HostOptions(string Command, string MountPoint, string ActorHandle, string? Path);

internal static class HostCommandLine
{
    public static HostOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var firstArgument = args.ElementAtOrDefault(0)?.ToUpperInvariant();
        if (firstArgument is "LIST" or "CAT")
        {
            return new HostOptions(
                firstArgument,
                "",
                args.ElementAtOrDefault(1) ?? throw new ArgumentException("An actor handle is required.", nameof(args)),
                args.ElementAtOrDefault(2));
        }

        if (firstArgument == "MOUNT")
        {
            return new HostOptions(
                "MOUNT",
                args.ElementAtOrDefault(1) ?? "F:",
                args.ElementAtOrDefault(2) ?? "@demo@example.social",
                null);
        }

        return new HostOptions(
            "MOUNT",
            args.ElementAtOrDefault(0) ?? "F:",
            args.ElementAtOrDefault(1) ?? "@demo@example.social",
            null);
    }
}
