# Migração do atualizador antigo para o GitHub

Publique `app-version.json` em `https://sistema.redeicpbrasil.com.br/gerenciador/app-version.json`. As versões antigas que consultam esse endereço e aceitam instaladores externos baixarão a versão 1.2.21 do GitHub, validarão seu SHA-256 e, após a instalação, consultarão as releases do GitHub diretamente.

O instalador e seu SHA-256 foram conferidos contra a release pública `v1.2.21`. O instalador tem `ProductVersion` 1.2.21 e foi assinado pelo certificado Rede ICP Brasil de impressão digital `038406E8CB7700739D12CD73A911DF5E90DED643`.

A versão 1.2.13 não pode usar essa ponte: seu código exige que o download permaneça no domínio antigo e que a assinatura seja da SignPath Foundation. Quem estiver nela precisa instalar a versão 1.2.21 manualmente. A versão 1.2.21 já consulta o GitHub e não depende deste manifesto.
