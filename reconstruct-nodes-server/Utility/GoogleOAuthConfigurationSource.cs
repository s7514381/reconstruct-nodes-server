using System.Text.Json;
using Microsoft.Extensions.Configuration.Json;

namespace ReconstructNodesServer.Utility;

internal sealed class GoogleOAuthConfigurationSource : JsonConfigurationSource
{
    public bool IsWebClient { get; init; }

    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);
        return new GoogleOAuthConfigurationProvider(this);
    }

    private sealed class GoogleOAuthConfigurationProvider(GoogleOAuthConfigurationSource source)
        : JsonConfigurationProvider(source)
    {
        public override void Load(Stream stream)
        {
            try
            {
                using var document = JsonDocument.Parse(stream);
                string rootName = source.IsWebClient ? "web" : "installed";
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty(rootName, out var credential)
                    || credential.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException(
                        $"{source.Path} must be a Google OAuth credential with a {rootName} object.");
                }

                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    [source.IsWebClient ? "GoogleAuth:ServerClientId" : "GoogleAuth:ClientId"] =
                        ReadRequiredString(credential, rootName, "client_id")
                };
                if (!source.IsWebClient)
                {
                    values["GoogleAuth:ClientSecret"] = ReadRequiredString(credential, rootName, "client_secret");
                }
                Data = values;
            }
            catch (JsonException)
            {
                throw new FormatException($"{source.Path} contains invalid JSON.");
            }
        }

        private string ReadRequiredString(JsonElement credential, string rootName, string name)
        {
            if (!credential.TryGetProperty(name, out var value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new FormatException(
                    $"{source.Path} requires a non-empty {rootName}.{name} string.");
            }

            return value.GetString()!;
        }
    }
}
