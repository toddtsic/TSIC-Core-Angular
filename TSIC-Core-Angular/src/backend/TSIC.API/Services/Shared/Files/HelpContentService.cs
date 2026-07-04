using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TSIC.API.Extensions;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Shared.Files;

/// <summary>
/// File-backed help content. Fragments live at <c>{HelpContentPath}/{component}/{topic}.html</c>
/// (default <c>{ContentRoot}/App_Data/Help</c>), git-tracked and deployed with the app.
/// This is the Phase-1 source behind the help endpoint; the read/write contract is deliberately narrow
/// so a DB-backed source could replace it later without touching callers.
/// </summary>
public sealed class HelpContentService : IHelpContentService
{
    private readonly string _basePath;
    private readonly bool _canEdit;
    private readonly ILogger<HelpContentService> _logger;

    public HelpContentService(
        IOptions<FileStorageOptions> options,
        IHostEnvironment env,
        ILogger<HelpContentService> logger)
    {
        var configured = options.Value.HelpContentPath;
        _basePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "Help")
            : Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(env.ContentRootPath, configured);
        // Model A: editing is available on sandbox (Development + Staging) only, never live production.
        _canEdit = env.IsSandbox();
        _logger = logger;
    }

    public bool CanEdit => _canEdit;

    // Defense in depth: the segments come from the client (route data → URL), so never let one escape
    // the help directory via traversal. Lowercase alphanumerics + hyphens only — the same shape our
    // help keys are authored in.
    public bool IsValidSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment.Length > 64) return false;
        foreach (var c in segment)
        {
            if (!((c is >= 'a' and <= 'z') || (c is >= '0' and <= '9') || c == '-')) return false;
        }
        return true;
    }

    public HelpManifestDto GetManifest()
    {
        var keys = new List<string>();
        if (Directory.Exists(_basePath))
        {
            foreach (var file in Directory.EnumerateFiles(_basePath, "*.html", SearchOption.AllDirectories))
            {
                // Expect exactly "{component}/{topic}.html" relative to the help root.
                var rel = Path.GetRelativePath(_basePath, file).Replace('\\', '/');
                if (!rel.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = rel[..^".html".Length].Split('/');
                if (parts.Length != 2) continue;
                if (!IsValidSegment(parts[0]) || !IsValidSegment(parts[1])) continue;

                keys.Add($"{parts[0]}/{parts[1]}");
            }
        }

        keys.Sort(StringComparer.Ordinal);
        return new HelpManifestDto { Keys = keys, CanEdit = _canEdit };
    }

    public async Task<HelpContentDto> GetAsync(string component, string topic, CancellationToken ct = default)
    {
        var path = ResolveFilePath(component, topic);
        string? html = null;
        var exists = false;

        if (path is not null && File.Exists(path))
        {
            html = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            exists = true;
        }

        return new HelpContentDto
        {
            Component = component,
            Topic = topic,
            Html = html,
            Exists = exists,
            CanEdit = _canEdit,
        };
    }

    public async Task<HelpContentDto> SaveAsync(string component, string topic, string html, CancellationToken ct = default)
    {
        var path = ResolveFilePath(component, topic)
            ?? throw new ArgumentException($"Invalid help key: {component}/{topic}");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Atomic write — temp file then move, so a reader never sees a half-written fragment
        // (mirrors MedFormService / the month-end export).
        var body = html ?? string.Empty;
        var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(tempPath, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }

        _logger.LogInformation(
            "Help content saved: {Component}/{Topic} ({Bytes} bytes).", component, topic, body.Length);

        return new HelpContentDto
        {
            Component = component,
            Topic = topic,
            Html = body,
            Exists = true,
            CanEdit = _canEdit,
        };
    }

    private string? ResolveFilePath(string component, string topic)
    {
        if (!IsValidSegment(component) || !IsValidSegment(topic)) return null;
        return Path.Combine(_basePath, component, topic + ".html");
    }
}
