# ReportViewerCore v2 Feature Showcase

This console project renders one backend-neutral two-page `ReportDocument` through every portable output path:

- PNG, one file per page
- PDF
- HTML/SVG
- XLSX
- DOCX

The generated `feature-showcase-manifest.json` lists the feature coverage and every expected output file. Run it from the repository root:

```powershell
dotnet run --project ReportViewerCore.Sample.FeatureShowcase -- artifacts/feature-showcase
python .devbuddy/tools/validate_v2_artifacts.py --showcase artifacts/feature-showcase
```

The showcase intentionally includes the recently added variable page sizes, table spans, chart kinds, image crop metadata, and vector clipping cases.
