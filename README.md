\# Document Converter



Generic document conversion worker for converting office documents to PDF using LibreOffice inside Docker.



\## Current Features



\- Converts supported office files to PDF

\- Supports DOCX, XLSX, PPTX, DOC, XLS, PPT, ODT, ODS, ODP

\- Runs as a .NET Worker Service

\- Runs inside Docker with LibreOffice

\- Uses local volume-based input/output folders

\- Moves processed files to `data/processed`

\- Moves failed files to `data/failed`

\- Keeps output PDFs in `data/output`



\## Folder Structure



```txt

data

&#x20; input       -> files waiting for conversion

&#x20; output      -> generated PDF files

&#x20; processed   -> successfully processed source files

&#x20; failed      -> failed source files

&#x20; temp        -> LibreOffice temporary profile folders

Run

docker compose up -d --build

Logs

docker compose logs -f converter-worker

Stop

docker compose down

Test



Place a supported file into:



data/input



The generated PDF will be written to:



data/output

Current Architecture



This is Phase 1 MVP.



The worker currently uses folder-based polling. Later phases may add:



Converter API

Conversion job database

Queue-based processing

Object storage support

Multi-worker scaling

Health checks and metrics

