using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// hcwCAD-KIT layer standard v4.0 (36 layers, AN/A/I/E/P/S/PR groups).
    /// </summary>
    public class HcwLayerCommands
    {
        private const LayerStateMasks AllLayerStateMasks =
            LayerStateMasks.On | LayerStateMasks.Frozen | LayerStateMasks.Locked |
            LayerStateMasks.Plot | LayerStateMasks.NewViewport | LayerStateMasks.Color |
            LayerStateMasks.LineType | LayerStateMasks.LineWeight | LayerStateMasks.PlotStyle |
#if !ZWCAD
            LayerStateMasks.CurrentViewport | LayerStateMasks.Transparency;
#else
            LayerStateMasks.CurrentViewport;
#endif

        [CommandMethod("HCWLAYERS")]
        public void HcwLayers()
        {
            var db = Util.Db; var ed = Util.Ed;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var ld in LayerData.Hcw)
                    Util.EnsureLayer(tr, db, ld.Name, (short)ld.Aci, ld.Linetype, Util.MmToLineWeight(ld.LwMm));
                tr.Commit();
                ed.WriteMessage($"\nHCWLAYERS v{"4.0"}: {LayerData.Hcw.Length} layers created/verified.");
            }
        }

        [CommandMethod("HCWRESET")]
        public void HcwReset()
        {
            var db = Util.Db; var ed = Util.Ed;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var names = new HashSet<string>(LayerData.Hcw.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                int n = 0;
                foreach (var ld in LayerData.Hcw)
                {
                    if (!lt.Has(ld.Name)) continue;
                    var ltr = (LayerTableRecord)tr.GetObject(lt[ld.Name], OpenMode.ForWrite);
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)ld.Aci);
                    ltr.LineWeight = Util.MmToLineWeight(ld.LwMm);
                    ltr.IsOff = false; ltr.IsFrozen = false; ltr.IsLocked = false;
                    ltr.LinetypeObjectId = Util.LoadLinetype(tr, db, ld.Linetype);
                    n++;
                }
                tr.Commit();
                ed.WriteMessage($"\nHCWRESET: {n} layer(s) reset to standard colour/linetype/lineweight and unlocked/thawed/on.");
            }
        }

        [CommandMethod("HCWPURGE")]
        public void HcwPurge()
        {
            var doc = Util.Doc; var ed = Util.Ed;
            using (doc.LockDocument())
            {
                // Hand off to AutoCAD's own PURGE command (repeated ALL/*/N reliably
                // purges everything purgeable in one pass, including nested empty blocks).
                doc.SendStringToExecute("_.-PURGE _All _* _N _.-PURGE _All _* _N ", true, false, false);
                ed.WriteMessage("\nHCWPURGE: PURGE ALL issued (run twice to catch nested empties).");
            }
        }

        [CommandMethod("HCWAUDIT")]
        public void HcwAudit()
        {
            var db = Util.Db; var ed = Util.Ed;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                int missing = 0;
                foreach (var ld in LayerData.Hcw)
                {
                    if (!lt.Has(ld.Name)) { ed.WriteMessage($"\n  MISSING: {ld.Name}"); missing++; }
                }
                ed.WriteMessage(missing == 0
                    ? "\nHCWAUDIT: all 36 standard layers present. PASS."
                    : $"\nHCWAUDIT: {missing} standard layer(s) missing (see above). Run HCWLAYERS to create them.");
                tr.Commit();
            }
        }

        [CommandMethod("HCWINFO")]
        public void HcwInfo()
        {
            var ed = Util.Ed;
            ed.WriteMessage("\n================================================================");
            ed.WriteMessage($"\n HCW Layer Standard v4.0  -  {LayerData.Hcw.Length} layers, 7 groups (AN/A/I/E/P/S/PR)");
            ed.WriteMessage("\n================================================================");
            foreach (var ld in LayerData.Hcw)
                ed.WriteMessage($"\n  {Util.Pad(ld.Name, 12)} ACI {Util.Pad(ld.Aci.ToString(), 4)} {Util.Pad(ld.Linetype, 12)} {ld.LwMm:F2}mm  {ld.Description}");
        }

        [CommandMethod("HCWLOCK")]
        public void HcwLock() => SetLocked(true);

        [CommandMethod("HCWUNLOCK")]
        public void HcwUnlock() => SetLocked(false);

        private void SetLocked(bool locked)
        {
            var db = Util.Db; var ed = Util.Ed;
            var pso = new PromptSelectionOptions { MessageForAdding = $"\nSelect objects whose layer should be {(locked ? "locked" : "unlocked")}: " };
            var psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (SelectedObject so in psr.Value)
                {
                    var ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                    if (done.Contains(ent.Layer)) continue;
                    var ltr = (LayerTableRecord)tr.GetObject(lt[ent.Layer], OpenMode.ForWrite);
                    ltr.IsLocked = locked;
                    done.Add(ent.Layer);
                }
                tr.Commit();
                ed.WriteMessage($"\n{(locked ? "HCWLOCK" : "HCWUNLOCK")}: {done.Count} layer(s) {(locked ? "locked" : "unlocked")}.");
            }
        }

        [CommandMethod("HCWAUDIT2")]
        public void HcwAudit2()
        {
            // Report entities whose colour/linetype is NOT ByLayer (explicit overrides).
            var db = Util.Db; var ed = Util.Ed;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var overrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    bool overridden = ent.Color.ColorMethod != Autodesk.AutoCAD.Colors.ColorMethod.ByLayer
                                       || ent.LinetypeId != db.ByLayerLinetype;
                    if (overridden)
                        overrides[ent.Layer] = overrides.TryGetValue(ent.Layer, out var c) ? c + 1 : 1;
                }
                if (overrides.Count == 0) ed.WriteMessage("\nHCWAUDIT2: no explicit colour/linetype overrides found. PASS.");
                else
                {
                    ed.WriteMessage($"\nHCWAUDIT2: {overrides.Values.Sum()} object(s) with explicit overrides:");
                    foreach (var kv in overrides.OrderBy(k => k.Key))
                        ed.WriteMessage($"\n  {Util.Pad(kv.Key, 16)} {kv.Value} object(s)");
                }
                tr.Commit();
            }
        }

        [CommandMethod("HCWBYBLOCK")]
        public void HcwByBlock()
        {
            var db = Util.Db; var ed = Util.Ed;
            var psr = ed.GetSelection();
            if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                int n = 0;
                foreach (SelectedObject so in psr.Value)
                {
                    var ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForWrite);
                    ent.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
                    ent.LinetypeId = db.ByLayerLinetype;
                    n++;
                }
                tr.Commit();
                ed.WriteMessage($"\nHCWBYBLOCK: {n} object(s) set back to ByLayer colour/linetype.");
            }
        }

        [CommandMethod("HCWMOVE")]
        public void HcwMove()
        {
            var db = Util.Db; var ed = Util.Ed;
            var psr = ed.GetSelection();
            if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }
            var pkr = ed.GetString("\nTarget layer name: ");
            if (pkr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(pkr.StringResult)) return;
            string target = pkr.StringResult.Trim();

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(target)) { ed.WriteMessage($"\n[Error] Layer '{target}' does not exist."); return; }
                int n = 0;
                foreach (SelectedObject so in psr.Value)
                {
                    var ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForWrite);
                    ent.Layer = target;
                    n++;
                }
                tr.Commit();
                ed.WriteMessage($"\nHCWMOVE: {n} object(s) moved to layer '{target}'.");
            }
        }

        [CommandMethod("HCWSCHEDULE")]
        public void HcwSchedule()
        {
            var db = Util.Db; var ed = Util.Ed;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    counts[ent.Layer] = counts.TryGetValue(ent.Layer, out var c) ? c + 1 : 1;
                }
                ed.WriteMessage("\nHCWSCHEDULE - object count by layer:");
                foreach (var kv in counts.OrderBy(k => k.Key))
                    ed.WriteMessage($"\n  {Util.Pad(kv.Key, 16)} {kv.Value}");
                tr.Commit();
            }
        }

        [CommandMethod("HCWLAYERSTATE")]
        public void HcwLayerState()
        {
            var db = Util.Db; var ed = Util.Ed;
            var pko = new PromptKeywordOptions("\n[Save/Restore] layer state <Save>: ");
            pko.Keywords.Add("Save"); pko.Keywords.Add("Restore"); pko.Keywords.Default = "Save";
            var pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK) return;
            var pnr = ed.GetString("\nLayer state name: ");
            if (pnr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(pnr.StringResult)) return;

            var lsm = db.LayerStateManager;
            using (Util.Doc.LockDocument())
            {
                if (pkr.StringResult == "Save")
                {
                    if (lsm.HasLayerState(pnr.StringResult)) lsm.DeleteLayerState(pnr.StringResult);
                    // 3rd param is a viewport ObjectId (for VPLAYER capture) - Null = model space / no viewport-specific state.
                    lsm.SaveLayerState(pnr.StringResult, AllLayerStateMasks, ObjectId.Null);
                    ed.WriteMessage($"\nHCWLAYERSTATE: saved '{pnr.StringResult}'.");
                }
                else
                {
                    if (!lsm.HasLayerState(pnr.StringResult)) { ed.WriteMessage("\n[Error] No such layer state."); return; }
                    // 3rd param (undefinedLayerStatePolicy) = 0: leave layers not covered by the saved state untouched.
                    lsm.RestoreLayerState(pnr.StringResult, ObjectId.Null, 0, AllLayerStateMasks);
                    ed.WriteMessage($"\nHCWLAYERSTATE: restored '{pnr.StringResult}'.");
                }
            }
        }

        [CommandMethod("HCWLEGEND")]
        public void HcwLegend()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var ppr = ed.GetPoint("\nPick legend insertion point (top-left): ");
            if (ppr.Status != PromptStatus.OK) return;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureLayer(tr, db, "AN-TEXT", 7, "Continuous", LineWeight.LineWeight018);
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                double h = 0.18, rowH = h * 2.0;
                var pt = ppr.Value;
                int row = 0;
                foreach (var ld in LayerData.Hcw)
                {
                    var swatch = new Line(new Point3d(pt.X, pt.Y - row * rowH, 0), new Point3d(pt.X + 0.4, pt.Y - row * rowH, 0))
                    {
                        Layer = ld.Name
                    };
                    btr.AppendEntity(swatch); tr.AddNewlyCreatedDBObject(swatch, true);

                    var txt = new DBText
                    {
                        Position = new Point3d(pt.X + 0.5, pt.Y - row * rowH - h / 2.0, 0),
                        Height = h, TextString = ld.Name + " - " + ld.Description, Layer = "AN-TEXT"
                    };
                    btr.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);
                    row++;
                }
                tr.Commit();
                ed.WriteMessage($"\nHCWLEGEND: {LayerData.Hcw.Length}-row layer legend drawn.");
            }
        }

        // Room-label commands for the hcwCAD-KIT ribbon. Each one uses
        // RoomUnitSelector.Current, which reads the drawing's INSUNITS.
        // Typed M-/F-/I- commands stay on their own engines.

        private static readonly string[] RoomTypeNames = LayerData.RoomTypes.Select(r => r.RoomType).ToArray();

        /// <summary>
        /// Unset INSUNITS used to be treated as metres, so a millimetre plan
        /// was labelled thousands of times too large. Ask once and store the choice.
        /// </summary>
        private static bool EnsureDrawingUnits()
        {
            var db = Util.Db;
            var ed = Util.Ed;
            if (db.Insunits != UnitsValue.Undefined) return true;

            var pko = new PromptKeywordOptions("\nDrawing units are unset. Treat distances as [Millimetres/Metres] <Millimetres>: ");
            pko.Keywords.Add("Millimetres");
            pko.Keywords.Add("Metres");
            pko.Keywords.Default = "Millimetres";
            pko.AllowNone = true;
            var r = ed.GetKeywords(pko);
            if (r.Status == PromptStatus.Cancel) return false;

            bool metres = r.Status == PromptStatus.OK && r.StringResult == "Metres";
            using (Util.Doc.LockDocument())
                db.Insunits = metres ? UnitsValue.Meters : UnitsValue.Millimeters;
            ed.WriteMessage(metres ? "\nUnits set to metres." : "\nUnits set to millimetres.");
            return true;
        }

        [CommandMethod("ROOM")]
        [CommandMethod("HCWROOM")]
        public void HcwRoom()
        {
            if (!EnsureDrawingUnits()) return;
            using (var dlg = new UI.RoomPickerForm(RoomTypeNames))
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedRoomType))
                    UI.RoomUnitSelector.Current.CreateLabel(Util.Ed, Util.Db, dlg.SelectedRoomType);
                else Util.Ed.WriteMessage("\n| Room selection cancelled.");
            }
        }

        [CommandMethod("ROOMC")]
        [CommandMethod("HCWCUSTOMROOM")]
        public void HcwCustomRoom()
        {
            if (!EnsureDrawingUnits()) return;
            var r = Util.Ed.GetString("\n| Enter room type: ");
            if (r.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(r.StringResult))
                UI.RoomUnitSelector.Current.CreateLabel(Util.Ed, Util.Db, r.StringResult);
            else Util.Ed.WriteMessage("\nCancelled.");
        }

        [CommandMethod("RAREA")]
        [CommandMethod("HCWROOMAREA")]
        public void HcwRoomArea()
        {
            if (!EnsureDrawingUnits()) return;
            UI.RoomUnitSelector.Current.AreaLabelForSelected(Util.Ed, Util.Db);
        }

        [CommandMethod("HCWROOMRECT")]
        public void HcwRoomRect() => UI.RoomUnitSelector.Current.ToggleRect(Util.Ed);

        [CommandMethod("HCWROOMHIDERECT")]
        public void HcwRoomHideRect() => UI.RoomUnitSelector.Current.HideRect(Util.Ed, Util.Db);

        [CommandMethod("HCWROOMTH")]
        public void HcwRoomTh() => UI.RoomUnitSelector.Current.SetTextHeight(Util.Ed);

        [CommandMethod("HCWROOMLAYER")]
        public void HcwRoomLayer() => UI.RoomUnitSelector.Current.SetLayer(Util.Ed);

        [CommandMethod("HCWROOMSET")]
        public void HcwRoomSet() => UI.RoomUnitSelector.Current.ShowSettings(Util.Ed);

        [CommandMethod("HCWROOMRESET")]
        public void HcwRoomReset() => UI.RoomUnitSelector.Current.ResetAndAnnounce(Util.Ed);

        [CommandMethod("HCWROOMFLOOR")]
        public void HcwRoomFloor() => UI.RoomUnitSelector.Current.SetFloorPrefix(Util.Ed);

        [CommandMethod("RTAG")]
        [CommandMethod("HCWROOMRELABEL")]
        public void HcwRoomRelabel() => UI.RoomUnitSelector.Current.Relabel(Util.Ed, Util.Db);

        [CommandMethod("HCWROOMAUDIT")]
        public void HcwRoomAudit() => UI.RoomUnitSelector.Current.Audit(Util.Ed, Util.Db);

        [CommandMethod("HCWROOMCHECK")]
        public void HcwRoomCheck() => UI.RoomUnitSelector.Current.Check(Util.Ed, Util.Db);

        [CommandMethod("HCWROOMSCHEDULE")]
        public void HcwRoomSchedule() => UI.RoomUnitSelector.Current.Schedule(Util.Ed, Util.Db);

        [CommandMethod("HCWROOMTOTAL")]
        public void HcwRoomTotal() => UI.RoomUnitSelector.Current.Total(Util.Ed);

        [CommandMethod("HCWROOMHELP")]
        public void HcwRoomHelp()
        {
            var ed = Util.Ed;
            var engine = UI.RoomUnitSelector.Current;
            ed.WriteMessage("\n================================================================");
            ed.WriteMessage($"\n   ROOM DIMENSION TOOL — detected drawing unit: {engine.UnitDisplayName}");
            ed.WriteMessage("\n================================================================");
            ed.WriteMessage("\n  ROOM reads the drawing unit (INSUNITS). If units are unset it asks");
            ed.WriteMessage("\n  millimetres or metres once and stores that on the drawing.");
            ed.WriteMessage("\n  Label text uses the default height until you run HCWROOMTH.");
            ed.WriteMessage("\n================================================================");
        }

        // ==================== Standard text/dimension style tiers ====================
        // Creates Site/Working/Detail text + dimension style tiers, each sized
        // from a real-world plotted text height (2.5mm, a common ISO drafting
        // convention) scaled by that tier's typical print scale, converted into
        // whatever unit this drawing is actually set up in (INSUNITS).

        private const double StylePlottedMm = 2.5;
        private const double StyleSiteScale = 200;    // typical 1:200 site plan
        private const double StyleWorkingScale = 50;  // typical 1:50 general arrangement
        private const double StyleDetailScale = 10;   // typical 1:10 detail drawing

        [CommandMethod("HCWSTYLES")]
        public void HcwStyles()
        {
            var db = Util.Db;
            var ed = Util.Ed;
            double siteHeight, workingHeight, detailHeight;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                siteHeight = CreateStyleTier(tr, db, "HCW-SITE", StyleSiteScale);
                workingHeight = CreateStyleTier(tr, db, "HCW-WORKING", StyleWorkingScale);
                detailHeight = CreateStyleTier(tr, db, "HCW-DETAIL", StyleDetailScale);
                tr.Commit();
            }

            ed.WriteMessage("\n============================================");
            ed.WriteMessage("\n   HCWSTYLES: created/verified 3 text + dimension style tiers");
            ed.WriteMessage("\n============================================");
            ed.WriteMessage($"\n  HCW-SITE     site plans     (~1:{StyleSiteScale:F0})   text height {siteHeight:F3} drawing units");
            ed.WriteMessage($"\n  HCW-WORKING  working dwgs   (~1:{StyleWorkingScale:F0})    text height {workingHeight:F3} drawing units");
            ed.WriteMessage($"\n  HCW-DETAIL   detail dwgs    (~1:{StyleDetailScale:F0})    text height {detailHeight:F3} drawing units");
            ed.WriteMessage("\n  Set the matching layout viewport scale and use these styles for consistent, plot-correct text.");
        }

        /// <summary>Creates/updates one text style + matching dimension style, returns the text height used.</summary>
        private static double CreateStyleTier(Transaction tr, Database db, string name, double scale)
        {
            double height = Util.MmToDrawingUnits(StylePlottedMm * scale);

            var styleTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            ObjectId styleId;
            if (styleTable.Has(name))
            {
                var existing = (TextStyleTableRecord)tr.GetObject(styleTable[name], OpenMode.ForWrite);
                existing.TextSize = height;
                styleId = existing.ObjectId;
            }
            else
            {
                styleTable.UpgradeOpen();
                var rec = new TextStyleTableRecord { Name = name, TextSize = height, FileName = "romans.shx" };
                styleId = styleTable.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }

            var dimTable = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            DimStyleTableRecord dimRec;
            if (dimTable.Has(name))
            {
                dimRec = (DimStyleTableRecord)tr.GetObject(dimTable[name], OpenMode.ForWrite);
            }
            else
            {
                dimTable.UpgradeOpen();
                dimRec = new DimStyleTableRecord { Name = name };
                dimTable.Add(dimRec);
                tr.AddNewlyCreatedDBObject(dimRec, true);
            }
            dimRec.Dimtxt = height;
            dimRec.Dimasz = height;
            dimRec.Dimexe = height * 0.45;
            dimRec.Dimexo = height * 0.2;
            dimRec.Dimgap = height * 0.2;
            dimRec.Dimtxsty = styleId;
            dimRec.Dimscale = 1.0;

            return height;
        }
    }
}
