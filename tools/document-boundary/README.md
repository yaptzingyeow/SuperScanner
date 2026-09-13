# Document boundary model operations

The production repository defaults to `OpenCvOnly`. ONNX model binaries, evaluation photographs, probability masks, and protected manifests must remain outside Git.

## Fetch a candidate

Run `fetch_u2netp_model.ps1 -DestinationDirectory .task-tools/document-boundary/models`. The script downloads the fixed U-2-Net small candidate, then prints its exact byte size and SHA-256. It deliberately does not update Worker configuration or enable AI.

## Promotion requirements

Before enabling a candidate:

1. Review and record the upstream model and dataset licenses. The U-2-Net source project declares Apache-2.0; confirm the downloaded artifact and all training data are acceptable for the intended distribution.
2. Label and independently review the protected 60-image release manifest.
3. Run the AI and OpenCV benchmarks and confirm every promotion gate passes, including zero high-confidence severe misses.
4. Review ONNX Runtime mobile-usability output for Phase 2 reuse.
5. In a pull request, manually copy the evaluated model version, checksum, source, license, and input contract into `document-boundary-model.json`.
6. Provision the exact verified model outside source control and begin with a 5% deployment rollout. Keep the repository default at `OpenCvOnly`.

Never paste document text, image paths, model tensors, API keys, or image data into logs or benchmark summaries.

## Railway observation

In the Worker service log search, filter for `Document boundary completed for page`. Track the structured `Source`, `Confidence`, `DiagnosticsCode`, and `ElapsedMilliseconds` fields. Compare AI share with the configured rollout, watch fallback and timeout rates, and investigate any increase in `manual_required`. Never export document content while reviewing detector operations.
