# Cross-platform rendering fixtures

This directory is the home for deterministic RDLC inputs and expected output metadata used by the v2 renderer tests.

The first smoke fixture is the existing `ReportViewerCore.Sample.Console/Report.rdlc`. Add new fixtures by behavior rather than by renderer: text metrics, RTL/complex scripts, images, charts, links, pagination, and OpenXML embedding. Keep generated PDFs/PNGs out of source control unless they are intentionally approved golden files.

Required fixture metadata should record the target format, page size, font bundle, expected page count, and the tolerance used for visual comparisons. Do not use machine-specific system fonts as the only fixture dependency.

## Fixture coverage index

Every RDLC in `engine/` is copied into the test output and validated as non-empty, well-formed `Report` XML by `.devbuddy/tools/validate_v2_artifacts.py`.

| Fixture | Coverage |
| --- | --- |
| `merged-cell-table.rdlc` | Tablix ColSpan propagation into an OpenXML merged-cell range |
| `simple.rdlc` | Basic field binding and single-page HTML rendering |
| `image.rdlc`, `image-expression.rdlc`, `tablix-image.rdlc` | Embedded, resolved, and tablix-cell images |
| `chart.rdlc`, `column-chart.rdlc` | Bar, column, line, area, pie, doughnut, and unsupported-chart regression inputs |
| `multi-tablix.rdlc`, `mixed-tablix-subreport.rdlc`, `tablix-visual-items.rdlc` | Multiple regions, mixed subreport content, and cell-relative text/image/rectangle/line/chart placement |
| `subreport-parent.rdlc`, `subreport-parameter-child.rdlc` | Explicit subreport resolution and parameter mapping |
| `header-footer.rdlc`, `header-footer-tablix.rdlc`, `nested-header-footer.rdlc` | Repeating page decorations for textbox, tablix, and nested-container reports |
| `parameter-default.rdlc`, `parameter-case-insensitive.rdlc`, `multi-value-parameter.rdlc` | Scalar/default, case-insensitive, and multi-value parameter behavior plus allow-listed `Join` |
| `hyperlink.rdlc` | Safe hyperlink propagation to HTML, PDF, and OpenXML |
| `nested-items.rdlc`, `styled-text.rdlc`, `international-text.rdlc` | Nested placement, styles, color, Thai/Arabic/CJK/RTL, and vertical text |
| `multiline.rdlc` | Multiline text mapped to native Word line-break nodes |
| `composite-expression.rdlc`, `string-functions.rdlc`, `string-functions-advanced.rdlc`, `is-nothing.rdlc`, `visibility.rdlc` | Allow-listed expressions/string functions, search/replace, null checks, boolean composition, and initial hidden state |
| `sorted-tablix.rdlc`, `culture-sorted-tablix.rdlc` | Stable row sorting and culture-specific decimal sorting |
| `grouped-tablix.rdlc`, `nested-grouped-tablix.rdlc`, `grouped-null-keys.rdlc` | Scoped grouping, nested prefix scopes, null keys, headers, subtotals, and aggregates |
| `sibling-group-branches.rdlc`, `sibling-group-no-header.rdlc`, `sibling-group-start-pagebreak.rdlc`, `sibling-group-start-end-pagebreak.rdlc`, `sibling-group-start-end-pagebreak-disabled.rdlc`, `nested-sibling-group-branches.rdlc`, `nested-sibling-child-end-pagebreak.rdlc`, `nested-sibling-single-root.rdlc`, `nested-sibling-single-root-no-header.rdlc` | Terminal and nested sibling row-group branches rendered as independent group/detail sections with optional static header, recursive dynamic/static child ordering, single-root nested branches, leading/trailing static child, root interstitial static member, and branch `Start`/`StartAndEnd`/parameter-disabled page-break coverage |
| `no-rows-message.rdlc` | Empty tablix `NoRowsMessage` rendering |
| `scoped-aggregates.rdlc` | `First`, `Last`, `Count`, `Min`, and `Max` over the materialized scope |
| `grouped-pagebreak.rdlc`, `grouped-pagebreak-disabled.rdlc`, `grouped-pagebreak-field-disabled.rdlc`, `nested-group-pagebreak.rdlc`, `nested-group-start-end-pagebreak.rdlc` | Supported grouped `Between`, parameter/field-disabled, and nested linear `StartAndEnd` page breaks at their configured scopes |
| `nested-group-static-detail-subtotal.rdlc` | Nested dynamic groups with repeated static detail rows and scoped static subtotals |
| `nested-static-wrapper-single-child.rdlc`, `nested-static-wrapper-multiple-children.rdlc` | Single-child and multi-child static wrappers around nested groups with root-level totals and scoped leading/trailing rows |
| `arbitrary-nested-member-tree.rdlc` | Hierarchy-first row-member traversal with root and nested static members between independent dynamic branches, without the legacy header/detail template shape |
| `unsupported-expression.rdlc`, `unsupported-map.rdlc`, `unsupported-chart.rdlc` | Explicit security and unsupported-feature rejection cases |
| `unsupported-group-branch.rdlc`, `unsupported-pagebreak.rdlc`, `unsupported-tablix-subreport.rdlc`, `unsupported-nested-subreport.rdlc` | Explicit rejection of branching members, unsupported break locations, and unsupported nested subreports |

The `legacy-bridge/legacy-bridge-report.rdlc` fixture is a schema-valid legacy RDL input used only by the Windows headless bridge sample; it keeps legacy data-source metadata separate from the constrained engine fixtures.
