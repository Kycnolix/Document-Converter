# Document Converter

Phase 5 MVP for converting Office documents to PDF with a simple HTTP API in front of a folder-based worker.

## Projects

- `src/DocumentConverter.Api`: minimal API for job submission, status checks, and result download
- `src/DocumentConverter.Worker`: background worker that polls `data/input` and converts supported files to PDF with LibreOffice
- `src/DocumentConverter.Shared`: shared job model and storage abstractions

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
  jobs        -> legacy Phase 4 JSON metadata files kept for compatibility
```

## MongoDB Requirement

- Default connection: `mongodb://host.docker.internal:27017`
- Database: `document_converter`
- Collection: `conversionJobs`
- MongoDB must be reachable from the API and worker containers

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

Compose environment variables:

```txt
Mongo__ConnectionString=mongodb://host.docker.internal:27017
Mongo__DatabaseName=document_converter
Mongo__ConversionJobsCollectionName=conversionJobs
Converter__WorkerId=converter-worker-1
Converter__JobLockSeconds=300
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

- Mongo-backed tracked jobs with local filesystem document storage
- Legacy direct folder drops still supported
- No queue yet
- No authentication yet
- No object storage yet
- One worker is recommended until more concurrency testing is done
- MongoDB must be reachable from containers
- Only PDF target format is supported
- Filesystem JSON metadata is now legacy/backward-compatibility only

## Job Lifecycle Statuses

- `Pending`: API accepted the upload and created a MongoDB job
- `Processing`: worker claimed the job and started converting it
- `Ready`: output PDF exists and the job completed successfully
- `Failed`: conversion failed, timed out, or finished without a valid PDF
- `Unsupported`: tracked job source type was not supported
- `Unknown`: API could not reconcile metadata with the current filesystem state

## Runtime Behavior

- The API writes uploaded files into `data/input`
- The API creates and reads tracked conversion jobs in MongoDB
- The worker claims tracked jobs atomically from MongoDB and updates their lifecycle there
- The worker converts supported files to PDF and writes output to `data/output`
- Successful source files move to `data/processed`
- Failed source files move to `data/failed`
- Direct file drops into `data/input` still work without creating MongoDB jobs

