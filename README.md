# Document Converter

Phase 3 MVP for converting Office documents to PDF with a simple HTTP API in front of a folder-based worker.

## Projects

- `src/DocumentConverter.Api`: minimal API for job submission, status checks, and result download
- `src/DocumentConverter.Worker`: background worker that polls `data/input` and converts supported files to PDF with LibreOffice

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

## Folder Structure

```txt
data
  input       -> files waiting for conversion
  output      -> generated PDF files
  processed   -> successfully processed source files
  failed      -> failed source files
  temp        -> LibreOffice temporary profile folders
  jobs        -> API job metadata files
```

## How To Run

```bash
docker compose up -d --build
```

API base URL:
`http://localhost:8088`

Useful commands:

```bash
docker compose ps
docker compose logs -f converter-api
docker compose logs -f converter-worker
docker compose down
```

## API Endpoints

### Health

```bash
curl http://localhost:8088/health
```

### Upload Conversion

```bash
curl -F "file=@data/sample.docx" -F "targetFormat=pdf" http://localhost:8088/api/conversions
```

### Check Status

```bash
curl http://localhost:8088/api/conversions/{jobId}
```

### Download Result

```bash
curl -L -o result.pdf http://localhost:8088/api/conversions/{jobId}/result
```

## Current Limitations

- Folder/filesystem-based MVP
- No database yet
- No authentication yet
- Status is inferred from files on disk
- Only PDF target format is supported

## Runtime Behavior

- The API writes uploaded files into `data/input`
- The API stores job metadata in `data/jobs`
- The worker converts supported files to PDF and writes output to `data/output`
- Successful source files move to `data/processed`
- Failed source files move to `data/failed`

