using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TSIC.Contracts.Dtos.Email;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Shared.AiCompose;

public class AiComposeService : IAiComposeService
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicSettings _settings;
    private readonly IJobRepository _jobRepo;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AiComposeService(
        HttpClient httpClient,
        IOptions<AnthropicSettings> settings,
        IJobRepository jobRepo)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _jobRepo = jobRepo;
    }

    public async Task<AiComposeResponse> ComposeEmailAsync(
        Guid jobId,
        string prompt,
        CancellationToken ct = default)
    {
        var jobName = await _jobRepo.GetJobNameAsync(jobId, ct) ?? "the organization";
        var season = await _jobRepo.GetJobSeasonAsync(jobId, ct) ?? "";

        var systemPrompt = BuildSystemPrompt(jobName, season);

        var requestBody = new
        {
            model = _settings.Model,
            max_tokens = 1024,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _settings.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Anthropic API returned {(int)response.StatusCode}: {responseJson}");
        }

        return ParseResponse(responseJson);
    }

    private static string BuildSystemPrompt(string jobName, string season)
    {
        var seasonClause = string.IsNullOrWhiteSpace(season) ? "" : $" for the {season} season";

        return
            "You are an email composer for \"" + jobName + "\"" + seasonClause + ".\n" +
            "The user will describe what they want to communicate. You will draft a professional, " +
            "warm email appropriate for a youth/amateur sports organization.\n\n" +
            "You MUST return valid JSON with exactly two fields:\n" +
            "{\"subject\": \"...\", \"body\": \"...\"}\n\n" +
            "Rules:\n" +
            "- The body should be plain text with line breaks (\\n), not HTML.\n" +
            "- Keep the tone professional but approachable — these emails go to parents, coaches, and players.\n" +
            "- Keep emails concise. Most should be 3-8 sentences.\n" +
            "- You may use these personalization tokens in the body (they will be replaced with real values per recipient):\n" +
            "  !PERSON — recipient's name\n" +
            "  !EMAIL — recipient's email address\n" +
            "  !JOBNAME — the league/organization name (\"" + jobName + "\")\n" +
            "  !AMTFEES — total fees amount\n" +
            "  !AMTPAID — amount paid\n" +
            "  !AMTOWED — amount owed\n" +
            "  !SEASON — season name\n" +
            "  !SPORT — sport name\n" +
            "  !FAMILYUSERNAME — the family's login username\n" +
            "  !JOBLINK — a link to the organization's website\n" +
            "- Only use tokens that are relevant to the message. Do not force tokens into every email.\n" +
            "- Use !PERSON at the start of the email as a greeting (e.g., \"Dear !PERSON,\") when appropriate.\n" +
            "- Return ONLY the JSON object. No markdown, no code fences, no explanation.";
    }

    private static AiComposeResponse ParseResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        // Extract text from Anthropic's response format: { content: [{ type: "text", text: "..." }] }
        var contentArray = root.GetProperty("content");
        var textBlock = contentArray[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Empty response from AI");

        // The AI should return JSON — parse it
        // Strip markdown code fences if the model wraps the response
        var cleaned = textBlock.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline > 0) cleaned = cleaned[(firstNewline + 1)..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();
        }

        using var emailDoc = JsonDocument.Parse(cleaned);
        var emailRoot = emailDoc.RootElement;

        return new AiComposeResponse
        {
            Subject = emailRoot.GetProperty("subject").GetString() ?? "",
            Body = emailRoot.GetProperty("body").GetString() ?? ""
        };
    }
}
