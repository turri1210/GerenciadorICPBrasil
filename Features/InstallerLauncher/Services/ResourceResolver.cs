using System.Reflection;

namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.Services;

public static class ResourceResolver
{
    public static string? TryGetLocalFile(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, relativePath),
            TryResolveFromWorkspace(relativePath)
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static string? TryResolveFromWorkspace(string relativePath)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var baseDir = Path.GetDirectoryName(assembly.Location);
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                return null;
            }

            var root = Directory.GetParent(baseDir)?.Parent?.Parent?.Parent?.FullName;
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var candidate = Path.Combine(root, relativePath);
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }
}
