"""Render each checked-in RDLC with a typed sample row on Windows CI."""
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
reports = root / "reporting" / "ReportRenderer" / "Reports"
renderer = root / "reporting" / "ReportRenderer" / "bin" / "Release" / "net48" / "ReportRenderer.exe"
definitions = json.loads((reports / "definitions.json").read_text(encoding="utf-8"))
samples = {"string": "Sample", "long": 2, "decimal": 25.5, "date": "2026-10-19T00:00:00Z"}

for name, definition in definitions.items():
    rdlc = ET.parse(reports / f"{name}.rdlc")
    xsd = ET.parse(reports / f"{name}.xsd")
    schema = "{http://www.w3.org/2001/XMLSchema}"
    report = "{http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition}"
    designer = "{http://schemas.microsoft.com/SQLServer/reporting/reportdesigner}"
    xsd_fields = {
        field.attrib["name"]: field.attrib["type"].removeprefix("xs:")
        for field in xsd.findall(f".//{schema}sequence/{schema}element")
    }
    rdlc_fields = {
        field.attrib["Name"]: field.findtext(f"{designer}TypeName", "").removeprefix("System.")
        for field in rdlc.findall(f".//{report}DataSet[@Name='Items']/{report}Fields/{report}Field")
    }
    field_types = {"string": ("string", "String"), "long": ("long", "Int64"),
                   "decimal": ("decimal", "Decimal"), "date": ("dateTime", "DateTime")}
    expected_xsd = {field: field_types[kind][0] for field, kind in definition["Columns"].items()}
    expected_rdlc = {field: field_types[kind][1] for field, kind in definition["Columns"].items()}
    if xsd_fields != expected_xsd or rdlc_fields != expected_rdlc:
        raise SystemExit(f"RDLC/XSD dataset fields mismatch for {name}: {xsd_fields} / {rdlc_fields}")
    row = {key: samples[kind] for key, kind in definition["Columns"].items()}
    with tempfile.TemporaryDirectory() as directory:
        output = Path(directory) / "result.pdf"
        result = subprocess.run([renderer, name, str(output)], input=json.dumps([row]),
                                text=True, capture_output=True, timeout=30, check=False)
        if result.returncode or not output.exists() or not output.read_bytes().startswith(b"%PDF"):
            raise SystemExit(f"RDLC {name} failed: {result.stderr}")
    print(f"PASS: {name}")
