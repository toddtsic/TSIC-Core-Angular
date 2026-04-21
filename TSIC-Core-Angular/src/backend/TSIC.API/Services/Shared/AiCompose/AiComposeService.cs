using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TSIC.API.Services.Shared.Bulletins.TokenResolution;
using TSIC.Contracts.Dtos.Email;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Shared.AiCompose;

public class AiComposeService : IAiComposeService
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicSettings _settings;
    private readonly IJobRepository _jobRepo;
    private readonly BulletinTokenRegistry _tokenRegistry;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Canonical visual vocabulary for bulletin drafting + formatting. Edit the
    // markdown file, restart the API, and the next AI call picks it up.
    private static readonly Lazy<string> BulletinExemplars = new(() =>
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Services", "Shared", "AiCompose", "BulletinExemplars.md");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    });

    public AiComposeService(
        HttpClient httpClient,
        IOptions<AnthropicSettings> settings,
        IJobRepository jobRepo,
        BulletinTokenRegistry tokenRegistry)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _jobRepo = jobRepo;
        _tokenRegistry = tokenRegistry;
    }

    public async Task<AiComposeResponse> ComposeEmailAsync(
        Guid jobId,
        string prompt,
        CancellationToken ct = default)
    {
        var jobName = await _jobRepo.GetJobNameAsync(jobId, ct) ?? "the organization";
        var season = await _jobRepo.GetJobSeasonAsync(jobId, ct) ?? "";
        var systemPrompt = BuildEmailSystemPrompt(jobName, season);
        return await CallAnthropicAsync(systemPrompt, prompt, ct);
    }

    public async Task<AiComposeResponse> ComposeBulletinAsync(
        Guid jobId,
        string prompt,
        CancellationToken ct = default)
    {
        var jobName = await _jobRepo.GetJobNameAsync(jobId, ct) ?? "the organization";
        var season = await _jobRepo.GetJobSeasonAsync(jobId, ct) ?? "";
        var systemPrompt = BuildBulletinSystemPrompt(jobName, season);
        return await CallAnthropicAsync(systemPrompt, prompt, ct);
    }

    public async Task<AiFormatResponse> FormatBulletinAsync(
        Guid jobId,
        string existingHtml,
        CancellationToken ct = default)
    {
        var jobName = await _jobRepo.GetJobNameAsync(jobId, ct) ?? "the organization";
        var systemPrompt = BuildFormatBulletinSystemPrompt(jobName, _tokenRegistry.All);

        var requestBody = new
        {
            model = _settings.Model,
            max_tokens = 2048,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = existingHtml }
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

        // Model returns raw HTML (no JSON wrapper per format prompt). Strip code fences if present.
        using var doc = JsonDocument.Parse(responseJson);
        var textBlock = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Empty response from AI");

        var cleaned = textBlock.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline > 0) cleaned = cleaned[(firstNewline + 1)..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();
        }

        return new AiFormatResponse { Html = cleaned };
    }

    private static string BuildFormatBulletinSystemPrompt(
        string jobName,
        IReadOnlyCollection<IBulletinTokenResolver> tokens)
    {
        var tokenLines = string.Join("\n", tokens.Select(t =>
            $"  !{t.TokenName} — {t.Description}" +
            (t.GatingConditions.Length > 0
                ? $" (visible when: {string.Join(", ", t.GatingConditions)})"
                : " (always visible)")));

        return
            "You are a bulletin HTML reformatter for \"" + jobName + "\".\n" +
            "The user message is the CURRENT HTML of a bulletin. Reformat it for better UX and clarity.\n\n" +
            "Rules:\n" +
            "- Preserve the author's intent and information. Do not invent new facts.\n" +
            "- Match the shapes shown in the STYLE GUIDE below. Pick the shape whose 'when to use' best fits the content. Do not invent new shapes or use CSS classes outside the style guide.\n" +
            "- Where the existing HTML contains a call-to-action (e.g. 'click here to register', 'view schedule'), REPLACE the anchor/sentence with the appropriate token below. Tokens render as styled buttons or cards at display time and stay in sync with backend state.\n" +
            "- Available bulletin tokens (use ONLY these names exactly, including the leading '!'):\n" +
            tokenLines + "\n" +
            "- Also supported for text substitution: !JOBNAME (replaced with org name), !USLAXVALIDTHROUGHDATE (USLax membership date).\n" +
            "- Do NOT wrap the output in JSON or markdown fences. Return raw HTML only.\n" +
            "- Do NOT add explanations or comments. Return only the reformatted HTML.\n\n" +
            "===== STYLE GUIDE =====\n" +
            BulletinExemplars.Value;
    }

    private async Task<AiComposeResponse> CallAnthropicAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        var requestBody = new
        {
            model = _settings.Model,
            max_tokens = 1024,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
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

    private static string BuildEmailSystemPrompt(string jobName, string season)
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
            "  !JOBLINK — the job name as a clickable link (e.g., write \"visit !JOBLINK\" — it renders as the hyperlinked job name)\n" +
            "- Only use tokens that are relevant to the message. Do not force tokens into every email.\n" +
            "- Use !PERSON at the start of the email as a greeting (e.g., \"Dear !PERSON,\") when appropriate.\n" +
            "- Return ONLY the JSON object. No markdown, no code fences, no explanation.";
    }

    private static string BuildBulletinSystemPrompt(string jobName, string season)
    {
        var seasonClause = string.IsNullOrWhiteSpace(season) ? "" : $" for the {season} season";

        return
            "You are a bulletin/announcement writer for \"" + jobName + "\"" + seasonClause + ".\n" +
            "The user will describe what they want to announce. You will draft a clear, engaging " +
            "bulletin appropriate for a youth/amateur sports organization website.\n\n" +
            "You MUST return valid JSON with exactly two fields:\n" +
            "{\"subject\": \"...\", \"body\": \"...\"}\n\n" +
            "Where \"subject\" is the bulletin TITLE and \"body\" is the bulletin CONTENT.\n\n" +
            "Rules:\n" +
            "- The body MUST be simple HTML suitable for a rich text editor (use <p>, <strong>, <em>, <ul>/<li>, <br> tags).\n" +
            "- Do NOT use <h1>-<h6> tags — the title is displayed separately.\n" +
            "- Keep the tone enthusiastic but professional — these announcements appear on the organization's public website.\n" +
            "- Keep bulletins concise. Most should be 2-5 short paragraphs.\n" +
            "- The title should be short and attention-grabbing (under 80 characters).\n" +
            "- You may use these tokens in the body (replaced with real values at display time):\n" +
            "  !JOBNAME — the league/organization name (\"" + jobName + "\")\n" +
            "  !USLAXVALIDTHROUGHDATE — US Lacrosse membership valid-through date\n" +
            "- Only use tokens that are relevant to the message.\n" +
            "- Match the shapes shown in the STYLE GUIDE below when choosing HTML structure. Do not use CSS classes outside the style guide.\n" +
            "- Return ONLY the JSON object. No markdown, no code fences, no explanation.\n\n" +
            "===== STYLE GUIDE =====\n" +
            BulletinExemplars.Value;
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
