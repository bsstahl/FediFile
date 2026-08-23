namespace FediFile.Host;

internal static class HostPath
{
    public static string GetDefaultNotesPath(string actorHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorHandle);

        var normalizedActorHandle = actorHandle.StartsWith('@') ? actorHandle : $"@{actorHandle}";
        return $@"\{normalizedActorHandle}\Notes";
    }
}
