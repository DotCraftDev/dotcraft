using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Editor.Protocol
{
    #region JSON-RPC 2.0 Base Types

    /// <summary>
    /// Represents a JSON-RPC 2.0 request.
    /// </summary>
    public sealed class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public JsonElement? Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; }

        [JsonIgnore]
        public bool IsNotification => Id is null || Id.Value.ValueKind == JsonValueKind.Undefined;
    }

    /// <summary>
    /// Represents a JSON-RPC 2.0 response.
    /// </summary>
    public sealed class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public JsonElement? Id { get; set; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Result { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonRpcError Error { get; set; }
    }

    /// <summary>
    /// Represents a JSON-RPC 2.0 error.
    /// </summary>
    public sealed class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Data { get; set; }
    }

    /// <summary>
    /// Represents a JSON-RPC 2.0 notification (no id, no response expected).
    /// </summary>
    public sealed class JsonRpcNotification
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Params { get; set; }
    }

    #endregion
}
