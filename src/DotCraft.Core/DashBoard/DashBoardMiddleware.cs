using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Protocol;
using DotCraft.Tracing;
using DotCraft.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    public static void MapDashBoard(
        this IEndpointRouteBuilder endpoints,
        TraceStore traceStore,
        DotCraftPaths paths,
        TokenUsageStore? tokenUsageStore = null,
        bool setupMode = false,
        IEnumerable<IOrchestratorSnapshotProvider>? orchestratorProviders = null,
        IEnumerable<Type>? configTypes = null,
        SessionPersistenceService? persistence = null,
        Func<string, CancellationToken, Task>? deleteThreadAsync = null,
        IDashBoardSessionHandler? sessionHandler = null,
        bool refreshTraceFromDiskBeforeRead = false)
    {
        var logger = endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("DashBoard");

        void RefreshTraceFromDiskIfEnabled()
        {
            if (refreshTraceFromDiskBeforeRead)
                traceStore.RefreshFromDisk();
        }

        MapOrchestratorEndpoints(endpoints, orchestratorProviders);

        // Build schema and derive sensitive paths from it at startup
        var schema = configTypes != null
            ? ConfigSchemaBuilder.BuildAll(configTypes)
            : [];
        var sensitivePaths = ConfigSchemaBuilder.BuildSensitivePaths(schema);
        endpoints.MapGet("/dashboard/", ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            return ctx.Response.WriteAsync(DashBoardFrontend.GetHtml());
        });

        // Config schema endpoint: returns the full dashboard config schema derived from
        // [ConfigSection] / [ConfigField] attributes on config classes across all modules.
        var capturedSchema = schema;
        endpoints.MapGet("/dashboard/api/config/schema", () =>
            Results.Json(capturedSchema, JsonOptions));

        endpoints.MapGet("/dashboard/api/summary", () =>
        {
            RefreshTraceFromDiskIfEnabled();
            var summary = traceStore.GetSummary();
            return Results.Json(summary, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions", () =>
        {
            RefreshTraceFromDiskIfEnabled();
            var sessions = traceStore.GetSessions();
            var descriptors = persistence?.DescribeSessionDeletions(sessions.Select(s => s.SessionKey))
                              ?? new Dictionary<string, TraceSessionDeletionDescriptor>(StringComparer.Ordinal);
            var result = sessions.Select(s =>
            {
                descriptors.TryGetValue(s.SessionKey, out var descriptor);
                return new
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
                    firstUserRequest = s.FirstUserRequest,
                    finalSystemPrompt = s.FinalSystemPrompt,
                    toolNames = s.ToolNames,
                    lastFinishReason = s.LastFinishReason,
                    rootThreadId = descriptor?.RootThreadId,
                    bindingKind = descriptor?.BindingKind ?? "unbound",
                    deletionScope = descriptor?.DeletionScope ?? SessionPersistenceDeletionScopes.TraceOnly
                };
            });
            return Results.Json(result, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions/{sessionKey}/events", (string sessionKey) =>
        {
            RefreshTraceFromDiskIfEnabled();
            var events = traceStore.GetEvents(sessionKey);
            return Results.Json(events, JsonOptions);
        });

        var capturedHandler = sessionHandler;
        endpoints.MapDelete("/dashboard/api/sessions/{sessionKey}", async (HttpContext http, string sessionKey) =>
        {
            RefreshTraceFromDiskIfEnabled();
            if (persistence != null)
            {
                var cascadeDeleted = await persistence.DeleteTraceSessionAsync(sessionKey, deleteThreadAsync, http.RequestAborted);
                return cascadeDeleted
                    ? Results.Json(new { deleted = true, sessionKey }, JsonOptions)
                    : Results.Json(new { deleted = false, sessionKey }, JsonOptions, statusCode: 404);
            }

            var deleted = traceStore.ClearSession(sessionKey);

            if (capturedHandler != null)
            {
                try
                {
                    await capturedHandler.DeleteThreadAsync(sessionKey);
                }
                catch (KeyNotFoundException)
                {
                    // Thread may not exist (e.g. tracing-only session without a persisted thread) — ignore.
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "DeleteThreadAsync failed for session {SessionKey}", sessionKey);
                }
            }

            return deleted
                ? Results.Json(new { deleted = true, sessionKey }, JsonOptions)
                : Results.Json(new { deleted = false, sessionKey }, JsonOptions, statusCode: 404);
        });

        endpoints.MapDelete("/dashboard/api/sessions", async (HttpContext http) =>
        {
            // Align with on-disk state when the dashboard process is not the trace producer (e.g. CLI + AppServer subprocess).
            RefreshTraceFromDiskIfEnabled();
            // Capture keys before clearing so we can delete the underlying threads.
            var sessionKeys = traceStore.GetSessions().Select(s => s.SessionKey).ToList();
            if (persistence != null)
            {
                await persistence.DeleteTraceSessionsAsync(sessionKeys, deleteThreadAsync, http.RequestAborted);
                persistence.CompactStateIfWorthwhile();
                return Results.Json(new { cleared = true }, JsonOptions);
            }

            traceStore.ClearAll();

            if (capturedHandler != null)
            {
                try
                {
                    await capturedHandler.DeleteAllThreadsAsync(sessionKeys);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "DeleteAllThreadsAsync failed after clearing traces");
                }
            }

            return Results.Json(new { cleared = true }, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/tools", () =>
        {
            var icons = ToolRegistry.GetAllToolIcons();
            var tools = icons.Select(kv => new { name = kv.Key, icon = kv.Value });
            return Results.Json(new { tools }, JsonOptions);
        });

        // Config edit endpoints
        endpoints.MapGet("/dashboard/api/config/edit", () =>
        {
            var globalConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".craft",
                "config.json");
            var workspaceConfigPath = Path.Combine(paths.CraftPath, "config.json");

            var globalRaw = File.Exists(globalConfigPath)
                ? File.ReadAllText(globalConfigPath) : "{}";
            var workspaceRaw = File.Exists(workspaceConfigPath)
                ? File.ReadAllText(workspaceConfigPath) : "{}";

            var globalObj = (JsonObject)(JsonNode.Parse(globalRaw) ?? new JsonObject());
            var workspaceObj = (JsonObject)(JsonNode.Parse(workspaceRaw) ?? new JsonObject());

            // Compute merged result before masking
            var mergedObj = (JsonObject)MergeNodes(
                JsonNode.Parse(globalRaw) ?? new JsonObject(),
                JsonNode.Parse(workspaceRaw) ?? new JsonObject());

            bool hasApiKey = false;
            var apiKeyKey = FindKey(mergedObj, "ApiKey");
            if (apiKeyKey != null && mergedObj[apiKeyKey] is JsonValue apiKeyVal)
                hasApiKey = !string.IsNullOrWhiteSpace(apiKeyVal.ToString());

            // Mask all sensitive fields in all three views
            MaskSensitiveFields(globalObj, sensitivePaths);
            MaskSensitiveFields(workspaceObj, sensitivePaths);
            MaskSensitiveFields(mergedObj, sensitivePaths);

            // Check if authentication is enabled (Username and Password configured)
            bool authEnabled = false;
            if (mergedObj.TryGetPropertyValue("DashBoard", out var dashBoardNode) && dashBoardNode is JsonObject dashBoardObj)
            {
                var usernameKey = FindKey(dashBoardObj, "Username");
                var passwordKey = FindKey(dashBoardObj, "Password");
                authEnabled = usernameKey != null && passwordKey != null &&
                              dashBoardObj[usernameKey] is JsonValue usernameVal && usernameVal.ToString().Length > 0 &&
                              dashBoardObj[passwordKey] is JsonValue passwordVal && passwordVal.ToString().Length > 0;
            }

            return Results.Json(new
            {
                global = globalObj,
                workspace = workspaceObj,
                merged = mergedObj,
                globalPath = globalConfigPath,
                workspacePath = workspaceConfigPath,
                authEnabled,
                setupMode,
                hasApiKey,
                canEditGlobal = setupMode
            }, RawJsonOptions);
        });

        if (setupMode)
        {
            endpoints.MapPost("/dashboard/api/config/global", async ctx =>
            {
                var globalConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".craft",
                    "config.json");

                await SaveConfigAsync(ctx, globalConfigPath, sensitivePaths, logger);
            });
        }

        endpoints.MapPost("/dashboard/api/config/workspace", async ctx =>
        {
            var workspaceConfigPath = Path.Combine(paths.CraftPath, "config.json");
            await SaveConfigAsync(ctx, workspaceConfigPath, sensitivePaths, logger);
        });

        endpoints.MapGet("/dashboard/api/config/models", async (HttpContext ctx) =>
        {
            var workspaceConfigPath = Path.Combine(paths.CraftPath, "config.json");
            var config = ctx.RequestServices.GetService<IAppConfigMonitor>()?.Current
                ?? AppConfig.LoadWithGlobalFallback(workspaceConfigPath);
            var provider = ctx.RequestServices.GetService<OpenAIClientProvider>();
            var result = await OpenAIModelCatalog.FetchAsync(config, ctx.RequestAborted, provider);

            if (!result.Success)
            {
                return Results.Json(new
                {
                    success = false,
                    errorCode = result.ErrorCode.ToString(),
                    errorMessage = result.ErrorMessage,
                    models = Array.Empty<OpenAIModelCatalogEntry>()
                }, RawJsonOptions, statusCode: MapModelCatalogStatusCode(result.ErrorCode));
            }

            return Results.Json(new
            {
                success = true,
                models = result.Models
            }, RawJsonOptions);
        });

        if (tokenUsageStore != null)
        {
            endpoints.MapGet("/dashboard/api/usage/sources", () =>
            {
                var summaries = tokenUsageStore.GetSourceSummaries().Select(summary => new
                {
                    sourceId = summary.SourceId,
                    sourceMode = summary.SourceMode,
                    subjectKind = summary.SubjectKind,
                    contextKind = summary.ContextKind,
                    subjectCount = summary.SubjectCount,
                    contextCount = summary.ContextCount,
                    requestCount = summary.RequestCount,
                    totalInputTokens = summary.TotalInputTokens,
                    totalOutputTokens = summary.TotalOutputTokens,
                    totalTokens = summary.TotalTokens,
                    lastActiveAt = summary.LastActiveAt.ToString("o")
                });
                return Results.Json(summaries, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/usage/sources/{sourceId}/subjects", (string sourceId) =>
            {
                var entries = tokenUsageStore.GetSubjectBreakdown(sourceId).Select(entry => new
                {
                    kind = entry.Kind,
                    id = entry.Id,
                    label = entry.Label,
                    requestCount = entry.RequestCount,
                    relatedSubjectCount = entry.RelatedSubjectCount,
                    totalInputTokens = entry.TotalInputTokens,
                    totalOutputTokens = entry.TotalOutputTokens,
                    totalTokens = entry.TotalTokens,
                    lastActiveAt = entry.LastActiveAt.ToString("o")
                });
                return Results.Json(entries, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/usage/sources/{sourceId}/contexts", (string sourceId) =>
            {
                var entries = tokenUsageStore.GetContextBreakdown(sourceId).Select(entry => new
                {
                    kind = entry.Kind,
                    id = entry.Id,
                    label = entry.Label,
                    requestCount = entry.RequestCount,
                    relatedSubjectCount = entry.RelatedSubjectCount,
                    totalInputTokens = entry.TotalInputTokens,
                    totalOutputTokens = entry.TotalOutputTokens,
                    totalTokens = entry.TotalTokens,
                    lastActiveAt = entry.LastActiveAt.ToString("o")
                });
                return Results.Json(entries, JsonOptions);
            });
        }

        endpoints.MapGet("/dashboard/api/events/stream", async ctx =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var lifetime = ctx.RequestServices.GetRequiredService<IHostApplicationLifetime>();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                ctx.RequestAborted, lifetime.ApplicationStopping);
            var cancellationToken = cts.Token;
            var reader = traceStore.SseReader;

            logger?.LogDebug("Dashboard SSE client connected");
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
                // Client disconnected or server shutting down
            }
            finally
            {
                logger?.LogDebug("Dashboard SSE client disconnected");
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

    private static async Task SaveConfigAsync(
        HttpContext ctx,
        string targetPath,
        string[][] sensitivePaths,
        ILogger? logger)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();

        JsonObject postedObj;
        try
        {
            postedObj = (JsonObject)(JsonNode.Parse(body) ?? new JsonObject());
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Dashboard config save rejected: invalid JSON body");
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { success = false, error = "Invalid JSON" });
            return;
        }

        var existingObj = File.Exists(targetPath)
            ? (JsonObject)(JsonNode.Parse(await File.ReadAllTextAsync(targetPath)) ?? new JsonObject())
            : new JsonObject();

        RestoreSentinels(postedObj, existingObj, sensitivePaths);

        var output = postedObj.ToJsonString(RawJsonOptions);
        await File.WriteAllTextAsync(targetPath, output);
        logger?.LogInformation("Dashboard config saved to {Path}", targetPath);
        await ctx.Response.WriteAsJsonAsync(new { success = true, path = targetPath });
    }

    private static void MaskSensitiveFields(JsonObject obj, string[][] sensitivePaths)
    {
        foreach (var path in sensitivePaths)
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
    private static void RestoreSentinels(JsonObject posted, JsonObject existing, string[][] sensitivePaths)
    {
        foreach (var path in sensitivePaths)
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

    private static void MapOrchestratorEndpoints(
        IEndpointRouteBuilder endpoints,
        IEnumerable<IOrchestratorSnapshotProvider>? providers)
    {
        if (providers == null) return;

        foreach (var provider in providers)
        {
            var captured = provider;

            endpoints.MapGet($"/dashboard/api/orchestrators/{captured.Name}/state", () =>
            {
                var snapshot = captured.GetSnapshot();
                return Results.Json(snapshot, JsonOptions);
            });

            endpoints.MapPost($"/dashboard/api/orchestrators/{captured.Name}/refresh", () =>
            {
                captured.TriggerRefresh();
                return Results.Json(new { triggered = true, name = captured.Name }, JsonOptions);
            });
        }
    }

    private static int MapModelCatalogStatusCode(OpenAIModelCatalogErrorCode code) => code switch
    {
        OpenAIModelCatalogErrorCode.MissingApiKey => StatusCodes.Status400BadRequest,
        OpenAIModelCatalogErrorCode.InvalidEndpoint => StatusCodes.Status400BadRequest,
        OpenAIModelCatalogErrorCode.Unauthorized => StatusCodes.Status401Unauthorized,
        OpenAIModelCatalogErrorCode.Forbidden => StatusCodes.Status403Forbidden,
        OpenAIModelCatalogErrorCode.EndpointNotSupported => StatusCodes.Status404NotFound,
        OpenAIModelCatalogErrorCode.Timeout => StatusCodes.Status504GatewayTimeout,
        OpenAIModelCatalogErrorCode.Network => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError
    };
}
