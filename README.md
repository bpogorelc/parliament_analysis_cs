# Parliament Agent (C#)

ASP.NET Core port of the MCP-structured agent that analyses German Bundestag **Fraktion** (parliamentary group) affiliations via the public [DIP API](https://dip.bundestag.de/%C3%BCber-dip/hilfe/api), summarises results with Groq, and exposes the same capabilities through tools, CLI, and a web UI.

Tool contracts: [`schemas/tools.json`](schemas/tools.json).

## Solution idea

> DIP access and Fraktion math are **deterministic services**.  
> **MCP tools** are the capability boundary.  
> The **LLM never invents percentages** — it only summarises tool output or selects which tool to call.  
> Chat can run tools **directly** or via an **MCP client ↔ MCP server** path (in-process memory transport by default — Render-safe).

```text
DIP API (/person)
    → DipClient              (ApiKey + Origin, cursor pagination)
    → Affiliation / Distribution services
    → ParliamentTools        (get_person_info, get_fraktion_distribution, summarize_…)
        ├─ mode=direct → in-process handlers
        ├─ mode=mcp    → McpClient ↔ McpServer (System.IO.Pipelines memory transport)
        ├─ Cli `mcp`   → stdio MCP server (Cursor / external hosts)
        └─ Web UI / HTTP API
```

## Prerequisites

- .NET 8 SDK
- `DIP_API_KEY` (public portal key works; see `.env.example`)
- Optional: `GROQ_API_KEY` for LLM summaries and tool-calling chat

```bash
cp .env.example .env
# edit GROQ_API_KEY if desired
```

## Run locally

```bash
# Web UI + API (http://127.0.0.1:8000)
dotnet run --project src/ParliamentAnalysis.Web

# Interactive chat
dotnet run --project src/ParliamentAnalysis.Cli -- chat

# One-shot CLIs
dotnet run --project src/ParliamentAnalysis.Cli -- distribution --wahlperiode 20
dotnet run --project src/ParliamentAnalysis.Cli -- person "Friedrich Merz"
dotnet run --project src/ParliamentAnalysis.Cli -- summarize --wahlperiode 20 --language de

# MCP stdio server (for Cursor / MCP hosts)
dotnet run --project src/ParliamentAnalysis.Cli -- mcp
```

## HTTP API

| Method | Path | Notes |
| --- | --- | --- |
| `GET` | `/api/health` | Liveness + key configured flags |
| `POST` | `/api/distribution` | `{ "wahlperiode": 20, "include_unknown": true }` |
| `POST` | `/api/summary` | `{ "wahlperiode": 20, "language": "de" }` |
| `POST` | `/api/person` | `{ "query": "Friedrich Merz", "wahlperiode": 20 }` |
| `POST` | `/api/chat` | `{ "question": "...", "mode": "direct" \| "mcp" }` |

## Tests

```bash
dotnet test tests/ParliamentAnalysis.Tests
```

## Deploy on Render (Docker)

Render has **no native .NET runtime**. Deploy as a **Docker** web service:

1. Put this project in a Git repo (or push a prebuilt image).
2. New → Web Service → Language **Docker**.
3. Set environment variables from `.env.example` (`DIP_API_KEY`, optional `GROQ_API_KEY`, …).
4. Health check path: `/api/health`.
5. The app binds `0.0.0.0:$PORT` (see `Program.cs`).

```bash
docker build -t parliament-analysis .
docker run --rm -p 10000:10000 --env-file .env -e PORT=10000 parliament-analysis
```

## Layout

```text
src/
  ParliamentAnalysis.Core/   # config, DIP, services, tools, LLM, chat, MCP
  ParliamentAnalysis.Web/    # ASP.NET Minimal APIs + wwwroot UI
  ParliamentAnalysis.Cli/    # chat / distribution / mcp stdio
tests/
  ParliamentAnalysis.Tests/
schemas/tools.json
Dockerfile
```

## Interview pitch

DIP client and affiliation logic are deterministic. MCP exposes them as tools via the official ModelContextProtocol C# SDK. The LLM never invents percentages — it only summarises tool output or chooses which tool to call. On Render, chat `mode=mcp` uses in-process pipe transports so the protocol path works without subprocesses.
