using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>Room Dimension Tool - METRIC (M-) (ported from HCW-ALL.lsp Section 8, room:m:*).</summary>
    public class RoomCommandsMetric
    {
        public static readonly RoomEngineMetric Engine = new RoomEngineMetric();

        [CommandMethod("MBR")]
        public void MBR() => Engine.CreateLabel(Util.Ed, Util.Db, "Bedroom");

        [CommandMethod("MMB")]
        public void MMB() => Engine.CreateLabel(Util.Ed, Util.Db, "Master Bedroom");

        [CommandMethod("MGR")]
        public void MGR() => Engine.CreateLabel(Util.Ed, Util.Db, "Guest Room");

        [CommandMethod("MLI")]
        public void MLI() => Engine.CreateLabel(Util.Ed, Util.Db, "Living Room");

        [CommandMethod("MDI")]
        public void MDI() => Engine.CreateLabel(Util.Ed, Util.Db, "Dining Room");

        [CommandMethod("MKI")]
        public void MKI() => Engine.CreateLabel(Util.Ed, Util.Db, "Kitchen");

        [CommandMethod("MPA")]
        public void MPA() => Engine.CreateLabel(Util.Ed, Util.Db, "Pantry");

        [CommandMethod("MBA")]
        public void MBA() => Engine.CreateLabel(Util.Ed, Util.Db, "Bathroom");

        [CommandMethod("MAT")]
        public void MAT() => Engine.CreateLabel(Util.Ed, Util.Db, "Attached Toilet");

        [CommandMethod("MCT")]
        public void MCT() => Engine.CreateLabel(Util.Ed, Util.Db, "Common Toilet");

        [CommandMethod("MST")]
        public void MST() => Engine.CreateLabel(Util.Ed, Util.Db, "Study Room");

        [CommandMethod("MOF")]
        public void MOF() => Engine.CreateLabel(Util.Ed, Util.Db, "Office");

        [CommandMethod("MSR")]
        public void MSR() => Engine.CreateLabel(Util.Ed, Util.Db, "Store Room");

        [CommandMethod("MPR")]
        public void MPR() => Engine.CreateLabel(Util.Ed, Util.Db, "Pooja Room");

        [CommandMethod("MBC")]
        public void MBC() => Engine.CreateLabel(Util.Ed, Util.Db, "Balcony");

        [CommandMethod("MTE")]
        public void MTE() => Engine.CreateLabel(Util.Ed, Util.Db, "Terrace");

        [CommandMethod("MSC")]
        public void MSC() => Engine.CreateLabel(Util.Ed, Util.Db, "Staircase");

        [CommandMethod("MCO")]
        public void MCO() => Engine.CreateLabel(Util.Ed, Util.Db, "Corridor");

        [CommandMethod("MEN")]
        public void MEN() => Engine.CreateLabel(Util.Ed, Util.Db, "Entrance");

        [CommandMethod("MLO")]
        public void MLO() => Engine.CreateLabel(Util.Ed, Util.Db, "Lobby");

        [CommandMethod("MUT")]
        public void MUT() => Engine.CreateLabel(Util.Ed, Util.Db, "Utility");

        [CommandMethod("MLA")]
        public void MLA() => Engine.CreateLabel(Util.Ed, Util.Db, "Laundry");

        [CommandMethod("MGA")]
        public void MGA() => Engine.CreateLabel(Util.Ed, Util.Db, "Garage");

        [CommandMethod("MGD")]
        public void MGD() => Engine.CreateLabel(Util.Ed, Util.Db, "Garden");

        [CommandMethod("MCY")]
        public void MCY() => Engine.CreateLabel(Util.Ed, Util.Db, "Courtyard");

        [CommandMethod("MWR")]
        public void MWR() => Engine.CreateLabel(Util.Ed, Util.Db, "Wardrobe");

        [CommandMethod("MDR")]
        public void MDR() => Engine.CreateLabel(Util.Ed, Util.Db, "Dressing Room");

        [CommandMethod("MHT")]
        public void MHT() => Engine.CreateLabel(Util.Ed, Util.Db, "Home Theater");

        [CommandMethod("MGY")]
        public void MGY() => Engine.CreateLabel(Util.Ed, Util.Db, "Gym");

        [CommandMethod("MDO")]
        public void MDO()
        {
            var r = Util.Ed.GetString("\n| Enter room type: ");
            if (r.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(r.StringResult))
                Engine.CreateLabel(Util.Ed, Util.Db, r.StringResult);
            else Util.Ed.WriteMessage("\nCancelled.");
        }

        [CommandMethod("MROOM")]
        public void MROOM()
        {
            string[] roomTypes = { "Bedroom", "Master Bedroom", "Guest Room", "Living Room", "Dining Room", "Kitchen", "Pantry", "Bathroom", "Attached Toilet", "Common Toilet", "Study Room", "Office", "Store Room", "Pooja Room", "Balcony", "Terrace", "Staircase", "Corridor", "Entrance", "Lobby", "Utility", "Laundry", "Garage", "Garden", "Courtyard", "Wardrobe", "Dressing Room", "Home Theater", "Gym" };
            using (var dlg = new UI.RoomPickerForm(roomTypes))
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedRoomType))
                    Engine.CreateLabel(Util.Ed, Util.Db, dlg.SelectedRoomType);
                else Util.Ed.WriteMessage("\n| Room selection cancelled.");
            }
        }

        [CommandMethod("MAR")]
        public void MAR() => Engine.AreaLabelForSelected(Util.Ed, Util.Db);

        [CommandMethod("MRECT")]
        public void MRECT() => Engine.ToggleRect(Util.Ed);

        [CommandMethod("MHIDERECT")]
        public void MHIDERECT() => Engine.HideRect(Util.Ed, Util.Db);

        [CommandMethod("MTH")]
        public void MTH() => Engine.SetTextHeight(Util.Ed);

        [CommandMethod("MLAYER")]
        public void MLAYER() => Engine.SetLayer(Util.Ed);

        [CommandMethod("MSET")]
        public void MSET() => Engine.ShowSettings(Util.Ed);

        [CommandMethod("MRESET")]
        public void MRESET() => Engine.ResetAndAnnounce(Util.Ed);

        [CommandMethod("MFLOOR")]
        public void MFLOOR() => Engine.SetFloorPrefix(Util.Ed);

        [CommandMethod("MRELABEL")]
        public void MRELABEL() => Engine.Relabel(Util.Ed, Util.Db);

        [CommandMethod("MAUDIT")]
        public void MAUDIT() => Engine.Audit(Util.Ed, Util.Db);

        [CommandMethod("MCHECK")]
        public void MCHECK() => Engine.Check(Util.Ed, Util.Db);

        [CommandMethod("MSCHEDULE")]
        public void MSCHEDULE() => Engine.Schedule(Util.Ed, Util.Db);

        [CommandMethod("MTOTAL")]
        public void MTOTAL() => Engine.Total(Util.Ed);

        [CommandMethod("MHELP")]
        public void MHELP()
        {
            var ed = Util.Ed;
            ed.WriteMessage("\n================================================================");
            ed.WriteMessage("\n   ROOM DIMENSION TOOL — METRIC (M-)");
            ed.WriteMessage("\n================================================================");
            ed.WriteMessage("\n  MROOM   Pick room type from dialog     MDO   Custom room name");
            ed.WriteMessage("\n  MBR  MMB  MGR  MLI  MDI  MKI  MPA  MBA  MAT  MCT  MST  MOF  MSR  MPR  MBC  MTE  MSC  MCO  MEN  MLO  MUT  MLA  MGA  MGD  MCY  MWR  MDR  MHT  MGY");
            ed.WriteMessage("\n  MAR       Area label for selected polyline");
            ed.WriteMessage("\n  MRECT     Toggle rectangle drawing ON/OFF     MHIDERECT Freeze/thaw rectangle layer");
            ed.WriteMessage("\n  MTH      Change text height     MLAYER   Change label layer     MFLOOR Set floor prefix");
            ed.WriteMessage("\n  MSET     Show settings          MRESET Reset to defaults      MRELABEL Relabel existing text");
            ed.WriteMessage("\n  MAUDIT   Audit label/rect counts     MCHECK Check rectangle closure     MSCHEDULE Export CSV schedule");
            ed.WriteMessage("\n  MTOTAL    Total area by filter");
            ed.WriteMessage("\n================================================================");
        }
    }
}