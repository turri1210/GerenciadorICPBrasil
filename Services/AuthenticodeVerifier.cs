using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace GerenciadorIcpBrasil.Services;

internal static class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private const string RedeIcpBrasilThumbprint = "038406E8CB7700739D12CD73A911DF5E90DED643";
    private const uint UntrustedRoot = 0x800B0109;

    public static void VerifyOfficialRelease(string filePath)
    {
        var status = GetSignatureStatus(filePath);
        // O certificado é autoassinado; a raiz pode não ser confiável no Windows.
        // Qualquer outra falha da verificação Authenticode continua bloqueada.
        if (status != 0 && status != UntrustedRoot)
        {
            throw new InvalidOperationException($"A assinatura Authenticode do instalador é inválida (0x{status:X8}).");
        }

        using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
        if (!string.Equals(signer.Thumbprint, RedeIcpBrasilThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O instalador não foi assinado pelo certificado Rede ICP Brasil autorizado.");
        }
        if (DateTimeOffset.UtcNow < signer.NotBefore || DateTimeOffset.UtcNow > signer.NotAfter)
        {
            throw new InvalidOperationException("O certificado de assinatura do instalador está fora da validade.");
        }

        var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
        if (!string.Equals(versionInfo.ProductName?.Trim(), "Gerenciador ICP Brasil", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("O instalador assinado não pertence ao Gerenciador ICP Brasil.");
        }
    }

    public static string VerifyTrustedSignature(string filePath, IReadOnlyCollection<string>? expectedPublisherNames = null)
    {
        var result = GetSignatureStatus(filePath);
        if (result != 0)
        {
            throw new InvalidOperationException($"A assinatura Authenticode do arquivo é inválida (0x{result:X8}).");
        }

        using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
        var commonName = signer.GetNameInfo(X509NameType.SimpleName, false);
        if (expectedPublisherNames is not null &&
            (expectedPublisherNames.Count == 0 ||
             !expectedPublisherNames.Any(expected =>
                 !string.IsNullOrWhiteSpace(expected) &&
                 commonName.Contains(expected, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException($"Editor não autorizado para este pacote: {commonName}.");
        }

        return commonName;
    }

    private static uint GetSignatureStatus(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Arquivo para validação não encontrado.", filePath);
        }
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var trustData = new WinTrustData(fileInfoPointer);
            var trustDataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            try
            {
                Marshal.StructureToPtr(trustData, trustDataPointer, false);
                return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, trustDataPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(trustDataPointer);
            }

        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPointer);
            Marshal.FreeCoTaskMem(fileInfo.FilePath);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr windowHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;

        public WinTrustFileInfo(string filePath)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = Marshal.StringToCoTaskMemUni(filePath);
            FileHandle = IntPtr.Zero;
            KnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;

        public WinTrustData(IntPtr fileInfo)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = 2; // WTD_UI_NONE
            RevocationChecks = 0;
            UnionChoice = 1; // WTD_CHOICE_FILE
            FileInfo = fileInfo;
            StateAction = 0;
            StateData = IntPtr.Zero;
            UrlReference = IntPtr.Zero;
            ProviderFlags = 0x00000080; // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
            UiContext = 0;
        }
    }
}
