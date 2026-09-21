using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Formats.Asn1; // .NET 5+ para ler ASN.1
using System.Text.RegularExpressions;

class Program
{
    static void Main(string[] args)
    {
        var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var certificadosFiltrados = store.Certificates
            .Cast<X509Certificate2>()
            .Select((cert, i) => new { cert, index = i })
            .Where(x => x.cert.HasPrivateKey && x.cert.Subject.Contains("ICP-Brasil"))
            .ToList();

        if (args.Length > 0 && args[0] == "--json")
        {
            var resultado = certificadosFiltrados.Select(x =>
            {
                // ===== CN / Issuer CN
                string? rawCN = x.cert.Subject
                    .Split(',')
                    .FirstOrDefault(s => s.Trim().StartsWith("CN="));
                string? rawIssuerCN = x.cert.Issuer
                    .Split(',')
                    .FirstOrDefault(s => s.Trim().StartsWith("CN="));

                string nomeCompleto = rawCN?.Replace("CN=", "").Trim() ?? "";
                string[] partesNome = nomeCompleto.Split(':');

                string nome = partesNome.Length > 0 ? partesNome[0] : "(desconhecido)";
                string cpf = ExtractCpf(x.cert);
                string acEmissora = rawIssuerCN?.Replace("CN=", "").Trim() ?? "(desconhecido)";
                string validTo = x.cert.NotAfter.ToString("yyyy-MM-dd");
                string issuer = x.cert.Issuer ?? "";
                string serialNumber = x.cert.SerialNumber ?? "";

                // ===== OIDs de política (A1/A3 da ICP-Brasil)
                var policyOids = ExtractPolicyOids(x.cert);
                bool isA3Policy = policyOids.Any(oid => oid.StartsWith("2.16.76.1.2.3.", StringComparison.Ordinal));
                bool isA1Policy = policyOids.Any(oid => oid.StartsWith("2.16.76.1.2.1.", StringComparison.Ordinal));

                // ===== Heurística do provider local (hardware vs software)
                var (providerIsHardware, providerName) = DetectHardwareProvider(x.cert);

                // ===== Decisão final
                // Se a política disser A3, prevalece (cobre A3 em nuvem/HSM).
                // Caso contrário, usa a heurística do provider para estimar.
                bool isA3 = isA3Policy || providerIsHardware;
                string tipoCert = isA3 ? "A3" : (isA1Policy ? "A1" : (providerIsHardware ? "A3" : "A1"));

                return new
                {
                    id = x.index + 1,
                    nome,
                    cpf,
                    acEmissora,
                    validTo,
                    issuer,
                    serialNumber,
                    // ---- Campos novos/ajudantes ----
                    isHardware = isA3,               // mantém compat: true quando A3 (política ou hardware)
                    tipoCert,                         // "A3" | "A1"
                    providerName,                     // diagnóstico
                    providerIsHardware,               // heurística do provider local
                    isA3Policy,                       // veio da política do certificado
                    policyOids                        // lista de OIDs encontrados em 2.5.29.32
                };
            });

            Console.WriteLine(JsonSerializer.Serialize(resultado));
            store.Close();
            return;
        }

        if (args.Length == 2 && args[0] == "--check" && int.TryParse(args[1], out int checkIndex))
        {
            var item = certificadosFiltrados.FirstOrDefault(x => x.index + 1 == checkIndex);
            if (item == null)
            {
                Console.WriteLine("{}");
                return;
            }

            var cert = item.cert;

            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 0, 10);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            bool isValid = chain.Build(cert);
            bool isRevoked = chain.ChainStatus.Any(s => s.Status == X509ChainStatusFlags.Revoked);

            string status;
            if (isRevoked) status = "revogado";
            else if (isValid) status = "valido";
            else status = "indeterminado";

            var result = new
            {
                id = item.index + 1,
                cadeiaValida = isValid,
                revogado = isRevoked,
                status,
                checkedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            Console.WriteLine(JsonSerializer.Serialize(result));
            store.Close();
            return;
        }

        if (args.Length == 3 &&
            args[0] == "--sign" &&
            int.TryParse(args[1], out int index) &&
            TryDecodeChallenge(args[2], out byte[] dados))
        {
            var item = certificadosFiltrados.FirstOrDefault(x => x.index + 1 == index);
            if (item == null)
            {
                Console.WriteLine("{}");
                return;
            }

            var cert = item.cert;

            // ===== Cadeia/Revogação
            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 0, 10);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            bool cadeiaValida = chain.Build(cert);
            bool revogado = chain.ChainStatus.Any(s => s.Status == X509ChainStatusFlags.Revoked);

            // ===== Assinatura (força PIN no A3 também)
            byte[]? assinatura = null;

            try
            {
                using var chave = cert.GetRSAPrivateKey();
                assinatura = chave?.SignData(dados, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch
            {
                Console.WriteLine("{}");
                return;
            }

            // ===== OIDs de política + provider
            var policyOids = ExtractPolicyOids(cert);
            bool isA3Policy = policyOids.Any(oid => oid.StartsWith("2.16.76.1.2.3.", StringComparison.Ordinal));
            bool isA1Policy = policyOids.Any(oid => oid.StartsWith("2.16.76.1.2.1.", StringComparison.Ordinal));
            var (providerIsHardware, providerName) = DetectHardwareProvider(cert);

            bool isA3 = isA3Policy || providerIsHardware;
            string tipoCert = isA3 ? "A3" : (isA1Policy ? "A1" : (providerIsHardware ? "A3" : "A1"));

            var result = new
            {
                subject = cert.Subject,
                issuer = cert.Issuer,
                cpf = ExtractCpf(cert),
                validFrom = cert.NotBefore.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                validTo = cert.NotAfter.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                pem = Convert.ToBase64String(cert.RawData),
                cadeiaValida,
                revogado,
                assinatura = Convert.ToBase64String(assinatura ?? Array.Empty<byte>()),
                dadoOriginal = Convert.ToBase64String(dados),
                // ---- Campos novos/ajudantes ----
                isHardware = isA3,          // compat: consideramos A3 quando política disser A3 ou provider for hardware
                tipoCert,
                providerName,
                providerIsHardware,
                isA3Policy,
                policyOids
            };

            Console.WriteLine(JsonSerializer.Serialize(result));
            store.Close();
        }
        else
        {
            Console.WriteLine("{}");
        }
    }

    /// <summary>
    /// Lê a extensão Certificate Policies (OID 2.5.29.32) e extrai os Policy OIDs.
    /// Na ICP-Brasil, A1 = 2.16.76.1.2.1.* | A3 = 2.16.76.1.2.3.* (ITI).
    /// </summary>
    static List<string> ExtractPolicyOids(X509Certificate2 cert)
    {
        var list = new List<string>();
        var ext = cert.Extensions.Cast<X509Extension>()
            .FirstOrDefault(e => e?.Oid?.Value == "2.5.29.32"); // Certificate Policies

        if (ext == null) return list;

        try
        {
            var reader = new AsnReader(ext.RawData, AsnEncodingRules.DER);
            var seq = reader.ReadSequence(); // CertificatePolicies ::= SEQUENCE OF PolicyInformation
            while (seq.HasData)
            {
                var policyInfo = seq.ReadSequence();
                // policyIdentifier OBJECT IDENTIFIER
                var oid = policyInfo.ReadObjectIdentifier();
                list.Add(oid);

                // policyQualifiers [opcional]
                if (policyInfo.HasData)
                {
                    // ignoramos os qualifiers
                    policyInfo.ReadEncodedValue();
                }
            }
        }
        catch
        {
            // Fallback: string format — procura os OIDs por regex simples
            try
            {
                var s = new AsnEncodedData(ext.Oid, ext.RawData).Format(true);
                // Extrai padrões "n.n.n..." simples
                var parts = s.Split(new[] { ' ', '\r', '\n', '\t', ',', ';', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    if (p.Count(c => c == '.') >= 2 && char.IsDigit(p[0]) && p.All(ch => char.IsDigit(ch) || ch == '.'))
                        list.Add(p);
                }
            }
            catch { /* ignora */ }
        }

        return list.Distinct().ToList();
    }

    static bool TryDecodeChallenge(string value, out byte[] challenge)
    {
        challenge = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192)
        {
            return false;
        }

        try
        {
            challenge = Convert.FromBase64String(value);
            return challenge.Length is >= 16 and <= 4096;
        }
        catch (FormatException)
        {
            challenge = Array.Empty<byte>();
            return false;
        }
    }

    /// <summary>
    /// Extrai o CPF do Subject ou do otherName ICP-Brasil de pessoa física
    /// (OID 2.16.76.1.3.1, dentro da extensão Subject Alternative Name).
    /// </summary>
    static string ExtractCpf(X509Certificate2 cert)
    {
        var subjectCpf = ExtractValidCpf(cert.Subject ?? "");
        if (subjectCpf != null) return subjectCpf;

        var san = cert.Extensions.Cast<X509Extension>()
            .FirstOrDefault(e => e?.Oid?.Value == "2.5.29.17");
        if (san == null) return "(desconhecido)";

        try
        {
            var reader = new AsnReader(san.RawData, AsnEncodingRules.DER);
            var generalNames = reader.ReadSequence();
            while (generalNames.HasData)
            {
                var generalName = generalNames.ReadEncodedValue().ToArray();
                if (!ContainsIcpBrasilPessoaFisicaOid(generalName)) continue;

                var cpf = ExtractCpfFromIcpBrasilOtherName(generalName);
                if (cpf != null) return cpf;
            }
        }
        catch
        {
            // Certificados fora do formato esperado seguem sem CPF identificado.
        }

        return "(desconhecido)";
    }

    static bool ContainsIcpBrasilPessoaFisicaOid(byte[] value)
    {
        // DER de 2.16.76.1.3.1: 60 4C 08 01 03 01
        byte[] oid = { 0x60, 0x4C, 0x08, 0x01, 0x03, 0x01 };
        for (int i = 0; i <= value.Length - oid.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < oid.Length; j++)
            {
                if (value[i + j] != oid[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }

    static string? ExtractCpfFromIcpBrasilOtherName(byte[] encodedOtherName)
    {
        // O conteúdo ICP-Brasil inicia com data de nascimento (8 dígitos),
        // seguida pelo CPF (11 dígitos). O formato pode ser PrintableString,
        // UTF8String ou conter bytes de codificação entre os caracteres.
        string visible = Encoding.Latin1.GetString(encodedOtherName);
        foreach (Match match in Regex.Matches(visible, @"(?<!\d)\d{8}(?<cpf>\d{11})"))
        {
            string cpf = match.Groups["cpf"].Value;
            if (IsValidCpf(cpf)) return cpf;
        }

        string digits = new string(encodedOtherName
            .Where(b => b >= (byte)'0' && b <= (byte)'9')
            .Select(b => (char)b)
            .ToArray());
        for (int i = 0; i + 19 <= digits.Length; i++)
        {
            string cpf = digits.Substring(i + 8, 11);
            if (IsValidCpf(cpf)) return cpf;
        }
        return null;
    }

    static string? ExtractValidCpf(string text)
    {
        foreach (Match match in Regex.Matches(text, @"(?<!\d)(\d{11})(?!\d)"))
        {
            if (IsValidCpf(match.Groups[1].Value)) return match.Groups[1].Value;
        }
        return null;
    }

    static bool IsValidCpf(string cpf)
    {
        if (cpf.Length != 11 || cpf.Any(c => c < '0' || c > '9') || cpf.Distinct().Count() == 1)
            return false;
        int Sum(int length) => Enumerable.Range(0, length).Sum(i => (cpf[i] - '0') * (length + 1 - i));
        int digit1 = (Sum(9) * 10) % 11;
        if (digit1 == 10) digit1 = 0;
        int digit2 = (Sum(10) * 10) % 11;
        if (digit2 == 10) digit2 = 0;
        return digit1 == cpf[9] - '0' && digit2 == cpf[10] - '0';
    }

    /// <summary>
    /// Heurística do provider local: tenta detectar Smart Card/Token (hardware) vs Software.
    /// Útil como apoio; A3 em nuvem pode aparecer como "Software" aqui.
    /// </summary>
    static (bool providerIsHardware, string providerName) DetectHardwareProvider(X509Certificate2 cert)
    {
        try
        {
#pragma warning disable SYSLIB0028
            if (cert.PrivateKey is RSACryptoServiceProvider cspLegacy)
            {
                var info = cspLegacy.CspKeyContainerInfo;
                if (info != null)
                {
                    var provName = info.ProviderName ?? string.Empty;
                    if (info.HardwareDevice) return (true, provName);
                    if (provName.IndexOf("Smart Card", StringComparison.OrdinalIgnoreCase) >= 0)
                        return (true, provName);
                    return (false, provName);
                }
            }
#pragma warning restore SYSLIB0028

            using (var rsa = cert.GetRSAPrivateKey())
            {
#if NET6_0_OR_GREATER
                if (rsa is RSACng cng)
                {
                    var provName = cng.Key?.Provider?.ToString() ?? string.Empty;
                    if (provName.IndexOf("Smart Card", StringComparison.OrdinalIgnoreCase) >= 0)
                        return (true, provName);
                    if (provName.IndexOf("Software", StringComparison.OrdinalIgnoreCase) >= 0)
                        return (false, provName);
                    return (false, provName);
                }
#endif
            }
        }
        catch
        {
            // Ignora e cai no fallback
        }
        return (false, string.Empty);
    }
}
