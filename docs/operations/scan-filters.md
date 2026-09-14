# Scan filters

Open a JPEG/PNG document, choose **Crop & filters**, select a filter, then **Save scan**.
The corner-selection image stays unfiltered; the saved preview and thumbnail show the result.
Changing only the filter retains the current corners. Original keeps the perspective crop
and original colors without the Document enhancement. It does not restore uncropped geometry.

Available filters:
- Original: no color or contrast enhancement.
- Document (default): the existing mild local contrast and sharpening.
- Bright: gamma correction that lifts darker tones while preserving color.
- Grayscale: luminance conversion without thresholding.
- Black & White: adaptive thresholding for uneven lighting; faint handwriting may disappear.

Every render starts from the preserved upright crop source. Filters do not stack.
Filter is the requested setting; AppliedFilter identifies the last published preview.
The existing crop revision and guarded publication keep older jobs from overwriting newer
filter choices. Unknown filter names are rejected by API, Domain, and Worker.

## Installation / handoff

Apply EF migration `20260913000000_PageFilters` before restarting the updated API and Worker.
Alternatively, run `scripts/database/Apply-PageFilters.sql` as the database owner.
Existing rows default to Document to match the previous enhancement behavior. No images are
automatically reprocessed by the migration. Save scan generates the selected filter.

## Verification performed (2026-09-13)

- Applied migration `20260913000000_PageFilters` to the local PostgreSQL database.
- Restarted the local API and Worker after applying the migration.
- Angular development build passed.
- API and Worker .NET builds passed with zero warnings and zero errors.
- Python compilation passed.
- A focused Python smoke test passed for Original, Document, Bright, Grayscale, and
  BlackAndWhite.
- Browser end-to-end check passed: Grayscale was selected and saved, the page returned to
  Ready, and the persisted row reported Filter/AppliedFilter `Grayscale` with matching crop
  revision 6.
- Domain tests passed: 5/5.

The complete automated suite is not green yet:

- Application tests: 33 passed and 1 failed. The failing upload-validation runner test
  expects a page identifier, but the current result contains null.
- Angular tests: 27 passed and 8 failed. These tests still assert the previous protected,
  login-first navigation and older authentication method names; the product now uses a
  public scanner-first layout with optional sign-in.
- API and Infrastructure integration tests could not complete because Testcontainers could
  not connect to `npipe://./pipe/docker_engine`. No Docker executable or running Docker engine
  was found on this machine during this verification.
