# ReportViewerCore — Session Handoff Plan

อัปเดตล่าสุด: 24 กรกฎาคม 2026

## เป้าหมายโครงการ

พัฒนา ReportViewerCore v2 แบบข้ามแพลตฟอร์มโดยใช้ `ReportDocument`/`IRenderCanvas` เป็นสัญญากลาง และค่อย ๆ เพิ่ม RDLC parity โดยไม่เปลี่ยนพฤติกรรมของ legacy v1. ทุก feature ที่เพิ่มต้องมี RDLC fixture หรือ showcase ที่ใช้งานจริง, test ที่ตรวจผลลัพธ์, และ export PNG/PDF/HTML/DOCX/XLSX ที่ตรวจข้าม format ได้.

## สถานะปัจจุบัน

- รอบล่าสุดเพิ่ม `FormatNumber` แบบจำกัดขอบเขต, ยืนยัน RDLC hyperlink relationships ใน XLSX/DOCX, เพิ่ม fixture/test สำหรับภาพ RDLC ที่ถูกตัดขอบหน้าใน OpenXML, และยืนยัน package/smoke gate ครบ 7 v2 packages; renderer เดิมผ่าน image regression โดยไม่ต้องแก้ไข.
- Worktree **ยังมีการเปลี่ยนแปลงสะสมและยังไม่ commit**. ห้าม `git reset`, `git checkout`, หรือเขียนทับไฟล์ที่เปลี่ยนอยู่โดยไม่ review diff ก่อน.
- การเปลี่ยนแปลงสะสมครอบคลุม Engine, OpenXML, Feature Showcase, test fixtures, validators, checklist และ DevBuddy memory.
- Test ล่าสุด: **128/128 passed**; RDLC fixtures: **62**.

## ทำแล้ว

### Portable rendering และ exports

- มี renderer PNG (Skia), PDF (Skia), HTML/SVG, XLSX และ DOCX.
- Office exports แสดง Skia page preview ที่ตรงกับ PNG byte-for-byte และเก็บ OpenXML semantic parts แยกไว้สำหรับ text, links, images, shapes และ charts.
- Cross-format validator ตรวจ page counts, semantic markers, SVG/XML structure, Office preview parity, hyperlinks และ native chart/image/shape metadata.
- Direct showcase และ RDLC showcase export อย่างละ 7 ไฟล์: RDLC, PNG, PDF, HTML, XLSX, DOCX และ manifest.

### RDLC parity ล่าสุด

- รองรับ constrained tablix, groups, nested/sibling member trees, static wrapper rows, aggregates, visibility, headers/footers, images, hyperlinks, supported charts และ subreports แบบ direct-body.
- รองรับ `PageBreak` ของ group สำหรับ `Between`, `Start`, `End`, `StartAndEnd`; `Before` ยังถูก reject อย่างตั้งใจเพราะไม่ใช่ RDL schema value.
- `PageBreak/Disabled` ประเมินผ่าน allow-listed resolver ที่ materialized group scope:
  - parameter-driven break (`grouped-pagebreak-disabled.rdlc`)
  - field-driven break (`grouped-pagebreak-field-disabled.rdlc`)
  - sibling `StartAndEnd` break (`sibling-group-start-end-pagebreak-disabled.rdlc`)
- เพิ่ม `FormatNumber(value, digits)` แบบ allow-listed โดยจำกัด `digits` ที่ 0–6 และมี fixture `format-number.rdlc` ครอบคลุม output HTML/PDF/XLSX/DOCX รวม malformed/over-range cases.
- Regression ตรวจจำนวนหน้าจริงจาก PNG, HTML, PDF, DOCX และ XLSX ไม่ได้ตรวจเฉพาะ `ReportDocument.Pages`.
- Feature Showcase ใช้ `=Fields!DisablePageBreak.Value` เพื่อแสดง field-driven behavior โดย output ตัวอย่างยังคง 2 หน้า.

### หลักฐานตรวจล่าสุด

- `dotnet test ... --logger "trx;LogFileName=results.trx"`: 128 passed.
- `validate_v2_artifacts.py`: showcase 7 ไฟล์, test result 128 passed, fixture 62 ไฟล์ผ่าน.
- `validate_cross_format.py` ทั้ง direct และ RDLC showcase: PNG/HTML/PDF/DOCX/XLSX/manifest มี **2 หน้าเท่ากัน**.
- `validate_v2_artifacts.py --packages ... --smoke ...`: **7 nupkg, 7 snupkg, 7 smoke files** ผ่าน archive, metadata, dependency, symbol, and signature checks.
- `git diff --check` ผ่าน; คำเตือน LF→CRLF เป็น line-ending warning เท่านั้น.

## ไฟล์/งานที่ยังอยู่ใน worktree

ให้ review ก่อน commit และเก็บการเปลี่ยนแปลงเป็นชุดเล็กตาม concern:

- `ReportViewerCore.Engine/RdlcReportEngine.cs`: group page-break `Disabled` scope handling และ hierarchy traversal ที่เพิ่มมาก่อนหน้า.
- `ReportViewerCore.Rendering.OpenXml/OpenXmlRenderers.cs`: clipping/visible bounds ของ OpenXML text, links, images, shapes และ charts.
- `ReportViewerCore.Sample.FeatureShowcase/`: acceptance RDLC และ manifest; export ล่าสุดอยู่ใน `artifacts/feature-showcase/` (ignored).
- `tests/ReportViewerCore.Rendering.Tests/` และ `tests/fixtures/engine/`: regression/fixtures รวม `grouped-pagebreak-field-disabled.rdlc` ที่ยัง untracked.
- `.devbuddy/` และ `docs/cross-platform-v2-checklist.md`: handoff knowledge, validators และ progress documentation.

## งานค้างและลำดับแนะนำ

### P1 — ต้องใช้ Windows/CI หรือ authority ภายนอก

1. รัน legacy-vs-v2 semantic/page-count comparison บน GitHub Actions `windows-latest` และเก็บ artifact/ผลลัพธ์.
2. รัน WinForms v2 และ legacy bridge samples บน `windows-latest`.
3. Execute published RIDs บน runner ที่ตรง RID; `linux-arm64` ยัง publish-only บน x64 host.

### P2 — Portable feature work ที่ควรเลือกทีละ feature พร้อม fixture

1. เลือก 1 OpenXML fidelity gap ที่วัดได้ (hyperlink/image anchoring, cropping, or layout) แล้วเพิ่ม fixture + visual/cross-format assertion ก่อนแก้ renderer.
2. ขยาย allow-listed expression host เฉพาะ use case ที่มี RDLC fixture และ security review; ห้าม execute `Code.*` หรือ arbitrary report code.
3. ทำ RDLC member/layout parity ต่อเฉพาะ shape ที่ระบุได้ชัดเจน; static layouts ที่คลุมเครือให้ reject ต่อไปจนกว่าจะมี contract และ fixture.

### P3 — Release readiness

1. ทำ full solution build และ package gate ใหม่ก่อนอ้างผล release.
2. ตรวจ/บันทึกความแตกต่าง v1 สำหรับ printer, TIFF, EMF, BIFF8, Word97, Map และ legacy preview.
3. ปิด licensing/legal review ก่อน stable release.

## สิ่งที่ตั้งใจยังไม่รองรับ

- Full v1/RPL parity, arbitrary report code, unconstrained tablix member layouts, map/vector-style report items, Radar chart และ legacy printer/TIFF/EMF/BIFF8/Word97 behavior.
- Interactive `ToggleItem`; renderer ปัจจุบัน honor เฉพาะ initial `Hidden` state.
- `BreakLocation=Before`; ให้ reject ต่อไปตาม RDL schema.

## เริ่ม session ถัดไป

1. ตรวจ worktree ก่อน:

   ```powershell
   git status --short
   git diff --check
   git diff --stat
   ```

2. รัน portable regression และ export baseline:

   ```powershell
   dotnet test tests/ReportViewerCore.Rendering.Tests/ReportViewerCore.Rendering.Tests.csproj --no-restore -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false
   dotnet build ReportViewerCore.Sample.FeatureShowcase/ReportViewerCore.Sample.FeatureShowcase.csproj --no-restore -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false
   dotnet .\ReportViewerCore.Sample.FeatureShowcase\bin\Release\net10.0\ReportViewerCore.Sample.FeatureShowcase.dll artifacts\feature-showcase
   ```

3. เลือกงาน P1 หากมี CI/Windows authority; หากไม่มี ให้เลือก P2 เพียงหนึ่ง gap แล้วทำตามลำดับ: fixture → targeted test → full test → export → artifact/cross-format validation.

4. ก่อน release claim ให้รัน:

   ```powershell
   dotnet build ReportViewerCore.sln --no-restore -c Release -p:EnableWindowsTargeting=true -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false
   ```

## คำสั่ง validators

```powershell
$runtime = 'C:\Users\jenna\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
& $runtime .devbuddy\tools\validate_v2_artifacts.py --showcase artifacts\feature-showcase --test-results <path-to-results.trx> --fixtures tests\fixtures\engine --fixture-output tests\ReportViewerCore.Rendering.Tests\bin\Release\net10.0\fixtures\engine
& $runtime .devbuddy\tools\validate_cross_format.py artifacts\feature-showcase
& $runtime .devbuddy\tools\validate_cross_format.py artifacts\feature-showcase\rdlc-feature-showcase
```

Latest loop slice: added bounded allow-listed Left/Right expression coverage to the existing string-function fixture, with malformed-call behavior retained as empty output. The next unresolved items remain Windows/CI authority and broader parity decisions.

Latest loop slice: extended the RDLC OpenXML image fixture to cover negative left/top placement as well as right/bottom overflow, asserting native XLSX/DOCX crop metadata for all four page edges.

Latest loop slice: added RDLC relative-link coverage, proving that /reports/detail remains an external hyperlink target in both XLSX worksheet and DOCX document relationships.

Latest loop slice: extended the Left/Right fixture with oversized-length clamping and malformed-argument assertions; the allow-listed evaluator remains unchanged.

Latest loop slice: added fixed-arity CStr expression coverage with a malformed extra-argument case; no arbitrary conversion or report-code execution is enabled.

Verification refresh: the current evidence is 129/129 passed tests and 62 RDLC fixtures. This supersedes older count references above; the latest run also passed package/smoke and both cross-format validators.

Latest loop slice: extended safe string-function assertions to PDF/XLSX/DOCX in addition to HTML.

Latest verification: targeted safe-string test passed; full suite passed 129/129; solution build passed with 0 warnings and 0 errors; package/smoke and both cross-format validators passed. Historical 128-test references above are superseded by the verification refresh at line 126.

Latest loop slice: added nested unsupported-expression cases through CStr, Left, and FormatNumber; portable output assertions verify fail-closed behavior and no report-code text leakage.

Latest loop slice: extended the RDLC clipped-image regression to verify XLSX/DOCX image relationship targets and embed IDs alongside all-edge crop metadata.

Latest loop slice: extended the RDLC hyperlink regression with a query-plus-fragment HTTPS target and preserved it across HTML/PDF/XLSX/DOCX outputs.

Latest loop slice: extended composite RDLC expression coverage (`IIF`, `Format`, field/parameter lookup, and quoted equality) from HTML to PDF/XLSX/DOCX.

Latest loop slice: extended `IsNothing` and boolean `Not`/`And`/`Or` regression assertions from HTML to PDF/XLSX/DOCX while retaining fail-closed unknown expressions.

Latest loop slice: extended basic `Len`/`Trim`/`UCase`/`LCase` fixture assertions from HTML to PDF/XLSX/DOCX.

Latest loop slice: extended styled RDLC text coverage to assert red color and vertical writing semantics across HTML/PDF/XLSX/DOCX.

Latest loop slice: extended sorted-tablix aggregate and Alpha/Beta/Gamma ordering assertions from HTML to PDF/XLSX/DOCX.

Latest loop slice: extended `fr-FR` decimal-comma tablix ordering assertions from HTML to PDF/XLSX/DOCX with culture restoration retained.

Latest loop slice: extended grouped tablix scope assertions for headers, group counts, totals, subtotals, and averages from HTML to PDF/XLSX/DOCX.

Latest loop slice: extended scoped `First`/`Last`/`Count`/`Min`/`Max` aggregate assertions from HTML to PDF/XLSX/DOCX.

Latest loop slice: extended allow-listed visibility assertions for hidden and expanded content from HTML to PDF/XLSX/DOCX.

Latest loop slice: extended three-level nested row-group aggregate markers from HTML to PDF/XLSX/DOCX.

Latest loop slice: extended parameter-disabled group page-break semantic markers from HTML to PDF/XLSX/DOCX while retaining cross-renderer page-count assertions.

Latest loop slice: extended parameter-disabled sibling `StartAndEnd` semantic markers from HTML to PDF/XLSX/DOCX while retaining enabled/disabled page-count assertions.

Latest loop slice: extended field-disabled group page-break semantic markers from HTML to PDF/XLSX/DOCX while retaining group-scope page-count assertions.

Latest loop slice: extended case-insensitive undeclared-parameter resolution from HTML to PDF/XLSX/DOCX.

Latest loop slice: extended nested parent-offset coverage with the existing HTML coordinate assertion plus PDF/XLSX/DOCX item-presence checks.

Latest loop slice: extended parameter-backed image resolution across HTML/PDF/XLSX/DOCX, corrected the shared resolver fixture to valid PNG bytes, and verified native Office media relationships.

Latest loop slice: extended expression-backed tablix-cell image propagation across HTML/PDF/XLSX/DOCX with native Office media relationship checks.

Latest loop slice: extended embedded-image resolution across HTML/PDF/XLSX/DOCX with native Office media relationship checks.

Latest loop slice: extended explicit grouped page-break coverage across HTML/PDF/XLSX/DOCX and made XLSX marker checks page-aware across `sheet1.xml` and `sheet2.xml`.

Latest loop slice: extended sibling branch `Start` page-break coverage across HTML/PDF/XLSX/DOCX and aggregated markers across three XLSX page worksheets.

Latest loop slice: extended sibling branch `StartAndEnd` page-break coverage across HTML/PDF/XLSX/DOCX and aggregated markers across four XLSX page worksheets.

Latest loop slice: extended nested child `End` page-break coverage across HTML/PDF/XLSX/DOCX and aggregated wrapper/subtotal markers across four XLSX page worksheets.

Latest loop slice: extended nested sibling branch coverage across HTML/PDF/XLSX/DOCX and aggregated branch/subtotal/detail markers across eight XLSX page worksheets.

Latest loop slice: extended single-root nested sibling coverage across HTML/PDF/XLSX/DOCX and aggregated root/child/detail/total markers across two XLSX page worksheets.

Latest loop slice: extended the single-root nested sibling no-static-header fixture across HTML/PDF/XLSX/DOCX and aggregated category/region/child/subtotal/detail/total markers across two XLSX page worksheets.

Latest loop slice: extended terminal sibling-branch coverage across HTML/PDF/XLSX/DOCX with category/region/interstitial/detail markers and one-page parity checks.

Latest loop slice: extended hierarchy-first nested-member-tree coverage across HTML/PDF/XLSX/DOCX with category/region/name summaries, repeated nested-static-member markers, and one-page parity checks.

Latest loop slice: extended sibling row-group no-static-header coverage across HTML/PDF/XLSX/DOCX with category/region/detail/total markers and one-page parity checks.

Latest loop slice: extended nested group page-break scope coverage across HTML/PDF/XLSX/DOCX with category/region/detail markers aggregated across two XLSX page worksheets.

Latest loop slice: extended linear nested StartAndEnd page-break coverage across HTML/PDF/XLSX/DOCX with title/category/region/detail markers aggregated across four XLSX page worksheets.

Latest loop slice: extended nested static detail/subtotal coverage across HTML/PDF/XLSX/DOCX with repeated region/category subtotal counts, detail markers, grand total, and one-page parity.

Latest loop slice: extended single-child static-wrapper coverage across HTML/PDF/XLSX/DOCX with category/region/detail markers, wrapper occurrence counts, root total, and one-page parity.

Latest loop slice: extended multi-child static-wrapper coverage across HTML/PDF/XLSX/DOCX with category/region/product branches, wrapper/leading-row occurrence counts, scoped footers, root total, and one-page parity.

Latest loop slice: extended null group-key aggregate coverage across HTML/PDF/XLSX/DOCX with shared null-scope First/Last/Subtotal markers, detail/amount values, separate non-null grouping, and one-page parity.

Latest loop slice: extended empty-data-region static-header coverage across HTML/PDF/XLSX/DOCX with title-presence/detail-omission assertions and one-page parity.

Latest loop slice: hardened NoRowsMessage coverage across HTML/PDF/XLSX/DOCX with empty-state title/message presence, detail omission, and one-page parity assertions.

Latest loop slice: hardened embedded-image coverage with shared HTML/PDF/XLSX/DOCX page parity alongside existing native Office media relationship checks.

Latest loop slice: added shared one-page parity to clipped embedded-image crop/anchor coverage while retaining OpenXML-specific geometry, crop, and relationship assertions.

Latest loop slice: added shared one-page parity to bar/column chart coverage while retaining native HTML, PDF, XLSX, and DOCX chart assertions.

Latest loop slice: added shared one-page parity to line/area/pie/doughnut chart coverage while retaining native semantic chart assertions across HTML, PDF, XLSX, and DOCX.

Latest loop slice: added shared one-page parity to the dedicated column-chart fixture while retaining its focused HTML/PDF/XLSX/DOCX chart assertions.

Latest loop slice: completed 20 assertion-hardening slices across parameter, expression, layout, image, sorting, aggregate, visibility, nested-group, and chart regressions; all targeted slices and the full repository gate passed.

Latest loop slice: completed 50 assertion-hardening slices across engine and low-level renderer tests; targeted blocks, 129 full tests, solution build, validators, and diff review passed, with HTML-placeholder-image and U+001F delimiter fixtures intentionally excluded from portable parity.

Latest loop slice: investigated Excel openability across 46 generated XLSX packages, generated fresh cross-platform and feature-showcase diagnostics, added a dependency-free package graph validator, and removed the dangling default-font theme reference by emitting explicit ARGB black.

Latest loop slice: reproduced Excel's repair dialog on the exact `artifacts/cross-platform/cross-platform-smoke.xlsx`, confirmed the SpreadsheetML child-order defect in `sheet1.xml`, corrected `sheetViews`/`cols`/`sheetData`/`hyperlinks` ordering, regenerated the requested artifact, and verified it opens in installed Excel without repair.

Latest loop slice: added `.devbuddy/tools/clear_artifacts.py` as a standard-library, dry-run-by-default cleanup utility with exact-root and link-safety guards; current artifacts remain untouched until `--apply` is explicitly requested.

Latest loop slice: added the Thai manual `.devbuddy/tools/clear_artifacts.th.md`, linked it from the reusable-tools index, and documented that artifact cleanup cannot by itself repair malformed Excel OpenXML.

Latest loop slice: added the OpenXML preview-layer regression for semantic text, hyperlink, image, chart, and shape objects; targeted and full tests passed 130/130, and regenerated package, smoke, showcase, TRX, and cross-format evidence passed.

Latest loop slice: extended the existing multi-value `Join` fixture with malformed-arity and unsupported-code cases, then asserted the allow-listed result and fail-closed security behavior across HTML, PDF, XLSX, and DOCX; targeted 2/2 and full 130/130 tests passed.

Latest loop slice: made top-level static rows framing sibling row-group branches require explicit `KeepWithGroup='After'` before and `'Before'` after the branches; added an invalid-boundary fixture and rejection test while preserving supported static-wrapper layouts; targeted 4/4, full 131/131, solution build, artifact validators, and cross-format validators passed.

Latest loop slice: completed both sides of the static sibling-boundary contract with independent invalid-leading and invalid-trailing fixtures, converted the rejection regression to two cases, refreshed the fixture index/count to 64, and retained supported-wrapper coverage; targeted 5/5, full 132/132, solution build, artifact validators, and cross-format validators passed.

Latest loop slice: added the combined nested static detail/subtotal plus following sibling dynamic branch fixture and cross-format regression, proving recursive template indexing composes both supported shapes; fixture inventory reached 65, targeted 5/5, full 133/133, solution build, artifact validators, and cross-format validators passed.

Latest loop slice: added the no-header variant of the combined nested static detail/subtotal plus sibling branch shape, proving optional root headers do not shift recursive template offsets; fixture inventory reached 66, targeted 6/6, full 134/134, solution build, artifact validators, and cross-format validators passed.

Latest loop slice: expanded the composite nested static detail/subtotal plus sibling branch contract with `End` and `StartAndEnd` page-break fixtures, page-aware XLSX aggregation, and cross-format semantic assertions; materialized page counts are 4 and 6, fixture inventory reached 68, targeted 6/6, full 136/136, solution build, artifact validators, and cross-format validators passed.
Latest loop slice: completed the larger page-break control matrix for the composite nested static detail/subtotal plus sibling branch shape by adding a parameter-disabled `StartAndEnd` fixture and cross-format enabled/disabled assertions; enabled materializes 6 pages, disabled materializes 1 page, fixture inventory reached 69, targeted 4/4, full 137/137, solution build, artifact validators, and cross-format validators passed.
Latest export verification: imported and rendered all 3 exported XLSX workbooks across 5 worksheets, visually inspected the generated previews, verified all Office ZIP/XML packages and XLSX worksheet child order, matched embedded feature-preview PNGs byte-for-byte, confirmed PDF/manifest page counts, and reran artifact plus cross-format validators; no export corruption or Excel repair signature was found.
