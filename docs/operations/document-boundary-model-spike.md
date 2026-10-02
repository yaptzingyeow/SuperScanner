# Local document boundary model spike — 2026-09-29

## Scope

Evaluate a learned detector against the user's private folded-paper photo before
changing production behavior. No private image was uploaded. No paid API was used.
Existing detector configuration and running services were not changed.

## Candidate research

- FairScan segmentation: https://github.com/pynicolas/fairscan-segmentation-model
  Repository states GPL-3.0 and provides PyTorch/TFLite architecture. Not integrated.
- DocAligner: https://github.com/DocsaidLab/DocAligner
  Repository states Apache-2.0. Official inference source links ONNX weights through
  Google Drive. Weight-specific redistribution terms still need confirmation before
  packaging. This candidate predicts corner heatmaps, not a paper segmentation mask.

## Experiment

Local ignored harness: `.task-tools/docaligner_spike.py`. ONNX Runtime CPU, one
thread; official BGR resize to 256x256, NCHW float /255, corner threshold 0.3.
Only weight and public control-image downloads used the network.

The private tenancy photo failed to produce all four corners for LCNet100,
FastViT-T8, and the default FastViT-SA24. T8 peak confidences were
0.0335, 0.0374, 0.0141, 0.2516; SA24 peaks were
0.0070, 0.0026, 0.0125, 0.0015. Do not lower thresholds to manufacture a crop.

Control: author's `docs/run_test_card.jpg` with SA24 returned corners
(48.1519,223.4769), (387.1344,198.0996), (423.0362,345.5133),
(40.1486,361.3878), matching the published example. Corner peaks exceeded 0.93;
inference took about 0.22 seconds on this machine. This validates the basic harness
and shows that the private-image failure is not simply a broken model loader.

## Decision

Do not enable these models for Arks Scanner based on this experiment. The desired
quality is not achieved. Next candidate should be document-area segmentation with
verified model-weight licensing; compare its mask and warped output on multiple
real photographs, including this failure case. Manual crop remains available.
No claim is made that the scanner-quality problem is solved.

## Dedicated area segmentation follow-up

Official LearnOpenCV example:
https://github.com/spmallick/learnopencv/tree/master/Document-Scanner-Custom-Semantic-Segmentation-using-PyTorch-DeepLabV3

An isolated `.task-tools/segmentation-spike-runtime` was created for CPU PyTorch;
the running worker environment was not modified. The experimental harness is
`.task-tools/segmentation_spike.py`. Checkpoints are loaded with `weights_only=True`
and strict state-dictionary validation; no downloaded Python code is executed.
Production redistribution permission for these checkpoints remains unverified.

MobileNetV3 checkpoint SHA256:
`77B15575B37CD6C273B750DF7E1AC8FC4178BD86E40AE59B04F98F14255A1232`.
Using the published BGR/384px preprocessing, CPU inference took 0.21 seconds.
The mask covered 64.8% of the image. Its simplified polygon was
(847,134), (220,144), (88,1246), (867,1203).
Visual inspection found substantial missing paper at the folded top-left and
bottom-right, and the resulting warp skewed the printed frame. Not accepted.
Private images and outputs remain ignored under `.task-tools`, not source control.

ResNet50 comparison took 2.52 seconds. Its mask covered 56.4% and simplified
to eight vertices rather than four. Visual inspection showed a large lower-right
paper region excluded, with the mask following part of the printed border.
The harness deliberately refused to manufacture a four-corner warp. This second
area model is also rejected for automatic use on this case. A learned model alone
does not solve the failure. Both examples need either stronger domain training or
user-guided segmentation; no production integration was performed.
