# Contexto para agentes — gdd-unity

Briefing de entrada para qualquer sessão de agente neste repo. O app que este
plugin conversa é o **GDD Manager** (`Doublehitgames/GddApp`), que tem o próprio
`AGENTS.md` — leia lá o que é o GDD, as refs `$[...]` e o formato da API.

---

## O que é

Extensão **de editor** do Unity que liga um projeto ao GDD dele no GDD Manager.
É **produto**: serve a qualquer estúdio, em qualquer projeto Unity — não presuma
estrutura, nomes de pasta nem convenção de nenhum jogo específico (o Colheita
Feliz é o primeiro projeto de teste, não o alvo).

Dois papéis, os dois no mesmo pacote:

- **Porta de leitura** — o programador lê o GDD sem sair do Unity: janela GDD
  acoplável e a página de design vinculada aparecendo no Inspector do asset.
- **Gerador de documentação** — o plugin escreve no GDD o lado técnico que
  ninguém lembraria de documentar: registro de cada build com o retrato dos
  pacotes e versões com que ela saiu, e depois o inventário de ferramentas de editor.

Escopo da primeira versão: janela GDD + Inspector, e registro de build com pacotes.

## Estrutura

| O quê | Onde |
|---|---|
| O pacote que o usuário instala | `Packages/com.doublehitgames.gdd/` (embutido) |
| Código de editor | `Packages/com.doublehitgames.gdd/Editor/` (asmdef só-editor) |
| Projeto sandbox para desenvolver | raiz do repo (`Assets/`, `ProjectSettings/`) |

Instalação por git URL:
`https://github.com/Doublehitgames/gdd-unity.git?path=/Packages/com.doublehitgames.gdd`

Unity mínimo: **6000.0**. O sandbox roda na 6000.0.68f1 — não subir a versão do
sandbox sem decidir junto subir o `"unity"` do `package.json`.

## Princípios que não se negociam

- **Só editor.** Todo código em assembly com `includePlatforms: ["Editor"]`.
  Nada vai para a build do jogador.
- **Ligar e mostrar, não sincronizar dado.** O plugin **não** puxa valores de
  balanceamento do GDD para dentro de asset. Isso é o binding de planilha / Remote
  Config que o GDD Manager podou em 2026-08; não reintroduzir por aqui.
- **Vínculo é do time, credencial é da pessoa.** O vínculo asset ↔ página fica
  versionado junto do projeto (ex.: `userData` do `.meta`), então todo o time
  vê. Login/token fica em `EditorPrefs`, nunca em arquivo do projeto.
- **Nunca direto ao Supabase.** Só a REST `/api/v1/*` do GDD Manager.

## Contrato com o GDD Manager

O que liga os dois repos é a API HTTP, não código compartilhado. Mudança de
contrato nasce no `GddApp` (e, se for tool nova, nas duas cópias do MCP de lá);
aqui só se consome. Não quebrar `/api/v1` — o que precisar mudar de forma
incompatível vira versão nova da API.

## Segredo em arquivo

Este repositório é **público**. Nada de chave, token ou URL com credencial em
arquivo versionado. Ao encontrar um segredo commitado, a **revogação** é o
conserto; reescrever histórico de repo público não des-vaza nada.

## Pendências de decisão

- **Licença**: ainda sem arquivo de licença (= todos os direitos reservados). O
  GddApp é GPL-3.0, mas GPL costuma assustar estúdio dentro do projeto do jogo —
  decidir antes de divulgar.
