using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>Something that keeps part of the drawing up to date after the user changes it.</summary>
    internal interface ILiveWatcher
    {
        string Name { get; }
        /// <summary>Is this changed object one this watcher cares about? Called for every modified object, so it must be cheap.</summary>
        bool Interested(DBObject changed);
        /// <summary>Bring the drawing up to date. Called once after the command that changed things has ended.</summary>
        void Refresh(Document doc);
        /// <summary>Called when the service starts watching a drawing.</summary>
        void Attached(Document doc);
    }

    /// <summary>
    /// The live update service. While it is on, changes to objects are noted as they happen (the drawing cannot be edited from inside
    /// the change itself), and each watcher whose objects changed refreshes once when the command ends. It covers the electrical ID labels,
    /// level marks following their datum, associative auto dimensions and the area statement. HCWLIVE switches it on and off; the setting
    /// LiveUpdate (1 or 0) decides whether it starts with the plugin.
    /// </summary>
    internal static class LiveUpdate
    {
        internal static readonly List<ILiveWatcher> Watchers = new List<ILiveWatcher>
        {
            new ElectricalLabelWatcher(), new LevelWatcher(), new DimAnchorWatcher(), new AreaStatementWatcher(),
        };

        private class State { public readonly HashSet<ILiveWatcher> Dirty = new HashSet<ILiveWatcher>(); public CommandEventHandler Ended; public ObjectEventHandler Modified, Appended; public ObjectErasedEventHandler Erased; }
        private static readonly Dictionary<Database, State> States = new Dictionary<Database, State>();
        private static bool _busy;

        internal static bool IsOn { get; private set; }

        internal static void Start()
        {
            if (IsOn) return;
            IsOn = true;
            var docs = AcAp.DocumentManager;
            foreach (Document d in docs) Attach(d);
            docs.DocumentCreated += OnDocumentCreated;
            docs.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
        }

        internal static void Stop()
        {
            if (!IsOn) return;
            IsOn = false;
            var docs = AcAp.DocumentManager;
            docs.DocumentCreated -= OnDocumentCreated;
            docs.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            foreach (Document d in docs) Detach(d);
        }

        private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e) { if (IsOn) Attach(e.Document); }
        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e) { Detach(e.Document); }

        private static void Attach(Document doc)
        {
            var db = doc.Database;
            if (States.ContainsKey(db)) return;
            var st = new State();
            st.Modified = (s, a) => OnModified(db, a.DBObject);
            st.Appended = (s, a) => OnModified(db, a.DBObject);
            st.Erased = (s, a) => OnModified(db, a.DBObject);
            st.Ended = (s, a) => OnCommandEnded(doc);
            States[db] = st;
            db.ObjectModified += st.Modified;
            db.ObjectAppended += st.Appended;
            db.ObjectErased += st.Erased;
            doc.CommandEnded += st.Ended;
            doc.CommandCancelled += st.Ended;
            doc.CommandFailed += st.Ended;
            foreach (var w in Watchers)
                try { w.Attached(doc); } catch (System.Exception) { /* a watcher that cannot start must not stop the others */ }
        }

        private static void Detach(Document doc)
        {
            State st;
            var db = doc.Database;
            if (!States.TryGetValue(db, out st)) return;
            db.ObjectModified -= st.Modified;
            db.ObjectAppended -= st.Appended;
            db.ObjectErased -= st.Erased;
            doc.CommandEnded -= st.Ended;
            doc.CommandCancelled -= st.Ended;
            doc.CommandFailed -= st.Ended;
            States.Remove(db);
        }

        private static void OnModified(Database db, DBObject obj)
        {
            if (_busy) return;
            State st;
            if (!States.TryGetValue(db, out st) || obj == null) return;
            foreach (var w in Watchers)
            {
                if (st.Dirty.Contains(w)) continue;
                try { if (w.Interested(obj)) st.Dirty.Add(w); }
                catch (System.Exception) { /* the object could not be read: ignore it */ }
            }
        }

        private static void OnCommandEnded(Document doc)
        {
            State st;
            if (_busy || !States.TryGetValue(doc.Database, out st) || st.Dirty.Count == 0) return;
            var todo = st.Dirty.ToList();
            st.Dirty.Clear();
            RunRefresh(doc, todo);
        }

        /// <summary>Refreshes the given watchers now, with the service's own changes ignored so they do not trigger another round.</summary>
        internal static void RunRefresh(Document doc, IEnumerable<ILiveWatcher> watchers)
        {
            _busy = true;
            try
            {
                using (doc.LockDocument())
                    foreach (var w in watchers)
                        try { w.Refresh(doc); }
                        catch (System.Exception ex) { doc.Editor.WriteMessage("\n[hcwCAD-KIT] live update (" + w.Name + ") failed: " + ex.Message); }
            }
            finally { _busy = false; }
        }

        internal static void LiveCommand()
        {
            var ed = Util.Ed;
            var o = new PromptKeywordOptions("\nLive updates are " + (IsOn ? "on" : "off") + ". [On/Off/Refresh] <" + (IsOn ? "Refresh" : "On") + ">: ", "On Off Refresh") { AllowNone = true };
            o.Keywords.Default = IsOn ? "Refresh" : "On";
            var r = ed.GetKeywords(o);
            string choice = r.Status == PromptStatus.None ? (IsOn ? "Refresh" : "On") : r.Status == PromptStatus.OK ? r.StringResult : null;
            if (choice == null) return;
            if (choice == "On")
            {
                Start();
                ed.WriteMessage("\nHCWLIVE: on. Watching: " + string.Join(", ", Watchers.Select(w => w.Name)) + ". Each refreshes when a command that changed its objects ends.");
            }
            else if (choice == "Off") { Stop(); ed.WriteMessage("\nHCWLIVE: off."); }
            else
            {
                RunRefresh(Util.Doc, Watchers);
                ed.WriteMessage("\nHCWLIVE: everything refreshed.");
            }
        }
    }

    internal class ElectricalLabelWatcher : ILiveWatcher
    {
        public string Name => "electrical labels";
        public bool Interested(DBObject o) => o is BlockReference && o.GetXDataForApplication(ElectricalCommands.AppNameForLive) != null;
        public void Refresh(Document doc) => ElectricalCommands.LiveSync(doc.Database);
        public void Attached(Document doc) { }
    }

    internal class LevelWatcher : ILiveWatcher
    {
        public string Name => "level marks";
        public bool Interested(DBObject o)
        {
            var e = o as Entity;
            return e != null && (o is Circle || o is BlockReference) && (TitleBlockCommands.IsKind(e, "DATUM") || TitleBlockCommands.IsKind(e, "LEVEL-LIVE"));
        }
        public void Refresh(Document doc) => SymbolCommands.RefreshLevels(doc.Database);
        public void Attached(Document doc) { }
    }

    internal class DimAnchorWatcher : ILiveWatcher
    {
        public string Name => "associative dimensions";
        public bool Interested(DBObject o)
        {
            HashSet<long> handles;
            return DimAnchors.Anchored.TryGetValue(o.Database, out handles) && handles.Contains(o.Handle.Value);
        }
        public void Refresh(Document doc) => DimAnchors.Refresh(doc.Database);
        public void Attached(Document doc) => DimAnchors.Scan(doc.Database);
    }
}

namespace HCW.AutoCAD.Plugin.Commands
{
    internal class AreaStatementWatcher : ILiveWatcher
    {
        public string Name => "area statement";
        public bool Interested(DBObject o)
        {
            var pl = o as Polyline;
            KeyValuePair<HashSet<string>, HashSet<long>> w;
            return pl != null && AreaStatementCommands.Watched.TryGetValue(o.Database, out w) && (w.Key.Contains(pl.Layer) || w.Value.Contains(pl.Handle.Value));
        }
        public void Refresh(Document doc) => AreaStatementCommands.LiveRefresh(doc);
        public void Attached(Document doc) => AreaStatementCommands.LoadWatch(doc.Database);
    }
}

namespace HCW.AutoCAD.Plugin.Commands
{
    public class LiveUpdateCommands
    {
        [CommandMethod("HCWLIVE")]
        public void Live() => LiveUpdate.LiveCommand();
    }
}
