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
    for suffix in ("rdlc", "xsd"):
        ET.parse(reports / f"{name}.{suffix}")
    row = {key: samples[kind] for key, kind in definition["Columns"].items()}
    with tempfile.TemporaryDirectory() as directory:
        output = Path(directory) / "result.pdf"
        result = subprocess.run([renderer, name, str(output)], input=json.dumps([row]),
                                text=True, capture_output=True, timeout=30, check=False)
        if result.returncode or not output.exists() or not output.read_bytes().startswith(b"%PDF"):
            raise SystemExit(f"RDLC {name} failed: {result.stderr}")
    print(f"PASS: {name}")
