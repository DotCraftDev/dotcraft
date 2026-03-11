using DotCraft.Commands.Custom;
using DotCraft.Commands.Handlers;

namespace DotCraft.Commands.Core;

/// <summary>
/// Dispatches commands to appropriate handlers.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _knownCommands = [];
    private CustomCommandLoader? _customCommandLoader;
    
    /// <summary>
    /// Gets all known command names.
    /// </summary>
    public IReadOnlyList<string> KnownCommands => _knownCommands;
    
    /// <summary>
    /// Registers a command handler.
    /// </summary>
    /// <param name="handler">The handler to register.</param>
    public void RegisterHandler(ICommandHandler handler)
    {
        foreach (var cmd in handler.Commands)
        {
            _handlers[cmd] = handler;
            if (!_knownCommands.Contains(cmd))
                _knownCommands.Add(cmd);
        }
    }
    
    /// <summary>
    /// Attempts to dispatch and handle a command.
    /// Returns a <see cref="CommandResult"/> so callers can check <see cref="CommandResult.ExpandedPrompt"/>
    /// for custom commands that need agent processing.
    /// </summary>
    public async Task<CommandResult> TryDispatchAsync(string rawText, CommandContext context, ICommandResponder responder)
    {
        var trimmedText = rawText.Trim();
        if (!trimmedText.StartsWith('/'))
            return CommandResult.NotHandled();
        
        // Parse command and arguments
        var parts = trimmedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1..] : [];
        
        // Update context with parsed values
        context = context with
        {
            RawText = rawText,
            Command = cmd,
            Arguments = args
        };
        
        // Try built-in handler first
        if (_handlers.TryGetValue(cmd, out var handler))
            return await handler.HandleAsync(context, responder);

        // Try custom commands
        if (_customCommandLoader != null)
        {
            var resolved = _customCommandLoader.TryResolve(trimmedText);
            if (resolved != null)
                return CommandResult.PromptExpansion(resolved.ExpandedPrompt);
        }
        
        // Unknown command - format helpful message
        var msg = CommandHelper.FormatUnknownCommandMessage(rawText, _knownCommands.ToArray());
        await responder.SendTextAsync(msg);
        return CommandResult.HandledResult();
    }
    
    /// <summary>
    /// Creates a default dispatcher with all built-in handlers registered.
    /// </summary>
    public static CommandDispatcher CreateDefault(CustomCommandLoader? customCommandLoader = null)
    {
        var dispatcher = new CommandDispatcher
        {
            _customCommandLoader = customCommandLoader
        };
        dispatcher.RegisterHandler(new NewCommandHandler());
        dispatcher.RegisterHandler(new DebugCommandHandler());
        dispatcher.RegisterHandler(new StopCommandHandler());
        dispatcher.RegisterHandler(new HelpCommandHandler());
        dispatcher.RegisterHandler(new HeartbeatCommandHandler());
        dispatcher.RegisterHandler(new CronCommandHandler());
        return dispatcher;
    }
}
