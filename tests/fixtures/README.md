# Cross-platform rendering fixtures

This directory is the home for deterministic RDLC inputs and expected output metadata used by the v2 renderer tests.

The first smoke fixture is the existing `ReportViewerCore.Sample.Console/Report.rdlc`. Add new fixtures by behavior rather than by renderer: text metrics, RTL/complex scripts, images, charts, links, pagination, and OpenXML embedding. Keep generated PDFs/PNGs out of source control unless they are intentionally approved golden files.

Required fixture metadata should record the target format, page size, font bundle, expected page count, and the tolerance used for visual comparisons. Do not use machine-specific system fonts as the only fixture dependency.

## Fixture coverage index

Every RDLC in `engine/` is copied into the test output and validated as non-empty, well-formed `Report` XML by `.devbuddy/tools/validate_v2_artifacts.py`.

| Fixture | Coverage |
| --- | --- |
| `simple.rdlc` | Basic field binding and single-page HTML rendering |
| `image.rdlc`, `image-expression.rdlc`, `tablix-image.rdlc` | Embedded, resolved, and tablix-cell images |
| `chart.rdlc`, `column-chart.rdlc` | Bar, column, line, area, pie, doughnut, and unsupported-chart regression inputs |
| `multi-tablix.rdlc`, `tablix-visual-items.rdlc` | Multiple regions and cell-relative text/image/rectangle/line/chart placement |
| `subreport-parent.rdlc`, `subreport-parameter-child.rdlc` | Explicit subreport resolution and parameter mapping |
| `header-footer.rdlc`, `header-footer-tablix.rdlc` | Repeating page decorations for textbox and tablix reports |
| `parameter-default.rdlc`, `multi-value-parameter.rdlc` | Scalar and multi-value parameter defaults plus allow-listed `Join` |
| `hyperlink.rdlc` | Safe hyperlink propagation to HTML, PDF, and OpenXML |
| `nested-items.rdlc`, `styled-text.rdlc`, `international-text.rdlc` | Nested placement, styles, color, Thai/Arabic/CJK/RTL, and vertical text |
| `multiline.rdlc` | Multiline text mapped to native Word line-break nodes |
| `composite-expression.rdlc`, `string-functions.rdlc`, `string-functions-advanced.rdlc`, `is-nothing.rdlc`, `visibility.rdlc` | Allow-listed expressions/string functions, search/replace, null checks, boolean composition, and initial hidden state |
| `sorted-tablix.rdlc`, `culture-sorted-tablix.rdlc` | Stable row sorting and culture-specific decimal sorting |
| `grouped-tablix.rdlc`, `nested-grouped-tablix.rdlc`, `grouped-null-keys.rdlc` | Scoped grouping, nested prefix scopes, null keys, headers, subtotals, and aggregates |
| `no-rows-message.rdlc` | Empty tablix `NoRowsMessage` rendering |
| `scoped-aggregates.rdlc` | `First`, `Last`, `Count`, `Min`, and `Max` over the materialized scope |
| `grouped-pagebreak.rdlc`, `nested-group-pagebreak.rdlc` | Supported grouped `Between` page breaks at outer and nested scopes |
| `unsupported-expression.rdlc`, `unsupported-map.rdlc`, `unsupported-chart.rdlc` | Explicit security and unsupported-feature rejection cases |
| `unsupported-group-branch.rdlc`, `unsupported-pagebreak.rdlc` | Explicit rejection of branching members and unsupported break locations |
