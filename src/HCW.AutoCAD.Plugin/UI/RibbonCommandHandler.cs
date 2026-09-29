using System;
using System.Windows.Input;
using Autodesk.Windows;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>
    /// Single shared ICommand for every ribbon button: sends the button's
    /// CommandParameter (a "COMMAND " string) to the active document's
    /// command line, as if it had been typed. AutoCAD's ribbon framework
    /// invokes CommandHandler.Execute(this) - passing the RibbonButton
    /// itself, not its CommandParameter - so the command string has to be
    /// pulled back out of the button here. wrapUpInMacro is deliberately
    /// false: with it true, SendStringToExecute either crashes AutoCAD or
    /// throws eInvalidInput depending on call context, so the command
    /// string must not contain "^C^C" (it would be typed as literal text,
    /// not interpreted as Escape) - a fresh ribbon click never needs it.
    /// </summary>
    public class RibbonCommandHandler : ICommand
    {
        public static readonly RibbonCommandHandler Instance = new RibbonCommandHandler();

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => AcAp.DocumentManager.MdiActiveDocument != null;

        public void Execute(object parameter)
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var macro = (parameter as RibbonButton)?.CommandParameter as string ?? parameter as string;
            if (macro == null) return;
            doc.SendStringToExecute(macro, true, false, true);
        }
    }
}
