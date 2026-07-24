#!/usr/bin/env python3
"""Compare stable semantic content and page mapping across feature showcase exports."""

from __future__ import annotations

import argparse
import html
import json
import re
import zipfile
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

from PIL import Image
from pypdf import PdfReader


RDLC_MARKERS = (
    "RDLC engine feature showcase",
    "Fields, parameters, grouping, aggregates, visibility, links, images, shapes, and charts.",
    "Open parameter-driven hyperlink",
    "Conditional visibility is enabled",
    "Grouped tablix, expressions, and cell spans",
    "Category / item / amount (ColSpan=3)",
    "Category: North",
    "Rows: 2",
    "Sum: 20",
    "Item: Alpha",
    "Amount: 12",
    "Average: 10",
    "Item: Beta",
    "Amount: 8",
    "Subtotal = 20",
    "Group complete",
    "Hierarchy-first nested member tree",
    "Hierarchy-first template mapping",
    "Category branch: North (2)",
    "Static member between root branches",
    "Region branch: West (2)",
    "Nested static member",
    "Name branch: Alpha",
    "Category: South",
    "Rows: 2",
    "Sum: 21",
    "Item: Gamma",
    "Amount: 16",
    "Average: 10.5",
    "Item: Delta",
    "Amount: 5",
    "Subtotal = 21",
    "Bar",
    "Column",
    "Line",
    "Area",
    "Pie",
    "Doughnut",
    "repeating page header",
    "repeating page footer",
)

CANVAS_MARKERS = (
	"ReportViewer Core v2",
	"Feature showcase",
	"Text, style, direction, and hyperlinks",
	"Relative report link",
	"HTTP documentation link",
	"Images, shapes, and table-cell metadata",
	"Merged header (ColSpan=2)",
	"The same ReportDocument is exported to PNG, PDF, HTML, XLSX, and DOCX.",
	"Charts and page-boundary clipping",
	"Bar shim",
	"Column",
	"Line",
	"Area",
	"Pie",
	"Doughnut",
	"Clipping cases (objects intentionally cross the page edge)",
	"Visible portions remain inside the page in Skia, HTML, DOCX, and XLSX.",
)

SHOWCASES = {
    "feature-showcase": {
        "markers": CANVAS_MARKERS,
        "hyperlinks": ("/reports/detail", "https://example.com/report", "https://example.com/cell", "https://example.com/clipped-link", "https://example.com/vertical-clipped-link"),
        "images": 2,
        "excel_shapes": 8,
        "chart_types": {"barChart": 3, "lineChart": 1, "areaChart": 1, "pieChart": 1, "doughnutChart": 1},
        "cropped_image": True,
    },
    "rdlc-feature-showcase": {
        "markers": RDLC_MARKERS,
        "hyperlinks": ("https://example.com/rdlc-feature-showcase",),
        "images": 1,
        "excel_shapes": 5,
        "chart_types": {"barChart": 2, "lineChart": 1, "areaChart": 1, "pieChart": 1, "doughnutChart": 1},
        "cropped_image": False,
    },
}


def fail(message: str) -> "NoReturn":
    raise SystemExit(f"cross-format validation failed: {message}")


def normalize(value: str) -> str:
    value = re.sub(r"<[^>]+>", " ", value)
    return re.sub(r"\s+", " ", html.unescape(value)).strip()


def xml_text(path: Path) -> str:
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as error:
        fail(f"invalid XML {path}: {error}")
    return " ".join(normalize(node.text or "") for node in root.iter() if node.text and node.text.strip())


def archive_text(archive: zipfile.ZipFile, prefixes: tuple[str, ...]) -> str:
    values: list[str] = []
    for name in archive.namelist():
        if not name.endswith(".xml") or not name.startswith(prefixes):
            continue
        try:
            root = ET.fromstring(archive.read(name))
        except ET.ParseError as error:
            fail(f"invalid XML {name}: {error}")
        values.extend(normalize(node.text or "") for node in root.iter() if node.text and node.text.strip())
    return " ".join(values)


def relationship_targets(archive: zipfile.ZipFile, prefixes: tuple[str, ...], relationship_name: str) -> set[str]:
    targets: set[str] = set()
    for name in archive.namelist():
        if not name.endswith(".rels") or not name.startswith(prefixes):
            continue
        try:
            root = ET.fromstring(archive.read(name))
        except ET.ParseError as error:
            fail(f"invalid relationship XML {name}: {error}")
        for relationship in root:
            if relationship.attrib.get("Type", "").endswith(f"/{relationship_name}"):
                target = relationship.attrib.get("Target")
                if target:
                    targets.add(target)
    return targets


def html_hyperlink_targets(contents: str) -> set[str]:
    return {html.unescape(target) for target in re.findall(r'<a\b[^>]*\bhref="([^"]+)"', contents, flags=re.IGNORECASE)}


def pdf_hyperlink_targets(reader: PdfReader) -> set[str]:
    targets: set[str] = set()
    for page in reader.pages:
        for annotation_reference in page.get("/Annots", []):
            annotation = annotation_reference.get_object()
            action = annotation.get("/A")
            if action is None:
                continue
            target = action.get_object().get("/URI")
            if target:
                targets.add(str(target))
    return targets


def native_chart_types(archive: zipfile.ZipFile, prefix: str) -> Counter[str]:
    chart_types: Counter[str] = Counter()
    for name in archive.namelist():
        if not name.startswith(prefix) or not name.endswith(".xml"):
            continue
        try:
            root = ET.fromstring(archive.read(name))
        except ET.ParseError as error:
            fail(f"invalid chart XML {name}: {error}")
        chart_types.update(element.tag.rsplit("}", 1)[-1] for element in root.iter() if element.tag.rsplit("}", 1)[-1] in {"barChart", "lineChart", "areaChart", "pieChart", "doughnutChart"})
    return chart_types


def native_excel_shape_count(archive: zipfile.ZipFile) -> int:
    count = 0
    for name in archive.namelist():
        if not name.startswith("xl/drawings/") or not name.endswith(".xml"):
            continue
        try:
            root = ET.fromstring(archive.read(name))
        except ET.ParseError as error:
            fail(f"invalid drawing XML {name}: {error}")
        count += sum(element.tag.rsplit("}", 1)[-1] == "sp" for element in root.iter())
    return count


def validate_png_entries(archive: zipfile.ZipFile, entries: list[str], format_name: str) -> None:
    for entry in entries:
        if archive.read(entry)[:8] != b"\x89PNG\r\n\x1a\n":
            fail(f"{format_name} image is not a PNG: {entry}")


def validate_native_objects(directory: Path, prefix: str, showcase: dict[str, object]) -> None:
    expected_hyperlinks = set(showcase["hyperlinks"])
    expected_images = int(showcase["images"])
    expected_shapes = int(showcase["excel_shapes"])
    expected_chart_types = Counter(showcase["chart_types"])
    cropped_image = bool(showcase["cropped_image"])

    actual_hyperlinks = html_hyperlink_targets((directory / f"{prefix}.html").read_text(encoding="utf-8"))
    if not expected_hyperlinks.issubset(actual_hyperlinks):
        fail(f"HTML is missing hyperlinks: {sorted(expected_hyperlinks - actual_hyperlinks)}")

    actual_hyperlinks = pdf_hyperlink_targets(PdfReader(str(directory / f"{prefix}.pdf")))
    if not expected_hyperlinks.issubset(actual_hyperlinks):
        fail(f"PDF is missing hyperlinks: {sorted(expected_hyperlinks - actual_hyperlinks)}")

    with zipfile.ZipFile(directory / f"{prefix}.docx") as archive:
        actual_hyperlinks = relationship_targets(archive, ("word/_rels/",), "hyperlink")
        if not expected_hyperlinks.issubset(actual_hyperlinks):
            fail(f"DOCX is missing hyperlinks: {sorted(expected_hyperlinks - actual_hyperlinks)}")
        images = [name for name in archive.namelist() if name.startswith("word/media/image") and name.endswith(".png")]
        if len(images) != expected_images:
            fail(f"DOCX image count differs: expected {expected_images}, found {len(images)}")
        validate_png_entries(archive, images, "DOCX")
        chart_types = native_chart_types(archive, "word/charts/")
        if chart_types != expected_chart_types:
            fail(f"DOCX chart types differ: expected {dict(expected_chart_types)}, found {dict(chart_types)}")
        if cropped_image and "cropleft=" not in archive.read("word/document.xml").decode("utf-8"):
            fail("DOCX is missing page-clipped image crop metadata")

    with zipfile.ZipFile(directory / f"{prefix}.xlsx") as archive:
        actual_hyperlinks = relationship_targets(archive, ("xl/worksheets/_rels/",), "hyperlink")
        if not expected_hyperlinks.issubset(actual_hyperlinks):
            fail(f"XLSX is missing hyperlinks: {sorted(expected_hyperlinks - actual_hyperlinks)}")
        images = [name for name in archive.namelist() if name.startswith("xl/media/image") and name.endswith(".png")]
        if len(images) != expected_images:
            fail(f"XLSX image count differs: expected {expected_images}, found {len(images)}")
        validate_png_entries(archive, images, "XLSX")
        chart_types = native_chart_types(archive, "xl/charts/")
        if chart_types != expected_chart_types:
            fail(f"XLSX chart types differ: expected {dict(expected_chart_types)}, found {dict(chart_types)}")
        shape_count = native_excel_shape_count(archive)
        if shape_count != expected_shapes:
            fail(f"XLSX shape count differs: expected {expected_shapes}, found {shape_count}")
        if cropped_image and not any(b"srcRect" in archive.read(name) for name in archive.namelist() if name.startswith("xl/drawings/") and name.endswith(".xml")):
            fail("XLSX is missing page-clipped image crop metadata")


def validate(directory: Path) -> None:
    prefix, showcase = detect_showcase(directory)
    markers = showcase["markers"]
    manifest = json.loads((directory / f"{prefix}-manifest.json").read_text(encoding="utf-8"))
    html_path = directory / f"{prefix}.html"
    html_text = html_path.read_text(encoding="utf-8")
    svg_parts = re.findall(r"<svg\b.*?</svg>", html_text, flags=re.DOTALL | re.IGNORECASE)
    if not svg_parts:
        fail("HTML contains no SVG pages")
    for index, svg in enumerate(svg_parts, start=1):
        try:
            ET.fromstring(svg)
        except ET.ParseError as error:
            fail(f"HTML SVG page {index} is malformed: {error}")

    pdf_reader = PdfReader(str(directory / f"{prefix}.pdf"))
    pdf_text = " ".join(normalize(page.extract_text() or "") for page in pdf_reader.pages)

    with zipfile.ZipFile(directory / f"{prefix}.docx") as archive:
        if archive.testzip() is not None:
            fail("DOCX contains a corrupt entry")
        docx_text = archive_text(archive, ("word/",))
        document_root = ET.fromstring(archive.read("word/document.xml"))
        docx_pages = len(document_root.findall(".//{http://schemas.openxmlformats.org/wordprocessingml/2006/main}sectPr"))
        docx_previews = [archive.read(f"word/media/preview{index}.png") for index in range(1, docx_pages + 1)]

    with zipfile.ZipFile(directory / f"{prefix}.xlsx") as archive:
        if archive.testzip() is not None:
            fail("XLSX contains a corrupt entry")
        xlsx_text = archive_text(archive, ("xl/",))
        xlsx_pages = len([name for name in archive.namelist() if name.startswith("xl/worksheets/sheet") and name.endswith(".xml")])
        xlsx_previews = [archive.read(f"xl/media/preview{index}.png") for index in range(1, xlsx_pages + 1)]

    validate_native_objects(directory, prefix, showcase)

    formats = {
        "HTML": normalize(" ".join(re.findall(r"<text\b[^>]*>(.*?)</text>", html_text, flags=re.DOTALL))),
        "PDF": pdf_text,
        "DOCX": docx_text,
        "XLSX": xlsx_text,
    }
    missing = {
        marker: [name for name, text in formats.items() if marker.casefold() not in text.casefold()]
        for marker in markers
    }
    missing = {marker: names for marker, names in missing.items() if names}
    if missing:
        fail(f"semantic markers differ across formats: {missing}")

    pngs = sorted(directory.glob(f"{prefix}-page-*.png"))
    page_counts = {
        "PNG": len(pngs),
        "HTML": len(svg_parts),
        "PDF": len(pdf_reader.pages),
        "DOCX": docx_pages,
        "XLSX": xlsx_pages,
        "manifest": len(manifest.get("Pages", [])),
    }
    if len(set(page_counts.values())) != 1:
        fail(f"page counts differ: {page_counts}")
    for path in pngs:
        try:
            with Image.open(path) as image:
                if image.width <= 0 or image.height <= 0:
                    fail(f"PNG has invalid dimensions: {path.name}")
        except OSError as error:
            fail(f"PNG cannot be read: {path.name}: {error}")

    for index, png in enumerate(pngs, start=1):
        expected = png.read_bytes()
        if docx_previews[index - 1] != expected:
            fail(f"DOCX preview {index} differs from {png.name}")
        if xlsx_previews[index - 1] != expected:
            fail(f"XLSX preview {index} differs from {png.name}")

    print(f"validated cross-format {prefix}: {len(formats)} semantic formats, matching Office page previews, page_counts={page_counts}, markers={len(markers)}")


def detect_showcase(directory: Path) -> tuple[str, dict[str, object]]:
    for prefix, showcase in SHOWCASES.items():
        if (directory / f"{prefix}-manifest.json").is_file():
            return prefix, showcase
    fail(f"directory does not contain a supported showcase manifest: {directory}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path, help="feature-showcase or rdlc-feature-showcase directory")
    arguments = parser.parse_args()
    validate(arguments.directory)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
