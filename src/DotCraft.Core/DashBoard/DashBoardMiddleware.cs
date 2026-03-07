using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DotCraft.DashBoard;

public static class DashBoardMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions RawJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void MapDashBoard(this IEndpointRouteBuilder endpoints, TraceStore traceStore, AppConfig config, DotCraftPaths paths, TokenUsageStore? tokenUsageStore = null)
    {
        endpoints.MapGet("/dashboard/", ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            return ctx.Response.WriteAsync(DashBoardFrontend.GetHtml());
        });

        endpoints.MapGet("/dashboard/api/summary", () =>
        {
            var summary = traceStore.GetSummary();
            return Results.Json(summary, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions", () =>
        {
            var sessions = traceStore.GetSessions();
            var result = sessions.Select(s => new
            {
                s.SessionKey,
                startedAt = s.StartedAt.ToString("o"),
                lastActivityAt = s.LastActivityAt.ToString("o"),
                s.TotalInputTokens,
                s.TotalOutputTokens,
                totalTokens = s.TotalInputTokens + s.TotalOutputTokens,
                s.RequestCount,
                s.ResponseCount,
                s.ToolCallCount,
                s.ErrorCount,
                s.ContextCompactionCount,
                totalToolDurationMs = s.TotalToolDurationMs,
                avgToolDurationMs = s.AvgToolDurationMs,
                maxToolDurationMs = s.MaxToolDurationMs,
                finalSystemPrompt = s.FinalSystemPrompt,
                toolNames = s.ToolNames,
                lastFinishReason = s.LastFinishReason
            });
            return Results.Json(result, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions/{sessionKey}/events", (string sessionKey) =>
        {
            var events = traceStore.GetEvents(sessionKey);
            return Results.Json(events, JsonOptions);
        });

        endpoints.MapDelete("/dashboard/api/sessions/{sessionKey}", (string sessionKey) =>
        {
            var deleted = traceStore.ClearSession(sessionKey);
            return deleted
                ? Results.Json(new { deleted = true, sessionKey }, JsonOptions)
                : Results.Json(new { deleted = false, sessionKey }, JsonOptions, statusCode: 404);
        });

        endpoints.MapDelete("/dashboard/api/sessions", () =>
        {
            traceStore.ClearAll();
            return Results.Json(new { cleared = true }, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/tools", () =>
        {
            var icons = ToolRegistry.GetAllToolIcons();
            var tools = icons.Select(kv => new { name = kv.Key, icon = kv.Value });
            return Results.Json(new { tools }, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/config", () => Results.Json(new
        {
            // Core settings
            model = config.Model,
            endPoint = config.EndPoint,
            systemInstructions = config.SystemInstructions,
            maxToolCallRounds = config.MaxToolCallRounds,
            subagentMaxToolCallRounds = config.SubagentMaxToolCallRounds,
            subagentMaxConcurrency = config.SubagentMaxConcurrency,
            compactSessions = config.CompactSessions,
            maxContextTokens = config.MaxContextTokens,
            memoryWindow = config.MemoryWindow,
            debugMode = config.DebugMode,
            // Tools config
            tools = new
            {
                file = new
                {
                    requireApprovalOutsideWorkspace = config.Tools.File.RequireApprovalOutsideWorkspace,
                    maxFileSize = config.Tools.File.MaxFileSize
                },
                shell = new
                {
                    requireApprovalOutsideWorkspace = config.Tools.Shell.RequireApprovalOutsideWorkspace,
                    timeout = config.Tools.Shell.Timeout,
                    maxOutputLength = config.Tools.Shell.MaxOutputLength
                },
                web = new
                {
                    maxChars = config.Tools.Web.MaxChars,
                    timeout = config.Tools.Web.Timeout,
                    searchMaxResults = config.Tools.Web.SearchMaxResults,
                    searchProvider = config.Tools.Web.SearchProvider
                }
            },
            // QQ Bot config
            qqBot = new
            {
                enabled = config.QQBot.Enabled,
                host = config.QQBot.Host,
                port = config.QQBot.Port,
                accessToken = string.IsNullOrEmpty(config.QQBot.AccessToken) ? "(not set)" : "***",
                adminUsers = config.QQBot.AdminUsers.Count > 0 ? (object)config.QQBot.AdminUsers : "(none)",
                whitelistedUsers = config.QQBot.WhitelistedUsers.Count > 0 ? (object)config.QQBot.WhitelistedUsers : "(none)",
                whitelistedGroups = config.QQBot.WhitelistedGroups.Count > 0 ? (object)config.QQBot.WhitelistedGroups : "(none)",
                approvalTimeoutSeconds = config.QQBot.ApprovalTimeoutSeconds
            },
            // Security config
            security = new
            {
                blacklistedPaths = config.Security.BlacklistedPaths.Count > 0 ? (object)config.Security.BlacklistedPaths : "(none)"
            },
            // Heartbeat config
            heartbeat = new
            {
                enabled = config.Heartbeat.Enabled,
                intervalSeconds = config.Heartbeat.IntervalSeconds,
                notifyAdmin = config.Heartbeat.NotifyAdmin
            },
            // WeCom config
            weCom = new
            {
                enabled = config.WeCom.Enabled,
                webhookUrl = string.IsNullOrEmpty(config.WeCom.WebhookUrl) ? "(not set)" : "***"
            },
            // WeCom Bot config
            weComBot = new
            {
                enabled = config.WeComBot.Enabled,
                host = config.WeComBot.Host,
                port = config.WeComBot.Port,
                adminUsers = config.WeComBot.AdminUsers.Count > 0 ? (object)config.WeComBot.AdminUsers : "(none)",
                whitelistedUsers = config.WeComBot.WhitelistedUsers.Count > 0 ? (object)config.WeComBot.WhitelistedUsers : "(none)",
                whitelistedChats = config.WeComBot.WhitelistedChats.Count > 0 ? (object)config.WeComBot.WhitelistedChats : "(none)",
                approvalTimeoutSeconds = config.WeComBot.ApprovalTimeoutSeconds
            },
            // Cron config
            cron = new
            {
                enabled = config.Cron.Enabled,
                storePath = config.Cron.StorePath
            },
            // API config
            api = new
            {
                enabled = config.Api.Enabled,
                host = config.Api.Host,
                port = config.Api.Port,
                apiKey = string.IsNullOrEmpty(config.Api.ApiKey) ? "(not set)" : "***",
                autoApprove = config.Api.AutoApprove,
                approvalMode = config.Api.ApprovalMode,
                approvalTimeoutSeconds = config.Api.ApprovalTimeoutSeconds
            },
            enabledTools = config.EnabledTools.Count > 0
                ? (object)config.EnabledTools
                : "(all)",
            enabledToolsSource = config.EnabledTools.Count > 0
                ? "global"
                : "all",
            // Dashboard config
            dashBoard = new
            {
                enabled = config.DashBoard.Enabled,
                host = config.DashBoard.Host,
                port = config.DashBoard.Port,
                authEnabled = DashBoardAuth.IsEnabled(config),
                username = string.IsNullOrEmpty(config.DashBoard.Username) ? "(not set)" : "***",
                password = string.IsNullOrEmpty(config.DashBoard.Password) ? "(not set)" : "***"
            },
            // MCP servers
            mcpServers = config.McpServers.Select(m => new
            {
                name = m.Name,
                transport = m.Transport,
                enabled = m.Enabled
            })
        }, JsonOptions));

        // Config edit endpoints
        endpoints.MapGet("/dashboard/api/config/edit", () =>
        {
            var globalConfigPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".craft",
                "config.json");
            var workspaceConfigPath = System.IO.Path.Combine(paths.CraftPath, "config.json");

            var globalRaw = System.IO.File.Exists(globalConfigPath)
                ? System.IO.File.ReadAllText(globalConfigPath) : "{}";
            var workspaceRaw = System.IO.File.Exists(workspaceConfigPath)
                ? System.IO.File.ReadAllText(workspaceConfigPath) : "{}";

            var globalObj = (JsonObject)(JsonNode.Parse(globalRaw) ?? new JsonObject());
            var workspaceObj = (JsonObject)(JsonNode.Parse(workspaceRaw) ?? new JsonObject());

            // Compute merged result before masking
            var mergedObj = (JsonObject)MergeNodes(
                (JsonNode)JsonNode.Parse(globalRaw)! ?? new JsonObject(),
                (JsonNode)JsonNode.Parse(workspaceRaw)! ?? new JsonObject());

            // Mask all sensitive fields in all three views
            MaskSensitiveFields(globalObj);
            MaskSensitiveFields(workspaceObj);
            MaskSensitiveFields(mergedObj);

            return Results.Json(new
            {
                global = globalObj,
                workspace = workspaceObj,
                merged = mergedObj,
                globalPath = globalConfigPath,
                workspacePath = workspaceConfigPath
            }, RawJsonOptions);
        });

        endpoints.MapPost("/dashboard/api/config/workspace", async ctx =>
        {
            var workspaceConfigPath = System.IO.Path.Combine(paths.CraftPath, "config.json");
            var dir = System.IO.Path.GetDirectoryName(workspaceConfigPath);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);

            using var reader = new System.IO.StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();

            JsonObject postedObj;
            try
            {
                postedObj = (JsonObject)(JsonNode.Parse(body) ?? new JsonObject());
            }
            catch
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new { success = false, error = "Invalid JSON" });
                return;
            }

            // Read the existing file so we can preserve sentinel-masked sensitive values
            var existingObj = System.IO.File.Exists(workspaceConfigPath)
                ? (JsonObject)(JsonNode.Parse(System.IO.File.ReadAllText(workspaceConfigPath)) ?? new JsonObject())
                : new JsonObject();

            RestoreSentinels(postedObj, existingObj);

            var output = postedObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(workspaceConfigPath, output);
            await ctx.Response.WriteAsJsonAsync(new { success = true, path = workspaceConfigPath });
        });

        if (tokenUsageStore != null)
        {
            endpoints.MapGet("/dashboard/api/token-usage/summary", () =>
            {
                var summary = tokenUsageStore.GetSummary();
                return Results.Json(summary, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/token-usage/qq/private", () =>
            {
                var users = tokenUsageStore.GetQQPrivateUsers();
                var result = users.Select(u => new
                {
                    userId = u.UserId,
                    displayName = u.DisplayName,
                    totalInputTokens = u.TotalInputTokens,
                    totalOutputTokens = u.TotalOutputTokens,
                    totalTokens = u.TotalTokens,
                    requestCount = u.RequestCount,
                    lastActiveAt = u.LastActiveAt.ToString("o")
                });
                return Results.Json(result, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/token-usage/qq/groups", () =>
            {
                var groups = tokenUsageStore.GetQQGroups();
                var result = groups.Select(g => new
                {
                    groupId = g.GroupId,
                    groupName = g.GroupName,
                    totalInputTokens = g.TotalInputTokens,
                    totalOutputTokens = g.TotalOutputTokens,
                    totalTokens = g.TotalTokens,
                    totalRequestCount = g.TotalRequestCount,
                    lastActiveAt = g.LastActiveAt.ToString("o"),
                    users = g.Users.Values
                        .OrderByDescending(u => u.TotalTokens)
                        .Select(u => new
                        {
                            userId = u.UserId,
                            displayName = u.DisplayName,
                            totalInputTokens = u.TotalInputTokens,
                            totalOutputTokens = u.TotalOutputTokens,
                            totalTokens = u.TotalTokens,
                            requestCount = u.RequestCount,
                            lastActiveAt = u.LastActiveAt.ToString("o")
                        })
                });
                return Results.Json(result, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/token-usage/wecom", () =>
            {
                var users = tokenUsageStore.GetWeComUsers();
                var result = users.Select(u => new
                {
                    userId = u.UserId,
                    displayName = u.DisplayName,
                    totalInputTokens = u.TotalInputTokens,
                    totalOutputTokens = u.TotalOutputTokens,
                    totalTokens = u.TotalTokens,
                    requestCount = u.RequestCount,
                    lastActiveAt = u.LastActiveAt.ToString("o")
                });
                return Results.Json(result, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/token-usage/api", () =>
            {
                var users = tokenUsageStore.GetApiUsers();
                var result = users.Select(u => new
                {
                    userId = u.UserId,
                    displayName = u.DisplayName,
                    totalInputTokens = u.TotalInputTokens,
                    totalOutputTokens = u.TotalOutputTokens,
                    totalTokens = u.TotalTokens,
                    requestCount = u.RequestCount,
                    lastActiveAt = u.LastActiveAt.ToString("o")
                });
                return Results.Json(result, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/token-usage/cli", () =>
            {
                var users = tokenUsageStore.GetCliUsers();
                var result = users.Select(u => new
                {
                    userId = u.UserId,
                    displayName = u.DisplayName,
                    totalInputTokens = u.TotalInputTokens,
                    totalOutputTokens = u.TotalOutputTokens,
                    totalTokens = u.TotalTokens,
                    requestCount = u.RequestCount,
                    lastActiveAt = u.LastActiveAt.ToString("o")
                });
                return Results.Json(result, JsonOptions);
            });
        }

        endpoints.MapGet("/dashboard/api/events/stream", async ctx =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var cancellationToken = ctx.RequestAborted;
            var reader = traceStore.SseReader;

            try
            {
                await foreach (var evt in reader.ReadAllAsync(cancellationToken))
                {
                    var json = JsonSerializer.Serialize(evt, JsonOptions);
                    await ctx.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await ctx.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
            }
        });
    }

    private static JsonNode MergeNodes(JsonNode baseNode, JsonNode overrideNode)
    {
        if (overrideNode is JsonObject overrideObj && baseNode is JsonObject baseObj)
        {
            var result = JsonSerializer.Deserialize<JsonObject>(baseObj.ToJsonString()) ?? [];
            foreach (var property in overrideObj)
            {
                if (result.TryGetPropertyValue(property.Key, out var existingValue))
                    result[property.Key] = MergeNodes(existingValue ?? new JsonObject(), property.Value ?? new JsonObject());
                else
                    result[property.Key] = property.Value?.DeepClone();
            }
            return result;
        }
        return overrideNode.DeepClone();
    }

    // Sensitive field paths: each entry is [parent key(s)..., leaf key]
    private static readonly string[][] SensitivePaths =
    [
        ["ApiKey"],
        ["QQBot", "AccessToken"],
        ["Api", "ApiKey"],
        ["Tools", "Sandbox", "ApiKey"],
        ["WeCom", "WebhookUrl"],
        ["DashBoard", "Password"],
    ];

    private static void MaskSensitiveFields(JsonObject obj)
    {
        foreach (var path in SensitivePaths)
            MaskAtPath(obj, path, 0);
    }

    // Case-insensitive key lookup for JsonObject (config files may use camelCase or PascalCase)
    private static string? FindKey(JsonObject obj, string key) =>
        obj.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Key;

    private static void MaskAtPath(JsonObject obj, string[] path, int depth)
    {
        var actualKey = FindKey(obj, path[depth]);
        if (actualKey == null) return;

        if (depth == path.Length - 1)
        {
            // Leaf: mask if non-empty string
            if (obj[actualKey] is JsonValue val && val.ToString().Length > 0)
                obj[actualKey] = "***";
        }
        else
        {
            if (obj[actualKey] is JsonObject nested)
                MaskAtPath(nested, path, depth + 1);
        }
    }

    // For each sensitive path: if posted value is "***", restore from existing (or remove)
    private static void RestoreSentinels(JsonObject posted, JsonObject existing)
    {
        foreach (var path in SensitivePaths)
            RestoreAtPath(posted, existing, path, 0);
    }

    private static void RestoreAtPath(JsonObject posted, JsonObject existing, string[] path, int depth)
    {
        var postedKey = FindKey(posted, path[depth]);
        if (postedKey == null) return;

        if (depth == path.Length - 1)
        {
            if (posted[postedKey] is JsonValue val && val.ToString() == "***")
            {
                // Restore from existing, or remove the key if not present
                var existingKey = FindKey(existing, path[depth]);
                if (existingKey != null && existing[existingKey] is JsonNode existingVal)
                    posted[postedKey] = existingVal.DeepClone();
                else
                    posted.Remove(postedKey);
            }
        }
        else
        {
            if (posted[postedKey] is JsonObject postedNested)
            {
                var existingKey = FindKey(existing, path[depth]);
                var existingNested = (existingKey != null ? existing[existingKey] : null) as JsonObject ?? new JsonObject();
                RestoreAtPath(postedNested, existingNested, path, depth + 1);
            }
        }
    }
}
