# Configuração SignPath

O instalador Inno Setup exige duas solicitações de assinatura por release:

1. assinar o aplicativo e seus helpers próprios produzidos pelo build público;
2. compilar o instalador com esses arquivos assinados e assinar o EXE final.

Antes de habilitar o workflow de release, crie no SignPath as configurações equivalentes aos XMLs desta pasta e configure no GitHub:

- segredo `SIGNPATH_API_TOKEN`;
- variáveis `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`;
- variáveis `SIGNPATH_APP_ARTIFACT_CONFIG_SLUG` e `SIGNPATH_INSTALLER_ARTIFACT_CONFIG_SLUG`;
- variável `SIGNPATH_ENABLED=true` somente depois da aprovação e dos testes.

Todas as solicitações de release devem exigir aprovação manual. O workflow anterior à assinatura deve rodar integralmente em runners hospedados pelo GitHub.
