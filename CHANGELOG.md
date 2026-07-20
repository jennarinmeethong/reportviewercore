# 2.0.0-preview.1
 * Added the backend-neutral .NET 10 rendering boundary and cross-platform Skia, HTML, OpenXML, and Windows adapter packages.
 * Added the constrained RDLC engine workflow with tablixes, grouping, expressions, images, charts, subreports, pagination, and cell-local visual items.
 * Extended the constrained expression allow-list with `IsNothing`, `Not`, `And`, and `Or`, while unsupported null operands remain inert.
 * Preserved styled text colors in OpenXML output and reject chart types that the basic bar-chart contract cannot represent.
 * Added explicit validation for caller-registered Skia font files.
 * Preserved horizontal text offsets when writing DOCX paragraphs.
 * Preserved horizontal offsets for inline DOCX image and chart drawings.
 * Added explicit constrained-engine diagnostics for branching row-group hierarchies and unsupported grouped page-break locations.
 * Fixed Excel hyperlink references for multiple links placed on different rows.
 * Preserved leading and trailing whitespace in OpenXML text runs.
* Applied the safe hyperlink URL allow-list to OpenXML renderers.
* Kept composite RDLC group scopes separate when field values contain the internal grouping delimiter.
* Centralized hyperlink URL validation across the Skia, HTML, and OpenXML renderers.
* Preserved reverse-direction line geometry in Excel DrawingML and Word VML exports.
* Applied grouped RDLC page breaks only when the configured group scope changes.
 * Added opt-in `CreatePortableDocument`/`RenderPortable` bridges for the legacy .NETCore and WinForms APIs; the existing `Render` path remains unchanged.
 * This preview is not full SSRS/RPL parity. Windows bridge execution, matching-RID runtime smoke, and advanced recursive tablix/chart/map behavior remain release-gate work.

# 15.1.33
 * Updated to Microsoft.CodeAnalysis.VisualBasic version 5.0.0

# 15.1.32
 * Fixed missing string resources in NETCore package

# 15.1.31
 * Reverted back to NTML auth

# 15.1.30
 * Fixed satellite assemblies names and removed duplicated resources
 * Switched to Windows/Negotiate authentication

# 15.1.29
 * Included translated resources

# 15.1.28
 * Fixed transitive references resolving for WPF

# 15.1.27
 * Added .NET 10 version
 * Unified System.Resources.Extensions references for .NET 8 and .NET 9
 * AOT compatibility fixes
 * Minor changes towards .NET Standard support

# 15.1.26
 * Fixed custom headers support
 * Fixed CodeModule references resolving

# 15.1.25
 * Removed .NET 6 and .NET 7 support

# 15.1.24
 * Added .NET 9 version
 * Updated System.IO.Packaging and System.ServiceModel.Http references

# 15.1.23
 * Switched to version 4.8.0 of Microsoft.CodeAnalysis to match version required by Microsoft.VisualStudio.Web.CodeGeneration.Design 8.0.2+

# 15.1.22
 * Removed .NET Core 3.1 and .NET 5 support
 * Added CSV Renderer
 * Added XML Data Exporter

# 15.1.21
 * Added assembly strong name

# 15.1.20
 * Fixed bug causing errors when exporting report with invalid image placeholders
 * Removed dependencies on BinaryFormatter in ResourceManager

# 15.1.19
 * Added .NET 8 version

# 15.1.18
 * Added localized string for WinForms Report Viewer
 * Fixed hyperlink handling in WinForms Report Viewer
 * Added .NET 7 version
 * Removed BinaryFormatter dependencies for .NET 7+

# 15.1.17
 * Fixed broken error messages in NETCore project due to missing string resources

# 15.1.16
 * Fixed ValidValues floating point culture-dependent formatting
 * Custom system-wide culture (LOCALE_CUSTOM_UNSPECIFIED) workaround
 * Multi-target .NET Core 3.1, .NET 5 and .NET 6 due to Microsoft.CodeAnalysis.VisualBasic dependencies

# 15.1.15
 * Fixed race condition in WinForms ReportViewer caused by missing thread abort in RefreshReport
 * Removed SqlClient and OleDb dependencies
 * .NET 6 compatibility

# 15.1.14
 * Added missing System.Text.RegularExpressions.dll assembly reference
 * https support for server-side reports
 * Fixed chart HTML4/5 rendering
 * Fixed invalid PDF missing image placeholder format
 * Fixed InvalidImage rendering in non-WinForms applications

# 15.1.13
 * Fixed BinaryFormatter/ResX issues with PDF export on .NET 5

# 15.1.12
 * Changed HTML4.0 and HTML5 renderer to embed images as data URLs
 * Fixed HTML5 renderer image scaling with javascript disabled
 * Added workaround for aborting background worker thread
 * Added missing ImageRenderer resources
 * Added remote report support to .NETCore version

# 15.1.11
 * Added remote report support to WinForms version

# 15.1.10
 * Added missing resources needed for gauge control

# 15.1.9
 * Added HTML4.0 / HTML5 / MHTML output format

# 15.1.8
 * Added missing resources, fixed resource logical names

# 15.1.7
 * Fixed subreport assembly naming
 * Fixed PDF 1252 character encoding

# 15.1.6
 * Added report assembly unloading support
 * Added missing UI resources

# 15.1.5
 * Added .NET 5 support
 * Added missing WinForms UI resources

# 15.1.4
 * Fixed loading images from external sources

# 15.1.3
 * Fixed IIF report expression
 * Added detailed compilation error message

# 15.1.2
 * Basic cross-platform support
 * Removed unused native method references
 * Renamed ambiguous projects

# 15.1.1
 * Initial version based on ReportViewer 15.0.1404.0
