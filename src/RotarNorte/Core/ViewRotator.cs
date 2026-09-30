using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RotarNorte.Core
{
    /// <summary>
    /// Ajusta las vistas para que sigan mostrando lo mismo después de girar el modelo:
    /// regiones de recorte de plantas, cajas de sección y cámaras 3D, y ventanas gráficas en planos.
    /// </summary>
    internal class ViewRotator
    {
        private readonly Document _doc;
        private readonly RotationOptions _options;
        private readonly RotationReport _report;
        private readonly Transform _rot;

        public ViewRotator(Document doc, RotationOptions options, RotationReport report, Transform rotation)
        {
            _doc = doc;
            _options = options;
            _report = report;
            _rot = rotation;
        }

        // ------------------------------------------------------------------ plantas

        public static bool IsTrueNorthOriented(View view)
        {
            try
            {
                Parameter p = view.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH);
                return p != null && p.AsInteger() == 1;
            }
            catch { return false; }
        }

        public static bool HasScopeBox(View view)
        {
            try
            {
                Parameter p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                return p != null && p.AsElementId() != ElementId.InvalidElementId;
            }
            catch { return false; }
        }

        /// <summary>Procesa una vista de planta. Devuelve true si se modificó.</summary>
        public bool ProcessPlanView(ViewPlan view)
        {
            if (view == null || view.IsTemplate) return false;

            if (HasScopeBox(view))
            {
                // La caja de referencia gira como elemento de modelo y arrastra la orientación de la vista.
                _report.Notes.Add($"Vista '{view.Name}': controlada por caja de referencia; la caja gira con el modelo y la vista la sigue.");
                return false;
            }

            bool keepAppearance = _options.PlanViews == PlanViewMode.KeepAppearance || IsTrueNorthOriented(view);

            try
            {
                ViewCropRegionShapeManager mgr = view.GetCropRegionShapeManager();
                bool shaped = false;
                IList<CurveLoop> loops = null;
                try
                {
                    shaped = mgr.ShapeSet;
                    if (shaped) loops = mgr.GetCropShape();
                }
                catch { shaped = false; }

                bool wasActive = view.CropBoxActive;
                BoundingBoxXYZ cb = view.CropBox;

                if (keepAppearance)
                {
                    // Girar la región de recorte junto con el modelo: la vista se ve exactamente igual.
                    var nb = new BoundingBoxXYZ
                    {
                        Transform = _rot.Multiply(cb.Transform),
                        Min = cb.Min,
                        Max = cb.Max
                    };

                    if (shaped) mgr.RemoveCropRegionShape();
                    if (!wasActive) view.CropBoxActive = true;
                    view.CropBox = nb;
                    if (shaped) ReapplyShape(view, mgr, loops);
                    if (!wasActive) view.CropBoxActive = false;

                    _report.PlanViewsRotated++;
                }
                else
                {
                    // Mantener la orientación de la vista y reajustar el recorte para abarcar lo mismo.
                    if (shaped)
                    {
                        ReapplyShape(view, mgr, loops);
                    }
                    else
                    {
                        Transform t = cb.Transform;
                        Transform inv = t.Inverse;
                        var corners = new[]
                        {
                            new XYZ(cb.Min.X, cb.Min.Y, cb.Min.Z),
                            new XYZ(cb.Max.X, cb.Min.Y, cb.Min.Z),
                            new XYZ(cb.Max.X, cb.Max.Y, cb.Min.Z),
                            new XYZ(cb.Min.X, cb.Max.Y, cb.Min.Z),
                        };
                        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                        foreach (XYZ c in corners)
                        {
                            XYZ local = inv.OfPoint(_rot.OfPoint(t.OfPoint(c)));
                            minX = Math.Min(minX, local.X); maxX = Math.Max(maxX, local.X);
                            minY = Math.Min(minY, local.Y); maxY = Math.Max(maxY, local.Y);
                        }
                        var nb = new BoundingBoxXYZ
                        {
                            Transform = t,
                            Min = new XYZ(minX, minY, cb.Min.Z),
                            Max = new XYZ(maxX, maxY, cb.Max.Z)
                        };
                        if (!wasActive) view.CropBoxActive = true;
                        view.CropBox = nb;
                        if (!wasActive) view.CropBoxActive = false;
                    }
                    _report.PlanViewsRefit++;
                }
                return true;
            }
            catch (Exception ex)
            {
                _report.Fail(Compat.IdValue(view.Id), Compat.Describe(view), "No se pudo ajustar la región de recorte: " + ex.Message);
                return false;
            }
        }

        private void ReapplyShape(View view, ViewCropRegionShapeManager mgr, IList<CurveLoop> loops)
        {
            if (loops == null || loops.Count == 0) return;
            if (loops.Count > 1)
                _report.Notes.Add($"Vista '{view.Name}': tenía un recorte dividido; se conservó solo el primer contorno.");
            CurveLoop rotated = CurveLoop.CreateViaTransform(loops[0], _rot);
            mgr.SetCropShape(rotated);
        }

        // ------------------------------------------------------------------ 3D

        public void Process3DView(View3D view)
        {
            if (view == null || view.IsTemplate) return;

            // Caja de sección
            try
            {
                if (view.IsSectionBoxActive)
                {
                    BoundingBoxXYZ sb = view.GetSectionBox();
                    var nb = new BoundingBoxXYZ
                    {
                        Transform = _rot.Multiply(sb.Transform),
                        Min = sb.Min,
                        Max = sb.Max,
                        Enabled = true
                    };
                    view.SetSectionBox(nb);
                    _report.SectionBoxesRotated++;
                }
            }
            catch (Exception ex)
            {
                _report.Fail(Compat.IdValue(view.Id), Compat.Describe(view), "No se pudo girar la caja de sección: " + ex.Message);
            }

            // Cámara
            try
            {
                if (view.IsLocked)
                {
                    _report.Notes.Add($"Vista 3D '{view.Name}': está bloqueada, no se giró la cámara.");
                    return;
                }
                ViewOrientation3D o = view.GetOrientation();
                var no = new ViewOrientation3D(
                    _rot.OfPoint(o.EyePosition),
                    _rot.OfVector(o.UpDirection),
                    _rot.OfVector(o.ForwardDirection));
                view.SetOrientation(no);
                _report.CamerasRotated++;
            }
            catch (Exception ex)
            {
                _report.Fail(Compat.IdValue(view.Id), Compat.Describe(view), "No se pudo girar la cámara: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ secciones (respaldo)

        /// <summary>
        /// Respaldo para secciones que no aceptan RotateElement: recolocar la vista mediante su caja de recorte.
        /// </summary>
        public bool RotateSectionByCropBox(ViewSection view)
        {
            try
            {
                BoundingBoxXYZ cb = view.CropBox;
                var nb = new BoundingBoxXYZ
                {
                    Transform = _rot.Multiply(cb.Transform),
                    Min = cb.Min,
                    Max = cb.Max
                };
                view.CropBox = nb;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ planos

        /// <summary>Centros de las ventanas gráficas de las vistas indicadas, para restaurarlos después.</summary>
        public Dictionary<ElementId, XYZ> SnapshotViewportCenters(IEnumerable<ElementId> viewIds)
        {
            var wanted = new HashSet<long>(viewIds.Select(Compat.IdValue));
            var result = new Dictionary<ElementId, XYZ>();
            foreach (Viewport vp in new FilteredElementCollector(_doc).OfClass(typeof(Viewport)))
            {
                try
                {
                    if (!wanted.Contains(Compat.IdValue(vp.ViewId))) continue;
                    result[vp.Id] = vp.GetBoxCenter();
                }
                catch { }
            }
            return result;
        }

        public void RestoreViewportCenters(Dictionary<ElementId, XYZ> snapshot)
        {
            foreach (var kv in snapshot)
            {
                try
                {
                    if (!(_doc.GetElement(kv.Key) is Viewport vp)) continue;
                    XYZ now = vp.GetBoxCenter();
                    if (now.DistanceTo(kv.Value) > 1e-6)
                    {
                        vp.SetBoxCenter(kv.Value);
                        _report.ViewportsRecentered++;
                    }
                }
                catch (Exception ex)
                {
                    _report.Notes.Add("No se pudo recentrar una ventana gráfica: " + ex.Message);
                }
            }
        }
    }
}
