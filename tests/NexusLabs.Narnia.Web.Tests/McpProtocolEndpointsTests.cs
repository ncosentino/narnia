using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Narnia.Web.Tests;

/// <summary>
/// Wire-level contract tests for the in-process MCP server at <c>/mcp</c>.
///
/// These exist because Narnia's MCP endpoint is only ever exercised by external clients
/// (the Copilot CLI), so an SDK that silently stops speaking the revision those clients
/// require produces a broken integration that no other test in the suite can observe.
/// </summary>
public sealed class McpProtocolEndpointsTests
{
    /// <summary>
    /// The MCP revision current Copilot CLI builds negotiate. A client sending this version
    /// must not be rejected; older servers answered it with
    /// <c>-32000 "The MCP-Protocol-Version header value '2026-07-28' is not supported."</c>
    /// </summary>
    private const string CurrentProtocolVersion = "2026-07-28";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Mcp_NegotiatesProtocolVersionRequiredByCopilotCli()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var (status, payload) = await SendAsync(client, "server/discover", CurrentProtocolVersion);

        Assert.Equal(HttpStatusCode.OK, status);
        var supported = payload.RootElement
            .GetProperty("result")
            .GetProperty("supportedVersions")
            .EnumerateArray()
            .Select(version => version.GetString())
            .ToArray();
        Assert.Contains(CurrentProtocolVersion, supported);
    }

    [Fact]
    public async Task Mcp_ExposesNarniaToolsOverCurrentProtocolVersion()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var (status, payload) = await SendAsync(client, "tools/list", CurrentProtocolVersion);

        Assert.Equal(HttpStatusCode.OK, status);
        var tools = payload.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("get_session_turns", tools);
    }

    // Down-level interoperability matters as much as the upgrade: Narnia is a single always-on
    // server shared by every MCP client on the machine, so a client pinned to an older revision
    // must keep working after the SDK moves forward.
    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2025-06-18")]
    [InlineData("2025-03-26")]
    public async Task Mcp_StillNegotiatesDownLevelProtocolVersions(string protocolVersion)
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var (status, payload) = await SendAsync(client, "initialize", protocolVersion);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            protocolVersion,
            payload.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    private static async Task<(HttpStatusCode Status, JsonDocument Payload)> SendAsync(
        HttpClient client,
        string method,
        string protocolVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                BuildRequestBody(method, protocolVersion),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
        if (protocolVersion == CurrentProtocolVersion)
        {
            // SEP-2243 header standardization: the 2026-07-28 wire requires the method in a header.
            // Sending it on a down-level handshake would push the server onto the modern path and
            // fail negotiation, so legacy requests deliberately omit it.
            request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        }

        var response = await client.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(ExtractJsonRpcPayload(body)));
    }

    private static string BuildRequestBody(string method, string protocolVersion)
    {
        // Earlier revisions carry negotiation state in the `initialize` params; 2026-07-28 carries
        // it in `_meta` on every request. Mixing the two shapes is rejected as a version mismatch.
        if (protocolVersion != CurrentProtocolVersion)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = method,
                ["params"] = new Dictionary<string, object?>
                {
                    ["protocolVersion"] = protocolVersion,
                    ["capabilities"] = new Dictionary<string, object?>(),
                    ["clientInfo"] = ClientInfo(),
                },
            });
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = method,
            ["params"] = new Dictionary<string, object?>
            {
                ["_meta"] = new Dictionary<string, object?>
                {
                    ["io.modelcontextprotocol/protocolVersion"] = protocolVersion,
                    ["io.modelcontextprotocol/clientCapabilities"] = new Dictionary<string, object?>(),
                    ["io.modelcontextprotocol/clientInfo"] = ClientInfo(),
                },
            },
        });
    }

    private static Dictionary<string, object?> ClientInfo() => new()
    {
        ["name"] = "narnia-protocol-tests",
        ["version"] = "1.0.0",
    };

    /// <summary>
    /// Streamable HTTP answers either with a bare JSON body or an SSE stream, so tests read the
    /// single JSON-RPC message out of whichever framing the transport chose.
    /// </summary>
    private static string ExtractJsonRpcPayload(string body)
    {
        var data = body
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal));

        return data is null ? body : data["data:".Length..].Trim();
    }
}
