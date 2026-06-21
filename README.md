# Document Converter

Document Converter is a Dockerized Office-to-PDF pipeline with a small HTTP API, a LibreOffice-based worker, and MongoDB-backed tracked job metadata.

## Current Architecture

- `src/DocumentConverter.Api`: upload, status, result, health, and readiness endpoints
- `src/DocumentConverter.Worker`: background conversion worker using LibreOffice
- `src/DocumentConverter.Shared`: shared job models, Mongo options, and Mongo-backed job store
- MongoDB: primary metadata store for API-created tracked jobs
- Local `data/*` folders: source, output, processed, failed, temp, and legacy jobs storage

## Phase 6 Hardening

- environment-driven Docker Compose defaults
- API readiness endpoint at `GET /ready`
- Docker healthchecks and restart policies
- worker heartbeat file for container monitoring
- deployment documentation for Windows Server style Docker hosting

See [DEPLOYMENT.md](DEPLOYMENT.md) for the deployment flow.

## Supported Source Formats

- `.docx`
- `.xlsx`
- `.pptx`
- `.doc`
- `.xls`
- `.ppt`
- `.odt`
- `.ods`
- `.odp`

## Quick Start

1. Copy `.env.example` to `.env`.
2. Set the MongoDB connection string for your environment.
3. Start the stack:

```bash
docker compose up -d --build
```

Useful checks:

```bash
docker compose ps
docker compose logs -f converter-api
docker compose logs -f converter-worker
curl http://localhost:8088/health
curl http://localhost:8088/ready
```

Swagger UI:

- `http://localhost:8088/swagger`
- OpenAPI JSON: `http://localhost:8088/swagger/v1/swagger.json`
- Toggle with `API_ENABLE_SWAGGER=true|false`
- Default in Docker Compose: enabled
- Do not expose Swagger publicly without authentication or reverse-proxy/network restriction

## API Endpoints

- `GET /health`
- `GET /ready`
- `POST /api/conversions`
- `GET /api/conversions/{jobId}`
- `GET /api/conversions/{jobId}/result`

Swagger exposes all of these endpoints for browser-based testing, including multipart file upload for `POST /api/conversions`.

## Runtime Notes

- The API stores uploads in `data/input` and tracked job state in MongoDB.
- The worker claims tracked jobs from MongoDB and still supports legacy file drops into `data/input`.
- Successful conversions write PDFs to `data/output` and move source files to `data/processed`.
- Failed conversions move source files to `data/failed`.
- `data/jobs` remains only for legacy Phase 4 compatibility.

## Current Limitations

- Only PDF output is supported.
- No auth, queue, or object storage is included.
- One worker is still the recommended production shape until more concurrency testing is done.
- MongoDB must be reachable from containers.

