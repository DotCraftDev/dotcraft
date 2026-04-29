using DotCraft.CLI;

namespace DotCraft.Tests.CLI;

public sealed class CommandLineArgsTests
{
    [Fact]
    public void Parse_DefaultArgs_UsesCliMode()
    {
        var args = CommandLineArgs.Parse([]);

        Assert.Equal(CommandLineArgs.RunMode.Cli, args.Mode);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_GatewaySubcommand_UsesGatewayMode()
    {
        var args = CommandLineArgs.Parse(["gateway"]);

        Assert.Equal(CommandLineArgs.RunMode.Gateway, args.Mode);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_HubSubcommand_UsesHubMode()
    {
        var args = CommandLineArgs.Parse(["hub"]);

        Assert.Equal(CommandLineArgs.RunMode.Hub, args.Mode);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_AppServerSubcommand_UsesAppServerMode()
    {
        var args = CommandLineArgs.Parse(["app-server", "--listen", "ws://127.0.0.1:9100"]);

        Assert.Equal(CommandLineArgs.RunMode.AppServer, args.Mode);
        Assert.Equal("ws://127.0.0.1:9100", args.ListenUrl);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_SetupSubcommand_ParsesSetupFlags()
    {
        var args = CommandLineArgs.Parse([
            "setup",
            "--language", "English",
            "--model", "gpt-4o-mini",
            "--endpoint", "https://api.openai.com/v1",
            "--api-key", "sk-test",
            "--profile", "developer",
            "--save-user-config",
            "--prefer-existing-user-config"
        ]);

        Assert.Equal(CommandLineArgs.RunMode.Setup, args.Mode);
        Assert.Equal("English", args.SetupLanguage);
        Assert.Equal("gpt-4o-mini", args.SetupModel);
        Assert.Equal("https://api.openai.com/v1", args.SetupEndPoint);
        Assert.Equal("sk-test", args.SetupApiKey);
        Assert.Equal("developer", args.SetupProfile);
        Assert.True(args.SaveUserConfig);
        Assert.True(args.PreferExistingUserConfig);
        Assert.False(args.ReservesStdout);
    }
}
