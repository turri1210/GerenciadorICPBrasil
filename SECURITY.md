# Política de segurança

## Relato de vulnerabilidades

Não publique detalhes exploráveis em uma issue pública. Envie o relato para `dev@yez.digital` com descrição, impacto, versão afetada e passos mínimos de reprodução.

## Segredos

O aplicativo desktop é tratado como cliente público: nenhuma credencial compilada é considerada secreta. Certificados, PFX, senhas, tokens e chaves privadas não podem ser versionados.

Uma futura integração autenticada com backend deverá usar identidade individual por instalação ou OAuth 2.0 com PKCE/Device Flow, autorização no servidor e proteção contra repetição. Segredos estáticos compartilhados no cliente são proibidos.

## Releases

Releases oficiais são produzidas exclusivamente pelo workflow protegido, revisadas manualmente e assinadas pelo SignPath Foundation. Artefatos de terceiros não recebem a assinatura do projeto.
