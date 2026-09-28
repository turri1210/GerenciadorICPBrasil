# Publicação no GitHub

O repositório e suas releases precisam estar públicos para que o aplicativo consulte e baixe atualizações sem autenticação. O código-fonte não inclui certificados, senhas ou tokens.

## Release

1. Revise o commit e aguarde a aprovação do workflow `CI`.
2. Crie e envie uma tag no formato `v1.2.3`, com a versão correspondente à nova build.
3. Configure os segredos `CODE_SIGN_PFX_BASE64` (conteúdo Base64 do PFX Rede ICP Brasil) e `CODE_SIGN_PFX_PASSWORD` (senha do PFX) nas configurações do repositório. Nunca coloque esses valores no código, em commits ou no chat.
4. O workflow `Release` compila o aplicativo, confere a impressão digital do certificado, assina os binários e o instalador, e publica o EXE e `SHA256SUMS.txt` na release.
5. Confira se a release contém `GerenciadorICPBrasilSetup-<versão>.exe`, sem marcar a versão estável como pré-lançamento.

Para preencher `CODE_SIGN_PFX_BASE64` sem exibir o conteúdo no terminal, execute localmente o comando abaixo e cole o conteúdo da área de transferência no segredo do GitHub. Cadastre a senha do PFX diretamente em `CODE_SIGN_PFX_PASSWORD` na interface de segredos do repositório.

```powershell
$pfxPath = 'C:\Users\Bruno\OneDrive - Manuel Matos Consultoria Empresarial Ltda\Área de Trabalho\Certs\RedeIcpBrasil.pfx'
[Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)) | Set-Clipboard
```

O aplicativo consulta a API de releases do GitHub. Ele só aceita o instalador com o nome esperado, verifica o digest SHA-256 informado pelo GitHub, exige assinatura Authenticode do certificado Rede ICP Brasil com impressão digital `038406E8CB7700739D12CD73A911DF5E90DED643` e confere o nome e a versão do produto antes de iniciar o EXE. Como o certificado é autoassinado, o Windows pode mostrar avisos de confiança ou de editor desconhecido.

As versões já instaladas ainda consultam `https://sistema.redeicpbrasil.com.br/gerenciador/app-version.json`. A versão 1.2.13 instalada nesta máquina exige que o instalador esteja nesse domínio e tenha assinatura Authenticode da SignPath Foundation. O certificado local `RedeIcpBrasil.cer` identifica “Rede ICP Brasil” e não satisfaz essa regra específica. Portanto, alterar apenas o manifesto não migra essa versão para a release sem assinatura do GitHub. Uma versão anterior que usasse o certificado Rede ICP Brasil precisaria ser analisada separadamente. A primeira instalação desta linha na base 1.2.13 precisará ser manual ou usar uma ponte assinada por um editor aceito por ela.
