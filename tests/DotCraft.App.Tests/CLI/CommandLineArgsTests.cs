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
    public void Parse_AppServerSubcommand_UsesAppServerMode()
    {
        var args = CommandLineArgs.Parse(["app-server", "--listen", "ws://127.0.0.1:9100"]);

        Assert.Equal(CommandLineArgs.RunMode.AppServer, args.Mode);
        Assert.Equal("ws://127.0.0.1:9100", args.ListenUrl);
        Assert.False(args.ReservesStdout);
    }
}
