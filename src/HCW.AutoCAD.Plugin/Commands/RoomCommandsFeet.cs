using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>Room Dimension Tool - FEET (F-) (ported from HCW-ALL.lsp Section 8, room:f:*).</summary>
    public class RoomCommandsFeet
    {
        public static readonly RoomEngineFeet Engine = new RoomEngineFeet();

        [CommandMethod("FBR")]
        public void FBR() => Engine.CreateLabel(Util.Ed, Util.Db, "Bedroom");

        [CommandMethod("FMB")]
        public void FMB() => Engine.CreateLabel(Util.Ed, Util.Db, "Master Bedroom");

        [CommandMethod("FGR")]
        public void FGR() => Engine.CreateLabel(Util.Ed, Util.Db, "Guest Room");

        [CommandMethod("FLI")]
        public void FLI() => Engine.CreateLabel(Util.Ed, Util.Db, "Living Room");

        [CommandMethod("FDI")]
        public void FDI() => Engine.CreateLabel(Util.Ed, Util.Db, "Dining Room");

        [CommandMethod("FKI")]
        public void FKI() => Engine.CreateLabel(Util.Ed, Util.Db, "Kitchen");

        [CommandMethod("FPA")]
        public void FPA() => Engine.CreateLabel(Util.Ed, Util.Db, "Pantry");

        [CommandMethod("FBA")]
        public void FBA() => Engine.CreateLabel(Util.Ed, Util.Db, "Bathroom");

        [CommandMethod("FAT")]
        public void FAT() => Engine.CreateLabel(Util.Ed, Util.Db, "Attached Toilet");

        [CommandMethod("FCT")]
        public void FCT() => Engine.CreateLabel(Util.Ed, Util.Db, "Common Toilet");

        [CommandMethod("FST")]
        public void FST() => Engine.CreateLabel(Util.Ed, Util.Db, "Study Room");

        [CommandMethod("FOF")]
        public void FOF() => Engine.CreateLabel(Util.Ed, Util.Db, "Office");

        [CommandMethod("FSR")]
        public void FSR() => Engine.CreateLabel(Util.Ed, Util.Db, "Store Room");

        [CommandMethod("FPR")]
        public void FPR() => Engine.CreateLabel(Util.Ed, Util.Db, "Pooja Room");

        [CommandMethod("FBC")]
        public void FBC() => Engine.CreateLabel(Util.Ed, Util.Db, "Balcony");

        [CommandMethod("FTE")]
        public void FTE() => Engine.CreateLabel(Util.Ed, Util.Db, "Terrace");

        [CommandMethod("FSC")]
        public void FSC() => Engine.CreateLabel(Util.Ed, Util.Db, "Staircase");

        [CommandMethod("FCO")]
        public void FCO() => Engine.CreateLabel(Util.Ed, Util.Db, "Corridor");

        [CommandMethod("FEN")]
        public void FEN() => Engine.CreateLabel(Util.Ed, Util.Db, "Entrance");

        [CommandMethod("FLO")]
        public void FLO() => Engine.CreateLabel(Util.Ed, Util.Db, "Lobby");

        [CommandMethod("FUT")]
        public void FUT() => Engine.CreateLabel(Util.Ed, Util.Db, "Utility");

        [CommandMethod("FLA")]
        public void FLA() => Engine.CreateLabel(Util.Ed, Util.Db, "Laundry");

        [CommandMethod("FGA")]
        public void FGA() => Engine.CreateLabel(Util.Ed, Util.Db, "Garage");

        [CommandMethod("FGD")]
        public void FGD() => Engine.CreateLabel(Util.Ed, Util.Db, "Garden");

        [CommandMethod("FCY")]
        public void FCY() => Engine.CreateLabel(Util.Ed, Util.Db, "Courtyard");

        [CommandMethod("FWR")]
        public void FWR() => Engine.CreateLabel(Util.Ed, Util.Db, "Wardrobe");

        [CommandMethod("FDR")]
        public void FDR() => Engine.CreateLabel(Util.Ed, Util.Db, "Dressing Room");

        [CommandMethod("FHT")]
        public void FHT() => Engine.CreateLabel(Util.Ed, Util.Db, "Home Theater");

        [CommandMethod("FGY")]
        public void FGY() => Engine.CreateLabel(Util.Ed, Util.Db, "Gym");

        [CommandMethod("FDO")]
        public void FDO()
        {
            var r = Util.Ed.GetString("\n| Enter room type: ");
            if (r.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(r.StringResult))
                Engine.CreateLabel(Util.Ed, Util.Db, r.StringResult);
            else Util.Ed.WriteMessage("\nCancelled.");
        }

        [CommandMethod("FROOM")]
        public void FROOM()
        {
            string[] roomTypes = { "Bedroom", "Master Bedroom", "Guest Room", "Living Room", "Dining Room", "Kitchen", "Pantry", "Bathroom", "Attached Toilet", "Common Toilet", "Study Room", "Office", "Store Room", "Pooja Room", "Balcony", "Terrace", "Staircase", "Corridor", "Entrance", "Lobby", "Utility", "Laundry", "Garage", "Garden", "Courtyard", "Wardrobe", "Dressing Room", "Home Theater", "Gym" };
            using (var dlg = new UI.RoomPickerForm(roomTypes))
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedRoomType))
                    Engine.CreateLabel(Util.Ed, Util.Db, dlg.SelectedRoomType);
                else Util.Ed.WriteMessage("\n| Room selection cancelled.");
            }
        }

        [CommandMethod("FAR")]
        public void FAR() => Engine.AreaLabelForSelected(Util.Ed, Util.Db);

        [CommandMethod("FRECT")]
        public void FRECT() => Engine.ToggleRect(Util.Ed);

        [CommandMethod("FHIDERECT")]
        public void FHIDERECT() => Engine.HideRect(Util.Ed, Util.Db);

        [CommandMethod("FTH")]
        public void FTH() => Engine.SetTextHeight(Util.Ed);

        [CommandMethod("FLAYER")]
        public void FLAYER() => Engine.SetLayer(Util.Ed);

        [CommandMethod("FSET")]
        public void FSET() => Engine.ShowSettings(Util.Ed);

        [CommandMethod("FRESET")]
        public void FRESET() => Engine.ResetAndAnnounce(Util.Ed);

        [CommandMethod("FFLOOR")]
        public void FFLOOR() => Engine.SetFloorPrefix(Util.Ed);

        [CommandMethod("FRELABEL")]
        public void FRELABEL() => Engine.Relabel(Util.Ed, Util.Db);

        [CommandMethod("FAUDIT")]
        public void FAUDIT() => Engine.Audit(Util.Ed, Util.Db);

        [CommandMethod("FCHECK")]
        public void FCHECK() => Engine.Check(Util.Ed, Util.Db);

        [CommandMethod("FSCHEDULE")]
        public void FSCHEDULE() => Engine.Schedule(Util.Ed, Util.Db);

        [CommandMethod("FTOTAL")]
        public void FTOTAL() => Engine.Total(Util.Ed);

        [CommandMethod("FHELP")]
        public void FHELP()
        {
            var ed = Util.Ed;
            ed.WriteMessage("\n================================================================");
            ed.WriteMessage("\n   ROOM DIMENSION TOOL — FEET (F-)");
            ed.WriteMessage("\n================================================================");
            ed.WriteMessage("\n  FROOM   Pick room type from dialog     FDO   Custom room name");
            ed.WriteMessage("\n  FBR  FMB  FGR  FLI  FDI  FKI  FPA  FBA  FAT  FCT  FST  FOF  FSR  FPR  FBC  FTE  FSC  FCO  FEN  FLO  FUT  FLA  FGA  FGD  FCY  FWR  FDR  FHT  FGY");
            ed.WriteMessage("\n  FAR       Area label for selected polyline");
            ed.WriteMessage("\n  FRECT     Toggle rectangle drawing ON/OFF     FHIDERECT Freeze/thaw rectangle layer");
            ed.WriteMessage("\n  FTH      Change text height     FLAYER   Change label layer     FFLOOR Set floor prefix");
            ed.WriteMessage("\n  FSET     Show settings          FRESET Reset to defaults      FRELABEL Relabel existing text");
            ed.WriteMessage("\n  FAUDIT   Audit label/rect counts     FCHECK Check rectangle closure     FSCHEDULE Export CSV schedule");
            ed.WriteMessage("\n  FTOTAL    Total area by filter");
            ed.WriteMessage("\n================================================================");
        }
    }
}