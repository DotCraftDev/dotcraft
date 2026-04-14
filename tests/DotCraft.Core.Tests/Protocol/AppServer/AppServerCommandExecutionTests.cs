using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for command/* methods that mutate thread state.
/// </summary>
public sealed class AppServerCommandExecutionTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerCommandExecutionTests()
    {
        _h.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task CommandExecute_New_ReturnsSessionResetPayloadAndFreshThread()
    {
        var existing = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.CommandExecute, new
        {
            threadId = existing.Id,
            command = "/new"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("handled").GetBoolean());
        Assert.True(result.GetProperty("sessionReset").GetBoolean());
        Assert.True(result.GetProperty("createdLazily").GetBoolean());

        var archived = result.GetProperty("archivedThreadIds");
        Assert.Contains(archived.EnumerateArray().Select(e => e.GetString()), id => id == existing.Id);

        var newThread = result.GetProperty("thread");
        var newThreadId = newThread.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newThreadId));
        Assert.NotEqual(existing.Id, newThreadId);
        Assert.Equal("active", newThread.GetProperty("status").GetString());

        var oldThread = await _h.Service.GetThreadAsync(existing.Id);
        Assert.Equal(ThreadStatus.Archived, oldThread.Status);
    }
}
