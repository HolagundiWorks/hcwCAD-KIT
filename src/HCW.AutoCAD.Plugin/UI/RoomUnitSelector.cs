namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>
    /// The room-label engine the hcwCAD-KIT ribbon commands (HCWROOM*) use.
    /// No manual switch — it reads the current drawing's
    /// INSUNITS every time (see RoomEngineAuto) and scales automatically, so
    /// there's nothing to pick or get out of sync with the drawing. The
    /// typed M-/F-/I- commands keep using their own fixed engines unchanged.
    /// </summary>
    public static class RoomUnitSelector
    {
        public static readonly RoomEngine Current = new RoomEngineAuto();
    }
}
