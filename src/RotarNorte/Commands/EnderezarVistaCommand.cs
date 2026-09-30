using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RotarNorte.Core;

namespace RotarNorte.Commands
{
    /// <summary>
    /// Quita la rotación de la región de recorte de una vista de planta (la vista queda alineada con el
    /// Norte de Proyecto) manteniendo el mismo contenido abarcado. Útil después de una rotación en modo
    /// "conservar aspecto" para enderezar solo las vistas que se desee.
    /// Actúa sobre las vistas seleccionadas en el Navegador de proyectos o, si no hay, sobre la vista activa.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EnderezarVistaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null || doc.IsFamilyDocument) return Result.Cancelled;

            var views = new List<ViewPlan>();
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                if (doc.GetElement(id) is ViewPlan vp && !vp.IsTemplate && CandidateCollector.IsPlanType(vp.ViewType))
                    views.Add(vp);
            }
            if (views.Count == 0 && doc.ActiveView is ViewPlan active && CandidateCollector.IsPlanType(active.ViewType))
                views.Add(active);

            if (views.Count == 0)
            {
                TaskDialog.Show("Enderezar vista", "Active una vista de planta o seleccione vistas de planta en el Navegador de proyectos.");
                return Result.Cancelled;
            }

            int done = 0;
            var notes = new List<string>();

            using (var t = new Transaction(doc, "Enderezar vista"))
            {
                t.Start();
                var opts = t.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(new FailureCollector(false));
                t.SetFailureHandlingOptions(opts);

                // Recordar centros de ventanas gráficas para no mover nada en los planos
                var centers = new Dictionary<ElementId, XYZ>();
                var wanted = new HashSet<long>(views.Select(v => Compat.IdValue(v.Id)));
                foreach (Viewport vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)))
                {
                    try { if (wanted.Contains(Compat.IdValue(vp.ViewId))) centers[vp.Id] = vp.GetBoxCenter(); } catch { }
                }

                foreach (ViewPlan view in views)
                {
                    try
                    {
                        if (ViewRotator.HasScopeBox(view))
                        {
                            notes.Add($"'{view.Name}': está controlada por una caja de referencia; gire la caja en su lugar.");
                            continue;
                        }
                        if (Straighten(view)) done++;
                        else notes.Add($"'{view.Name}': ya estaba alineada con el Norte de Proyecto.");
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"'{view.Name}': {ex.Message}");
                    }
                }

                foreach (var kv in centers)
                {
                    try
                    {
                        if (doc.GetElement(kv.Key) is Viewport vp && vp.GetBoxCenter().DistanceTo(kv.Value) > 1e-6)
                            vp.SetBoxCenter(kv.Value);
                    }
                    catch { }
                }

                if (t.Commit() != TransactionStatus.Committed)
                {
                    message = "Revit no permitió el cambio.";
                    return Result.Failed;
                }
            }

            string text = $"{done} vista(s) enderezada(s).";
            if (notes.Count > 0) text += "\n\n" + string.Join("\n", notes);
            TaskDialog.Show("Enderezar vista", text);
            try { uidoc.RefreshActiveView(); } catch { }
            return Result.Succeeded;
        }

        /// <summary>Alinea la caja de recorte con los ejes del proyecto conservando el contenido. Devuelve false si no hacía falta.</summary>
        private static bool Straighten(ViewPlan view)
        {
            BoundingBoxXYZ cb = view.CropBox;
            Transform t = cb.Transform;
            if (t.BasisX.IsAlmostEqualTo(XYZ.BasisX) && t.BasisY.IsAlmostEqualTo(XYZ.BasisY)) return false;

            ViewCropRegionShapeManager mgr = view.GetCropRegionShapeManager();
            bool shaped = false;
            IList<CurveLoop> loops = null;
            try { shaped = mgr.ShapeSet; if (shaped) loops = mgr.GetCropShape(); } catch { shaped = false; }

            // Contorno actual en coordenadas de modelo
            var corners = new[]
            {
                t.OfPoint(new XYZ(cb.Min.X, cb.Min.Y, cb.Min.Z)),
                t.OfPoint(new XYZ(cb.Max.X, cb.Min.Y, cb.Min.Z)),
                t.OfPoint(new XYZ(cb.Max.X, cb.Max.Y, cb.Min.Z)),
                t.OfPoint(new XYZ(cb.Min.X, cb.Max.Y, cb.Min.Z)),
            };

            Transform nt = Transform.CreateTranslation(t.Origin);
            Transform inv = nt.Inverse;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (XYZ c in corners)
            {
                XYZ l = inv.OfPoint(c);
                minX = Math.Min(minX, l.X); maxX = Math.Max(maxX, l.X);
                minY = Math.Min(minY, l.Y); maxY = Math.Max(maxY, l.Y);
            }

            var nb = new BoundingBoxXYZ
            {
                Transform = nt,
                Min = new XYZ(minX, minY, cb.Min.Z),
                Max = new XYZ(maxX, maxY, cb.Max.Z)
            };

            bool wasActive = view.CropBoxActive;
            if (shaped) mgr.RemoveCropRegionShape();
            if (!wasActive) view.CropBoxActive = true;
            view.CropBox = nb;
            if (shaped && loops != null && loops.Count > 0) mgr.SetCropShape(loops[0]);
            if (!wasActive) view.CropBoxActive = false;
            return true;
        }
    }
}
