using System.Text.Json;
using System.Text.RegularExpressions;
using FODevManager.Utils;

namespace FODevManager.Operations;

public sealed class SecretRedactor(AppConfig config)
{
    public const string Replacement = "[REDACTED]";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex UserInfo = new(@"([a-z][a-z0-9+.-]*://)[^\s/?#]*@", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, RegexTimeout);
    private static readonly Regex QueryValue = new(@"([?&])([^\s=&#]+)=([^\s&#]*)", RegexOptions.NonBacktracking, RegexTimeout);
    private static readonly Regex PercentEscape = new("%[0-9A-Fa-f]{2}", RegexOptions.NonBacktracking, RegexTimeout);

    private static bool IsSecretName(string name)
    {
        var normalized = new string(Uri.UnescapeDataString(name).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized.Contains("password") || normalized.Contains("secret") || normalized.Contains("token")
            || normalized.Contains("apikey") || normalized.Contains("credential") || normalized.Contains("authorization")
            || normalized.EndsWith("signature", StringComparison.Ordinal)
            || normalized is "pat" or "azureartifactspat" or "azureartifactsusername" or "username"
                or "sig" or "signature" or "key" or "code" or "clientassertion";
    }

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        try
        {
            var secrets = new[] { config.AzureArtifactsPat, config.AzureArtifactsApiKey, config.AzureArtifactsUsername }
                .Where(secret => !string.IsNullOrEmpty(secret))
                .SelectMany(secret => new[] { (Value: secret, Encoded: false), (Value: Uri.EscapeDataString(secret), Encoded: true), (Value: WebEncode(secret), Encoded: true) })
                .Distinct().OrderByDescending(secret => secret.Value.Length);
            foreach (var secret in secrets)
            {
                if (secret.Encoded && secret.Value.Contains('%'))
                {
                    // Only escape hex digits are case-insensitive; credential letters remain ordinal
                    var pattern = PercentEscape.Replace(Regex.Escape(secret.Value), match => "(?i:" + match.Value + ")");
                    value = Regex.Replace(value, pattern, Replacement, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout);
                }
                else value = value.Replace(secret.Value, Replacement, StringComparison.Ordinal);
            }
            value = UserInfo.Replace(value, "$1" + Replacement + "@");
            return QueryValue.Replace(value, match => IsSecretName(match.Groups[2].Value)
                ? match.Groups[1].Value + match.Groups[2].Value + "=" + Replacement : match.Value);
        }
        catch
        {
            // Redaction must fail closed, including malformed escapes and regex timeouts
            return Replacement;
        }
    }

    private static string WebEncode(string value) => System.Net.WebUtility.UrlEncode(value);

    /// <summary>Converts data to a detached JSON value; sensitive fields are replaced recursively</summary>
    public object? RedactData(object? data)
    {
        if (data is null) return null;
        var serialized = JsonSerializer.SerializeToUtf8Bytes(data);
        if (serialized.Length > 1024 * 1024)
            throw new InvalidOperationException("Operation result exceeds the 1 MiB limit");
        using var source = JsonDocument.Parse(serialized);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) Write(writer, source.RootElement);
        if (buffer.Length > 1024 * 1024)
            throw new InvalidOperationException("Operation result exceeds the 1 MiB limit");
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(Redact(property.Name));
                    // Preserve only the typed presence indicator, never a credential value with this name
                    var presenceIndicator = property.Name.Equals("CredentialsConfigured", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False;
                    if (IsSecretName(property.Name) && !presenceIndicator) writer.WriteStringValue(Replacement);
                    else Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(Redact(element.GetString()));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
