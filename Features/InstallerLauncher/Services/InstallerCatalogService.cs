using GerenciadorIcpBrasil.Modules.InstallerLauncher.Models;

namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.Services;

public static class InstallerCatalogService
{
    private static readonly Uri[] JavaManualPageUris =
    {
        new("https://www.java.com/pt-BR/download/manual.jsp"),
        new("https://www.java.com/en/download/manual.jsp")
    };

    public static IReadOnlyList<InstallerPackage> GetAllPackages() =>
        GetBiometricPackages()
            .Concat(GetDriverPackages())
            .ToList();

    public static IReadOnlyList<InstallerPackage> GetBiometricPackages() =>
        new List<InstallerPackage>
        {
            new(
                Id: "bio-vcredist-2010",
                DisplayName: "1 - Visual C++ 2010 x86",
                Category: InstallerCategory.Biometria,
                RelativePath: @"Biometric\1-vcredist_x86.exe",
                DownloadUri: new Uri("https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe"),
                RequiredVersion: "10.0.40219",
                DetectionNames: new[] { "Microsoft Visual C++ 2010  x86", "Microsoft Visual C++ 2010 x86", "Microsoft Visual C++ 2010 x86 Redistributable" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/silent" },
                Sequence: 1),
            new(
                Id: "bio-vcredist-2012",
                DisplayName: "2 - Visual C++ 2012 x86",
                Category: InstallerCategory.Biometria,
                RelativePath: @"Biometric\2-C++ 2012 vcredist_x86.exe",
                DownloadUri: new Uri("https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x86.exe"),
                RequiredVersion: "11.0",
                DetectionNames: new[] { "Microsoft Visual C++ 2012  x86", "Microsoft Visual C++ 2012 x86", "Microsoft Visual C++ 2012 x86 Redistributable" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/quiet" },
                Sequence: 2),
            new(
                Id: "bio-vcredist-2013",
                DisplayName: "3 - Visual C++ 2013 x86",
                Category: InstallerCategory.Biometria,
                RelativePath: @"Biometric\3-C++ 2013 vcredist_x86.exe",
                DownloadUri: new Uri("https://aka.ms/highdpimfc2013x86enu"),
                RequiredVersion: "12.0",
                DetectionNames: new[] { "Microsoft Visual C++ 2013  x86", "Microsoft Visual C++ 2013 x86", "Microsoft Visual C++ 2013 x86 Redistributable" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/quiet" },
                Sequence: 3),
            new(
                Id: "bio-vcredist-2017",
                DisplayName: "4 - Microsoft Visual C++ v14 x86",
                Category: InstallerCategory.Biometria,
                RelativePath: @"Biometric\4-VC_redist_x86-2017.exe",
                DownloadUri: new Uri("https://aka.ms/vc14/vc_redist.x86.exe"),
                RequiredVersion: "14.0",
                DetectionNames: new[] { "Microsoft Visual C++ v14 Redistributable (x86)", "Microsoft Visual C++ 2017 x86", "Microsoft Visual C++ 2015-2019 x86", "Microsoft Visual C++ 2015-2022 x86", "Microsoft Visual C++ 2022 X86" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/quiet" },
                Sequence: 4),
            new(
                Id: "bio-dotnet481",
                DisplayName: "5 - .NET Framework 4.8.1",
                Category: InstallerCategory.Biometria,
                RelativePath: @"Biometric\5-dotNetF4.8.1_x86_x64.exe",
                DownloadUri: new Uri("https://go.microsoft.com/fwlink/?linkid=2203305"),
                RequiredVersion: "4.8.1",
                DetectionNames: new[] { ".NET Framework 4.8.1", "Microsoft .NET Framework 4.8.1", ".NET Framework 4.8" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/q" },
                Sequence: 5),
            new(
                Id: "bio-certiplugin",
                DisplayName: "6 - CertiPlugin",
                Category: InstallerCategory.Biometria,
                RelativePath: @"Biometric\6-Setup_CertiPlugin.exe",
                DownloadUri: new Uri("https://redeicpbrasil-my.sharepoint.com/:u:/g/personal/administrador_redeicpbrasil_com_br/EVMttJf8ZfdPjtw-biecYMQBtcdkQeBqzLvPUWu9MH9KMg?download=1"),
                RequiredVersion: "1.0.0",
                DetectionNames: new[] { "CertiPlugin" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/verysilent" },
                Sequence: 6),
            new(
                Id: "bio-certiplugin-browser",
                DisplayName: "7 - Extensão CertiPlugin (Chrome/Edge)",
                Category: InstallerCategory.Biometria,
                RelativePath: null,
                DownloadUri: null,
                ExtensionInstallUri: new Uri("https://chromewebstore.google.com/detail/certiplugin-chrome/chgajipdilbjolpgafadpdggjaeeapic"),
                Sequence: 7),
            new(
                Id: "bio-platform",
                DisplayName: "8 - Serviço Biométrico - Plataforma Local v1.2.0.2",
                Category: InstallerCategory.Biometria,
                RelativePath: @"Biometric\7-BiometricLocalServicePlataformMSI_v1.2.0.2.msi",
                DownloadUri: new Uri("https://redeicpbrasil-my.sharepoint.com/:u:/g/personal/administrador_redeicpbrasil_com_br/EbF62QeBip1OqD2mwLa0zj8B4hapNaQuGP_NPtRR-wZ8kQ?download=1"),
                RequiredVersion: "1.2.0.2",
                DetectionNames: new[] { "BiometricLocalServicePlataform", "Biometric Local Service Plataform" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/quiet" },
                Sequence: 8),
            new(
                Id: "driver-futronic",
                DisplayName: "Driver Futronic FS88h",
                Category: InstallerCategory.Biometria,
                RelativePath: @"Biometric\ftrDriverSetup_win8_whql_3471.zip",
                DownloadUri: new Uri("https://www.futronic-tech.com/futronic/attachment/upload/futronic/download/ftrDriverSetup_win8_whql_3471.zip"),
                ArchiveEntryPath: "ftrDriverSetup_win8_whql_3471.exe",
                RequiredVersion: "10.0.0.1",
                DetectionNames: new[] { "Futronic", "FS88", "FS88H" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "-silentinstall" },
                Sequence: 9)
        };

    public static IReadOnlyList<InstallerPackage> GetDriverPackages() =>
        new List<InstallerPackage>
        {
            new(
                Id: "drv-safesign",
                DisplayName: "SafeSign IC Std x64 3.5.3.0",
                Category: InstallerCategory.Midia,
                RelativePath: @"Drivers\SafeSign IC Standard Windows x64 3.5.3.0-AET.000.msi",
                DownloadUri: new Uri("https://drivers.certisign.com.br/midias/gerenciadores/safesign/64bits/SafeSignIC30124-x64-win-tu-admin.exe"),
                RequiredVersion: "3.5.3.0",
                DetectionNames: new[] { "SafeSign", "SafeSign IC" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/quiet" },
                Sequence: 1,
                RequiresManualConfirmation: true),
            new(
                Id: "drv-safenet",
                DisplayName: "SafeNet 10.6 x64",
                Category: InstallerCategory.Midia,
                RelativePath: @"Drivers\certisign10.6-x64-10.6.exe",
                DownloadUri: new Uri("https://drivers.certisign.com.br/midias/tokens/safenet/64bits/certisign10.6-x64-10.6.exe"),
                RequiredVersion: "10.6",
                DetectionNames: new[] { "SafeNet", "SafeNet Authentication Client", "SafeNet Client" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/passive" },
                Sequence: 2,
                RequiresManualConfirmation: true),
            new(
                Id: "drv-awp",
                DisplayName: "AWP Manager 5.1.8 x64",
                Category: InstallerCategory.Midia,
                RelativePath: @"Drivers\AWP_Manager_5.1.8_64_bits.exe",
                DownloadUri: new Uri("https://drivers.certisign.com.br/midias/leitoras/oberthur/64-bits/AWP_Manager_5.1.8_64_bits.exe"),
                RequiredVersion: "5.1.8",
                DetectionNames: new[] { "AWP Manager" },
                InstallArguments: Array.Empty<string>(),
                Sequence: 3,
                RequiresManualConfirmation: true),
            new(
                Id: "drv-gemalto",
                DisplayName: "Leitora Gemalto GEMPCTWIN USB",
                Category: InstallerCategory.Leitora,
                RelativePath: @"Drivers\gemccid_en-us_64.msi",
                DownloadUri: new Uri("https://drivers.certisign.com.br/midias/leitoras/gemalto/gempctwin-usb/64-bits/gemccid_en-us_64.msi"),
                RequiredVersion: "1.0.0",
                DetectionNames: new[] { "Gemalto", "GEMPC" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallArguments: new[] { "/quiet" },
                Sequence: 4,
                RequiresManualConfirmation: true),
            new(
                Id: "drv-omnikey",
                DisplayName: "Leitora Omnikey Cardman 3021",
                Category: InstallerCategory.Leitora,
                RelativePath: @"Drivers\OMNIKEY3x21_x64AMD_for_R1_2_2_8.exe",
                DownloadUri: new Uri("https://drivers.certisign.com.br/midias/leitoras/omnikey/cardman3021/64bits/OMNIKEY3x21_x64AMD_for_R1_2_2_8.exe"),
                RequiredVersion: "1.2.2.8",
                DetectionNames: new[] { "OMNIKEY", "CardMan" },
                InstallArguments: Array.Empty<string>(),
                Sequence: 5,
                RequiresManualConfirmation: true),
            new(
                Id: "drv-scr3310",
                DisplayName: "Leitora SCR3310 SCM Microsystems",
                Category: InstallerCategory.Leitora,
                RelativePath: @"Drivers\SCR3_DriversOnly_V8.41.exe",
                DownloadUri: new Uri("https://drivers.certisign.com.br/midias/leitoras/scm/scr3310/SCR3_DriversOnly_V8.41.exe"),
                RequiredVersion: "8.41",
                DetectionNames: new[] { "SCR3", "SCR3310", "SCM Microsystems" },
                InstallArguments: Array.Empty<string>(),
                Sequence: 6,
                RequiresManualConfirmation: true),
            new(
                Id: "drv-java",
                DisplayName: "Java Runtime (Desktop Oracle 64 bits)",
                Category: InstallerCategory.Java,
                RelativePath: null,
                DownloadUri: JavaManualPageUris[0],
                RequiredVersion: "1.8",
                DetectionNames: new[] { "Java", "Java Runtime", "Java SE Runtime" },
                InstallArguments: Array.Empty<string>(),
                AutomaticInstallerPath: "winget.exe",
                AutomaticInstallerArguments: new[]
                {
                    "install",
                    "--id", "Oracle.JavaRuntimeEnvironment",
                    "--exact",
                    "--source", "winget",
                    "--accept-source-agreements",
                    "--disable-interactivity",
                    "--silent",
                    "--accept-package-agreements",
                    "--force"
                },
                Sequence: 7)
        };

    public static IReadOnlyList<Uri> GetJavaManualUris() => JavaManualPageUris;
}
