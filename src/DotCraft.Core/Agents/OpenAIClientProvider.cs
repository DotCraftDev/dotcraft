using System.ClientModel;
using System.Collections.Concurrent;
using DotCraft.Configuration;
using OpenAI;
using OpenAI.Chat;

namespace DotCraft.Agents;

/// <summary>
/// Centralizes OpenAI-compatible client creation and model resolution for DotCraft runtime paths.
/// </summary>
public sealed class OpenAIClientProvider
{
    private readonly ConcurrentDictionary<ClientKey, OpenAIClient> _clients = new();
    private readonly ConcurrentDictionary<ChatClientKey, ChatClient> _chatClients = new();

    /// <summary>
    /// Resolves the effective MainAgent model for a workspace or thread.
    /// </summary>
    public string ResolveMainModel(AppConfig config, string? modelOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var model = string.IsNullOrWhiteSpace(modelOverride) ? config.Model : modelOverride;
        return NormalizeRequiredModel(model);
    }

    /// <summary>
    /// Resolves the effective native SubAgent model for the current thread context.
    /// </summary>
    public string ResolveSubAgentModel(AppConfig config, string effectiveMainModel)
    {
        ArgumentNullException.ThrowIfNull(config);

        var subAgentModel = config.SubAgent.Model;
        return string.IsNullOrWhiteSpace(subAgentModel)
            ? NormalizeRequiredModel(effectiveMainModel)
            : subAgentModel.Trim();
    }

    /// <summary>
    /// Resolves the effective memory consolidation model.
    /// </summary>
    public string ResolveConsolidationModel(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return string.IsNullOrWhiteSpace(config.ConsolidationModel)
            ? ResolveMainModel(config)
            : config.ConsolidationModel.Trim();
    }

    /// <summary>
    /// Gets a cached chat client for the workspace's effective MainAgent model.
    /// </summary>
    public ChatClient GetMainChatClient(AppConfig config, string? modelOverride = null) =>
        GetChatClient(config, ResolveMainModel(config, modelOverride));

    /// <summary>
    /// Gets a cached chat client for the effective native SubAgent model.
    /// </summary>
    public ChatClient GetSubAgentChatClient(AppConfig config, string effectiveMainModel) =>
        GetChatClient(config, ResolveSubAgentModel(config, effectiveMainModel));

    /// <summary>
    /// Gets a cached chat client for the effective memory consolidation model.
    /// </summary>
    public ChatClient GetConsolidationChatClient(AppConfig config) =>
        GetChatClient(config, ResolveConsolidationModel(config));

    /// <summary>
    /// Gets a cached chat client for a specific model.
    /// </summary>
    public ChatClient GetChatClient(AppConfig config, string model)
    {
        ArgumentNullException.ThrowIfNull(config);

        var normalizedModel = NormalizeRequiredModel(model);
        var key = ChatClientKey.From(config, normalizedModel);
        return _chatClients.GetOrAdd(key, static (chatKey, provider) =>
            provider.GetOpenAIClient(chatKey.Client).GetChatClient(chatKey.Model), this);
    }

    /// <summary>
    /// Tries to get a cached chat client for a specific model without throwing on invalid configuration.
    /// </summary>
    public bool TryGetChatClient(AppConfig config, string model, out ChatClient? chatClient)
    {
        try
        {
            chatClient = GetChatClient(config, model);
            return true;
        }
        catch (ArgumentException)
        {
            chatClient = null;
            return false;
        }
        catch (UriFormatException)
        {
            chatClient = null;
            return false;
        }
    }

    /// <summary>
    /// Gets a cached OpenAI-compatible client for the workspace endpoint and API key.
    /// </summary>
    public OpenAIClient GetOpenAIClient(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return GetOpenAIClient(ClientKey.From(config));
    }

    private OpenAIClient GetOpenAIClient(ClientKey key) =>
        _clients.GetOrAdd(key, static clientKey =>
            new OpenAIClient(
                new ApiKeyCredential(clientKey.ApiKey),
                new OpenAIClientOptions { Endpoint = clientKey.Endpoint }));

    private static string NormalizeRequiredModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));

        return model.Trim();
    }

    private readonly record struct ClientKey(Uri Endpoint, string ApiKey)
    {
        public static ClientKey From(AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.ApiKey))
                throw new ArgumentException("API key must be configured.", nameof(config));

            if (!Uri.TryCreate(config.EndPoint, UriKind.Absolute, out var endpoint))
                throw new ArgumentException("Endpoint must be an absolute URI.", nameof(config));

            return new ClientKey(endpoint, config.ApiKey);
        }
    }

    private readonly record struct ChatClientKey(ClientKey Client, string Model)
    {
        public static ChatClientKey From(AppConfig config, string model) =>
            new(ClientKey.From(config), model);
    }
}
