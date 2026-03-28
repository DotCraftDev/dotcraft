using DotCraft.Acp;

namespace DotCraft.Tests.Acp;

/// <summary>
/// Unit tests for <see cref="AcpBridgeHandler"/> capability mapping (ACP client → wire <c>acpExtensions</c>).
/// </summary>
public sealed class AcpBridgeHandlerCapabilityTests
{
    [Fact]
    public void BuildAcpExtensionCapability_Null_ReturnsNull()
    {
        Assert.Null(AcpBridgeHandler.BuildAcpExtensionCapability(null));
    }

    [Fact]
    public void BuildAcpExtensionCapability_EmptyCaps_ReturnsNull()
    {
        var caps = new ClientCapabilities();
        Assert.Null(AcpBridgeHandler.BuildAcpExtensionCapability(caps));
    }

    [Fact]
    public void BuildAcpExtensionCapability_FsRead_Maps()
    {
        var caps = new ClientCapabilities { Fs = new FsCapabilities { ReadTextFile = true } };
        var ext = AcpBridgeHandler.BuildAcpExtensionCapability(caps);
        Assert.NotNull(ext);
        Assert.True(ext!.FsReadTextFile);
        Assert.Null(ext.FsWriteTextFile);
    }

    [Fact]
    public void BuildAcpExtensionCapability_FsWrite_Maps()
    {
        var caps = new ClientCapabilities { Fs = new FsCapabilities { WriteTextFile = true } };
        var ext = AcpBridgeHandler.BuildAcpExtensionCapability(caps);
        Assert.NotNull(ext);
        Assert.True(ext!.FsWriteTextFile);
    }

    [Fact]
    public void BuildAcpExtensionCapability_Terminal_Maps()
    {
        var caps = new ClientCapabilities { Terminal = new TerminalCapabilities { Create = true } };
        var ext = AcpBridgeHandler.BuildAcpExtensionCapability(caps);
        Assert.NotNull(ext);
        Assert.True(ext!.TerminalCreate);
    }

    [Fact]
    public void BuildAcpExtensionCapability_CustomExtensions_Maps()
    {
        var caps = new ClientCapabilities { Extensions = ["_unity", "foo"] };
        var ext = AcpBridgeHandler.BuildAcpExtensionCapability(caps);
        Assert.NotNull(ext);
        Assert.NotNull(ext!.Extensions);
        Assert.Equal(["_unity", "foo"], ext.Extensions);
    }
}
