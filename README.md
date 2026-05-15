# devops-cli · `azdo`

> CLI + MCP server in C#/.NET 8 per Azure DevOps — configurazione centrale, zero file per repo.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-linux%20%7C%20macOS%20%7C%20windows-lightgrey)]()
[![Inspired by](https://img.shields.io/badge/inspired%20by-wangkanai%2Fdevops--mcp-blue)](https://github.com/wangkanai/devops-mcp)

Ispirato a [`wangkanai/devops-mcp`](https://github.com/wangkanai/devops-mcp), ridisegnato con
**configurazione centrale** (invece di un file `.azure-devops.json` per ogni repository) e con la
capacità di girare sia come CLI tradizionale sia come **MCP server** su stdio per GitHub Copilot CLI,
Claude Desktop, e qualunque client MCP compatibile.

---

## Indice

- [Modello di configurazione](#modello-di-configurazione)
- [Build e installazione](#build-e-installazione)
- [Utilizzo](#utilizzo)
- [Struttura sorgenti](#struttura-sorgenti)
- [Integrazione MCP](#integrazione-mcp)
  - [Avvio manuale](#avvio-manuale-smoke-test)
  - [Registrazione in GitHub Copilot CLI](#registrazione-in-github-copilot-cli)
  - [Tool MCP esposti](#tool-mcp-esposti)
  - [Note di design](#note-di-design-su-mcp)
- [Sicurezza](#sicurezza)
- [Estensioni naturali](#estensioni-naturali)
- [Licenza](#licenza)

---

## Modello di configurazione

Un solo file di configurazione per tutte le org:

| OS | Percorso |
|----|----------|
| Linux / macOS | `~/.config/devops-cli/config.json` |
| Windows | `%APPDATA%\devops-cli\config.json` |

Il file viene salvato in chiaro con permessi `600` su Unix e mappa **nome org → PAT**
(più URL, default project, ecc.).

**Rilevazione automatica dell'org** — il CLI legge il `git remote get-url origin` della
directory corrente. Formati supportati:

```
https://dev.azure.com/{org}/{project}/_git/{repo}
https://{org}.visualstudio.com/{project}/_git/{repo}
git@ssh.dev.azure.com:v3/{org}/{project}/{repo}
{user}@vs-ssh.visualstudio.com:v3/{org}/{project}/{repo}
```

Se l'org rilevata non è censita, il CLI chiede il PAT interattivamente (input nascosto)
e lo salva nella config centrale. L'opzione `--org` su qualunque comando bypassa la
rilevazione automatica.

---

## Build e installazione

```bash
cd src/DevOpsCli
dotnet build -c Release
# binario: bin/Release/net8.0/azdo  (o azdo.exe su Windows)
```

**Pubblicazione single-file:**

```bash
dotnet publish -c Release -r linux-x64 -o ../../publish
sudo cp ../../publish/azdo /usr/local/bin/
```

---

## Utilizzo

```bash
# Vedere cosa rileva il CLI dal repo corrente
cd ~/Sources/riversync
azdo context

# Aggiungere manualmente un'org (alternativa al prompt automatico)
azdo config add --org riversync --pat <token> --project RiverSync

# Elenco org configurate (PAT mascherati)
azdo config list

# Work item — query WIQL
azdo wi query --wiql "SELECT [System.Id],[System.Title] FROM WorkItems WHERE [System.State]='Active'"

# Work item — crea Task figlio
azdo wi create --type Task --title "Implement auth" --parent 1234 --assigned-to me@eng.it

# Lista repository
azdo repo list

# Pull request attive
azdo pr list --status active

# Crea una PR dal branch corrente verso main
azdo pr create --title "Add login endpoint" \
  --description "Implements /auth/login" \
  --work-item 1234 \
  --draft

# PR con source/target espliciti e reviewer (GUID identity)
azdo pr create --repo MyRepo \
  --source feature/auth --target develop \
  --title "WIP auth" \
  --reviewer 11111111-2222-3333-4444-555555555555

# Pipeline — trigger
azdo build trigger --definition 42 --branch refs/heads/main
```

### Mappatura con i tool del repo originale

| Tool MCP originale          | Comando CLI                        |
|-----------------------------|------------------------------------|
| `get-current-context`       | `azdo context`                     |
| `get-work-items` (WIQL)     | `azdo wi query --wiql ...`         |
| `get-work-items` (by id)    | `azdo wi get --ids 1,2,3`          |
| `create-work-item`          | `azdo wi create ...`               |
| `update-work-item`          | `azdo wi update --id ...`          |
| `add-work-item-comment`     | `azdo wi comment --id ... --text`  |
| `get-repositories`          | `azdo repo list`                   |
| `get-pull-requests`         | `azdo pr list`                     |
| *(nuovo)*                   | `azdo pr create --title ...`       |
| `get-builds`                | `azdo build list`                  |
| `get-pipeline-status`       | `azdo build status --id ...`       |
| `trigger-pipeline`          | `azdo build trigger --definition`  |

---

## Struttura sorgenti

```
src/DevOpsCli/
├── Program.cs                       # entry-point, wiring System.CommandLine
├── Config/
│   ├── CentralConfig.cs             # modello del config centrale
│   ├── ConfigStore.cs               # load/save + perms 600
│   └── PatPrompt.cs                 # prompt interattivo per PAT mancante
├── Context/
│   └── OrgDetector.cs               # parsing git remote → org/project/repo
├── AzureDevOps/
│   ├── AzureDevOpsClient.cs         # wrapper HttpClient + PAT Basic auth
│   └── ClientFactory.cs             # session = detect → ensure PAT → client
└── Commands/
    ├── ContextCommand.cs
    ├── ConfigCommand.cs
    ├── WorkItemCommand.cs
    ├── RepoCommand.cs
    ├── PullRequestCommand.cs
    └── BuildCommand.cs
```

---

## Integrazione MCP

Lo stesso binario `azdo` può girare come **MCP server** su stdio, così Copilot CLI
(o Claude Desktop, o qualunque client MCP) può chiamare i tool Azure DevOps con la
stessa logica di detection + config centrale.

### Avvio manuale (smoke test)

```bash
# Lista del manifest (per ispezione, NON è MCP wire format)
azdo mcp list-tools

# Server stdio (parla JSON-RPC 2.0 line-delimited)
azdo mcp serve < integration/smoke-test.jsonl
```

Il file `integration/smoke-test.jsonl` contiene una sequenza:
`initialize` → `notifications/initialized` → `tools/list` → `tools/call azdo_context`.

### Registrazione in GitHub Copilot CLI

1. **Build e installa** il binario in un path stabile:
   ```bash
   dotnet publish src/DevOpsCli -c Release -r linux-x64 \
     --self-contained false -o /usr/local/bin
   ```

2. **Trova il file di config MCP** di Copilot CLI (versione preview agentic):
   ```bash
   copilot --help | grep -i mcp
   # tipicamente: ~/.config/github-copilot/cli/mcp-config.json
   ```

3. **Aggiungi la voce `azdo`** come da `integration/copilot-cli-mcp.json`:
   ```json
   {
     "mcpServers": {
       "azdo": { "command": "/usr/local/bin/azdo", "args": ["mcp", "serve"] }
     }
   }
   ```

4. **Pre-registra le org** da shell normale (in modalità MCP non c'è TTY per i prompt):
   ```bash
   azdo config add --org riversync --pat <token> --project RiverSync
   ```

5. **Riavvia Copilot CLI.** Da dentro una repo Azure DevOps puoi usare linguaggio naturale:
   > *"crea una task figlia dell'Epic 1234 con titolo 'Add login endpoint' e assegnala a me"*

   Copilot risolverà l'org dalla `cwd` (git remote), userà il PAT salvato e
   chiamerà `azdo_wi_create`.

### Tool MCP esposti

Tutti i tool accettano `org` e `project` come override degli automatismi.

| Tool                  | Argomenti chiave                                | Equivalente CLI            |
|-----------------------|-------------------------------------------------|----------------------------|
| `azdo_context`        | —                                               | `azdo context`             |
| `azdo_wi_query`       | `wiql`                                          | `azdo wi query`            |
| `azdo_wi_get`         | `ids`, `fields`                                 | `azdo wi get`              |
| `azdo_wi_create`      | `type`, `title`, `parent`, `assignedTo`, …      | `azdo wi create`           |
| `azdo_wi_update`      | `id`, `state`, `title`, `parent`, …             | `azdo wi update`           |
| `azdo_wi_comment`     | `id`, `text`                                    | `azdo wi comment`          |
| `azdo_repo_list`      | —                                               | `azdo repo list`           |
| `azdo_pr_list`        | `repo`, `status`, `creator`                     | `azdo pr list`             |
| `azdo_pr_create`      | `title`, `source`, `target`, `repo`, `draft`, … | `azdo pr create`           |
| `azdo_build_list`     | `top`, `definition`                             | `azdo build list`          |
| `azdo_build_status`   | `id`                                            | `azdo build status`        |
| `azdo_build_trigger`  | `definition`, `branch`                          | `azdo build trigger`       |

### Note di design su MCP

- **Stdio è sacro** — tutto il logging diagnostico va su `stderr` (`Console.Error`).
  Su `stdout` passa solo JSON-RPC; qualunque altra scrittura rompe il client.
- **No prompt interattivi in MCP** — `PatPrompt` rileva `Console.IsInputRedirected`
  e lancia un'eccezione con messaggio chiaro (`"Run azdo config add --org X"`),
  restituita come `isError: true`. Il setup viene fatto una sola volta da shell normale.
- **Detection org automatica** — Copilot CLI eredita la `cwd` corrente, quindi
  `OrgDetector` legge il `git remote` della repo attiva senza configurazione aggiuntiva.

---

## Sicurezza

- PAT salvati in chiaro in `config.json` con permessi `600` (rw solo proprietario) su Unix.
- Su Windows i permessi NTFS ereditati dalla cartella profilo già limitano l'accesso.
- Il PAT non viene messo in cache oltre la durata del processo.
- Aggiungi `~/.config/devops-cli/` alla tua strategia di backup cifrato se necessario.

---

## Estensioni naturali

- `azdo config rotate --org X` per ruotare i PAT scaduti.
- Output strutturato (`--format json|table|csv`).
- Caching del WIT type per validare i tipi prima della `create`.
- Provider alternativi per lo storage del PAT (DPAPI / Keychain / libsecret) —
  vedi `ConfigStore.TryRestrictPermissions` come punto di estensione.

---

## Licenza

[MIT](LICENSE) — Copyright © 2026 Paolino Salamone.

Allineata alla licenza di [`wangkanai/devops-mcp`](https://github.com/wangkanai/devops-mcp) (anch'esso MIT).