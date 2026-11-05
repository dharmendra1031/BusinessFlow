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


def textbox(parent, name, value, bold=False, header=False, plain=False):
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
    if not plain:
        border = child(style, "Border")
        child(border, "Style", "Solid")
        child(border, "Color", "#D1D5DB")
    for side in ("Left", "Right", "Top", "Bottom"):
        child(style, f"Padding{side}", "3pt")
    return box


def make_report(report_type, definition):
    columns = definition["Columns"]
    quotation = report_type == "quotation"
    invoice = report_type == "invoice"
    portrait = quotation or invoice
    display = (["description", "quantity", "unitPrice", "discount", "tax", "lineTotal"]
               if quotation else ["description", "amount", "received", "balance"]
               if invoice else list(columns))
    report_width = 17 if portrait else 25
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
    child(title, "Width", f"{report_width}cm")
    if quotation:
        headings = [
            ("QuoteCustomer", "=\"Client: \" & First(Fields!clientName.Value, \"Items\")", "1.05cm"),
            ("QuoteReference", "=\"Reference: \" & First(Fields!quoteRef.Value, \"Items\") & \"  |  Revision: \" & First(Fields!revision.Value, \"Items\")", "1.7cm"),
            ("QuoteStatus", "=\"Status: \" & First(Fields!status.Value, \"Items\") & \"  |  Currency: \" & First(Fields!currency.Value, \"Items\") & \"  |  Valid until: \" & First(Fields!validUntil.Value, \"Items\")", "2.35cm"),
        ]
        for name, expression, top in headings:
            box = textbox(items, name, expression, plain=True)
            child(box, "Top", top)
            child(box, "Left", "0cm")
            child(box, "Height", "0.65cm")
            child(box, "Width", "17cm")
    if invoice:
        headings = [
            ("InvoiceClient", '="Client: " & First(Fields!clientName.Value, "Items")', "1.05cm"),
            ("InvoiceRef", '="Invoice: " & First(Fields!invoiceRef.Value, "Items") & "  |  Contract: " & First(Fields!contractRef.Value, "Items")', "1.6cm"),
            ("InvoiceDates", '="Issued: " & First(Fields!issueDate.Value, "Items") & "  |  Due: " & First(Fields!dueDate.Value, "Items")', "2.15cm"),
            ("InvoiceStatus", '="Status: " & First(Fields!status.Value, "Items") & "  |  Currency: " & First(Fields!currency.Value, "Items")', "2.7cm"),
            ("InvoiceTaxNote", '=First(Fields!taxNote.Value, "Items")', "3.25cm"),
        ]
        for name, expression, top in headings:
            box = textbox(items, name, expression, plain=True)
            child(box, "Top", top)
            child(box, "Left", "0cm")
            child(box, "Height", "0.55cm")
            child(box, "Width", "17cm")
    table = child(items, "Tablix", Name="ReportTable")
    table_body = child(table, "TablixBody")
    widths = child(table_body, "TablixColumns")
    widths_cm = ([6.0, 1.9, 2.3, 2.2, 2.0, 2.6] if quotation
                 else [7.0, 3.3, 3.3, 3.4] if invoice
                 else [25 / len(columns)] * len(columns)
    for width in widths_cm:
        child(child(widths, "TablixColumn"), "Width", f"{width:.3f}cm")
    rows = child(table_body, "TablixRows")
    for row_index in range(2):
        row = child(rows, "TablixRow")
        child(row, "Height", "0.65cm")
        cells = child(row, "TablixCells")
        for index, name in enumerate(display):
            cell = child(child(cells, "TablixCell"), "CellContents")
            labels = {
                "sourceId": "Source ID", "sourceName": "Lead Source",
                "leadCount": "Total Leads", "qualifiedCount": "Qualified",
                "wonCount": "Won", "conversionRate": "Conversion %",
                "pipelineAmount": "Pipeline Amount", "wonAmount": "Won Pipeline"
            }
            label = labels.get(name, name.replace("AtUtc", " Date").replace("Id", " ID"))
            expression = f"=Fields!{name}.Value"
            if portrait and row_index == 1 and name != "description":
                expression = f'=If(IsNothing(Fields!{name}.Value), "", Format(Fields!{name}.Value, "N3"))'
            if quotation:
                label = {"description": "ITEM / DESCRIPTION", "quantity": "QTY",
                         "unitPrice": "UNIT PRICE", "discount": "DISCOUNT",
                         "tax": "TAX", "lineTotal": "TOTAL"}[name]
            if invoice:
                label = {"description": "DESCRIPTION", "amount": "AMOUNT",
                         "received": "RECEIVED", "balance": "BALANCE"}[name]
            if report_type == "source-wise" and name == "conversionRate":
                expression = '=Format(Fields!conversionRate.Value, "N1") & "%"'
            elif report_type == "source-wise" and name in ("pipelineAmount", "wonAmount"):
                expression = f'=Format(Fields!{name}.Value, "N3")'
            textbox(cell, f"Cell_{row_index}_{index}", label if row_index == 0 else expression,
                    bold=row_index == 0)
    hierarchy = child(child(table, "TablixColumnHierarchy"), "TablixMembers")
    for _ in display:
        child(hierarchy, "TablixMember")
    hierarchy = child(child(table, "TablixRowHierarchy"), "TablixMembers")
    header_member = child(hierarchy, "TablixMember")
    child(header_member, "KeepWithGroup", "After")
    if report_type == "source-wise":
        child(header_member, "RepeatOnNewPage", "true")
    child(child(hierarchy, "TablixMember"), "Group", Name="Details")
    child(table, "DataSetName", "Items")
    child(table, "Top", "4.1cm" if invoice else "3.2cm" if quotation else "1.2cm")
    child(table, "Left", "0cm")
    child(table, "Height", "1.3cm")
    child(table, "Width", f"{report_width}cm")
    child(body, "Height", "6cm" if invoice else "5cm" if quotation else "3cm")
    child(section, "Width", f"{report_width}cm")
    page = child(section, "Page")
    for name, value in {"PageHeight": "29.7cm" if portrait else "21cm",
                        "PageWidth": "21cm" if portrait else "29.7cm",
                        "LeftMargin": "2cm", "RightMargin": "2cm",
                        "TopMargin": "2cm", "BottomMargin": "2cm"}.items():
        child(page, name, value)
    parameter = child(child(root, "ReportParameters"), "ReportParameter", Name="Title")
    child(parameter, "DataType", "String")
    child(parameter, "Prompt", "Title")
    ET.indent(root)
    ET.ElementTree(root).write(ROOT / f"{report_type}.rdlc",
                               encoding="utf-8", xml_declaration=True)

    # Every report has a proper ADO.NET DataSet with a strongly typed Items table.
    msdata = "urn:schemas-microsoft-com:xml-msdata"
    ET.register_namespace("msdata", msdata)
    schema_name = ("SourceReportDataSet" if report_type == "source-wise"
                   else report_type.replace("-", "_") + "ReportDataSet")
    schema = ET.Element(f"{{{XS}}}schema", {"id": schema_name})
    root_element = ET.SubElement(schema, f"{{{XS}}}element",
                                 {"name": schema_name,
                                  f"{{{msdata}}}IsDataSet": "true",
                                  f"{{{msdata}}}UseCurrentLocale": "true"})
    choice = ET.SubElement(ET.SubElement(root_element, f"{{{XS}}}complexType"),
                           f"{{{XS}}}choice", {"minOccurs": "0", "maxOccurs": "unbounded"})
    items = ET.SubElement(choice, f"{{{XS}}}element", {"name": "Items"})
    sequence = ET.SubElement(ET.SubElement(items, f"{{{XS}}}complexType"),
                             f"{{{XS}}}sequence")
    xsd_types = {"string": "string", "long": "long",
                 "decimal": "decimal", "date": "dateTime"}
    for name, kind in columns.items():
        ET.SubElement(sequence, f"{{{XS}}}element",
                      {"name": name, "type": f"xs:{xsd_types[kind]}",
                       "minOccurs": "0"})
    ET.indent(schema)
    ET.ElementTree(schema).write(ROOT / f"{report_type}.xsd",
                                 encoding="utf-8", xml_declaration=True)

if __name__ == "__main__":
    for report_type, definition in json.loads((ROOT / "definitions.json").read_text()).items():
        make_report(report_type, definition)
