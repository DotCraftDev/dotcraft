namespace DotCraft.GitHubTracker.Tests.Fakes;

/// <summary>
/// A delegating handler that returns preconfigured JSON responses for adapter-level tests.
/// Register responses by URL prefix using <see cref="AddResponse"/>.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(string UrlContains, HttpResponseMessage Response)> _responses = [];

    public void AddResponse(string urlContains, string jsonBody, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        _responses.Add((urlContains, new HttpResponseMessage(status)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
        }));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";
        foreach (var (contains, response) in _responses)
        {
            if (url.Contains(contains, StringComparison.OrdinalIgnoreCase))
            {
                // Clone the response so it can be consumed multiple times.
                var clone = new HttpResponseMessage(response.StatusCode)
                {
                    Content = new StringContent(
                        response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult(),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
                return Task.FromResult(clone);
            }
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        });
    }
}
