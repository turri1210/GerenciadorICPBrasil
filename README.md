# Gerenciador ICP Brasil

Aplicativo Windows de código aberto para preparar, verificar e configurar estações que utilizam certificados digitais no ecossistema ICP-Brasil.

## Estado do projeto

Este repositório contém somente código-fonte e recursos redistribuíveis. Binários gerados, certificados, drivers, SDKs e instaladores proprietários não são versionados.

O produto usa .NET 8 e Windows App SDK. Todas as funcionalidades pertencem à mesma versão e são distribuídas pelo mesmo instalador EXE, produzido com Inno Setup. A distribuição é self-contained: o .NET e o Windows App SDK necessários acompanham o programa e não são instalados separadamente no computador.

O código da interface e das funcionalidades está em `Features/`. Executáveis auxiliares próprios, usados para isolamento técnico ou elevação administrativa, estão em `Helpers/` e são compilados, atualizados junto com o Gerenciador. Não existe atualização independente de módulos.

## Compilação

Pré-requisitos para compilar o aplicativo:

- Windows 10 versão 1809 ou posterior;
- SDK .NET 8.0.425;
- Visual Studio 2022 com desenvolvimento para desktop Windows, ou Build Tools equivalentes;
- Inno Setup 6 para gerar o instalador.

```powershell
dotnet restore GerenciadorICPBrasil.sln --locked-mode
./scripts/build-release.ps1 -Version 1.2.13
```

O script `scripts/build-release.ps1` é a referência para builds de release reproduzíveis. Ele produz o aplicativo, o seletor de certificados e o executor administrativo na mesma árvore de artefato.

## Componentes proprietários opcionais

Algumas funcionalidades precisam de software ou SDKs dos respectivos fabricantes para funcionar completamente. Eles não fazem parte deste projeto, não são cobertos pela licença Apache-2.0 e não são incluídos na release.

| Componente | Utilização | Obtenção |
|---|---|---|
| Futronic SDK (`FTRAPI.dll`, `ftrScanAPI.dll`, `ftrSDKHelper13.dll`) | Captura com a leitora Futronic FS88H | Obtenha diretamente com a Futronic ou distribuidor autorizado e aceite a licença do fabricante. |
| Middleware Certisign/SafeNet/Thales/Gemalto/OMNIKEY | Operação de tokens e leitoras de certificado | Utilize exclusivamente os canais oficiais do fabricante ou da Autoridade Certificadora. |
| Plataforma biométrica Certibio/Innovatrics | Operação e licenciamento biométrico | Obtenha com o fornecedor responsável pelo ambiente. |

Coloque arquivos obtidos legitimamente em `vendor/` somente para builds locais. Essa pasta é ignorada pelo Git e não deve ser incluída na release.

Para habilitar a captura Futronic em uma compilação local, coloque as três DLLs licenciadas em `vendor/futronic` e compile `Helpers/Biometrics/BiometriaFs88h.csproj` informando `FutronicSdkPath`, quando necessário. O executável resultante deve ficar em `Helpers/Biometrics` ao lado do aplicativo instalado. Esse helper e as DLLs do fabricante não fazem parte da release pública.

O produto funciona sem esses componentes. Somente as funcionalidades dependentes ficam indisponíveis até que os pré-requisitos sejam instalados pelo usuário.

## Atualizações

O Gerenciador ICP Brasil não baixa pacotes de funcionalidades nem mantém versões independentes. Qualquer alteração é publicada como uma nova versão completa do instalador. O atualizador consulta as releases públicas do GitHub, exige digest SHA-256 válido, assinatura Authenticode do certificado Rede ICP Brasil específico e metadados de produto antes da execução. Por ser autoassinado, o certificado pode continuar gerando avisos do Windows. A versão 1.2.13 instalada exige SignPath no domínio oficial e não pode migrar para esta linha apenas com uma alteração no manifesto.

## Privacidade

A edição pública não envia telemetria, hostname, endereço MAC ou arquivos de licença. Eventos técnicos ficam armazenados somente no computador. O backup biométrico é local, opcional e protegido pelo Windows para o usuário atual. Consulte [PRIVACY.md](PRIVACY.md).

## Segurança

Não coloque certificados, senhas, tokens ou chaves de API no repositório. Vulnerabilidades devem ser comunicadas conforme [SECURITY.md](SECURITY.md).

A integração de assinatura em `127.0.0.1` aceita somente origens autorizadas. Cada chamada a `/assinar` deve enviar um `challenge` Base64 aleatório, com pelo menos 16 bytes, emitido pelo servidor e consumido uma única vez. O servidor é responsável por vincular o desafio à sessão, origem, finalidade e prazo de validade e por rejeitar reutilizações.

Política de privacidade: [PRIVACY.md](PRIVACY.md). O programa não transfere informações para outros sistemas em rede, exceto quando essa ação é solicitada especificamente pela pessoa que instala ou opera o aplicativo.

## Licença e marcas

O código é disponibilizado sob a [Apache License 2.0](LICENSE). Nomes, logotipos e marcas da Rede ICP Brasil, YeZ, Certisign e de terceiros não são concedidos por essa licença. Consulte [NOTICE.md](NOTICE.md).
