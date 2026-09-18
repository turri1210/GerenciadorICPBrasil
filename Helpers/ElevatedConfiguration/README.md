# Configuração e auditoria

Módulo WPF para verificar e aplicar configurações de segurança no Windows.

```powershell
dotnet restore --locked-mode
dotnet publish ConfigAuditoria.csproj -c Release -r win-x64 --self-contained true
```

Não assine builds locais com certificados armazenados no repositório. Releases oficiais são assinadas exclusivamente pelo pipeline SignPath do projeto.
