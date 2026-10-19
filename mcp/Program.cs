// mcp/Program.cs — a stdio MCP server exposing the KF2_SHELL command channel
// (patches/AgentServer.cs) as typed tools to any MCP host.
//
// The game is untouched: this process is a translator between MCP's
// newline-delimited JSON-RPC 2.0 over stdin/stdout and the shell's line
// protocol over TCP loopback (one request per line, one single-line JSON
// reply). See "The command channel" in docs/PATCHES_AND_MODS.md and "The MCP
// layer" below it.
//
// stdout carries protocol messages and nothing else ever; diagnostics go to
// stderr prefixed "[kf2-mcp] ". Requests are processed sequentially off one
// stdin reader — hosts issue calls serially, and a slow warp delays the next
// call by at most ~15 s. No exception path may kill the process or write
// non-protocol bytes to stdout: every failure lands in an error response so
// the host session survives a dead game.

using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kf2.Mcp;

internal static class Program
{
    const string DefaultProtocolVersion = "2024-11-05";

    static int Main()
    {
        ShellClient shell;
        try
        {
            shell = new ShellClient(Environment.GetEnvironmentVariable("KF2_MCP_ENDPOINT"));
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine($"[kf2-mcp] {e.Message}");
            return 2;
        }

        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            try
            {
                Handle(line, shell);
            }
            catch (Exception e)
            {
                // A request we could not answer must still not take the server
                // down; the host sees a missing reply for that one call only.
                Console.Error.WriteLine($"[kf2-mcp] dropped request: {e.Message}");
            }
        }
        return 0;
    }

    // ---- one incoming line ----

    static void Handle(string line, ShellClient shell)
    {
        JsonNode? msg;
        try
        {
            msg = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            WriteError(null, -32700, "Parse error");
            return;
        }

        if (msg is not JsonObject req)
        {
            WriteError(null, -32600, "Invalid Request");
            return;
        }

        bool hasId = req.ContainsKey("id");
        JsonNode? id = req["id"];
        var method = Str(req["method"]);

        if (method is null)
        {
            if (hasId) WriteError(id, -32600, "Invalid Request: missing method");
            return;
        }

        // Notifications carry no id and are never answered — initialized,
        // cancelled, anything else.
        if (!hasId) return;

        switch (method)
        {
            case "initialize":
                WriteResult(id, InitializeResult(Obj(req["params"])));
                break;
            case "ping":
                WriteResult(id, []);
                break;
            case "tools/list":
                WriteResult(id, ToolsList());
                break;
            case "tools/call":
                ToolsCall(id, Obj(req["params"]), shell);
                break;
            default:
                WriteError(id, -32601, $"Method not found: {method}");
                break;
        }
    }

    // ---- initialize / tools/list ----

    static JsonObject InitializeResult(JsonObject? parameters)
    {
        // Echo the host's requested version verbatim; invent nothing.
        var version = DefaultProtocolVersion;
        if (parameters?["protocolVersion"] is JsonValue v &&
            v.TryGetValue<string>(out var requested) &&
            !string.IsNullOrEmpty(requested))
            version = requested;

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = "kf2-mcp", ["version"] = "1.0.0" },
            ["instructions"] =
                "Controls the running King's Field II port through its KF2_SHELL channel. " +
                "Start the game first, e.g.: KF2_SHELL=1 KF2_AUTOSTART=2 dotnet run " +
                "--project Deception.csproj -c Release -- disc/KingsField2.cue " +
                "— until then every tool returns a connection error.",
        };
    }

    static JsonObject ToolsList() => new()
    {
        ["tools"] = new JsonArray(
            Tool("kf2_state",
                "Snapshot of the running game. Fields: overlay,inGame,dead,hp,maxHp,mp,maxMp," +
                "level,exp,area,slot,deathFrames,pos. inGame:false means title/menus, not an area.",
                []),
            Tool("kf2_nearby",
                "Positions of live world-table records within radius horizontal units of the player " +
                "(8192 ≈ four tiles), nearest first, capped at 16 items, tagged objects/entities. " +
                "Entity identity is not confirmed — treat as unnamed contacts.",
                [IntProp("radius", "Horizontal radius around the player; the shell defaults to 8192.", 1, 65536)]),
            Tool("kf2_load_save",
                "Loads save slot 1..3 through the game's own loader; needs a live area (in game), " +
                "else times out after 5 s.",
                [IntProp("slot", "Save slot to load.", 1, 3)], ["slot"]),
            Tool("kf2_warp",
                "Re-enters area index 0..7 (the beacon's area numbering) via the game's own entry " +
                "routine; also needs a live area (in game); other area indices do not exist.",
                [IntProp("area", "Area index to re-enter.", 0, 7)], ["area"]),
            Tool("kf2_press_button",
                "Presses a pad button for holdMs (default 150); works wherever the pad is read " +
                "including boot menus; one synthetic press at a time — the next replaces it.",
                [
                    EnumProp("button",
                        "The button to press.",
                        ["Select", "Start", "Cross", "Circle", "Square", "Triangle",
                         "L1", "R1", "L2", "R2", "L3", "R3", "Up", "Down", "Left", "Right"]),
                    IntProp("holdMs", "How long to hold the button, in milliseconds.", 1, 5000),
                ],
                ["button"]),
            Tool("kf2_kill",
                "Drops HP to zero the way a hit would; check the result via kf2_state.",
                [])),
    };

    static JsonObject Tool(string name, string description,
                           (string Name, JsonObject Schema)[] properties,
                           string[]? required = null)
    {
        var props = new JsonObject();
        foreach (var (key, schema) in properties) props[key] = schema;

        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["additionalProperties"] = false,
        };
        if (required is { Length: > 0 })
            inputSchema["required"] = new JsonArray(
                required.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray());

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema,
        };
    }

    static (string, JsonObject) IntProp(string name, string description, long min, long max) =>
        (name, new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description,
            ["minimum"] = min,
            ["maximum"] = max,
        });

    static (string, JsonObject) EnumProp(string name, string description, string[] values)
    {
        var enumValues = new JsonArray();
        foreach (var v in values) enumValues.Add(JsonValue.Create(v));
        return (name, new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = enumValues,
        });
    }

    // ---- tools/call ----

    sealed class ParamError(string message) : Exception(message);

    static void ToolsCall(JsonNode? id, JsonObject? parameters, ShellClient shell)
    {
        var name = Str(parameters?["name"]);
        if (string.IsNullOrEmpty(name))
        {
            WriteError(id, -32602, "Invalid params: tools/call requires a tool name");
            return;
        }

        var args = parameters?["arguments"] as JsonObject ?? [];
        string requestLine;
        try
        {
            requestLine = name switch
            {
                "kf2_state" => "state",
                "kf2_kill" => "kill",
                "kf2_nearby" => Optional(args, "radius") is { } r
                    ? FormattableString.Invariant($"nearby {r}")
                    : "nearby",
                "kf2_load_save" => FormattableString.Invariant($"load {Required(args, "slot")}"),
                "kf2_warp" => FormattableString.Invariant($"warp {Required(args, "area")}"),
                "kf2_press_button" => Optional(args, "holdMs") is { } ms
                    ? FormattableString.Invariant($"press {Button(args)} {ms}")
                    : $"press {Button(args)}",
                _ => null,
            } ?? throw new ParamError($"Unknown tool: {name}");
        }
        catch (ParamError e)
        {
            WriteError(id, -32602, e.Message);
            return;
        }

        // The shell validates everything and always answers within ~5 s; its
        // reply line is already single-line JSON, so it goes back verbatim.
        var reply = shell.Call(requestLine);
        if (reply is null)
        {
            WriteResult(id, TextResult(
                $"cannot reach the game on {shell.Endpoint} — start it with KF2_SHELL=1 " +
                "(see docs/PATCHES_AND_MODS.md \"The command channel\")", isError: true));
            return;
        }
        WriteResult(id, TextResult(reply, IsOkFalse(reply)));
    }

    static long Required(JsonObject args, string name) =>
        Optional(args, name)
        ?? throw new ParamError($"Invalid params: missing required argument '{name}'");

    static long? Optional(JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out var node) || node is null) return null;
        if (node is JsonValue v && v.TryGetValue(out long i)) return i;
        throw new ParamError($"Invalid params: argument '{name}' must be an integer");
    }

    static string Button(JsonObject args)
    {
        if (!args.TryGetPropertyValue("button", out var node) ||
            node is not JsonValue v || !v.TryGetValue(out string? s) || string.IsNullOrEmpty(s))
            throw new ParamError("Invalid params: argument 'button' must be a string");
        return s;
    }

    /// <summary>A node's string value, or null when absent or not a string.
    /// The non-throwing read: <c>GetValue&lt;string&gt;()</c> on a number-shaped
    /// node throws, and a malformed-but-legal-JSON request must cost its host
    /// a -32602 reply, not a silently dropped call.</summary>
    static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    /// <summary>params as the object the handlers index into, or null when
    /// absent or mistyped: the string indexer on an array-shaped node throws,
    /// so the shape is narrowed before anything indexes it.</summary>
    static JsonObject? Obj(JsonNode? node) => node as JsonObject;

    /// <summary>True iff the shell's reply parses as an object with ok === false.</summary>
    static bool IsOkFalse(string reply)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("ok", out var ok) &&
                   ok.ValueKind == JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false; // unparsable: returned verbatim, not flagged
        }
    }

    static JsonObject TextResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    // ---- outbound framing ----

    static readonly Stream Stdout = Console.OpenStandardOutput();

    static void WriteResult(JsonNode? id, JsonObject result) => WriteMessage(new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    });

    static void WriteError(JsonNode? id, int code, string message) => WriteMessage(new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    });

    static void WriteMessage(JsonObject message)
    {
        // Relaxed: this stream is a pipe to an MCP host, not HTML — emit
        // apostrophes and non-ASCII as themselves instead of \u0027 escapes.
        using var buf = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buf, new JsonWriterOptions
               {
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
            message.WriteTo(writer); // flushes into buf on dispose
        buf.WriteByte((byte)'\n');
        Stdout.Write(buf.ToArray());
        Stdout.Flush(); // one line, immediately — the host may be waiting on it
    }
}

/// <summary>
/// The persistent connection to the game's command channel: lazy, guarded by
/// one lock, one request in flight at a time. Transport trouble is reported
/// through tool results, never exceptions. A failed round trip is never
/// resent once the request has reached the wire — a lost reply may mean the
/// game already warped, loaded or pressed, and resending would do it twice.
/// Only a failure before anything was sent (the connect itself) retries once.
/// </summary>
sealed class ShellClient
{
    const int ReplyTimeoutMs = 10_000; // the shell answers within ~5 s (it answers its own timeouts)
    const int ConnectTimeoutMs = 5_000; // a filtered remote endpoint would otherwise park the single-threaded stdin loop in the OS SYN-retry window, minutes

    readonly string _host;
    readonly int _port;
    readonly object _gate = new();
    TcpClient? _client;
    StreamReader? _reader;
    StreamWriter? _writer;

    public ShellClient(string? spec)
    {
        spec = string.IsNullOrWhiteSpace(spec) ? "127.0.0.1:27900" : spec.Trim();

        string hostPart, portPart;
        if (spec.StartsWith('['))
        {
            // IPv6 literal: [::1]:27900
            var close = spec.IndexOf(']');
            var colon = close >= 0 && close + 1 < spec.Length && spec[close + 1] == ':'
                ? close + 1 : -1;
            if (colon < 0) throw new FormatException($"cannot parse KF2_MCP_ENDPOINT '{spec}'");
            hostPart = spec[1..close];
            portPart = spec[(colon + 1)..];
        }
        else
        {
            var colon = spec.LastIndexOf(':');
            if (colon <= 0 || colon == spec.Length - 1)
                throw new FormatException($"cannot parse KF2_MCP_ENDPOINT '{spec}'");
            hostPart = spec[..colon];
            portPart = spec[(colon + 1)..];
        }

        if (!int.TryParse(portPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out _port) ||
            _port is < 1 or > 65535)
            throw new FormatException($"cannot parse KF2_MCP_ENDPOINT '{spec}': bad port");
        _host = hostPart;
    }

    public string Endpoint => $"{_host}:{_port}";

    public string? Call(string requestLine)
    {
        lock (_gate)
        {
            if (TryRoundTrip(requestLine, out var reply, out var sent)) return reply;

            // Resend only what was never sent: a refused or timed-out connect
            // delivered nothing. Anything after that — a write that fell
            // over, a reply that stalled past the receive timeout — may have
            // executed the command, so it fails terminally rather than risk a
            // doubled warp/load/press.
            if (!sent && TryRoundTrip(requestLine, out reply, out _)) return reply;

            Close(); // stale bytes after a failure would desync the next reply
            return null;
        }
    }

    void Close()
    {
        try { _client?.Close(); }
        catch { /* closing a dying socket is best-effort */ }
        _client = null;
        _reader = null;
        _writer = null;
    }

    bool TryRoundTrip(string line, out string? reply, out bool sent)
    {
        reply = null;
        sent = false;
        try
        {
            if (_client is null || !_client.Connected)
            {
                Close();
                var tcp = new TcpClient
                {
                    NoDelay = true,
                    ReceiveTimeout = ReplyTimeoutMs,
                    SendTimeout = ReplyTimeoutMs,
                };

                // Bounded connect: loopback refuses instantly, but a
                // KF2_MCP_ENDPOINT override aimed at a filtered address must
                // not hold the stdin loop for the OS SYN-retry window.
                if (!tcp.ConnectAsync(_host, _port).Wait(ConnectTimeoutMs))
                {
                    tcp.Close(); // the abandoned connect dies with the socket
                    return false; // nothing sent: Call may retry
                }

                var stream = tcp.GetStream();
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                _reader = new StreamReader(stream, utf8);
                _writer = new StreamWriter(stream, utf8) { AutoFlush = true, NewLine = "\n" };
                _client = tcp;
            }
        }
        catch
        {
            Close();
            return false; // connect trouble: nothing was sent
        }

        // Past this point the request can reach the game however we exit, so
        // every failure reports sent=true and Call will not resend.
        sent = true;
        try
        {
            _writer!.WriteLine(line);
            var received = _reader!.ReadLine(); // exactly one reply line; timeout throws SocketException
            if (received is null || received.Length == 0) return false;
            reply = received;
            return true;
        }
        catch
        {
            Close();
            return false;
        }
    }
}
