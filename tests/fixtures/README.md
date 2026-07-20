# Cross-platform rendering fixtures

This directory is the home for deterministic RDLC inputs and expected output metadata used by the v2 renderer tests.

The first smoke fixture is the existing `ReportViewerCore.Sample.Console/Report.rdlc`. Add new fixtures by behavior rather than by renderer: text metrics, RTL/complex scripts, images, charts, links, pagination, and OpenXML embedding. Keep generated PDFs/PNGs out of source control unless they are intentionally approved golden files.

Required fixture metadata should record the target format, page size, font bundle, expected page count, and the tolerance used for visual comparisons. Do not use machine-specific system fonts as the only fixture dependency.
