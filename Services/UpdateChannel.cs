namespace GerenciadorIcpBrasil.Services;

public static class UpdateChannel
{
    private const string GerenciadorBaseUrl = "https://sistema.redeicpbrasil.com.br/gerenciador";
    private static readonly object Sync = new();

    private static bool _initialized;
    private static bool _isBetaMode;

    public static bool IsBetaMode
    {
        get
        {
            EnsureInitialized();
            return _isBetaMode;
        }
    }

    public static void Initialize(string? launchArguments)
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            _isBetaMode = HasBetaArgument(Environment.GetCommandLineArgs())
                || HasBetaArgument(ParseArguments(launchArguments));
            _initialized = true;
        }
    }

    public static string GetFileName(string defaultFileName)
    {
        if (string.IsNullOrWhiteSpace(defaultFileName))
        {
            return defaultFileName;
        }

        EnsureInitialized();
        if (!_isBetaMode || defaultFileName.StartsWith("beta-", StringComparison.OrdinalIgnoreCase))
        {
            return defaultFileName;
        }

        return $"beta-{defaultFileName}";
    }

    public static string GetGerenciadorUrl(string defaultFileName)
        => $"{GerenciadorBaseUrl}/{GetFileName(defaultFileName)}";

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Initialize(null);
    }

    private static bool HasBetaArgument(IEnumerable<string> args)
        => args.Any(arg =>
        {
            var normalized = arg.Trim().Trim('"', '\'');
            return string.Equals(normalized, "-beta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "--beta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "/beta", StringComparison.OrdinalIgnoreCase);
        });

    private static IEnumerable<string> ParseArguments(string? rawArguments)
    {
        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return Array.Empty<string>();
        }

        return rawArguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim('"', '\''));
    }
}
