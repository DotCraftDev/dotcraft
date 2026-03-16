using System.Text.Json;
using DotCraft.AppServerTestClient;
using DotCraft.Sessions.Protocol;
using DotCraft.Sessions.Protocol.AppServer;

// ─────────────────────────────────────────────────────────────────────────────
//  dotcraft-test-client -- CLI tool for end-to-end AppServer protocol testing
//
//  Usage:
//    dotcraft-test-client --dotcraft-bin <path> [--workspace <path>] <command> [args...]
//
//  Commands:
//    init                          -- Perform initialize/initialized handshake, print server info
//    send-message <text>           -- init → thread/start → turn/start → stream until complete
//    thread-list                   -- init → thread/list → print threads
//    thread-resume <threadId>      -- init → thread/resume → stream notifications until Ctrl+C
//    watch                         -- init → print all inbound messages until Ctrl+C
//    trigger-approval <text>       -- send-message with approval-triggering prompt;
//                                     auto-accepts all approvals and logs them
// ─────────────────────────────────────────────────────────────────────────────

static void Usage()
{
    Console.Error.WriteLine("""
        dotcraft-test-client -- AppServer JSON-RPC protocol test client

        Usage:
          dotcraft-test-client --dotcraft-bin <path> [--workspace <dir>] <command> [args...]

        Options:
          --dotcraft-bin <path>    Path to the dotcraft executable (required)
          --workspace <dir>        Working directory for the dotcraft subprocess (default: current)

        Commands:
          init
              Perform initialize + initialized handshake. Print server capabilities.

          send-message "<text>"
              Full turn flow: init → thread/start → turn/start → stream until turn/completed.

          thread-list
              init → thread/list → print all threads.

          thread-resume <threadId>
              init → thread/resume <threadId> → stream notifications until Ctrl+C.

          watch
              init → print every inbound notification/message until Ctrl+C.

          trigger-approval "<text>"
              Like send-message but intercepts item/approval/request and auto-accepts.
              Logs all approval requests and responses.
        """);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Argument parsing
// ─────────────────────────────────────────────────────────────────────────────

string? dotcraftBin = null;
string? workspace = null;
string? command = null;
string? commandArg = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dotcraft-bin" when i + 1 < args.Length:
            dotcraftBin = args[++i];
            break;
        case "--workspace" when i + 1 < args.Length:
            workspace = args[++i];
            break;
        case "--help" or "-h":
            Usage();
            return 0;
        default:
            if (command == null) command = args[i];
            else if (commandArg == null) commandArg = args[i];
            break;
    }
}

if (dotcraftBin == null)
{
    Console.Error.WriteLine("Error: --dotcraft-bin is required.");
    Usage();
    return 1;
}

if (command == null)
{
    Console.Error.WriteLine("Error: a command is required.");
    Usage();
    return 1;
}

workspace ??= Directory.GetCurrentDirectory();

// ─────────────────────────────────────────────────────────────────────────────
//  Subcommand dispatch
// ─────────────────────────────────────────────────────────────────────────────

try
{
    return command switch
    {
        "init" => await RunInitAsync(dotcraftBin, workspace),
        "send-message" => await RunSendMessageAsync(dotcraftBin, workspace, commandArg),
        "thread-list" => await RunThreadListAsync(dotcraftBin, workspace),
        "thread-resume" => await RunThreadResumeAsync(dotcraftBin, workspace, commandArg),
        "watch" => await RunWatchAsync(dotcraftBin, workspace),
        "trigger-approval" => await RunTriggerApprovalAsync(dotcraftBin, workspace, commandArg),
        _ => PrintUnknownCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[error] {ex.Message}");
    return 1;
}

// ─────────────────────────────────────────────────────────────────────────────
//  init
// ─────────────────────────────────────────────────────────────────────────────

static async Task<int> RunInitAsync(string bin, string workspace)
{
    Console.Error.WriteLine("[init] Spawning dotcraft app-server...");
    await using var client = await AppServerClient.SpawnAsync(bin, workspace);

    var resp = await client.InitializeAsync();
    PrintJson("< initialize response", resp);

    Console.Error.WriteLine("[init] Handshake complete. Server is ready.");
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
//  send-message
// ─────────────────────────────────────────────────────────────────────────────

static async Task<int> RunSendMessageAsync(string bin, string workspace, string? text)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        Console.Error.WriteLine("Error: send-message requires a message text argument.");
        return 1;
    }

    Console.Error.WriteLine($"[send-message] '{text}'");
    await using var client = await AppServerClient.SpawnAsync(bin, workspace);

    var initResp = await client.InitializeAsync();
    PrintJson("< initialize", initResp);

    // thread/start
    var threadResp = await client.SendRequestAsync(AppServerMethods.ThreadStart, new
    {
        identity = new
        {
            channelName = "appserver-test-client",
            workspacePath = workspace
        }
    });
    PrintJson("< thread/start", threadResp);
    var threadId = threadResp.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

    // turn/start
    var turnResp = await client.SendRequestAsync(AppServerMethods.TurnStart, new
    {
        threadId,
        input = new[] { new { type = "text", text } }
    });
    PrintJson("< turn/start", turnResp);
    var turnId = turnResp.RootElement.GetProperty("result").GetProperty("turn").GetProperty("id").GetString()!;

    // stream notifications until turn completes
    Console.Error.WriteLine($"[send-message] Streaming turn {turnId}...");
    await client.StreamTurnAsync(threadId, turnId,
        onNotification: notif =>
        {
            PrintJson("< notification", notif);
        },
        timeout: TimeSpan.FromMinutes(10));

    Console.Error.WriteLine("[send-message] Turn complete.");
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
//  thread-list
// ─────────────────────────────────────────────────────────────────────────────

static async Task<int> RunThreadListAsync(string bin, string workspace)
{
    await using var client = await AppServerClient.SpawnAsync(bin, workspace);
    await client.InitializeAsync();

    var resp = await client.SendRequestAsync(AppServerMethods.ThreadList, new
    {
        identity = new
        {
            channelName = "appserver-test-client",
            workspacePath = workspace
        }
    });

    var data = resp.RootElement.GetProperty("result").GetProperty("data");
    Console.WriteLine($"Threads ({data.GetArrayLength()}):");
    foreach (var thread in data.EnumerateArray())
    {
        var id = thread.GetProperty("id").GetString();
        var status = thread.GetProperty("status").GetString();
        var lastActive = thread.TryGetProperty("lastActiveAt", out var la) ? la.GetString() : "?";
        Console.WriteLine($"  {id}  status={status}  lastActive={lastActive}");
    }
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
//  thread-resume
// ─────────────────────────────────────────────────────────────────────────────

static async Task<int> RunThreadResumeAsync(string bin, string workspace, string? threadId)
{
    if (string.IsNullOrWhiteSpace(threadId))
    {
        Console.Error.WriteLine("Error: thread-resume requires a thread ID argument.");
        return 1;
    }

    await using var client = await AppServerClient.SpawnAsync(bin, workspace);
    await client.InitializeAsync();

    var resp = await client.SendRequestAsync(AppServerMethods.ThreadResume, new { threadId });
    PrintJson("< thread/resume", resp);

    Console.Error.WriteLine("[thread-resume] Streaming notifications until Ctrl+C...");
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    while (!cts.Token.IsCancellationRequested)
    {
        var notif = await client.WaitForNotificationAsync(timeout: TimeSpan.FromSeconds(5));
        if (notif != null) PrintJson("< notification", notif);
    }
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
//  watch
// ─────────────────────────────────────────────────────────────────────────────

static async Task<int> RunWatchAsync(string bin, string workspace)
{
    await using var client = await AppServerClient.SpawnAsync(bin, workspace);
    var initResp = await client.InitializeAsync();
    PrintJson("< initialize", initResp);

    Console.Error.WriteLine("[watch] Connected. Streaming all inbound messages until Ctrl+C...");
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    while (!cts.Token.IsCancellationRequested)
    {
        var notif = await client.WaitForNotificationAsync(timeout: TimeSpan.FromSeconds(2));
        if (notif != null) PrintJson("< message", notif);
    }
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
//  trigger-approval
// ─────────────────────────────────────────────────────────────────────────────

static async Task<int> RunTriggerApprovalAsync(string bin, string workspace, string? text)
{
    var prompt = text ?? "Run `echo hello` and show me the output.";

    Console.Error.WriteLine($"[trigger-approval] prompt='{prompt}'");
    await using var client = await AppServerClient.SpawnAsync(bin, workspace);

    await client.InitializeAsync();

    var threadResp = await client.SendRequestAsync(AppServerMethods.ThreadStart, new
    {
        identity = new { channelName = "appserver-test-client", workspacePath = workspace }
    });
    var threadId = threadResp.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
    Console.Error.WriteLine($"[trigger-approval] thread={threadId}");

    var turnResp = await client.SendRequestAsync(AppServerMethods.TurnStart, new
    {
        threadId,
        input = new[] { new { type = "text", text = prompt } }
    });
    var turnId = turnResp.RootElement.GetProperty("result").GetProperty("turn").GetProperty("id").GetString()!;
    Console.Error.WriteLine($"[trigger-approval] turn={turnId}");

    var approvalCount = 0;

    // Stream notifications, handling approval requests inline
    await client.StreamTurnAsync(threadId, turnId, onNotification: notif =>
    {
        if (!notif.RootElement.TryGetProperty("method", out var method)) return;
        var m = method.GetString();

        if (m == AppServerMethods.ItemApprovalRequest)
        {
            approvalCount++;
            var @params = notif.RootElement.GetProperty("params");
            var requestId = @params.TryGetProperty("requestId", out var rid) ? rid.GetString() : "?";
            var operation = @params.TryGetProperty("operation", out var op) ? op.GetString() : "?";
            Console.Error.WriteLine($"[trigger-approval] approval #{approvalCount}: requestId={requestId} op={operation}");
            Console.Error.WriteLine("[trigger-approval] Auto-accepting...");

            // Send approval response back to server
            if (notif.RootElement.TryGetProperty("id", out var approvalReqId))
                client.SendApprovalResponseAsync(approvalReqId, "accept").GetAwaiter().GetResult();
        }
        else
        {
            PrintJson("< notification", notif);
        }
    }, timeout: TimeSpan.FromMinutes(5));

    Console.Error.WriteLine($"[trigger-approval] Turn complete. Total approvals: {approvalCount}");
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Helpers
// ─────────────────────────────────────────────────────────────────────────────

static int PrintUnknownCommand(string cmd)
{
    Console.Error.WriteLine($"Error: unknown command '{cmd}'.");
    Usage();
    return 1;
}

static void PrintJson(string label, JsonDocument doc)
{
    var pretty = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine($"{label}:");
    Console.WriteLine(pretty);
}
