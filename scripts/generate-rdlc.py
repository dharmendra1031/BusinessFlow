"""Generate checked-in RDLC templates and XSD datasets from report definitions."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1] / "reporting" / "ReportRenderer" / "Reports"
RDL = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"
RD = "http://schemas.microsoft.com/SQLServer/reporting/reportdesigner"
XS = "http://www.w3.org/2001/XMLSchema"
ET.register_namespace("", RDL)
ET.register_namespace("rd", RD)
ET.register_namespace("xs", XS)


def child(parent, name, value=None, namespace=RDL, **attributes):
    element = ET.SubElement(parent, f"{{{namespace}}}{name}", attributes)
    if value is not None:
        element.text = str(value)
    return element


def textbox(parent, name, value, bold=False, header=False):
    box = child(parent, "Textbox", Name=name)
    child(box, "CanGrow", "true")
    paragraphs = child(box, "Paragraphs")
    paragraph = child(paragraphs, "Paragraph")
    run = child(child(paragraph, "TextRuns"), "TextRun")
    child(run, "Value", value)
    style = child(run, "Style")
    child(style, "FontFamily", "Arial")
    child(style, "FontSize", "8pt" if not header else "10pt")
    if bold:
        child(style, "FontWeight", "Bold")
    child(paragraph, "Style")
    style = child(box, "Style")
    border = child(style, "Border")
    child(border, "Style", "Solid")
    child(border, "Color", "#D1D5DB")
    for side in ("Left", "Right", "Top", "Bottom"):
        child(style, f"Padding{side}", "3pt")
    return box


def make_report(report_type, definition):
    columns = definition["Columns"]
    root = ET.Element(f"{{{RDL}}}Report")
    child(root, "AutoRefresh", 0)
    source = child(child(root, "DataSources"), "DataSource", Name="LocalSource")
    connection = child(source, "ConnectionProperties")
    child(connection, "DataProvider", "System.Data.DataSet")
    child(connection, "ConnectString", "/* Local Connection */")
    dataset = child(child(root, "DataSets"), "DataSet", Name="Items")
    query = child(dataset, "Query")
    child(query, "DataSourceName", "LocalSource")
    child(query, "CommandText", "/* Local Query */")
    fields = child(dataset, "Fields")
    type_names = {"string": "String", "long": "Int64", "decimal": "Decimal", "date": "DateTime"}
    for name, kind in columns.items():
        field = child(fields, "Field", Name=name)
        child(field, "DataField", name)
        child(field, "TypeName", f"System.{type_names[kind]}", namespace=RD)

    section = child(child(root, "ReportSections"), "ReportSection")
    body = child(section, "Body")
    items = child(body, "ReportItems")
    title = textbox(items, "ReportTitle", "=Parameters!Title.Value", bold=True, header=True)
    child(title, "Top", "0cm")
    child(title, "Left", "0cm")
    child(title, "Height", "1cm")
    child(title, "Width", "25cm")
    table = child(items, "Tablix", Name="ReportTable")
    table_body = child(table, "TablixBody")
    widths = child(table_body, "TablixColumns")
    width = 25 / len(columns)
    for _ in columns:
        child(child(widths, "TablixColumn"), "Width", f"{width:.3f}cm")
    rows = child(table_body, "TablixRows")
    labels = definition.get("Labels", {})
    formats = definition.get("Formats", {})
    for row_index in range(2):
        row = child(rows, "TablixRow")
        child(row, "Height", "0.65cm")
        cells = child(row, "TablixCells")
        for index, name in enumerate(columns):
            cell = child(child(cells, "TablixCell"), "CellContents")
            label = name.replace("AtUtc", " Date").replace("Id", " ID")
            expression = formats.get(name, f"=Fields!{name}.Value")
            textbox(cell, f"Cell_{row_index}_{index}",
                    labels.get(name, label) if row_index == 0 else expression,
                    bold=row_index == 0)
    hierarchy = child(child(table, "TablixColumnHierarchy"), "TablixMembers")
    for _ in columns:
        child(hierarchy, "TablixMember")
    hierarchy = child(child(table, "TablixRowHierarchy"), "TablixMembers")
    child(child(hierarchy, "TablixMember"), "KeepWithGroup", "After")
    child(child(hierarchy, "TablixMember"), "Group", Name="Details")
    child(table, "DataSetName", "Items")
    child(table, "Top", "1.2cm")
    child(table, "Left", "0cm")
    child(table, "Height", "1.3cm")
    child(table, "Width", "25cm")
    child(body, "Height", "3cm")
    child(section, "Width", "25cm")
    page = child(section, "Page")
    for name, value in {"PageHeight": "21cm", "PageWidth": "29.7cm", "LeftMargin": "2cm",
                        "RightMargin": "2cm", "TopMargin": "2cm", "BottomMargin": "2cm"}.items():
        child(page, name, value)
    parameter = child(child(root, "ReportParameters"), "ReportParameter", Name="Title")
    child(parameter, "DataType", "String")
    child(parameter, "Prompt", "Title")
    ET.indent(root)
    ET.ElementTree(root).write(ROOT / f"{report_type}.rdlc", encoding="utf-8", xml_declaration=True)

    schema = ET.Element(f"{{{XS}}}schema", {"id": "Items"})
    element = ET.SubElement(schema, f"{{{XS}}}element", {"name": "Items"})
    complex_type = ET.SubElement(element, f"{{{XS}}}complexType")
    sequence = ET.SubElement(complex_type, f"{{{XS}}}sequence")
    xsd_types = {"string": "string", "long": "long", "decimal": "decimal", "date": "dateTime"}
    for name, kind in columns.items():
        ET.SubElement(sequence, f"{{{XS}}}element", {"name": name, "type": f"xs:{xsd_types[kind]}",
                                                     "minOccurs": "0"})
    ET.indent(schema)
    ET.ElementTree(schema).write(ROOT / f"{report_type}.xsd", encoding="utf-8", xml_declaration=True)


if __name__ == "__main__":
    for report_type, definition in json.loads((ROOT / "definitions.json").read_text()).items():
        make_report(report_type, definition)
