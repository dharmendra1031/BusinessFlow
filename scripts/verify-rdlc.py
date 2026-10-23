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
    report = ET.parse(reports / f"{name}.rdlc")
    dataset = ET.parse(reports / f"{name}.xsd")
    xs = "{http://www.w3.org/2001/XMLSchema}"
    rdl = "{http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition}"
    rd = "{http://schemas.microsoft.com/SQLServer/reporting/reportdesigner}"
    msdata = "{urn:schemas-microsoft-com:xml-msdata}IsDataSet"
    if not any(element.get(msdata) == "true" for element in dataset.getroot().iter(f"{xs}element")):
        raise SystemExit(f"{name}: not a typed DataSet")
    expected = definition["Columns"]
    schema_types = {"string": "string", "long": "long", "decimal": "decimal", "date": "dateTime"}
    report_types = {"string": "String", "long": "Int64", "decimal": "Decimal", "date": "DateTime"}
    xsd_columns = {x.get("name"): x.get("type") for x in
                   dataset.findall(f".//{xs}element[@name='Items']/{xs}complexType/{xs}sequence/{xs}element")}
    rdl_columns = {x.get("Name"): x.findtext(f"{rd}TypeName") for x in
                   report.findall(f".//{rdl}DataSet[@Name='Items']/{rdl}Fields/{rdl}Field")}
    if xsd_columns != {key: "xs:" + schema_types[kind] for key, kind in expected.items()}:
        raise SystemExit(f"{name}: XSD column types differ from definitions.json")
    if rdl_columns != {key: "System." + report_types[kind] for key, kind in expected.items()}:
        raise SystemExit(f"{name}: RDLC column types differ from definitions.json")
    row = {key: samples[kind] for key, kind in definition["Columns"].items()}
    with tempfile.TemporaryDirectory() as directory:
        output = Path(directory) / "result.pdf"
        result = subprocess.run([renderer, name, str(output)], input=json.dumps([row]),
                                text=True, capture_output=True, timeout=30, check=False)
        if result.returncode or not output.exists() or not output.read_bytes().startswith(b"%PDF"):
            raise SystemExit(f"RDLC {name} failed: {result.stderr}")
    print(f"PASS: {name}")
