using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Runtime;

[assembly: AssemblyTitle("hcwCAD-KIT")]
[assembly: AssemblyDescription("hcwCAD-KIT — Holgundi Consulting Works AutoCAD tools for layers, room tags, quantity take-off and text")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Holgundi Consulting Works")]
[assembly: AssemblyProduct("hcwCAD-KIT")]
[assembly: AssemblyCopyright("Copyright 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("6f2c9e2a-6b3e-4a0e-9d55-9a2b6a2f5c31")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// Registers every [CommandMethod] in the assembly and runs IExtensionApplication
// (HCW.AutoCAD.Plugin.UI.HcwRibbonApplication) on load/unload.
[assembly: ExtensionApplication(typeof(HCW.AutoCAD.Plugin.UI.HcwRibbonApplication))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.BpltCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.TitleBlockCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.HcwLayerCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.MeasureCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.PolyAreaCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.DeleteAreaTextCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.VhLayerCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.TextToolCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.IncArrayCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.LayoutRenumberCommands))]
[assembly: CommandClass(typeof(HCW.AutoCAD.Plugin.Commands.DraftExtraCommands))]
