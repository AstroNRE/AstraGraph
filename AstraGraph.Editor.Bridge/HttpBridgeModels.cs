using System.Collections.Specialized;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace AstraGraph.Editor.Bridge;

/// <summary>
/// Represents an incoming HTTP request to the local loopback bridge.
/// </summary>
public sealed class HttpBridgeRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required string QueryString { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public required IPEndPoint RemoteEndPoint { get; init; }
    public byte[] Body { get; init; } = Array.Empty<byte>();

    public string BodyText => Encoding.UTF8.GetString(Body);

    public NameValueCollection QueryParameters
    {
        get
        {
            var collection = new NameValueCollection();
            if (string.IsNullOrEmpty(QueryString))
                return collection;

            string query = QueryString.TrimStart('?');
            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                int idx = pair.IndexOf('=');
                if (idx >= 0)
                {
                    string key = Uri.UnescapeDataString(pair[..idx]);
                    string val = Uri.UnescapeDataString(pair[(idx + 1)..]);
                    collection.Add(key, val);
                }
                else
                {
                    collection.Add(Uri.UnescapeDataString(pair), string.Empty);
                }
            }
            return collection;
        }
    }
}

/// <summary>
/// Represents an HTTP response sent back by the local loopback bridge.
/// </summary>
public sealed class HttpBridgeResponse
{
    public int StatusCode { get; set; } = 200;
    public string StatusDescription { get; set; } = "OK";
    public string ContentType { get; set; } = "text/plain; charset=utf-8";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Content { get; set; } = Array.Empty<byte>();

    public static HttpBridgeResponse Ok(string text, string contentType = "text/plain; charset=utf-8")
    {
        return new HttpBridgeResponse
        {
            StatusCode = 200,
            StatusDescription = "OK",
            ContentType = contentType,
            Content = Encoding.UTF8.GetBytes(text)
        };
    }

    public static HttpBridgeResponse Json(string json) => Ok(json, "application/json; charset=utf-8");

    public static HttpBridgeResponse Html(string html) => Ok(html, "text/html; charset=utf-8");

    public static HttpBridgeResponse NotFound(string message = "404 Not Found")
    {
        return new HttpBridgeResponse
        {
            StatusCode = 404,
            StatusDescription = "Not Found",
            ContentType = "text/plain; charset=utf-8",
            Content = Encoding.UTF8.GetBytes(message)
        };
    }

    public static HttpBridgeResponse Forbidden(string message = "403 Forbidden")
    {
        return new HttpBridgeResponse
        {
            StatusCode = 403,
            StatusDescription = "Forbidden",
            ContentType = "text/plain; charset=utf-8",
            Content = Encoding.UTF8.GetBytes(message)
        };
    }

    public static HttpBridgeResponse BadRequest(string message = "400 Bad Request")
    {
        return new HttpBridgeResponse
        {
            StatusCode = 400,
            StatusDescription = "Bad Request",
            ContentType = "text/plain; charset=utf-8",
            Content = Encoding.UTF8.GetBytes(message)
        };
    }
}

/// <summary>
/// Context for an established WebSocket connection over the local bridge.
/// </summary>
public sealed class WebSocketBridgeContext
{
    public required WebSocket WebSocket { get; init; }
    public required HttpBridgeRequest HandshakeRequest { get; init; }
    public required IPEndPoint RemoteEndPoint { get; init; }
    public NonceInfo? RedeemedNonce { get; set; }
}
