# Running Lab AI Assistant with Docker

## Prerequisites

- Docker Engine 20.10+ or Docker Desktop
- Docker Compose v2 (included in Docker Desktop)
- A valid Gemini API key from [Google AI Studio](https://makersuite.google.com/)

## Quick Start

### 1. Configure environment

```bash
cp .env.example .env
# Edit .env and paste your GEMINI_API_KEY
```

### 2. Build and run

```bash
docker compose up --build
```

The application will be available at http://localhost:5000

### 3. Verify

```bash
curl http://localhost:5000/health
```

Expected response:
```json
{
  "status": "healthy",
  "geminiKeyPresent": true,
  "chatModel": "gemini-3.5-flash",
  "embeddingModel": "gemini-embedding-001",
  "vectorIndexSize": 0,
  "vectorDimension": 3072
}
```

## Development workflow

### Run tests inside container

```bash
docker compose run --rm labai dotnet test --filter "Category!=Live"
```

### View logs

```bash
docker compose logs -f
```

### Access the database

```bash
docker compose exec labai sqlite3 /app/data/lab.db ".tables"
```

### Stop and clean up

```bash
# Stop containers (data persists in volumes)
docker compose down

# Remove containers AND volumes (deletes all data)
docker compose down -v
```

## Production deployment

For production, consider:

1. **Secrets management**: Use Docker secrets or a vault instead of `.env`:
   ```yaml
   secrets:
     gemini_api_key:
       file: ./secrets/gemini_key.txt
   ```

2. **Reverse proxy**: Place nginx/Caddy in front for TLS termination:
   ```yaml
   services:
     caddy:
       image: caddy:2
       ports:
         - "443:443"
       volumes:
         - ./Caddyfile:/etc/caddy/Caddyfile
   ```

3. **Resource limits**: Already configured in `docker-compose.yml` (1G memory, 1 CPU).

4. **Backup strategy**: Mount named volumes to host paths for easy backup:
   ```bash
   docker volume inspect labai-data
   tar czf lab-backup.tar.gz /var/lib/docker/volumes/labai-data/_data/
   ```

## Troubleshooting

### Container exits immediately

Check logs:
```bash
docker compose logs labai
```

Common causes:
- Missing `GEMINI_API_KEY` — set it in `.env`
- Port 5000 already in use — change the port mapping in `docker-compose.yml`

### Database not persisting

Ensure volumes are mounted:
```bash
docker volume ls | grep labai
```

### Health check fails

Wait 30 seconds for startup, then retry:
```bash
sleep 30 && curl http://localhost:5000/health
```

## Image size optimization

The multi-stage build produces a ~200MB runtime image:
- Build stage: .NET SDK 10 (~900MB)
- Runtime stage: ASP.NET Core runtime only (~200MB)

To reduce further, enable trimming in `LabAi.Web.csproj`:
```xml
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>link</TrimMode>
```

Note: Trimming may break reflection-heavy frameworks like Semantic Kernel.
