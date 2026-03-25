using DotCraft.Cron;

namespace DotCraft.Tests.Cron;

public class CronServiceEnableTests : IDisposable
{
    private readonly string _path;

    public CronServiceEnableTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"dotcraft_cron_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* ignore */ }
    }

    [Fact]
    public void EnableJob_PreservesFutureNextRunAtMs()
    {
        var future = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();
        var json =
            $$"""{"Version":1,"Jobs":[{"Id":"abc12345","Name":"t","Enabled":true,"Schedule":{"Kind":"every","EveryMs":3600000},"Payload":{"Message":"x","Deliver":false},"State":{"NextRunAtMs":{{future}},"LastRunAtMs":null},"CreatedAtMs":1,"DeleteAfterRun":false}]}""";
        File.WriteAllText(_path, json);

        using var svc = new CronService(_path);
        const string id = "abc12345";

        svc.EnableJob(id, false);
        Assert.False(svc.ListJobs(true)[0].Enabled);

        svc.EnableJob(id, true);
        Assert.Equal(future, svc.ListJobs(true)[0].State.NextRunAtMs);
    }

    [Fact]
    public void EnableJob_RecomputesWhenNextRunInPast()
    {
        var past = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var json =
            $$"""{"Version":1,"Jobs":[{"Id":"abc12345","Name":"t","Enabled":false,"Schedule":{"Kind":"every","EveryMs":60000},"Payload":{"Message":"x","Deliver":false},"State":{"NextRunAtMs":{{past}},"LastRunAtMs":null},"CreatedAtMs":1,"DeleteAfterRun":false}]}""";
        File.WriteAllText(_path, json);

        using var svc = new CronService(_path);
        const string id = "abc12345";

        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        svc.EnableJob(id, true);
        var next = svc.ListJobs(true)[0].State.NextRunAtMs;
        Assert.NotNull(next);
        Assert.InRange(next.Value, before + 59_000, before + 120_000);
    }
}
