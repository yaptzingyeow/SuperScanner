# Document preview processing

Accepted uploads enqueue one `ProcessDocument` job keyed by upload ID and preview version.
The existing leased Worker handles both validation and preview jobs. Retries use the existing
bounded backoff. Preview generation preserves the original, auto-orients images, strips metadata,
and writes JPEG previews (maximum 2000px) and thumbnails (maximum 320px) to private R2.
PDF previews render the first page using Poppler; original downloads preserve the complete PDF.
HEIC, PNG and JPEG are decoded with Magick.NET. This stage does not perform OCR or document enhancement.

Apply the `DocumentPreviews` EF migration before starting the updated services.
`scripts/database/Apply-DocumentPreviews.sql` is the manual PostgreSQL alternative and also queues
previously accepted uploads without previews. It is safe to rerun and requires the database owner.
The Worker container installs `poppler-utils`; local installations configure `Preview__PdfToPpmPath`
or put `pdftoppm` on PATH. No external OCR API or paid processing service is used.

Owner-authorized endpoints serve detail, preview, thumbnail, original download and failed-job retry.
Documents become Ready after all their page previews are saved. The list refreshes while active;
open a document title for previews, progress and retry. Failed preview jobs and rejected uploads
are reflected in the list. SQL records retain original lifecycle values for rejected uploads.

Validation handoff: automated tests were explicitly deferred to the user's other model.
API, Worker and Angular compilation were checked. Three existing accepted local documents were
processed to Ready with preview and thumbnail keys stored. Further verification should cover
ownership, retries, concurrent multi-page processing, PDF/HEIC handling, and object-URL cleanup.
The image library package is https://www.nuget.org/packages/Magick.NET-Q8-AnyCPU/14.17.1.
