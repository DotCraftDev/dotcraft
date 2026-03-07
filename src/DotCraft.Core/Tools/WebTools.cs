using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Configuration;

namespace DotCraft.Tools;

/// <summary>
/// Web tools: web_search and web_fetch.
/// </summary>
public sealed class WebTools
{
    private readonly HttpClient _httpClient;
    
    private readonly int _maxChars;
    
    private readonly int _timeoutSeconds;

    private readonly int _searchMaxResults;

    private readonly string _searchProvider;

    private const string UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_7_2) AppleWebKit/537.36";

    private const string BingUrl = "https://www.bing.com/search";

    private const string ExaMcpUrl = "https://mcp.exa.ai/mcp";

    public WebTools(
        int maxChars = 50000,
        int timeoutSeconds = 30,
        int searchMaxResults = 5,
        string searchProvider = WebSearchProvider.Exa)
    {
        _maxChars = maxChars;
        _timeoutSeconds = timeoutSeconds;
        _searchMaxResults = searchMaxResults;
        _searchProvider = searchProvider;
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    /// <summary>
    /// Search the web and return results.
    /// Supports multiple providers: Bing (globally accessible), Exa (AI-optimized, free via MCP).
    /// </summary>
    [Description("Search the web for current information. Returns titles, URLs, and snippets.")]
    [Tool(Icon = "🔍", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.WebSearch))]
    public async Task<string> WebSearch(
        [Description("The search query.")] string query,
        [Description("Maximum number of results to return (1-10).")] int? maxResults = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonSerializer.Serialize(new { error = "Query cannot be empty." });
        }

        var count = Math.Clamp(maxResults ?? _searchMaxResults, 1, 10);

        try
        {
            List<SearchResult> results;

            if (_searchProvider.Equals(WebSearchProvider.Exa, StringComparison.OrdinalIgnoreCase))
            {
                return await SearchExa(query, count);
            }

            if (_searchProvider.Equals(WebSearchProvider.Bing, StringComparison.OrdinalIgnoreCase))
            {
                results = await SearchBing(query, count);
            }
            else
            {
                throw new ArgumentException(nameof(_searchProvider));
            }

            if (results.Count == 0)
            {
                return JsonSerializer.Serialize(new { query, message = "No results found." });
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Search results for: {query}");
            sb.AppendLine();

            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                sb.AppendLine($"{i + 1}. {r.Title}");
                sb.AppendLine($"   URL: {r.Url}");
                if (!string.IsNullOrWhiteSpace(r.Snippet))
                {
                    sb.AppendLine($"   {r.Snippet}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (HttpRequestException ex)
        {
            return JsonSerializer.Serialize(new { error = $"Search request failed: {ex.Message}", query });
        }
        catch (TaskCanceledException)
        {
            return JsonSerializer.Serialize(new { error = $"Search timed out after {_timeoutSeconds} seconds.", query });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, query });
        }
    }

    /// <summary>
    /// Fetch and extract content from a URL.
    /// </summary>
    [Description("Fetch URL and extract readable content. Supports HTML, JSON, and plain text.")]
    [Tool(Icon = "🌐", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.WebFetch))]
    public async Task<string> WebFetch(
        [Description("The URL to fetch.")] string url,
        [Description("Extraction mode: 'markdown', 'text', or 'raw' for HTML.")] string extractMode = "markdown",
        [Description("Maximum characters to extract.")] int? maxChars = null)
    {
        var actualMaxChars = maxChars ?? _maxChars;
        
        if (!IsValidUrl(url, out var validationError))
        {
            return JsonSerializer.Serialize(new { error = $"URL validation failed: {validationError}", url });
        }

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var content = await response.Content.ReadAsStringAsync();
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

            string extractedContent;
            string extractor;

            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                // Pretty print JSON
                try
                {
                    var jsonDoc = JsonDocument.Parse(content);
                    extractedContent = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
                    extractor = "json";
                }
                catch
                {
                    extractedContent = content;
                    extractor = "raw_json";
                }
            }
            else if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) || 
                     content.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                     content.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                // HTML content - extract readable parts
                if (extractMode == "markdown")
                {
                    extractedContent = HtmlToMarkdown(content);
                    extractor = "markdown";
                }
                else if (extractMode == "text")
                {
                    extractedContent = HtmlToText(content);
                    extractor = "text";
                }
                else
                {
                    extractedContent = content;
                    extractor = "raw_html";
                }
            }
            else
            {
                extractedContent = content;
                extractor = "raw";
            }

            var truncated = extractedContent.Length > actualMaxChars;
            if (truncated)
            {
                extractedContent = extractedContent[..actualMaxChars];
            }

            var result = new
            {
                url,
                finalUrl,
                status = (int)response.StatusCode,
                extractor,
                truncated,
                length = extractedContent.Length,
                text = extractedContent
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (HttpRequestException ex)
        {
            return JsonSerializer.Serialize(new { error = $"HTTP request failed: {ex.Message}", url });
        }
        catch (TaskCanceledException)
        {
            return JsonSerializer.Serialize(new { error = $"Request timed out after {_timeoutSeconds} seconds.", url });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, url });
        }
    }

    private static bool IsValidUrl(string url, out string error)
    {
        try
        {
            var uri = new Uri(url);
            
            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                error = $"Only http/https allowed, got '{uri.Scheme}'";
                return false;
            }

            if (string.IsNullOrWhiteSpace(uri.Host))
            {
                error = "Missing domain";
                return false;
            }

            // Reject localhost and private IPs (optional, can be disabled)
            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.StartsWith("10.", StringComparison.OrdinalIgnoreCase))
            {
                error = "Localhost and private IP addresses are not allowed";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (UriFormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string HtmlToText(string html)
    {
        // Remove script and style blocks
        var withoutScripts = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        var withoutStyles = Regex.Replace(withoutScripts, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        
        // Remove all HTML tags
        var withoutTags = Regex.Replace(withoutStyles, @"<[^>]+>", " ");
        
        // Decode HTML entities
        var decoded = WebUtility.HtmlDecode(withoutTags);
        
        // Normalize whitespace
        var normalized = Regex.Replace(decoded, @"\s+", " ").Trim();
        
        return normalized;
    }

    private static string HtmlToMarkdown(string html)
    {
        // Simple HTML to Markdown conversion
        var result = html;

        // Remove script and style blocks
        result = Regex.Replace(result, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);

        // Convert links: <a href="url">text</a> -> [text](url)
        result = Regex.Replace(result, @"<a\s+[^>]*href=[""']([^""']+)[""'][^>]*>([\s\S]*?)</a>",
            m => $"[{HtmlToText(m.Groups[2].Value)}]({m.Groups[1].Value})",
            RegexOptions.IgnoreCase);

        // Convert headings: <h1-6>text</h1-6> -> # text
        result = Regex.Replace(result, @"<h([1-6])[^>]*>([\s\S]*?)</h\1>",
            m => $"\n{new string('#', int.Parse(m.Groups[1].Value))} {HtmlToText(m.Groups[2].Value)}\n",
            RegexOptions.IgnoreCase);

        // Convert lists: <li>text</li> -> - text
        result = Regex.Replace(result, @"<li[^>]*>([\s\S]*?)</li>",
            m => $"\n- {HtmlToText(m.Groups[1].Value)}",
            RegexOptions.IgnoreCase);

        // Convert block elements to newlines
        result = Regex.Replace(result, @"</(p|div|section|article)>", "\n\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<(br|hr)\s*/?>", "\n", RegexOptions.IgnoreCase);

        // Remove remaining HTML tags
        result = Regex.Replace(result, @"<[^>]+>", " ");

        // Decode HTML entities
        result = WebUtility.HtmlDecode(result);

        // Normalize whitespace - remove extra spaces but keep paragraph breaks
        result = Regex.Replace(result, @"[ \t]+", " ");
        result = Regex.Replace(result, @"\n{3,}", "\n\n");

        return result.Trim();
    }

    private sealed record SearchResult(string Title, string Url, string Snippet);

    #region Bing Search Provider

    private async Task<List<SearchResult>> SearchBing(string query, int maxResults)
    {
        var url = $"{BingUrl}?q={Uri.EscapeDataString(query)}&count={maxResults}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "text/html");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        return ParseBingResults(html, maxResults);
    }

    private static List<SearchResult> ParseBingResults(string html, int maxResults)
    {
        var results = new List<SearchResult>();

        var blockMatches = Regex.Matches(html,
            @"<li[^>]*b_algo[^>]*>(.*?)</li>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match blockMatch in blockMatches)
        {
            if (results.Count >= maxResults)
                break;

            var block = blockMatch.Groups[1].Value;

            var titleMatch = Regex.Match(block,
                @"<h2[^>]*>\s*<a[^>]+href=""([^""]*)""[^>]*>(.*?)</a>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (!titleMatch.Success)
                continue;

            var title = WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[2].Value, @"<[^>]+>", "")).Trim();

            var citeMatch = Regex.Match(block,
                @"<cite[^>]*>(.*?)</cite>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var resultUrl = string.Empty;
            if (citeMatch.Success)
            {
                resultUrl = WebUtility.HtmlDecode(Regex.Replace(citeMatch.Groups[1].Value, @"<[^>]+>", "")).Trim();
                if (!resultUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    resultUrl = "https://" + resultUrl;
                }
            }

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(resultUrl))
                continue;

            var snippet = string.Empty;
            var snippetMatch = Regex.Match(block,
                @"<p[^>]*>(.*?)</p>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (snippetMatch.Success)
            {
                snippet = WebUtility.HtmlDecode(Regex.Replace(snippetMatch.Groups[1].Value, @"<[^>]+>", "")).Trim();
            }

            results.Add(new SearchResult(title, resultUrl, snippet));
        }

        return results;
    }

    #endregion

    #region Exa Search Provider (Legacy MCP)

    // NOTE: This is a legacy manual MCP call to Exa. Consider using the MCP server integration instead:
    // Add to McpServers config: { "Name": "exa", "Transport": "http", "Url": "https://mcp.exa.ai/mcp" }
    // Then the Exa tools (web_search_exa, etc.) will be available as standard MCP tools.
    private async Task<string> SearchExa(string query, int numResults)
    {
        var requestBody = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "web_search_exa",
                arguments = new
                {
                    query,
                    type = "auto",
                    numResults,
                    livecrawl = "fallback",
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ExaMcpUrl);
        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();

        foreach (var line in responseText.Split('\n'))
        {
            if (!line.StartsWith("data: ")) continue;
            var json = line[6..];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("result", out var result) &&
                result.TryGetProperty("content", out var content) &&
                content.GetArrayLength() > 0)
            {
                var text = content[0].GetProperty("text").GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return JsonSerializer.Serialize(new { query, message = "No results found." });
    }

    #endregion
}
