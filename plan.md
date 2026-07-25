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
