using AstraGraph.Core;

namespace AstraGraph.Editor.InGame;

/// <summary>
/// The action that Studio should perform when opened via deep link.
/// </summary>
public enum StudioAction
{
    OpenStudio,
    InspectEntity,
    OpenGraph,
    OpenRevision,
    OpenNode,
    OpenDebugSession,
    OpenRuntimeError
}

/// <summary>
/// Full context for launching Astra Studio at a specific location in the graph/entity hierarchy.
/// Serialized into the launch URL as query parameters.
/// </summary>
public sealed record StudioDeepLinkContext
{
    public StudioAction Action { get; init; } = StudioAction.OpenStudio;
    public string? EntityUid { get; init; }
    public string? GraphId { get; init; }
    public string? NodeId { get; init; }
    public string? PinId { get; init; }
    public string? Revision { get; init; }
    public string? DiagnosticCode { get; init; }
    public long? ExecutionTick { get; init; }
}

/// <summary>
/// Builds and parses deep-link URLs for Astra Studio.
/// URL format: http://127.0.0.1:{port}/?nonce={nonce}&action={action}[&entity=...][&graph=...][&node=...][&pin=...][&revision=...][&error=...][&tick=...]
/// </summary>
public static class StudioDeepLinkUrlBuilder
{
    /// <summary>
    /// Serializes a context into URL query parameters appended to the base URL.
    /// </summary>
    public static string Build(string baseUrl, StudioDeepLinkContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(context);

        var sb = new System.Text.StringBuilder(baseUrl);

        // baseUrl may already have '?' from nonce; append '&' if so, else '?'
        char separator = baseUrl.Contains('?') ? '&' : '?';

        void Append(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            sb.Append(separator);
            sb.Append(key);
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        Append("action", context.Action.ToString().ToLowerInvariant());
        Append("entity", context.EntityUid);
        Append("graph", context.GraphId);
        Append("node", context.NodeId);
        Append("pin", context.PinId);
        Append("revision", context.Revision);
        Append("error", context.DiagnosticCode);
        if (context.ExecutionTick.HasValue)
            Append("tick", context.ExecutionTick.Value.ToString());

        return sb.ToString();
    }

    /// <summary>
    /// Parses query parameters from a launch URL into a <see cref="StudioDeepLinkContext"/>.
    /// Returns null if the URL cannot be parsed.
    /// </summary>
    public static StudioDeepLinkContext? Parse(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        StudioAction action = StudioAction.OpenStudio;
        string? rawAction = query["action"];
        if (!string.IsNullOrEmpty(rawAction))
        {
            if (!Enum.TryParse<StudioAction>(rawAction, ignoreCase: true, out action))
                action = StudioAction.OpenStudio;
        }

        long? tick = null;
        if (long.TryParse(query["tick"], out long parsedTick))
            tick = parsedTick;

        return new StudioDeepLinkContext
        {
            Action = action,
            EntityUid = Sanitize(query["entity"]),
            GraphId = Sanitize(query["graph"]),
            NodeId = Sanitize(query["node"]),
            PinId = Sanitize(query["pin"]),
            Revision = Sanitize(query["revision"]),
            DiagnosticCode = Sanitize(query["error"]),
            ExecutionTick = tick
        };
    }

    /// <summary>
    /// Prevents injection by stripping any characters outside safe identifier set.
    /// </summary>
    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        // Allow alphanumeric, hyphens, underscores, dots, colons (for entity UIDs like "42:3")
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':')
                sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }
}
