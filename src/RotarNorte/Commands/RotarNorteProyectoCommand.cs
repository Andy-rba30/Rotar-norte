using System;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RotarNorte.Core;
using RotarNorte.UI;
using RevitOperationCanceled = Autodesk.Revit.Exceptions.OperationCanceledException;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RotarNorte.Commands
{
    /// <summary>Comando principal: rotación segura del Norte de Proyecto.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RotarNorteProyectoCommand : IExternalCommand
    {
        private static RotationOptions _lastOptions;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null) return Result.Cancelled;

            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show("Rotar Norte", "Este comando solo funciona en proyectos, no en familias.");
                return Result.Cancelled;
            }

            var options = _lastOptions ?? new RotationOptions();
            string preview = BuildPreview(doc, options);

            var form = new RotateNorthForm(preview);
            form.LoadFrom(options);
            if (_lastOptions == null) form.AngleDegrees = 0;

            while (true)
            {
                DialogResult result = form.ShowDialog(new RevitWindowHandle(commandData.Application.MainWindowHandle));

                if (result == DialogResult.Cancel) return Result.Cancelled;

                if (result == DialogResult.Retry)
                {
                    switch (form.Pick)
                    {
                        case PickRequest.MeasureAngle:
                            {
                                double? angle = MeasureAngle(uidoc, form.AlignToEast);
                                if (angle.HasValue) form.AngleDegrees = angle.Value;
                                break;
                            }
                        case PickRequest.PickCenter:
                            {
                                XYZ p = PickPoint(uidoc, "Elija el punto de giro");
                                if (p != null) form.SetCustomCenter(p);
                                break;
                            }
                    }
                    continue;
                }

                // OK: ejecutar
                form.ApplyTo(options);
                _lastOptions = options;
                break;
            }

            RotationReport report;
            using (var progress = new ProgressForm(options.DryRun ? "Rotar Norte - Simulando..." : "Rotar Norte - Girando..."))
            {
                progress.Show(new RevitWindowHandle(commandData.Application.MainWindowHandle));
                progress.Report("Preparando...");
                var engine = new NorthRotationEngine(doc, options, progress.Report);
                report = engine.Run();
                progress.Close();
            }

            using (var reportForm = new ReportForm(report))
                reportForm.ShowDialog(new RevitWindowHandle(commandData.Application.MainWindowHandle));

            if (report.Success && !report.DryRun)
            {
                try { uidoc.RefreshActiveView(); } catch { }
            }

            return report.Success ? Result.Succeeded : Result.Cancelled;
        }

        private static string BuildPreview(Document doc, RotationOptions options)
        {
            try
            {
                CandidateSet set = NorthRotationEngine.Preview(doc, options);
                return $"Se girarán aproximadamente {set.Model.Count + set.Spatial.Count} elementos de modelo, {set.Annotations.Count} anotaciones, " +
                       $"{set.SectionViews.Count} secciones y {set.ElevationMarkers.Count} marcas de alzado; " +
                       $"se ajustarán {set.PlanViews.Count} vistas de planta y {set.Views3D.Count} vistas 3D. " +
                       $"{set.Pinned.Count} elementos anclados se desanclarán temporalmente." +
                       (doc.IsWorkshared ? " El modelo es de trabajo compartido: se solicitará la propiedad de los elementos." : "");
            }
            catch (Exception ex)
            {
                return "No se pudo calcular la vista previa: " + ex.Message;
            }
        }

        private static XYZ PickPoint(UIDocument uidoc, string prompt)
        {
            try
            {
                return uidoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Centers | ObjectSnapTypes.Points, prompt);
            }
            catch (RevitOperationCanceled) { return null; }
            catch (Exception ex)
            {
                TaskDialog.Show("Rotar Norte", "No se pudo elegir el punto en esta vista (use una vista de planta).\n\n" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Pide dos puntos y devuelve el giro (grados) necesario para que esa dirección apunte al
        /// Norte de Proyecto (o quede horizontal si alignToEast). Se elige el giro más corto (-90°, 90°].
        /// </summary>
        private static double? MeasureAngle(UIDocument uidoc, bool alignToEast)
        {
            XYZ p1 = PickPoint(uidoc, "Primer punto de la dirección a alinear");
            if (p1 == null) return null;
            XYZ p2 = PickPoint(uidoc, "Segundo punto de la dirección a alinear");
            if (p2 == null) return null;

            double ax = p2.X - p1.X, ay = p2.Y - p1.Y;
            if (Math.Sqrt(ax * ax + ay * ay) < 1e-6)
            {
                TaskDialog.Show("Rotar Norte", "Los dos puntos coinciden.");
                return null;
            }

            // Ángulo con signo desde la dirección elegida hasta +Y (norte) o +X (este), en sentido antihorario.
            double rad = alignToEast ? Math.Atan2(-ay, ax) : Math.Atan2(ax, ay);
            double deg = Compat.RadToDeg(rad);
            while (deg > 90.0) deg -= 180.0;
            while (deg <= -90.0) deg += 180.0;
            return deg;
        }
    }

    /// <summary>Envuelve el HWND de Revit para que los diálogos WinForms sean modales respecto a Revit.</summary>
    internal class RevitWindowHandle : IWin32Window
    {
        public IntPtr Handle { get; }
        public RevitWindowHandle(IntPtr handle) { Handle = handle; }
    }
}
