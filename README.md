# Lab AI Assistant

A Retrieval-Augmented Generation (RAG) application for laboratory compliance assistance. Upload SOPs, instrument manuals, and regulatory documents, then ask questions with grounded answers and full audit trail.

## Architecture

- **Domain**: Clean Architecture ports (interfaces), entities, value objects, enums
- **Application**: RAG pipeline orchestration, PII masking, prompt composition
- **Infrastructure**: EF Core persistence, Gemini AI adapter, document parsing, chunking strategies
- **Web**: Blazor Server UI, REST API endpoints, cookie authentication

## Features

- **Document Ingest**: Upload Markdown, CSV, JSON, PDF files with automatic chunking
- **Semantic Search**: Vector embeddings via Gemini embedding model, brute-force top-K retrieval
- **Grounded Q&A**: Answers cite source fragments [S1]..[Sn], refuse when context is insufficient
- **Audit Journal**: Append-only log of all AI queries with PII masking, prompt hashing, refusal detection
- **Role-Based Access**: Operator (ask questions), Analyst (ingest + ask), Administrator (full access)

## Quick Start

### Prerequisites

- .NET 10 SDK
- SQLite (bundled, no separate installation)
- Gemini API key (for live AI; golden-set tests work without it)

### Running Locally

```bash
# Clone and enter the project
git clone <repository-url>
cd Lab-AI-Assistant

# Set up environment variables
cp .env.example .env
# Edit .env and add your GEMINI_API_KEY

# Run the application
dotnet run --project src/LabAi.Web/LabAi.Web.csproj
```

The app starts on `http://localhost:5000`. Default demo accounts are seeded on first run:

| Username | Password     | Role          |
|----------|--------------|---------------|
| admin    | (see console)| Administrator |
| analyst  | (see console)| Analyst       |
| operator | (see console)| Operator      |

Passwords are printed to console only (never logged) and use the value from `Demo:Password` in `.env`.

### Docker

```bash
docker compose up --build
```

## Project Structure

```
src/
├── LabAi.Domain/           # Entities, interfaces, value objects, enums
├── LabAi.Application/      # RAG pipeline, PII masking, prompt composition
├── LabAi.Infrastructure/   # EF Core, Gemini adapter, parsing, chunking
└── LabAi.Web/              # Blazor Server UI, API endpoints
tests/
└── LabAi.Tests/            # Unit tests, architecture tests, golden-set tests
```

## Milestones

- **M1**: Semantic Kernel integration + basic chat ✅
- **M2**: Document ingest pipeline ✅
- **M3**: Embeddings + vector store + top-K search ✅
- **M4**: Grounded generation + citations + refusal detection ✅
- **M5**: AI audit log + PII masking ✅
- **M6**: Chat UI + sources panel + audit journal ✅
- **M7**: Tests + README ✅

## Key Design Decisions

See [docs/adr/](docs/adr/) for Architecture Decision Records:

- **ADR-0001**: Brute-force vector search for demo scale
- **ADR-0002**: Gemini via Semantic Kernel alpha connector
- **ADR-0003**: Cookie authentication for Blazor Server + API
- **ADR-0004**: Fail-closed audit ordering

## Testing

```bash
# Run all tests
dotnet test

# Run golden-set tests (no API key required)
dotnet test --filter "Category=GoldenSet"

# Run live Gemini tests (requires API key)
dotnet test --filter "Category=LiveGemini"
```

## License

Internal project — not for external distribution.
