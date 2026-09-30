using System;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RotarNorte.Core;
using RotarNorte.UI;

namespace RotarNorte.Commands
{
    /// <summary>
    /// Cambia el ángulo a Norte Verdadero. No mueve ningún elemento: es la alternativa segura
    /// cuando lo que se quiere es corregir la orientación real del edificio.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RotarNorteVerdaderoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null || doc.IsFamilyDocument) return Result.Cancelled;

            ProjectLocation loc = doc.ActiveProjectLocation;
            ProjectPosition pos = loc.GetProjectPosition(XYZ.Zero);
            double currentDeg = Compat.NormalizeDeg(Compat.RadToDeg(pos.Angle));

            using (var form = new TrueNorthForm(currentDeg))
            {
                if (form.ShowDialog(new RevitWindowHandle(commandData.Application.MainWindowHandle)) != DialogResult.OK)
                    return Result.Cancelled;

                double newDeg = form.NewAngleDegrees;
                if (Math.Abs(newDeg - currentDeg) < 1e-9) return Result.Cancelled;

                using (var t = new Transaction(doc, "Rotar Norte Verdadero"))
                {
                    t.Start();
                    try
                    {
                        loc.SetProjectPosition(XYZ.Zero, new ProjectPosition(pos.EastWest, pos.NorthSouth, pos.Elevation, Compat.DegToRad(newDeg)));
                        t.Commit();
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        message = ex.Message;
                        return Result.Failed;
                    }
                }
            }

            try { uidoc.RefreshActiveView(); } catch { }
            return Result.Succeeded;
        }
    }
}
