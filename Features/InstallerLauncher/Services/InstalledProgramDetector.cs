using GerenciadorIcpBrasil.Modules.InstallerLauncher.Models;
using Microsoft.Win32;
using System.Management;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.Services;

public sealed class InstalledProgramDetector
{
    private static readonly Regex LeadingOrdinalRegex = new(@"^\*?\s*\d+\s*-\s*", RegexOptions.Compiled);
    private static readonly string[] UninstallKeys =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    public InstallerPackageState GetState(InstallerPackage package)
    {
        var specializedState = package.Id switch
        {
            "bio-vcredist-2010" => GetVisualCppState(package, "10.0", "2010"),
            "bio-vcredist-2012" => GetVisualCppState(package, "11.0", "2012"),
            "bio-vcredist-2013" => GetVisualCppState(package, "12.0", "2013"),
            "bio-vcredist-2017" => GetVisualCppState(package, "14.0", "v14", "2015-2022", "2017", "2019", "2022"),
            "bio-dotnet481" => GetDotNetFrameworkState(package),
            "driver-futronic" => GetPnpDriverState(package,
                new[] { "VID_1491&PID_0088" },
                new[] { "Futronic USB Fingerprint Scanner", "Futronic Fingerprint Scanner" }),
            "drv-gemalto" => GetPnpDriverState(package,
                new[] { "VID_08E6&PID_3437" },
                new[] { "GEMPC", "Gemalto PC USB" }),
            "drv-omnikey" => GetPnpDriverState(package,
                new[] { "VID_076B&PID_3021" },
                new[] { "OMNIKEY 3021", "CardMan 3021" }),
            "drv-scr3310" => GetPnpDriverState(package,
                new[] { "VID_04E6&PID_5116", "VID_04E6&PID_E003" },
                new[] { "SCR3310", "SCR 3310" }),
            _ => null
        };

        if (specializedState is not null)
        {
            return specializedState;
        }

        var required = TryParseVersion(package.RequiredVersion);
        var matches = FindMatches(package).ToList();

        if (matches.Count == 0)
        {
            return new InstallerPackageState(false, false, null, null, null);
        }

        var bestCandidate = matches
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.Version)
            .First();
        var bestMatch = bestCandidate.Entry;
        var bestVersion = bestCandidate.Version;
        var installedVersion = bestVersion?.ToString() ?? bestMatch.DisplayVersion;
        var installed = true;
        var upToDate = required is null || (bestVersion is not null && bestVersion >= required);

        return new InstallerPackageState(
            installed,
            upToDate,
            installedVersion,
            bestMatch.UninstallString,
            bestMatch.QuietUninstallString);
    }

    private static InstallerPackageState GetVisualCppState(
        InstallerPackage package,
        string runtimeVersion,
        params string[] nameMarkers)
    {
        var registryVersion = TryReadVisualCppRuntimeVersion(runtimeVersion);
        if (registryVersion is not null)
        {
            return BuildInstalledState(package, registryVersion);
        }

        var entry = EnumerateUninstallEntries()
            .Where(candidate =>
                candidate.NormalizedDisplayName.Contains("microsoft visual c", StringComparison.Ordinal) &&
                candidate.NormalizedDisplayName.Contains("x86", StringComparison.Ordinal) &&
                nameMarkers.Any(marker => candidate.NormalizedDisplayName.Contains(
                    NormalizeForMatch(marker), StringComparison.Ordinal)))
            .Select(candidate => new
            {
                Entry = candidate,
                Version = TryParseVersion(candidate.DisplayVersion) ?? TryParseVersion(candidate.DisplayName)
            })
            .OrderByDescending(candidate => candidate.Version)
            .FirstOrDefault();

        return entry is null
            ? NotInstalledState()
            : BuildInstalledState(package, entry.Version, entry.Entry);
    }

    private static Version? TryReadVisualCppRuntimeVersion(string runtimeVersion)
    {
        var suffixes = new[]
        {
            $@"SOFTWARE\Microsoft\VisualStudio\{runtimeVersion}\VC\Runtimes\x86",
            $@"SOFTWARE\Microsoft\VisualStudio\{runtimeVersion}\VC\VCRedist\x86"
        };

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (var suffix in suffixes)
            {
                using var key = localMachine.OpenSubKey(suffix);
                if (key is null || !IsOne(key.GetValue("Installed")))
                {
                    continue;
                }

                var parsed = TryParseVersion(key.GetValue("Version")?.ToString());
                if (parsed is not null)
                {
                    return parsed;
                }

                var components = new[] { "Major", "Minor", "Bld", "Rbld" }
                    .Select(name => Convert.ToInt32(key.GetValue(name, 0), CultureInfo.InvariantCulture))
                    .ToArray();
                return new Version(components[0], components[1], components[2], components[3]);
            }
        }

        return null;
    }

    private static InstallerPackageState GetDotNetFrameworkState(InstallerPackage package)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            if (key is null || !IsOne(key.GetValue("Install")))
            {
                continue;
            }

            var release = Convert.ToInt32(key.GetValue("Release", 0), CultureInfo.InvariantCulture);
            var versionText = GetDotNetFrameworkVersion(release) ?? key.GetValue("Version")?.ToString();
            return BuildInstalledState(package, TryParseVersion(versionText), displayVersion: versionText);
        }

        return NotInstalledState();
    }

    private static string? GetDotNetFrameworkVersion(int release) => release switch
    {
        >= 533320 => "4.8.1",
        >= 528040 => "4.8",
        >= 461808 => "4.7.2",
        >= 461308 => "4.7.1",
        >= 460798 => "4.7",
        >= 394802 => "4.6.2",
        >= 394254 => "4.6.1",
        >= 393295 => "4.6",
        _ => null
    };

    private static InstallerPackageState GetPnpDriverState(
        InstallerPackage package,
        IReadOnlyCollection<string> hardwareIdMarkers,
        IReadOnlyCollection<string> deviceNameMarkers)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, Manufacturer, DriverProviderName, DriverVersion, DeviceID, HardwareID FROM Win32_PnPSignedDriver");
            var matches = new List<(Version? Version, string? DisplayVersion)>();
            foreach (ManagementObject driver in searcher.Get())
            {
                using (driver)
                {
                    var deviceName = driver["DeviceName"]?.ToString() ?? string.Empty;
                    var deviceId = driver["DeviceID"]?.ToString() ?? string.Empty;
                    var hardwareIds = driver["HardwareID"] switch
                    {
                        string[] values => string.Join(";", values),
                        string value => value,
                        _ => string.Empty
                    };
                    var identity = $"{deviceName};{deviceId};{hardwareIds}";
                    var isMatch = hardwareIdMarkers.Any(marker =>
                                      identity.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
                                  deviceNameMarkers.Any(marker =>
                                      deviceName.Contains(marker, StringComparison.OrdinalIgnoreCase));
                    if (!isMatch)
                    {
                        continue;
                    }

                    var displayVersion = driver["DriverVersion"]?.ToString();
                    matches.Add((TryParseVersion(displayVersion), displayVersion));
                }
            }

            var best = matches.OrderByDescending(match => match.Version).FirstOrDefault();
            return matches.Count == 0
                ? NotInstalledState()
                : BuildInstalledState(package, best.Version, displayVersion: best.DisplayVersion);
        }
        catch
        {
            return NotInstalledState();
        }
    }

    private static InstallerPackageState BuildInstalledState(
        InstallerPackage package,
        Version? installedVersion,
        UninstallEntry? entry = null,
        string? displayVersion = null)
    {
        var required = TryParseVersion(package.RequiredVersion);
        var upToDate = required is null || (installedVersion is not null && installedVersion >= required);
        return new InstallerPackageState(
            true,
            upToDate,
            installedVersion?.ToString() ?? displayVersion,
            entry?.UninstallString,
            entry?.QuietUninstallString);
    }

    private static InstallerPackageState NotInstalledState() => new(false, false, null, null, null);

    private static IEnumerable<MatchCandidate> FindMatches(InstallerPackage package)
    {
        var candidates = EnumerateUninstallEntries().ToList();
        var aliases = BuildAliases(package);
        if (aliases.Count == 0)
        {
            yield break;
        }

        foreach (var candidate in candidates)
        {
            var score = 0;
            foreach (var alias in aliases)
            {
                var aliasScore = GetAliasScore(candidate.NormalizedDisplayName, alias);
                if (aliasScore > score)
                {
                    score = aliasScore;
                }
            }

            if (score <= 0)
            {
                continue;
            }

            yield return new MatchCandidate(candidate, score, TryParseVersion(candidate.DisplayVersion) ?? TryParseVersion(candidate.DisplayName));
        }
    }

    private static List<string> BuildAliases(InstallerPackage package)
    {
        var aliases = new List<string>();
        if (package.DetectionNames is { Length: > 0 })
        {
            aliases.AddRange(package.DetectionNames);
        }

        aliases.Add(package.DisplayName);
        aliases.Add(LeadingOrdinalRegex.Replace(package.DisplayName ?? string.Empty, string.Empty));

        return aliases
            .Select(NormalizeForMatch)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static int GetAliasScore(string normalizedDisplayName, string normalizedAlias)
    {
        if (string.IsNullOrWhiteSpace(normalizedDisplayName) || string.IsNullOrWhiteSpace(normalizedAlias))
        {
            return 0;
        }

        if (normalizedDisplayName.Equals(normalizedAlias, StringComparison.Ordinal))
        {
            return 1200 + normalizedAlias.Length;
        }

        if (normalizedDisplayName.StartsWith(normalizedAlias + " ", StringComparison.Ordinal) ||
            normalizedDisplayName.EndsWith(" " + normalizedAlias, StringComparison.Ordinal) ||
            normalizedDisplayName.Contains(" " + normalizedAlias + " ", StringComparison.Ordinal))
        {
            return 900 + normalizedAlias.Length;
        }

        if (normalizedDisplayName.Contains(normalizedAlias, StringComparison.Ordinal))
        {
            return 700 + normalizedAlias.Length;
        }

        var words = normalizedAlias.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1 && words.All(word => normalizedDisplayName.Contains(word, StringComparison.Ordinal)))
        {
            return 500 + words.Sum(word => word.Length);
        }

        return 0;
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var noDiacritics = RemoveDiacritics(value).ToLowerInvariant();
        var builder = new StringBuilder(noDiacritics.Length);
        foreach (var ch in noDiacritics)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static IEnumerable<UninstallEntry> EnumerateUninstallEntries()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in UninstallKeys)
            {
                using var root = hive.OpenSubKey(path);
                if (root == null)
                {
                    continue;
                }

                foreach (var sub in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(sub);
                    if (key == null)
                    {
                        continue;
                    }

                    if (IsOne(key.GetValue("SystemComponent")))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(key.GetValue("ParentKeyName") as string))
                    {
                        continue;
                    }

                    var displayName = key.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    yield return new UninstallEntry(
                        displayName.Trim(),
                        NormalizeForMatch(displayName),
                        key.GetValue("DisplayVersion") as string,
                        key.GetValue("UninstallString") as string,
                        key.GetValue("QuietUninstallString") as string);
                }
            }
        }
    }

    private static bool IsOne(object? value)
        => value switch
        {
            int i => i == 1,
            long l => l == 1L,
            string s when int.TryParse(s, out var parsed) => parsed == 1,
            _ => false
        };

    private static Version? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value
            .Where(ch => char.IsDigit(ch) || ch == '.')
            .ToArray());

        return Version.TryParse(cleaned, out var version) ? version : null;
    }

    private sealed record UninstallEntry(
        string DisplayName,
        string NormalizedDisplayName,
        string? DisplayVersion,
        string? UninstallString,
        string? QuietUninstallString);

    private sealed record MatchCandidate(
        UninstallEntry Entry,
        int Score,
        Version? Version);
}
