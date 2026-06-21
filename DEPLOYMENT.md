# Deployment Guide

This project is prepared for Windows Server style deployment with Docker and an external MongoDB instance.

## Prerequisites

- Docker Desktop or Docker Engine running Linux containers
- MongoDB reachable from the API and worker containers
- The API port open on the server if remote access is required

## Folder Layout

Recommended layout:

```txt
document-converter/
  .env
  docker-compose.yaml
  data/
    input/
    output/
    processed/
    failed/
    temp/
    jobs/
```

## Environment Setup

1. Copy `.env.example` to `.env`.
2. Set `MONGO_CONNECTION_STRING` to the production MongoDB server.
3. Set `API_PORT` if `8088` is already in use.
4. Adjust upload size, polling, timeout, and lock settings only if your environment needs it.

Do not commit `.env`.

## Start

```bash
docker compose up -d --build
```

## Check

```bash
docker compose ps
docker compose logs -f converter-api
docker compose logs -f converter-worker
curl http://localhost:8088/health
curl http://localhost:8088/ready
```

## Stop

```bash
docker compose down
```

## Update

```bash
git pull
docker compose up -d --build
```

## Backup

- MongoDB database: `document_converter`
- `data/output`
- `data/processed` if you need an archive of completed source files

## Troubleshooting

- MongoDB connection refused:
  Verify the MongoDB service is running, reachable from containers, and the connection string is correct.
- `host.docker.internal` not resolving:
  On some server setups you may need to replace it with the host IP or DNS name.
- LibreOffice conversion timeout:
  Increase `CONVERTER_CONVERSION_TIMEOUT_SECONDS` carefully if large documents need more time.
- Output PDF missing:
  Check worker logs for LibreOffice errors and confirm the source file moved to `data/processed` or `data/failed`.
- Docker container unhealthy:
  Check `docker compose ps`, inspect `/ready` for API readiness details, and review logs.
- Port already in use:
  Change `API_PORT` in `.env` and restart the stack.

## Security Notes

- Do not expose the API publicly without authentication and a reverse proxy.
- Do not commit `.env`.
- Use MongoDB credentials in production.
- Keep the allowed file types restricted to the current supported set.
- Set `CONVERTER_MAX_UPLOAD_BYTES` to an appropriate production limit.
