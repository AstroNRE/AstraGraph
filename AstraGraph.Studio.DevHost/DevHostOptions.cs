namespace AstraGraph.Studio.DevHost;

public sealed record DevHostOptions
{
    public DevHostMode Mode { get; init; } = DevHostMode.Standalone;

    public string ListenAddress { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 5173;

    public string? Token { get; init; }

    public string? StudioAssetsPath { get; init; }

    public bool? OpenBrowser { get; init; }

    public string? ProjectPath { get; init; }

    public string? DataPath { get; init; }

    public string Permissions { get; init; } = "developer";

    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    public static DevHostOptions Parse(string[] args, IReadOnlyDictionary<string, string?>? environment = null)
    {
        environment ??= ReadEnvironment();
        var mode = ParseMode(environment.GetValueOrDefault("ASTRA_STUDIO_MODE"));
        var listen = environment.GetValueOrDefault("ASTRA_STUDIO_LISTEN") ?? "127.0.0.1";
        var port = int.TryParse(environment.GetValueOrDefault("ASTRA_STUDIO_PORT"), out var envPort) ? envPort : 5173;
        var token = environment.GetValueOrDefault("ASTRA_STUDIO_TOKEN");
        var assets = environment.GetValueOrDefault("ASTRA_STUDIO_ASSETS");
        var project = environment.GetValueOrDefault("ASTRA_STUDIO_PROJECT");
        var data = environment.GetValueOrDefault("ASTRA_STUDIO_DATA");
        string permissions = "developer";
        bool? openBrowser = null;
        var origins = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--mode":
                    mode = ParseMode(Next(args, ref i, arg)) ?? DevHostMode.Standalone;
                    break;
                case "--listen":
                    listen = Next(args, ref i, arg);
                    break;
                case "--port":
                    port = int.Parse(Next(args, ref i, arg), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--token":
                    token = Next(args, ref i, arg);
                    break;
                case "--assets":
                    assets = Next(args, ref i, arg);
                    break;
                case "--project":
                    project = Next(args, ref i, arg);
                    break;
                case "--data":
                    data = Next(args, ref i, arg);
                    break;
                case "--permissions":
                    permissions = Next(args, ref i, arg);
                    break;
                case "--open-browser":
                    openBrowser = bool.Parse(Next(args, ref i, arg));
                    break;
                case "--allowed-origin":
                    origins.Add(Next(args, ref i, arg));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown argument '{arg}'.");
            }
        }

        return new DevHostOptions
        {
            Mode = mode ?? DevHostMode.Standalone,
            ListenAddress = listen,
            Port = port,
            Token = string.IsNullOrWhiteSpace(token) ? null : token,
            StudioAssetsPath = assets,
            OpenBrowser = openBrowser,
            ProjectPath = project,
            DataPath = data,
            Permissions = permissions,
            AllowedOrigins = origins
        };
    }

    private static DevHostMode? ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse<DevHostMode>(value, ignoreCase: true, out var mode))
        {
            throw new InvalidOperationException($"Unknown Astra Studio mode '{value}'.");
        }

        return mode;
    }

    private static string Next(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"Missing value for {name}.");
        }

        index++;
        return args[index];
    }

    private static Dictionary<string, string?> ReadEnvironment()
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ASTRA_STUDIO_MODE"] = Environment.GetEnvironmentVariable("ASTRA_STUDIO_MODE"),
            ["ASTRA_STUDIO_LISTEN"] = Environment.GetEnvironmentVariable("ASTRA_STUDIO_LISTEN"),
            ["ASTRA_STUDIO_PORT"] = Environment.GetEnvironmentVariable("ASTRA_STUDIO_PORT"),
            ["ASTRA_STUDIO_TOKEN"] = Environment.GetEnvironmentVariable("ASTRA_STUDIO_TOKEN"),
            ["ASTRA_STUDIO_ASSETS"] = Environment.GetEnvironmentVariable("ASTRA_STUDIO_ASSETS"),
            ["ASTRA_STUDIO_PROJECT"] = Environment.GetEnvironmentVariable("ASTRA_STUDIO_PROJECT"),
            ["ASTRA_STUDIO_DATA"] = Environment.GetEnvironmentVariable("ASTRA_STUDIO_DATA")
        };
    }
}
