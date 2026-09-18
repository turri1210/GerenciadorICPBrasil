# Política de privacidade

Última atualização: 18 de setembro de 2026.

O Assistente ICP processa localmente informações técnicas necessárias para verificar e configurar a estação Windows.

## Edição pública

- Não transmite telemetria, hostname, endereço MAC, chaves privadas, PINs, senhas ou arquivos de licença.
- Mantém localmente eventos técnicos de instalação e funcionamento para diagnóstico.
- Cria backup de licença biométrica somente quando solicitado pelo usuário.
- Protege o backup local para o usuário atual com a proteção de dados do Windows.
- Não oferece backup em nuvem nesta edição.

Algumas funcionalidades abrem sites oficiais ou baixam componentes solicitados pelo usuário. Esses acessos ficam sujeitos às políticas dos respectivos fornecedores.

A integração com portais de certificado digital abre um serviço exclusivamente em `127.0.0.1:8357`. Solicitações originadas de navegadores são limitadas aos domínios autorizados no código-fonte. Chaves privadas e PINs permanecem sob controle do provedor criptográfico instalado no Windows.

## Alterações no computador

Ações administrativas são opcionais e dependem de solicitação explícita e confirmação do Controle de Conta de Usuário do Windows.

## Contato

Questões sobre privacidade podem ser encaminhadas para `dev@yez.digital`.
