using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace HCW.AutoCAD.Plugin
{
    /// <summary>Lines of text kept in the drawing itself (a dictionary of Xrecords in the named objects dictionary).</summary>
    public static class DrawingStore
    {
        public static List<string> Read(Transaction tr, Database db, string dictionary, string record)
        {
            var lines = new List<string>();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(dictionary)) return lines;
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(dictionary), OpenMode.ForRead);
            if (!dict.Contains(record)) return lines;
            var xr = tr.GetObject(dict.GetAt(record), OpenMode.ForRead) as Xrecord;
            if (xr?.Data == null) return lines;
            foreach (TypedValue tv in xr.Data)
                if (tv.TypeCode == (int)DxfCode.Text) lines.Add(tv.Value as string);
            return lines;
        }

        /// <summary>The record names in a dictionary, or none when the dictionary does not exist.</summary>
        public static List<string> Keys(Transaction tr, Database db, string dictionary)
        {
            var keys = new List<string>();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(dictionary)) return keys;
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(dictionary), OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in dict) keys.Add(entry.Key);
            return keys;
        }

        public static void Write(Transaction tr, Database db, string dictionary, string record, IEnumerable<string> lines)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            DBDictionary dict;
            if (nod.Contains(dictionary))
                dict = (DBDictionary)tr.GetObject(nod.GetAt(dictionary), OpenMode.ForWrite);
            else
            {
                nod.UpgradeOpen();
                dict = new DBDictionary();
                nod.SetAt(dictionary, dict);
                tr.AddNewlyCreatedDBObject(dict, true);
            }

            var data = new ResultBuffer();
            foreach (var line in lines)
                data.Add(new TypedValue((int)DxfCode.Text, line));
            if (data.AsArray().Length == 0)
                data.Add(new TypedValue((int)DxfCode.Text, "EMPTY"));

            if (dict.Contains(record))
            {
                var xr = (Xrecord)tr.GetObject(dict.GetAt(record), OpenMode.ForWrite);
                xr.Data = data;
            }
            else
            {
                var xr = new Xrecord { Data = data };
                dict.SetAt(record, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
        }
    }
}
