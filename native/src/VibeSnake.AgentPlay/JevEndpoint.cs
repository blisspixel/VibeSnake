namespace VibeSnake.AgentPlay;

public enum JevProviderKind
{
    OpenRouterDecisions = 0,
    OpenRouterSystemOne = 1,
    TypeSafeHosted = 2,
    Local = 3,
}

/// <summary>
/// One System One endpoint. OpenRouter and TypeSafe use a pinned model and an environment variable for the key.
/// A local endpoint sends no authorization header.
/// </summary>
public sealed record JevEndpoint(
    JevProviderKind Kind,
    Uri Address,
    string Model,
    string? ApiKeyVariable)
{
    public const string PinnedOpenRouterModel = "typesafe/jev-1.13";
    public const string LatestOpenRouterModel = "~typesafe/jev-latest";
    public const string PinnedTypeSafeModel = "jev-1.13";
    public const string OpenRouterKeyVariable = "OPENROUTER_API_KEY";
    public const string TypeSafeKeyVariable = "TYPESAFE_API_KEY";

    public static JevEndpoint OpenRouterDecisions(string? model = null) => new(
        JevProviderKind.OpenRouterDecisions,
        new Uri("https://openrouter.ai/api/alpha/decisions"),
        RequireModel(model ?? PinnedOpenRouterModel),
        OpenRouterKeyVariable);

    public static JevEndpoint OpenRouterSystemOne(string? model = null) => new(
        JevProviderKind.OpenRouterSystemOne,
        new Uri("https://openrouter.ai/api/v1/systemone"),
        RequireModel(model ?? PinnedOpenRouterModel),
        OpenRouterKeyVariable);

    public static JevEndpoint TypeSafeHosted(string? model = null) => new(
        JevProviderKind.TypeSafeHosted,
        new Uri("https://api.typesafe.ai/v1/systemone"),
        RequireModel(model ?? PinnedTypeSafeModel),
        TypeSafeKeyVariable);

    public static JevEndpoint Local(Uri address, string model = "local-jev")
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "A local Jev endpoint must be an absolute http or https URL.",
                nameof(address));
        }

        return new(JevProviderKind.Local, address, RequireModel(model), null);
    }

    private static string RequireModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 128 || model.Contains(' '))
        {
            throw new ArgumentException(
                "A Jev model id must be 1 to 128 characters without spaces.",
                nameof(model));
        }

        return model;
    }
}
