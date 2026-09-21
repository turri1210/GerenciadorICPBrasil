# Publicação no GitHub

Este repositório foi preparado para publicação pública. Crie no GitHub um repositório vazio, sem gerar README, licença ou `.gitignore`, porque esses arquivos já existem localmente.

## Primeira publicação

```powershell
git remote add origin <URL-DO-REPOSITORIO>
git push -u origin main
```

Antes de enviar, confirme que `git status --short` não apresenta alterações inesperadas. Não envie `artifacts/`, `bin/`, `obj/`, `vendor/`, certificados, SDKs proprietários ou instaladores.

## Proteções recomendadas

No GitHub, configure a branch `main` com:

- pull request obrigatório;
- aprovação de pelo menos um revisor;
- execução bem-sucedida do workflow `CI`;
- bloqueio de force push e exclusão da branch;
- alertas do Dependabot e secret scanning habilitados.

Proteja também as tags `v*`. Uma tag de release aciona o workflow de assinatura e só deve ser criada depois que a SignPath Foundation aprovar o projeto e as variáveis descritas em `.signpath/README.md` estiverem configuradas.

## Release oficial

Não publique como release oficial o instalador local de `artifacts/installer-unsigned`. Releases oficiais devem ser geradas pelo workflow `Release assinada`, passar pela aprovação manual da SignPath e conter:

- instalador com assinatura Authenticode válida da SignPath Foundation;
- arquivo `SHA256SUMS.txt` correspondente;
- tag no formato `v1.2.3` apontando para o commit revisado;
- manifesto hospedado `app-version.json` atualizado com a mesma versão, URL HTTPS oficial e SHA-256 do instalador assinado.
