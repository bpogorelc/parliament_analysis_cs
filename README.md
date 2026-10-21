# Parliament Agent

**Live demo:** [https://parliament-analysis-467685600833.europe-west1.run.app/](https://parliament-analysis-467685600833.europe-west1.run.app/)

An MCP-structured agentic system in **C# / ASP.NET Core** that analyses German Bundestag **Fraktion** (parliamentary group) affiliations from the public [DIP API](https://dip.bundestag.de/%C3%BCber-dip/hilfe/api), exposes capabilities as tools, and uses an LLM only for tool selection and natural-language summaries — never for inventing numbers.

| | |
| --- | --- |
| **Production** | [Cloud Run (europe-west1)](https://parliament-analysis-467685600833.europe-west1.run.app/) |
| **Health** | [/api/health](https://parliament-analysis-467685600833.europe-west1.run.app/api/health) |
| **Stack** | .NET 8 · ASP.NET Core Minimal APIs · Model Context Protocol · Groq · Docker · Google Cloud Run |
| **Domain** | Bundestag DIP person data → Fraktion affiliation & percentage shares |

> **Note:** Cloud Run scales to zero on the free tier. The first request after idle time may take 30–60 seconds to cold-start. Loading a Fraktion distribution then paginates DIP person data and can take several additional seconds.

---

## Why this project

Built as a first-customer / coding-challenge delivery that demonstrates:

- Integrating a real public government API with production quirks (auth headers, pagination, messy affiliation fields)
- Keeping **deterministic business logic** separate from the LLM
- Implementing the **Model Context Protocol (MCP)** as a real tool boundary — including a dual path (`direct` vs `mcp`) that works in a serverless container
- Shipping a demoworthy web UI, HTTP API, CLI, and cloud deployment

**Interview pitch**

> DIP access and Fraktion math are deterministic services. MCP tools are the capability boundary. The LLM never invents percentages — it only summarises tool output or chooses which tool to call. Chat can execute the same tools in-process or over an in-memory MCP client↔server pair so the protocol path stays reliable on Cloud Run.

---

## Try the live demo

1. Open [the production UI](https://parliament-analysis-467685600833.europe-west1.run.app/) and wait until the status shows API / DIP readiness.
2. Under **Fraktionsverteilung**, keep Wahlperiode `20` → **Laden**.
3. Optionally click **Zusammenfassen** for an LLM summary (Groq).
4. In **Agent-Chat**, ask for example:
   - `Wer ist Friedrich Merz?`
   - `Wie ist die Fraktionsverteilung in der 20. Wahlperiode?`
5. Toggle **Direkt (Tool Calling)** vs **MCP (Client ↔ Server)** and compare the reported path / tools under the answer.

---

## Features

| Capability | Details |
| --- | --- |
| Fraktion distribution | Percentage shares per parliamentary group for a legislative period (Wahlperiode) |
| Person lookup | Resolve a politician by name or DIP id, with ambiguity handling and data-quality notes |
| LLM summary | Concise DE/EN summary grounded only in tool JSON (fallback template if Groq is unavailable) |
| Agentic chat | Groq tool-calling loop (max 4 rounds) with keyword-router fallback |
| Dual execution path | `direct` = in-process handlers · `mcp` = MCP Client ↔ Server over in-memory pipes |
| MCP stdio server | CLI entry for external hosts (e.g. Cursor) |
| Web UI + REST API | Customer-facing shell over the same tool handlers |
| Production deploy | Multi-stage Docker image on Google Cloud Run |

---

## Architecture

```text
User (Web UI / CLI / HTTP)
        │
        ▼
   ChatHost / API
        │  Groq selects tool  (or keyword router)
        ├──────────────────┬─────────────────────┐
        │ mode=direct      │ mode=mcp            │
        ▼                  ▼                     │
 ParliamentTools    McpClient ◄──pipes──► McpServer
        │                  │                     │
        └────────┬─────────┘                     │
                 ▼                               │
    Affiliation + Distribution services          │
                 ▼                               │
            DipClient  ──────────────────────────┘
                 ▼
         DIP API (/person)
```

**Design rule:** numbers always come from tools/services. The LLM summarises or routes; it does not invent Fraktion shares.

Tool contracts (source of truth): [`schemas/tools.json`](schemas/tools.json)

| Tool | Purpose |
| --- | --- |
| `get_person_info` | Politician identity, roles, Fraktion for a Wahlperiode |
| `get_fraktion_distribution` | Counts + percentages (+ methodology / notes) |
| `summarize_fraktion_distribution` | Distribution + grounded natural-language summary |

More detail: [`docs/architecture.md`](docs/architecture.md)

---

## Tech stack

| Layer | Choice |
| --- | --- |
| Language / runtime | C# · .NET 8 |
| Web | ASP.NET Core Minimal APIs · static UI |
| Agent protocol | Official [ModelContextProtocol](https://csharp.sdk.modelcontextprotocol.io/) C# SDK |
| LLM | Groq (OpenAI-compatible chat completions + tool calling) |
| HTTP client | `HttpClient` against DIP search API |
| Config | Environment variables · `.env` locally |
| Tests | xUnit (affiliation + distribution) |
| Deploy | Docker · Google Cloud Run |

---

## Repository layout

```text
parliament_analysis_cs/
├── src/
│   ├── ParliamentAnalysis.Core/     # DIP, services, tools, LLM, chat, MCP
│   ├── ParliamentAnalysis.Web/      # HTTP API + wwwroot UI
│   └── ParliamentAnalysis.Cli/      # chat | distribution | person | summarize | mcp
├── tests/
│   └── ParliamentAnalysis.Tests/
├── schemas/tools.json               # MCP tool I/O contracts
├── docs/architecture.md
├── Dockerfile
└── .env.example
```

---

## HTTP API

| Method | Path | Body / notes |
| --- | --- | --- |
| `GET` | `/api/health` | Liveness + whether DIP / Groq keys are configured |
| `POST` | `/api/distribution` | `{ "wahlperiode": 20, "include_unknown": true }` |
| `POST` | `/api/summary` | `{ "wahlperiode": 20, "language": "de" }` |
| `POST` | `/api/person` | `{ "query": "Friedrich Merz", "wahlperiode": 20 }` |
| `POST` | `/api/chat` | `{ "question": "...", "mode": "direct" \| "mcp" }` → `answer`, `engine`, `path`, `tools_called` |

---

## Affiliation logic (explainable)

For a chosen Wahlperiode (default **20**):

1. Prefer `person_roles` entries that match the period and have a Fraktion.
2. Prefer roles that look like MdB / parliamentary membership over ministerial-only roles.
3. Fall back to top-level `fraktion` fields, then any role Fraktion (with a note).
4. Otherwise classify as `Unknown` (optional bucket).

Percentages = `count(fraktion) / total_persons_in_scope × 100` (2 decimal places). Data inconsistencies are surfaced in `notes` rather than hidden.

---

## Run locally

**Prerequisites:** .NET 8 SDK · optional Groq API key

```bash
cp .env.example .env
# set DIP_API_KEY (portal key works) and optionally GROQ_API_KEY

dotnet run --project src/ParliamentAnalysis.Web
# → http://127.0.0.1:8000

dotnet run --project src/ParliamentAnalysis.Cli -- chat
dotnet run --project src/ParliamentAnalysis.Cli -- distribution --wahlperiode 20
dotnet run --project src/ParliamentAnalysis.Cli -- person "Friedrich Merz"
dotnet run --project src/ParliamentAnalysis.Cli -- summarize --wahlperiode 20 --language de
dotnet run --project src/ParliamentAnalysis.Cli -- mcp   # stdio MCP server
```

```bash
dotnet test tests/ParliamentAnalysis.Tests
```

### Environment variables

| Variable | Required | Purpose |
| --- | --- | --- |
| `DIP_API_KEY` | Yes | DIP search API key |
| `DIP_API_BASE_URL` | No | Default `https://search.dip.bundestag.de/api/v1` |
| `DIP_SSL_VERIFY` | No | Default `true` (set `false` only on broken local trust stores) |
| `GROQ_API_KEY` | No | Enables LLM summary + Groq tool-calling chat |
| `GROQ_MODEL` | No | Default `openai/gpt-oss-120b` |
| `DEFAULT_WAHLPERIODE` | No | Default `20` |
| `MCP_TRANSPORT` | No | `memory` (default) or `stdio` |

---

## Production deployment (Google Cloud Run)

The live service is deployed from the multi-stage [`Dockerfile`](Dockerfile) to **Google Cloud Run** in `europe-west1`:

**https://parliament-analysis-467685600833.europe-west1.run.app/**

```bash
gcloud auth login
gcloud config set project parliament-analysis-cs
gcloud run deploy parliament-analysis \
  --source . \
  --region europe-west1 \
  --allow-unauthenticated \
  --set-env-vars "DIP_API_KEY=...,GROQ_API_KEY=...,DIP_SSL_VERIFY=true,MCP_TRANSPORT=memory"
```

The container binds `0.0.0.0:$PORT`. Chat `mode=mcp` uses in-process memory transport (no subprocess) so MCP works inside Cloud Run.

---

## What this demonstrates on a CV

- **Agentic systems with guardrails** — LLM for routing/summary, tools for facts  
- **MCP in production-shaped C#** — official SDK, dual direct/MCP paths, stdio + in-memory transports  
- **Real API engineering** — auth quirks, cursor pagination, explainable affiliation heuristics  
- **Full delivery loop** — domain services, API, UI, tests, Docker, cloud deploy  

---

## License / data

DIP data is provided by the German Bundestag’s public DIP interface. This project is a technical portfolio / coding-challenge delivery and is not an official Bundestag product.
