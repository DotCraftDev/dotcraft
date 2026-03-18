using System.Text.Json;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for the external channel adapter protocol extensions:
/// - channelAdapter capability in initialize handshake
/// - AppServerConnection channel adapter state tracking
/// - ext/channel/* method constants
/// - ChannelRejected error code
/// </summary>
public sealed class ExternalChannelProtocolTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // channelAdapter capability in initialize
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_WithChannelAdapter_SetsConnectionState()
    {
        // Arrange: build an initialize request with channelAdapter capability
        var initMsg = InMemoryTransport.BuildRequest(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = "telegram-adapter", version = "1.0.0" },
            capabilities = new
            {
                approvalSupport = true,
                streamingSupport = true,
                channelAdapter = new
                {
                    channelName = "telegram",
                    deliverySupport = true
                }
            }
        });

        // Act
        await _h.ExecuteRequestAsync(initMsg);

        // Assert: read the response the handler wrote to the transport
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        // Assert: connection state reflects channel adapter
        Assert.True(_h.Connection.IsInitialized);
        Assert.True(_h.Connection.IsChannelAdapter);
        Assert.Equal("telegram", _h.Connection.ChannelAdapterName);
        Assert.True(_h.Connection.SupportsDelivery);
    }

    [Fact]
    public async Task Initialize_WithChannelAdapter_DeliverySupportFalse()
    {
        var initMsg = InMemoryTransport.BuildRequest(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = "readonly-adapter", version = "1.0.0" },
            capabilities = new
            {
                channelAdapter = new
                {
                    channelName = "readonly-channel",
                    deliverySupport = false
                }
            }
        });

        await _h.ExecuteRequestAsync(initMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(_h.Connection.IsChannelAdapter);
        Assert.Equal("readonly-channel", _h.Connection.ChannelAdapterName);
        Assert.False(_h.Connection.SupportsDelivery);
    }

    [Fact]
    public async Task Initialize_WithChannelAdapter_DeliverySupportDefaultTrue()
    {
        // When deliverySupport is not specified, it should default to true
        var initMsg = InMemoryTransport.BuildRequest(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = "simple-adapter", version = "1.0.0" },
            capabilities = new
            {
                channelAdapter = new
                {
                    channelName = "simple"
                }
            }
        });

        await _h.ExecuteRequestAsync(initMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(_h.Connection.IsChannelAdapter);
        Assert.True(_h.Connection.SupportsDelivery);
    }

    [Fact]
    public async Task Initialize_WithoutChannelAdapter_ConnectionIsNotAdapter()
    {
        // Regular client without channelAdapter
        await _h.InitializeAsync();

        Assert.False(_h.Connection.IsChannelAdapter);
        Assert.Null(_h.Connection.ChannelAdapterName);
        Assert.True(_h.Connection.SupportsDelivery); // default
    }

    // -------------------------------------------------------------------------
    // Protocol constants
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtChannelMethods_HaveCorrectValues()
    {
        Assert.Equal("ext/channel/deliver", AppServerMethods.ExtChannelDeliver);
        Assert.Equal("ext/channel/heartbeat", AppServerMethods.ExtChannelHeartbeat);
    }

    [Fact]
    public void ChannelRejectedErrorCode_HasCorrectValue()
    {
        Assert.Equal(-32030, AppServerErrors.ChannelRejectedCode);
    }

    [Fact]
    public void ChannelRejectedError_ContainsChannelName()
    {
        var ex = AppServerErrors.ChannelRejected("test-channel");
        Assert.Equal(AppServerErrors.ChannelRejectedCode, ex.Code);
        Assert.Contains("test-channel", ex.Message);
    }

    // -------------------------------------------------------------------------
    // ChannelAdapterCapability serialization
    // -------------------------------------------------------------------------

    [Fact]
    public void ChannelAdapterCapability_RoundTrips()
    {
        var cap = new ChannelAdapterCapability
        {
            ChannelName = "discord",
            DeliverySupport = true
        };

        var json = JsonSerializer.Serialize(cap, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var deserialized = JsonSerializer.Deserialize<ChannelAdapterCapability>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(deserialized);
        Assert.Equal("discord", deserialized.ChannelName);
        Assert.True(deserialized.DeliverySupport);
    }

    [Fact]
    public void AppServerClientCapabilities_WithChannelAdapter_RoundTrips()
    {
        var caps = new AppServerClientCapabilities
        {
            ApprovalSupport = true,
            StreamingSupport = true,
            ChannelAdapter = new ChannelAdapterCapability
            {
                ChannelName = "telegram",
                DeliverySupport = false
            }
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(caps, options);
        var deserialized = JsonSerializer.Deserialize<AppServerClientCapabilities>(json, options);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.ChannelAdapter);
        Assert.Equal("telegram", deserialized.ChannelAdapter.ChannelName);
        Assert.False(deserialized.ChannelAdapter.DeliverySupport);
    }

    [Fact]
    public void AppServerClientCapabilities_WithoutChannelAdapter_OmitsInJson()
    {
        var caps = new AppServerClientCapabilities
        {
            ApprovalSupport = true,
            ChannelAdapter = null
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(caps, options);

        // channelAdapter should not appear in JSON when null
        Assert.DoesNotContain("channelAdapter", json);
    }
}
