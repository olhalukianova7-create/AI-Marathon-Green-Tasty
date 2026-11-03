namespace Restaurant.Aqa.Configuration;

public sealed class TestEnvironment
{
    private readonly IReadOnlyDictionary<string, string> localValues;

    public TestEnvironment()
    {
        localValues = ReadLocalEnvironment();
    }

    public Uri UiBaseUrl => RequiredUrl("AQA_UI_BASE_URL");
    public Uri ApiBaseUrl => RequiredUrl("AQA_API_BASE_URL");
    public bool Headless => !string.Equals(Value("AQA_HEADLESS"), "false", StringComparison.OrdinalIgnoreCase);

    public string ResultsDirectory => Path.GetFullPath(
        Value("AQA_RESULTS_DIR") ?? Path.Combine(Directory.GetCurrentDirectory(), "TestResults"));

    private string? Value(string name) => Environment.GetEnvironmentVariable(name)
        ?? (localValues.TryGetValue(name, out var value) ? value : null);

    private Uri RequiredUrl(string name)
    {
        var value = Value(name);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)
            || !string.IsNullOrEmpty(url.UserInfo))
        {
            throw new InvalidOperationException(
                $"Set {name} to an absolute HTTP(S) base URL without credentials, query, or fragment. "
                + "Use .env locally, repository variables in GitHub, or CodeBuild environment variables.");
        }

        // Preserve an API Gateway stage when resolving relative paths.
        return new Uri(url.AbsoluteUri.TrimEnd('/') + "/");
    }

    private static Dictionary<string, string> ReadLocalEnvironment()
    {
        // CI uses environment variables only; it must never consume a local .env.
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)
            || Environment.GetEnvironmentVariable("CODEBUILD_BUILD_ID") is not null)
            return new();

        var explicitPath = Environment.GetEnvironmentVariable("AQA_ENV_FILE");
        var path = explicitPath ?? FindEnvironmentFile();
        if (path is null) return new();
        if (!File.Exists(path)) throw new FileNotFoundException("AQA_ENV_FILE does not exist.", path);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) throw new FormatException("Expected KEY=value in the AQA .env file.");
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
            values[line[..separator].Trim()] = value;
        }
        return values;
    }

    private static string? FindEnvironmentFile()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null;
             directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "Restaurant.Aqa.csproj");
            if (File.Exists(project))
            {
                var path = Path.Combine(directory.FullName, ".env");
                return File.Exists(path) ? path : null;
            }
            var nested = Path.Combine(directory.FullName, "aqa", "restaurant-aqa-framework", ".env");
            if (File.Exists(nested)) return nested;
        }
        return null;
    }
}
