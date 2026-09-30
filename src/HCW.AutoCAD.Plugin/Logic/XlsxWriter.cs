using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>One cell: text, a number, or a formula (such as "C2*D2"). No CAD types are used here.</summary>
    public class XlsxCell
    {
        public string Text;
        public double? Number;
        public string Formula;
        public bool Bold;

        public static XlsxCell Str(string text, bool bold = false) => new XlsxCell { Text = text ?? "", Bold = bold };
        public static XlsxCell Num(double value, bool bold = false) => new XlsxCell { Number = value, Bold = bold };
        public static XlsxCell Calc(string formula, bool bold = false) => new XlsxCell { Formula = formula, Bold = bold };

        /// <summary>A number when the text is one (invariant culture, no letters), otherwise text.</summary>
        public static XlsxCell Auto(string text, bool bold = false)
        {
            double v;
            if (!string.IsNullOrWhiteSpace(text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                && !double.IsNaN(v) && !double.IsInfinity(v))
                return Num(v, bold);
            return Str(text, bold);
        }
    }

    public class XlsxSheet
    {
        public string Name = "Sheet";
        public List<List<XlsxCell>> Rows = new List<List<XlsxCell>>();
    }

    /// <summary>Writes a plain .xlsx workbook (one sheet per XlsxSheet) without any library.</summary>
    public static class XlsxWriter
    {
        public static void Write(string path, IEnumerable<XlsxSheet> sheets)
        {
            var list = sheets.ToList();
            if (list.Count == 0) list.Add(new XlsxSheet { Name = "Sheet1" });
            var names = UniqueNames(list.Select(s => s.Name));

            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", ContentTypes(list.Count));
                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                    + "</Relationships>");
                Add(zip, "xl/workbook.xml", Workbook(names));
                Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(list.Count));
                Add(zip, "xl/styles.xml", Styles());
                for (int i = 0; i < list.Count; i++)
                    Add(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", Sheet(list[i]));
            }
        }

        /// <summary>Excel sheet names: at most 31 characters, none of [ ] : * ? / \, and no repeats.</summary>
        public static List<string> UniqueNames(IEnumerable<string> raw)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in raw)
            {
                var clean = new string((name ?? "").Select(c => "[]:*?/\\".IndexOf(c) >= 0 ? '-' : c).ToArray()).Trim();
                if (clean.Length == 0) clean = "Sheet";
                if (clean.Length > 31) clean = clean.Substring(0, 31);
                string candidate = clean;
                int n = 2;
                while (!seen.Add(candidate))
                {
                    string suffix = "-" + n++;
                    candidate = clean.Substring(0, Math.Min(clean.Length, 31 - suffix.Length)) + suffix;
                }
                result.Add(candidate);
            }
            return result;
        }

        private static void Add(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name);
            using (var stream = entry.Open())
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static string Esc(string s) => SecurityElement.Escape(s ?? "");

        private static string ContentTypes(int sheets)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 1; i <= sheets; i++)
                sb.Append("<Override PartName=\"/xl/worksheets/sheet" + i + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string Workbook(List<string> names)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            for (int i = 0; i < names.Count; i++)
                sb.Append("<sheet name=\"" + Esc(names[i]) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>");
            sb.Append("</sheets></workbook>");
            return sb.ToString();
        }

        private static string WorkbookRels(int sheets)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 1; i <= sheets; i++)
                sb.Append("<Relationship Id=\"rId" + i + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + i + ".xml\"/>");
            sb.Append("<Relationship Id=\"rId" + (sheets + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        // Style 0 is the default; style 1 is bold.
        private static string Styles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
                + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
                + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>"
                + "</styleSheet>";
        }

        private static string Sheet(XlsxSheet sheet)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                sb.Append("<row r=\"" + (r + 1) + "\">");
                var row = sheet.Rows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    var cell = row[c];
                    if (cell == null) continue;
                    string reference = ColumnName(c) + (r + 1);
                    string style = cell.Bold ? " s=\"1\"" : "";
                    if (cell.Formula != null)
                        sb.Append("<c r=\"" + reference + "\"" + style + "><f>" + Esc(cell.Formula) + "</f></c>");
                    else if (cell.Number.HasValue)
                        sb.Append("<c r=\"" + reference + "\"" + style + "><v>" + cell.Number.Value.ToString("R", CultureInfo.InvariantCulture) + "</v></c>");
                    else if (!string.IsNullOrEmpty(cell.Text))
                        sb.Append("<c r=\"" + reference + "\"" + style + " t=\"inlineStr\"><is><t xml:space=\"preserve\">" + Esc(cell.Text) + "</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        /// <summary>0 becomes A, 25 becomes Z, 26 becomes AA.</summary>
        public static string ColumnName(int index)
        {
            string name = "";
            index++;
            while (index > 0)
            {
                int rem = (index - 1) % 26;
                name = (char)('A' + rem) + name;
                index = (index - 1) / 26;
            }
            return name;
        }
    }
}
