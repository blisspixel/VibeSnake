using System.Text;
using System.Text.Json;

namespace VibeSnake.Persistence;

internal static class ProfileDocumentWriter
{
    public static void Write(
        string path,
        string payload,
        int currentSchemaVersion,
        IPreferencesWriteOperations? operations = null)
    {
        operations ??= PhysicalPreferencesWriteOperations.Instance;
        EnsureReplaceable(path, currentSchemaVersion);
        operations.CreateDirectory(Path.GetDirectoryName(path)!);
        var stage = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            operations.WriteAllText(stage, payload, new UTF8Encoding(false));
            EnsureReplaceable(path, currentSchemaVersion);
            operations.Move(stage, path, overwrite: true);
        }
        finally
        {
            try
            {
                operations.Delete(stage);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Cleanup must preserve the original failure and other writers' stages.
            }
        }
    }

    private static void EnsureReplaceable(string path, int currentSchemaVersion)
    {
        if (!File.Exists(path))
        {
            return;
        }

        JsonDocument existing;
        try
        {
            existing = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            // Explicit recovery can replace malformed JSON.
            return;
        }

        using (existing)
        {
            if (existing.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            int? declared = null;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in existing.RootElement.EnumerateObject())
            {
                if (property.Name is not ("schemaVersion" or "schema_version"))
                {
                    continue;
                }

                if (!names.Add(property.Name)
                    || property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetInt32(out var version)
                    || version < 1
                    || version > currentSchemaVersion
                    || (declared is not null && declared != version))
                {
                    throw new InvalidOperationException(
                        "Existing profile data has unsupported or ambiguous schema metadata and was preserved. "
                            + "Changes are available for this session only.");
                }

                declared = version;
            }
        }
    }
}
