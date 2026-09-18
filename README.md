# Assistente ICP

Aplicativo Windows de código aberto para preparar, verificar e configurar estações que utilizam certificados digitais no ecossistema ICP-Brasil.

## Estado do projeto

Este repositório contém somente código-fonte e recursos redistribuíveis. Binários gerados, certificados, drivers, SDKs e instaladores proprietários não são versionados.

O produto usa .NET 8 e Windows App SDK. Todas as funcionalidades pertencem à mesma versão e são distribuídas pelo mesmo instalador EXE, produzido com Inno Setup.

O código da interface e das funcionalidades está em `Features/`. Executáveis auxiliares próprios, usados para isolamento técnico ou elevação administrativa, estão em `Helpers/` e são compilados, atualizados e assinados junto com o Gerenciador. Não existe atualização independente de módulos.

## Compilação

Pré-requisitos para o aplicativo principal:

- Windows 10 versão 1809 ou posterior;
- SDK .NET 8.0.425;
- Visual Studio 2022 com desenvolvimento para desktop Windows, ou Build Tools equivalentes;
- Inno Setup 6 para gerar o instalador.

```powershell
dotnet restore AssistenteICP.sln --locked-mode
./scripts/build-release.ps1 -Version 1.2.13
```

O script `scripts/build-release.ps1` é a referência para builds de release reproduzíveis. Ele produz o aplicativo, o seletor de certificados e o executor administrativo na mesma árvore de artefato.

## Componentes proprietários opcionais

Algumas funcionalidades precisam de software ou SDKs dos respectivos fabricantes para funcionar completamente. Eles não fazem parte deste projeto, não são cobertos pela licença Apache-2.0 e não são assinados pelo certificado do projeto.

| Componente | Utilização | Obtenção |
|---|---|---|
| Futronic SDK (`FTRAPI.dll`, `ftrScanAPI.dll`, `ftrSDKHelper13.dll`) | Captura com a leitora Futronic FS88H | Obtenha diretamente com a Futronic ou distribuidor autorizado e aceite a licença do fabricante. |
| Middleware Certisign/SafeNet/Thales/Gemalto/OMNIKEY | Operação de tokens e leitoras de certificado | Utilize exclusivamente os canais oficiais do fabricante ou da Autoridade Certificadora. |
| Plataforma biométrica Certibio/Innovatrics | Operação e licenciamento biométrico | Obtenha com o fornecedor responsável pelo ambiente. |

Coloque arquivos obtidos legitimamente em `vendor/` somente para builds locais. Essa pasta é ignorada pelo Git e nunca deve ser enviada ao SignPath.

Para habilitar a captura Futronic em uma compilação local, coloque as três DLLs licenciadas em `vendor/futronic` e compile `Helpers/Biometrics/BiometriaFs88h.csproj` informando `FutronicSdkPath`, quando necessário. O executável resultante deve ficar em `Helpers/Biometrics` ao lado do aplicativo instalado. Esse helper e as DLLs do fabricante não fazem parte da release pública da SignPath.

O produto funciona sem esses componentes. Somente as funcionalidades dependentes ficam indisponíveis até que os pré-requisitos sejam instalados pelo usuário.

## Atualizações

O Assistente ICP não baixa pacotes de funcionalidades nem mantém versões independentes. Qualquer alteração é publicada como uma nova versão completa do instalador. O atualizador exige SHA-256 válido e assinatura Authenticode emitida para a SignPath Foundation antes de executar o instalador.

## Privacidade

A edição pública não envia telemetria, hostname, endereço MAC ou arquivos de licença. Eventos técnicos ficam armazenados somente no computador. O backup biométrico é local, opcional e protegido pelo Windows para o usuário atual. Consulte [PRIVACY.md](PRIVACY.md).

## Segurança

Não coloque certificados, senhas, tokens ou chaves de API no repositório. Vulnerabilidades devem ser comunicadas conforme [SECURITY.md](SECURITY.md).

## Code signing policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

Somente binários produzidos integralmente pelo código deste repositório e pelo workflow oficial podem receber a assinatura do projeto. Drivers, runtimes, DLLs, SDKs e instaladores de terceiros não são assinados como parte do Assistente ICP.

Cada release exige revisão e aprovação humana antes da assinatura. O fluxo usa runners hospedados pelo GitHub e verificação de origem do artefato.

Papéis do projeto, a serem preenchidos com os perfis públicos antes da candidatura:

- Authors/Committers: mantenedores autorizados da Rede ICP Brasil;
- Reviewers: revisores independentes designados;
- Approvers: responsáveis pela aprovação manual das solicitações de assinatura.

## Licença e marcas

O código é disponibilizado sob a [Apache License 2.0](LICENSE). Nomes, logotipos e marcas da Rede ICP Brasil, YeZ, Certisign e de terceiros não são concedidos por essa licença. Consulte [NOTICE.md](NOTICE.md).
