using FODevManager.Messages;
using FODevManager.Utils;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace FODevManager.Services.EasyGit
{
    public sealed class AzureOpenAiGitAssistant : IAiGitAssistant
    {
        private readonly AppConfig _config;
        private readonly HttpClient _httpClient;

        public AzureOpenAiGitAssistant(AppConfig config)
        {
            _config = config;
            _httpClient = new HttpClient();
        }

        public async Task<AiCommitSuggestionResult> GenerateCommitMessageAsync(string diffText, CancellationToken cancellationToken = default)
        {
            if (!CanUseAi(out var reason))
                return new AiCommitSuggestionResult { Succeeded = false, FailureReason = reason };

            if (string.IsNullOrWhiteSpace(diffText))
            {
                return new AiCommitSuggestionResult
                {
                    Succeeded = true,
                    CommitMessage = "chore: update repository state"
                };
            }

            var prompt = "You generate concise git commit messages. " +
                         "Return exactly one line in Conventional Commits style, max 72 chars, based on why the change matters.";

            var userInput = $"Staged diff:\n{diffText}";

            var completion = await CreateChatCompletionAsync(prompt, userInput, cancellationToken).ConfigureAwait(false);
            if (!completion.Succeeded)
                return new AiCommitSuggestionResult { Succeeded = false, FailureReason = completion.Error };

            var message = completion.Content.Trim();
            if (message.IsNullOrEmpty())
                message = "chore: update repository state";

            var firstLine = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (firstLine.IsNullOrEmpty())
                firstLine = "chore: update repository state";

            return new AiCommitSuggestionResult
            {
                Succeeded = true,
                CommitMessage = firstLine
            };
        }

        public async Task<AiConflictResolutionResult> ResolveConflictAsync(AiConflictResolutionRequest request, CancellationToken cancellationToken = default)
        {
            if (!CanUseAi(out var reason))
                return new AiConflictResolutionResult { Succeeded = false, FailureReason = reason };

            var systemPrompt = "You are a senior C#/XML/JSON merge resolver. " +
                               "Resolve git conflict markers safely. " +
                               "Return strict JSON only with keys: confidence (0..1), resolvedContent. " +
                               "Do not include markdown.";

            var userPrompt =
                $"Repository: {request.RepositoryRootFolder}\n" +
                $"File: {request.RelativeFilePath}\n" +
                $"Current branch: {request.CurrentBranch}\n" +
                $"Main branch: {request.MainBranchName}\n" +
                "File content with git conflict markers:\n" +
                request.FileContentsWithMarkers;

            var completion = await CreateChatCompletionAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
            if (!completion.Succeeded)
            {
                return new AiConflictResolutionResult
                {
                    Succeeded = false,
                    FailureReason = completion.Error
                };
            }

            try
            {
                var jsonText = TryExtractJsonObject(completion.Content);
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                var confidence = root.TryGetProperty("confidence", out var confidenceElement)
                    ? confidenceElement.GetDouble()
                    : 0;

                var resolved = root.TryGetProperty("resolvedContent", out var resolvedElement)
                    ? resolvedElement.GetString() ?? string.Empty
                    : string.Empty;

                return new AiConflictResolutionResult
                {
                    Succeeded = !resolved.IsNullOrEmpty(),
                    Confidence = confidence,
                    IsHighConfidence = confidence >= _config.AiAutoResolveConfidenceThreshold,
                    ResolvedContent = resolved,
                    FailureReason = resolved.IsNullOrEmpty() ? "AI did not return resolved content." : null
                };
            }
            catch (Exception ex)
            {
                MessageLogger.Warning($"AI conflict response parsing failed: {ex.Message}");
                return new AiConflictResolutionResult
                {
                    Succeeded = false,
                    FailureReason = "Unable to parse AI conflict response."
                };
            }
        }

        private bool CanUseAi(out string reason)
        {
            if (_config.AzureOpenAiEndpoint.IsNullOrEmpty())
            {
                reason = "AzureOpenAiEndpoint is not configured.";
                return false;
            }

            if (_config.AzureOpenAiDeployment.IsNullOrEmpty())
            {
                reason = "AzureOpenAiDeployment is not configured.";
                return false;
            }

            if (_config.AzureOpenAiApiKey.IsNullOrEmpty())
            {
                reason = "AzureOpenAiApiKey is not configured.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private async Task<(bool Succeeded, string Content, string Error)> CreateChatCompletionAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken)
        {
            var endpoint = _config.AzureOpenAiEndpoint.TrimEnd('/');
            var deployment = Uri.EscapeDataString(_config.AzureOpenAiDeployment);
            var url = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version=2024-10-21";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("api-key", _config.AzureOpenAiApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.1,
                max_tokens = 1600
            };

            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (false, string.Empty, $"Azure OpenAI call failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0)
                    return (false, string.Empty, "Azure OpenAI returned no choices.");

                var content = choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
                return (true, content, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Failed to parse Azure OpenAI response: {ex.Message}");
            }
        }

        private static string TryExtractJsonObject(string raw)
        {
            var text = raw?.Trim() ?? string.Empty;
            if (text.StartsWith("{", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal))
                return text;

            var first = text.IndexOf('{');
            var last = text.LastIndexOf('}');

            if (first >= 0 && last > first)
                return text.Substring(first, last - first + 1);

            return text;
        }
    }
}
