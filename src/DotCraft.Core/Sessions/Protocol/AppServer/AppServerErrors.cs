using System.Text.Json.Serialization;

namespace DotCraft.Sessions.Protocol.AppServer;

/// <summary>
/// JSON-RPC 2.0 error object for outbound error responses.
/// </summary>
public sealed class AppServerError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }
}

/// <summary>
/// Exception thrown by AppServer request handlers to produce a JSON-RPC error response.
/// The handler catches this and serializes it as a JSON-RPC error.
/// </summary>
public sealed class AppServerException(int code, string message, object? errorData = null) : Exception(message)
{
    public int Code { get; } = code;

    public object? ErrorData { get; } = errorData;

    public AppServerError ToError() => new() { Code = Code, Message = Message, Data = ErrorData };
}

/// <summary>
/// Error code constants and factory methods from spec Section 8.2 and 8.3.
/// </summary>
public static class AppServerErrors
{
    // ── JSON-RPC standard codes (Section 8.2) ──

    public const int ParseErrorCode = -32700;
    public const int InvalidRequestCode = -32600;
    public const int MethodNotFoundCode = -32601;
    public const int InvalidParamsCode = -32602;
    public const int InternalErrorCode = -32603;

    // ── DotCraft-specific codes (Section 8.3) ──

    public const int ServerOverloadedCode = -32001;
    public const int NotInitializedCode = -32002;
    public const int AlreadyInitializedCode = -32003;
    public const int ThreadNotFoundCode = -32010;
    public const int ThreadNotActiveCode = -32011;
    public const int TurnInProgressCode = -32012;
    public const int TurnNotFoundCode = -32013;
    public const int TurnNotRunningCode = -32014;
    public const int ApprovalTimeoutCode = -32020;

    // ── Factory methods ──

    public static AppServerException ParseError(string? detail = null) =>
        new(ParseErrorCode, "Parse error", detail is null ? null : new { detail });

    public static AppServerException InvalidRequest(string detail) =>
        new(InvalidRequestCode, "Invalid request", new { detail });

    public static AppServerException MethodNotFound(string method) =>
        new(MethodNotFoundCode, $"Method not found: {method}");

    public static AppServerException InvalidParams(string detail) =>
        new(InvalidParamsCode, "Invalid params", new { detail });

    public static AppServerException InternalError(string detail) =>
        new(InternalErrorCode, "Internal error", new { detail });

    public static AppServerException ServerOverloaded() =>
        new(ServerOverloadedCode, "Server overloaded; retry later.");

    public static AppServerException NotInitialized() =>
        new(NotInitializedCode, "Not initialized");

    public static AppServerException AlreadyInitialized() =>
        new(AlreadyInitializedCode, "Already initialized");

    public static AppServerException ThreadNotFound(string threadId) =>
        new(ThreadNotFoundCode, $"Thread not found: {threadId}");

    public static AppServerException ThreadNotActive(string threadId) =>
        new(ThreadNotActiveCode, $"Thread is not active: {threadId}");

    public static AppServerException TurnInProgress(string threadId) =>
        new(TurnInProgressCode, $"A turn is already in progress on thread: {threadId}");

    public static AppServerException TurnNotFound(string turnId) =>
        new(TurnNotFoundCode, $"Turn not found: {turnId}");

    public static AppServerException TurnNotRunning(string turnId) =>
        new(TurnNotRunningCode, $"Turn is not running: {turnId}");

    public static AppServerException ApprovalTimeout() =>
        new(ApprovalTimeoutCode, "Approval request timed out");
}
