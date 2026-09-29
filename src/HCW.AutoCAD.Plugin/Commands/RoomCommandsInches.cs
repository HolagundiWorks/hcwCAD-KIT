using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.EditorInput;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>hcwCAD-KIT room labels — inches (I) commands.</summary>
    public class RoomCommandsInches
    {
        public static readonly RoomEngineInches Engine = new RoomEngineInches();

        [CommandMethod("IBR")]
        public void IBR() => Engine.CreateLabel(Util.Ed, Util.Db, "Bedroom");

        [CommandMethod("IMB")]
        public void IMB() => Engine.CreateLabel(Util.Ed, Util.Db, "Master Bedroom");

        [CommandMethod("IGR")]
        public void IGR() => Engine.CreateLabel(Util.Ed, Util.Db, "Guest Room");

        [CommandMethod("ILI")]
        public void ILI() => Engine.CreateLabel(Util.Ed, Util.Db, "Living Room");

        [CommandMethod("IDI")]
        public void IDI() => Engine.CreateLabel(Util.Ed, Util.Db, "Dining Room");

        [CommandMethod("IKI")]
        public void IKI() => Engine.CreateLabel(Util.Ed, Util.Db, "Kitchen");

        [CommandMethod("IPA")]
        public void IPA() => Engine.CreateLabel(Util.Ed, Util.Db, "Pantry");

        [CommandMethod("IBA")]
        public void IBA() => Engine.CreateLabel(Util.Ed, Util.Db, "Bathroom");

        [CommandMethod("IAT")]
        public void IAT() => Engine.CreateLabel(Util.Ed, Util.Db, "Attached Toilet");

        [CommandMethod("ICT")]
        public void ICT() => Engine.CreateLabel(Util.Ed, Util.Db, "Common Toilet");

        [CommandMethod("IST")]
        public void IST() => Engine.CreateLabel(Util.Ed, Util.Db, "Study Room");

        [CommandMethod("IOF")]
        public void IOF() => Engine.CreateLabel(Util.Ed, Util.Db, "Office");

        [CommandMethod("ISR")]
        public void ISR() => Engine.CreateLabel(Util.Ed, Util.Db, "Store Room");

        [CommandMethod("IPR")]
        public void IPR() => Engine.CreateLabel(Util.Ed, Util.Db, "Pooja Room");

        [CommandMethod("IBC")]
        public void IBC() => Engine.CreateLabel(Util.Ed, Util.Db, "Balcony");

        [CommandMethod("ITE")]
        public void ITE() => Engine.CreateLabel(Util.Ed, Util.Db, "Terrace");

        [CommandMethod("ISC")]
        public void ISC() => Engine.CreateLabel(Util.Ed, Util.Db, "Staircase");

        [CommandMethod("ICO")]
        public void ICO() => Engine.CreateLabel(Util.Ed, Util.Db, "Corridor");

        [CommandMethod("IEN")]
        public void IEN() => Engine.CreateLabel(Util.Ed, Util.Db, "Entrance");

        [CommandMethod("ILO")]
        public void ILO() => Engine.CreateLabel(Util.Ed, Util.Db, "Lobby");

        [CommandMethod("IUT")]
        public void IUT() => Engine.CreateLabel(Util.Ed, Util.Db, "Utility");

        [CommandMethod("ILA")]
        public void ILA() => Engine.CreateLabel(Util.Ed, Util.Db, "Laundry");

        [CommandMethod("IGA")]
        public void IGA() => Engine.CreateLabel(Util.Ed, Util.Db, "Garage");

        [CommandMethod("IGD")]
        public void IGD() => Engine.CreateLabel(Util.Ed, Util.Db, "Garden");

        [CommandMethod("ICY")]
        public void ICY() => Engine.CreateLabel(Util.Ed, Util.Db, "Courtyard");

        [CommandMethod("IWR")]
        public void IWR() => Engine.CreateLabel(Util.Ed, Util.Db, "Wardrobe");

        [CommandMethod("IDR")]
        public void IDR() => Engine.CreateLabel(Util.Ed, Util.Db, "Dressing Room");

        [CommandMethod("IHT")]
        public void IHT() => Engine.CreateLabel(Util.Ed, Util.Db, "Home Theater");

        [CommandMethod("IGY")]
        public void IGY() => Engine.CreateLabel(Util.Ed, Util.Db, "Gym");

        [CommandMethod("IDO")]
        public void IDO()
        {
            var r = Util.Ed.GetString("\n| Enter room type: ");
            if (r.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(r.StringResult))
                Engine.CreateLabel(Util.Ed, Util.Db, r.StringResult);
            else Util.Ed.WriteMessage("\nCancelled.");
        }

        [CommandMethod("IROOM")]
        public void IROOM()
        {
            string[] roomTypes = LayerData.RoomTypes.Select(r => r.RoomType).ToArray();
            using (var dlg = new UI.RoomPickerForm(roomTypes))
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedRoomType))
                    Engine.CreateLabel(Util.Ed, Util.Db, dlg.SelectedRoomType);
                else Util.Ed.WriteMessage("\n| Room selection cancelled.");
            }
        }

        [CommandMethod("IAR")]
        public void IAR() => Engine.AreaLabelForSelected(Util.Ed, Util.Db);

        [CommandMethod("IRECT")]
        public void IRECT() => Engine.ToggleRect(Util.Ed);

        [CommandMethod("IHIDERECT")]
        public void IHIDERECT() => Engine.HideRect(Util.Ed, Util.Db);

        [CommandMethod("ITH")]
        public void ITH() => Engine.SetTextHeight(Util.Ed);

        [CommandMethod("ILAYER")]
        public void ILAYER() => Engine.SetLayer(Util.Ed);

        [CommandMethod("ISET")]
        public void ISET() => Engine.ShowSettings(Util.Ed);

        [CommandMethod("IRESET")]
        public void IRESET() => Engine.ResetAndAnnounce(Util.Ed);

        [CommandMethod("IFLOOR")]
        public void IFLOOR() => Engine.SetFloorPrefix(Util.Ed);

        [CommandMethod("IRELABEL")]
        public void IRELABEL() => Engine.Relabel(Util.Ed, Util.Db);

        [CommandMethod("IAUDIT")]
        public void IAUDIT() => Engine.Audit(Util.Ed, Util.Db);

        [CommandMethod("ICHECK")]
        public void ICHECK() => Engine.Check(Util.Ed, Util.Db);

        [CommandMethod("ISCHEDULE")]
        public void ISCHEDULE() => Engine.Schedule(Util.Ed, Util.Db);

        [CommandMethod("ITOTAL")]
        public void ITOTAL() => Engine.Total(Util.Ed);

        [CommandMethod("IHELP")]
        public void IHELP()
        {
            var ed = Util.Ed;
            ed.WriteMessage("\n================================================================");
            ed.WriteMessage("\n   ROOM DIMENSION TOOL — INCHES (I-)");
            ed.WriteMessage("\n================================================================");
            ed.WriteMessage("\n  IROOM   Pick room type from dialog     IDO   Custom room name");
            ed.WriteMessage("\n  IBR  IMB  IGR  ILI  IDI  IKI  IPA  IBA  IAT  ICT  IST  IOF  ISR  IPR  IBC  ITE  ISC  ICO  IEN  ILO  IUT  ILA  IGA  IGD  ICY  IWR  IDR  IHT  IGY");
            ed.WriteMessage("\n  IAR       Area label for selected polyline");
            ed.WriteMessage("\n  IRECT     Toggle rectangle drawing ON/OFF     IHIDERECT Freeze/thaw rectangle layer");
            ed.WriteMessage("\n  ITH      Change text height     ILAYER   Change label layer     IFLOOR Set floor prefix");
            ed.WriteMessage("\n  ISET     Show settings          IRESET Reset to defaults      IRELABEL Relabel existing text");
            ed.WriteMessage("\n  IAUDIT   Audit label/rect counts     ICHECK Check rectangle closure     ISCHEDULE Export CSV schedule");
            ed.WriteMessage("\n  ITOTAL    Total area by filter");
            ed.WriteMessage("\n================================================================");
        }
    }
}