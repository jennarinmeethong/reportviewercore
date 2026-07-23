#!/usr/bin/env python3
"""Compare the stable semantic content and page mapping of RDLC showcase exports."""

from __future__ import annotations

import argparse
import html
import json
import re
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image
from pypdf import PdfReader


MARKERS = (
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


def validate(directory: Path) -> None:
    manifest = json.loads((directory / "rdlc-feature-showcase-manifest.json").read_text(encoding="utf-8"))
    html_path = directory / "rdlc-feature-showcase.html"
    html_text = html_path.read_text(encoding="utf-8")
    svg_parts = re.findall(r"<svg\b.*?</svg>", html_text, flags=re.DOTALL | re.IGNORECASE)
    if not svg_parts:
        fail("HTML contains no SVG pages")
    for index, svg in enumerate(svg_parts, start=1):
        try:
            ET.fromstring(svg)
        except ET.ParseError as error:
            fail(f"HTML SVG page {index} is malformed: {error}")

    pdf_reader = PdfReader(str(directory / "rdlc-feature-showcase.pdf"))
    pdf_text = " ".join(normalize(page.extract_text() or "") for page in pdf_reader.pages)

    with zipfile.ZipFile(directory / "rdlc-feature-showcase.docx") as archive:
        if archive.testzip() is not None:
            fail("DOCX contains a corrupt entry")
        docx_text = archive_text(archive, ("word/",))
        document_root = ET.fromstring(archive.read("word/document.xml"))
        docx_pages = len(document_root.findall(".//{http://schemas.openxmlformats.org/wordprocessingml/2006/main}sectPr"))
        docx_previews = [archive.read(f"word/media/preview{index}.png") for index in range(1, docx_pages + 1)]

    with zipfile.ZipFile(directory / "rdlc-feature-showcase.xlsx") as archive:
        if archive.testzip() is not None:
            fail("XLSX contains a corrupt entry")
        xlsx_text = archive_text(archive, ("xl/",))
        xlsx_pages = len([name for name in archive.namelist() if name.startswith("xl/worksheets/sheet") and name.endswith(".xml")])
        xlsx_previews = [archive.read(f"xl/media/preview{index}.png") for index in range(1, xlsx_pages + 1)]

    formats = {
        "HTML": normalize(" ".join(re.findall(r"<text\b[^>]*>(.*?)</text>", html_text, flags=re.DOTALL))),
        "PDF": pdf_text,
        "DOCX": docx_text,
        "XLSX": xlsx_text,
    }
    missing = {
        marker: [name for name, text in formats.items() if marker.casefold() not in text.casefold()]
        for marker in MARKERS
    }
    missing = {marker: names for marker, names in missing.items() if names}
    if missing:
        fail(f"semantic markers differ across formats: {missing}")

    pngs = sorted(directory.glob("rdlc-feature-showcase-page-*.png"))
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

    print(f"validated cross-format showcase: {len(formats)} semantic formats, matching Office page previews, page_counts={page_counts}, markers={len(MARKERS)}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path, help="rdlc-feature-showcase directory")
    arguments = parser.parse_args()
    validate(arguments.directory)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
