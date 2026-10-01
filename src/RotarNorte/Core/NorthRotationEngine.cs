using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using RevitException = Autodesk.Revit.Exceptions.ApplicationException;

namespace RotarNorte.Core
{
    /// <summary>
    /// Motor de la rotación segura del Norte de Proyecto.
    ///
    /// Estrategia:
    ///  1. Se recogen los elementos que hay que girar (sin duplicar anfitrionados) y se desanclan
    ///     temporalmente los anclados (vínculos, DWG, rejillas...).
    ///  2. Modelo, anotaciones de planta, secciones y marcas de alzado se giran en UNA sola llamada
    ///     (como hace Revit al seleccionar todo y girar) para que cotas, etiquetas y referencias
    ///     se mantengan coherentes.
    ///  3. Si Revit rechaza la operación, se aíslan por bisección los elementos culpables
    ///     (en un grupo de transacciones que se deshace) y se repite sin ellos, informando de cada uno.
    ///  4. Habitaciones/espacios/áreas se trasladan por su punto de ubicación.
    ///  5. Vistas: se giran las regiones de recorte de planta, cajas de sección y cámaras 3D,
    ///     y se recentran las ventanas gráficas de los planos.
    ///  6. Se corrige el ángulo a Norte Verdadero para que las coordenadas compartidas no cambien.
    ///  7. Se vuelven a anclar los elementos.
    /// Todo va dentro de un TransactionGroup: un único "Deshacer" lo revierte por completo, y en modo
    /// simulación se deshace al final sin tocar el modelo.
    /// </summary>
    public class NorthRotationEngine
    {
        private readonly Document _doc;
        private readonly RotationOptions _options;
        private readonly Action<string> _progress;
        private readonly RotationReport _report = new RotationReport();

        private XYZ _center;
        private Line _axis;
        private Transform _rot;
        private FailureCollector _failures;

        public NorthRotationEngine(Document doc, RotationOptions options, Action<string> progress = null)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _progress = progress ?? (_ => { });
        }

        // ------------------------------------------------------------------ preparación

        public static XYZ ResolveCenter(Document doc, RotationOptions options, out string description)
        {
            switch (options.CenterMode)
            {
                case RotationCenterMode.InternalOrigin:
                    description = "Origen interno";
                    return XYZ.Zero;

                case RotationCenterMode.SurveyPoint:
                    {
                        XYZ p = GetBasePoint(doc, true) ?? XYZ.Zero;
                        description = $"Punto de levantamiento ({Fmt(p)})";
                        return Flat(p);
                    }

                case RotationCenterMode.Custom:
                    {
                        XYZ p = options.CustomCenter ?? XYZ.Zero;
                        description = $"Punto elegido ({Fmt(p)})";
                        return Flat(p);
                    }

                default:
                    {
                        XYZ p = GetBasePoint(doc, false) ?? XYZ.Zero;
                        description = $"Punto base del proyecto ({Fmt(p)})";
                        return Flat(p);
                    }
            }
        }

        private static XYZ Flat(XYZ p) => new XYZ(p.X, p.Y, 0);

        private static string Fmt(XYZ p)
        {
            double f = 0.3048;
            return $"X={p.X * f:0.###} m, Y={p.Y * f:0.###} m";
        }

        private static XYZ GetBasePoint(Document doc, bool shared)
        {
            foreach (BasePoint bp in new FilteredElementCollector(doc).OfClass(typeof(BasePoint)))
            {
                try
                {
                    if (bp.IsShared != shared) continue;
                    return bp.Position;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Recuento rápido para mostrar antes de ejecutar.</summary>
        public static CandidateSet Preview(Document doc, RotationOptions options)
        {
            return new CandidateCollector(doc, options, new RotationReport()).Collect();
        }

        // ------------------------------------------------------------------ ejecución

        public RotationReport Run()
        {
            var sw = Stopwatch.StartNew();
            _report.DryRun = _options.DryRun;
            _report.AngleDegrees = _options.AngleDegrees;

            try
            {
                if (_doc.IsFamilyDocument) return Abort("El documento es una familia, no un proyecto.");
                if (_doc.IsReadOnly) return Abort("El documento es de solo lectura.");
                if (Math.Abs(_options.AngleDegrees) < 1e-9) return Abort("El ángulo es cero: no hay nada que girar.");

                _center = ResolveCenter(_doc, _options, out string centerDesc);
                _report.CenterDescription = centerDesc;
                _axis = Line.CreateBound(_center, _center + XYZ.BasisZ * 10.0);
                _rot = Transform.CreateRotationAtPoint(XYZ.BasisZ, _options.AngleRadians, _center);
                _failures = new FailureCollector(_options.AutoResolveErrors);

                _progress("Analizando el modelo...");
                CandidateSet set = new CandidateCollector(_doc, _options, _report).Collect();
                if (set.TotalToRotate == 0) return Abort("No se encontraron elementos que girar.");

                _progress("Comprobando propiedad de elementos (trabajo compartido)...");
                if (!CheckoutIfWorkshared(set)) return Abort("No se pudo obtener la propiedad de los elementos en el modelo central.");

                // Coordenadas compartidas de un punto de prueba, para verificar luego el signo del ángulo.
                ProjectLocation activeLoc = _doc.ActiveProjectLocation;
                XYZ testPoint = _center + new XYZ(100, 37, 0);
                ProjectPosition testBefore = activeLoc.GetProjectPosition(testPoint);
                _report.TrueNorthBefore = FormatAngle(activeLoc.GetProjectPosition(_center).Angle);

                using (var group = new TransactionGroup(_doc, "Rotar Norte de Proyecto (seguro)"))
                {
                    group.Start();
                    bool ok = false;
                    try
                    {
                        ok = Execute(set, activeLoc, testPoint, testBefore);
                    }
                    catch (Exception ex)
                    {
                        _report.AbortReason = "Error inesperado: " + ex.Message;
                        ok = false;
                    }

                    if (ok && !_options.DryRun)
                    {
                        group.Assimilate();
                        _report.Success = true;
                    }
                    else
                    {
                        group.RollBack();
                        _report.Success = ok;
                    }
                }
            }
            catch (Exception ex)
            {
                _report.Success = false;
                _report.AbortReason = "Error inesperado: " + ex.Message;
            }

            _report.Elapsed = sw.Elapsed;
            return _report;
        }

        private RotationReport Abort(string reason)
        {
            _report.Success = false;
            _report.AbortReason = reason;
            return _report;
        }

        private bool Execute(CandidateSet set, ProjectLocation activeLoc, XYZ testPoint, ProjectPosition testBefore)
        {
            // 1. Desanclar
            if (set.Pinned.Count > 0)
            {
                _progress($"Desanclando {set.Pinned.Count} elementos...");
                if (!RunTransaction("Desanclar", () =>
                {
                    foreach (ElementId id in set.Pinned)
                    {
                        Element e = _doc.GetElement(id);
                        if (e != null && e.Pinned) e.Pinned = false;
                    }
                }))
                {
                    _report.AbortReason = "No se pudieron desanclar los elementos. " + LastErrors();
                    return false;
                }
            }

            // 2. Comprobar si las vistas de sección / marcas de alzado admiten RotateElement en esta versión
            var bulk = new List<ElementId>(set.Model);
            bulk.AddRange(set.Annotations);

            var sectionsForFallback = new List<ElementId>();
            if (set.SectionViews.Count > 0)
            {
                if (ProbeAny(set.SectionViews)) bulk.AddRange(set.SectionViews);
                else
                {
                    sectionsForFallback.AddRange(set.SectionViews);
                    _report.Notes.Add("Esta versión de Revit no admite girar secciones como elementos; se recolocaron por su caja de recorte.");
                }
            }
            if (set.ElevationMarkers.Count > 0)
            {
                if (ProbeAny(set.ElevationMarkers)) bulk.AddRange(set.ElevationMarkers);
                else
                {
                    foreach (var id in set.ElevationMarkers)
                        _report.Fail(Compat.IdValue(id), Compat.Describe(_doc.GetElement(id)), "Esta versión de Revit no admite girar marcas de alzado por API");
                }
            }

            // 3. Rotación en bloque (con aislamiento de culpables si hace falta)
            _progress($"Girando {bulk.Count} elementos en bloque...");
            if (!TryBulkRotate(bulk))
            {
                _progress("Revit rechazó la rotación en bloque; aislando elementos problemáticos...");
                _report.UsedElementByElementFallback = true;
                Dictionary<long, string> culprits = IsolateCulprits(bulk, out bool tooMany);
                var culpritSet = new HashSet<long>(culprits.Keys);

                // Las familias culpables se pueden recrear giradas; el resto se informa.
                var recreatable = new List<ElementId>();
                foreach (var kv in culprits)
                {
                    Element c = _doc.GetElement(Compat.MakeId(kv.Key));
                    if (_options.RecreateUnrotatableFamilies && c is FamilyInstance && !tooMany)
                        recreatable.Add(c.Id);
                    else
                        _report.Fail(kv.Key, Compat.Describe(c), "Revit no permitió girarlo: " + kv.Value);
                }

                if (tooMany)
                {
                    _report.AbortReason = $"Demasiados elementos problemáticos (más de {_options.MaxIsolatedFailures}); los encontrados se listan abajo. " +
                                          "Ejecute una simulación, revise esos elementos (o su familia) y vuelva a intentarlo. " + LastErrors();
                    return false;
                }
                set.Unrotatable.AddRange(recreatable);

                bulk = bulk.Where(id => !culpritSet.Contains(Compat.IdValue(id))).ToList();
                _progress($"Girando {bulk.Count} elementos (sin los problemáticos)...");
                if (!TryBulkRotate(bulk))
                {
                    _report.AbortReason = "La rotación en bloque volvió a fallar tras aislar los elementos problemáticos. " + LastErrors();
                    return false;
                }
            }
            CountRotated(bulk, set);

            // 4. Familias que Revit no permite girar: recrearlas giradas
            if (set.Unrotatable.Count > 0)
            {
                if (_options.RecreateUnrotatableFamilies)
                {
                    _progress($"Recreando {set.Unrotatable.Count} familias que no admiten giro...");
                    if (!RunTransaction("Recrear familias giradas", () => RecreateRotated(set)))
                    {
                        _report.AbortReason = "No se pudieron recrear las familias que no admiten giro. " + LastErrors();
                        return false;
                    }
                }
                else
                {
                    foreach (ElementId id in set.Unrotatable)
                        _report.Fail(Compat.IdValue(id), Compat.Describe(_doc.GetElement(id)),
                            "Familia basada en plano vertical/inclinado: Revit no permite girarla (opción de recrear desactivada)");
                }
            }

            // 5. Secciones que necesitan el respaldo por caja de recorte
            var viewRotator = new ViewRotator(_doc, _options, _report, _rot);
            if (sectionsForFallback.Count > 0)
            {
                _progress("Recolocando secciones...");
                RunTransaction("Recolocar secciones", () =>
                {
                    foreach (ElementId id in sectionsForFallback)
                    {
                        var vs = _doc.GetElement(id) as ViewSection;
                        if (vs == null) continue;
                        if (viewRotator.RotateSectionByCropBox(vs)) _report.SectionsRotated++;
                        else _report.Fail(Compat.IdValue(id), Compat.Describe(vs), "No se pudo recolocar la sección");
                    }
                });
            }

            // 6. Habitaciones, espacios y áreas
            if (set.Spatial.Count > 0)
            {
                _progress($"Trasladando {set.Spatial.Count} habitaciones/espacios/áreas...");
                RunTransaction("Trasladar habitaciones", () =>
                {
                    foreach (ElementId id in set.Spatial)
                    {
                        Element e = _doc.GetElement(id);
                        if (!(e?.Location is LocationPoint lp)) continue;
                        try
                        {
                            XYZ p = lp.Point;
                            XYZ q = _rot.OfPoint(p);
                            ElementTransformUtils.MoveElement(_doc, id, q - p);
                            _report.ModelElementsRotated++;
                            _report.CountCategory(Compat.CategoryName(e));
                        }
                        catch (Exception ex)
                        {
                            _report.Fail(Compat.IdValue(id), Compat.Describe(e), "No se pudo trasladar: " + ex.Message);
                        }
                    }
                });
            }

            // 7. Vistas
            _progress("Ajustando vistas de planta, 3D y planos...");
            var viewIds = new List<ElementId>(set.PlanViews);
            viewIds.AddRange(set.SectionViews);
            viewIds.AddRange(set.Views3D);
            Dictionary<ElementId, XYZ> viewportCenters = viewRotator.SnapshotViewportCenters(viewIds);

            if (!RunTransaction("Ajustar vistas", () =>
            {
                foreach (ElementId id in set.PlanViews)
                    viewRotator.ProcessPlanView(_doc.GetElement(id) as ViewPlan);
                foreach (ElementId id in set.Views3D)
                    viewRotator.Process3DView(_doc.GetElement(id) as View3D);
                viewRotator.RestoreViewportCenters(viewportCenters);
            }))
            {
                _report.AbortReason = "No se pudieron ajustar las vistas. " + LastErrors();
                return false;
            }

            // 8. Norte Verdadero / coordenadas compartidas
            if (_options.PreserveTrueNorth)
            {
                _progress("Corrigiendo el ángulo a Norte Verdadero...");
                if (!RunTransaction("Corregir Norte Verdadero", () => PreserveSharedCoordinates(activeLoc, testPoint, testBefore)))
                {
                    _report.AbortReason = "No se pudo corregir el ángulo a Norte Verdadero. " + LastErrors();
                    return false;
                }
            }
            else
            {
                _report.Notes.Add("No se corrigió el ángulo a Norte Verdadero: las coordenadas compartidas del edificio han cambiado.");
                _report.TrueNorthAfter = _report.TrueNorthBefore;
            }

            // 9. Volver a anclar
            if (set.Pinned.Count > 0)
            {
                _progress("Volviendo a anclar elementos...");
                RunTransaction("Anclar", () =>
                {
                    foreach (ElementId id in set.Pinned)
                    {
                        Element e = _doc.GetElement(id);
                        if (e == null) continue;
                        try { e.Pinned = true; _report.PinnedRestored++; }
                        catch (Exception ex) { _report.Notes.Add($"No se pudo volver a anclar {Compat.Describe(e)}: {ex.Message}"); }
                    }
                });
            }

            return true;
        }

        // ------------------------------------------------------------------ rotación en bloque

        private bool TryBulkRotate(ICollection<ElementId> ids)
        {
            if (ids.Count == 0) return true;
            return RunTransaction("Girar elementos", () => ElementTransformUtils.RotateElements(_doc, ids, _axis, _options.AngleRadians));
        }

        /// <summary>Prueba (y deshace) el giro de hasta tres elementos para saber si el API admite ese tipo.</summary>
        private bool ProbeAny(List<ElementId> ids)
        {
            foreach (ElementId id in ids.Take(3))
            {
                bool ok;
                using (var g = new TransactionGroup(_doc, "Prueba"))
                {
                    g.Start();
                    ok = TryBulkRotate(new[] { id });
                    g.RollBack();
                }
                if (ok) return true;
            }
            return false;
        }

        /// <summary>
        /// Encuentra por bisección los elementos que hacen fallar la rotación en bloque.
        /// Cada intento se ejecuta en un grupo de transacciones que se deshace, así el modelo queda intacto.
        /// Devuelve null si hay más culpables que el máximo permitido.
        /// </summary>
        private Dictionary<long, string> IsolateCulprits(List<ElementId> ids, out bool tooMany)
        {
            tooMany = false;
            var culprits = new Dictionary<long, string>();
            var pending = new Stack<List<ElementId>>();
            pending.Push(ids);
            int attempts = 0;

            while (pending.Count > 0)
            {
                List<ElementId> chunk = pending.Pop();
                attempts++;
                _progress($"Aislando elementos problemáticos... (intento {attempts}, {chunk.Count} elementos, {culprits.Count} encontrados)");

                bool ok;
                using (var g = new TransactionGroup(_doc, "Prueba"))
                {
                    g.Start();
                    ok = TryBulkRotate(chunk);
                    g.RollBack();
                }
                if (ok) continue;

                if (chunk.Count == 1)
                {
                    culprits[Compat.IdValue(chunk[0])] = LastErrorsShort();
                    if (culprits.Count > _options.MaxIsolatedFailures) { tooMany = true; return culprits; }
                    continue;
                }

                int half = chunk.Count / 2;
                pending.Push(chunk.GetRange(half, chunk.Count - half));
                pending.Push(chunk.GetRange(0, half));
            }

            return culprits;
        }

        /// <summary>
        /// Copia las familias con la transformación de giro (como "Pegar alineado") y borra las originales.
        /// Cambian de Id; se conserva el anclaje y todos los parámetros de ejemplar.
        /// </summary>
        private void RecreateRotated(CandidateSet set)
        {
            var pinned = new HashSet<long>(set.Pinned.Select(Compat.IdValue));
            var toDelete = new List<ElementId>();

            foreach (ElementId id in set.Unrotatable)
            {
                Element original = _doc.GetElement(id);
                if (original == null) continue;
                try
                {
                    ICollection<ElementId> copies = ElementTransformUtils.CopyElements(
                        _doc, new[] { id }, _doc, _rot, new CopyPasteOptions());
                    if (copies == null || copies.Count == 0)
                    {
                        _report.Fail(Compat.IdValue(id), Compat.Describe(original), "No se pudo recrear girada (la copia no devolvió elementos)");
                        continue;
                    }
                    if (pinned.Contains(Compat.IdValue(id)))
                        foreach (ElementId c in copies) { try { _doc.GetElement(c).Pinned = true; } catch { } }

                    toDelete.Add(id);
                    _report.FamiliesRecreated++;
                    _report.CountCategory(Compat.CategoryName(original));
                }
                catch (Exception ex)
                {
                    _report.Fail(Compat.IdValue(id), Compat.Describe(original), "No se pudo recrear girada: " + ex.Message);
                }
            }

            if (toDelete.Count > 0)
            {
                _doc.Delete(toDelete);
                _report.Notes.Add($"{toDelete.Count} familias basadas en plano vertical/inclinado se recrearon giradas y tienen un Id nuevo (sus etiquetas, si las había, se pierden).");
            }
        }

        private void CountRotated(List<ElementId> bulk, CandidateSet set)
        {
            var annotations = new HashSet<long>(set.Annotations.Select(Compat.IdValue));
            var sections = new HashSet<long>(set.SectionViews.Select(Compat.IdValue));
            var markers = new HashSet<long>(set.ElevationMarkers.Select(Compat.IdValue));

            foreach (ElementId id in bulk)
            {
                long v = Compat.IdValue(id);
                if (annotations.Contains(v)) { _report.AnnotationsRotated++; continue; }
                if (sections.Contains(v)) { _report.SectionsRotated++; continue; }
                if (markers.Contains(v)) { _report.ElevationMarkersRotated++; continue; }
                _report.ModelElementsRotated++;
                _report.CountCategory(Compat.CategoryName(_doc.GetElement(id)));
            }
        }

        // ------------------------------------------------------------------ coordenadas compartidas

        private void PreserveSharedCoordinates(ProjectLocation activeLoc, XYZ testPoint, ProjectPosition testBefore)
        {
            double theta = _options.AngleRadians;
            XYZ testAfter = _rot.OfPoint(testPoint);
            ProjectPosition basePos = activeLoc.GetProjectPosition(_center);
            double original = basePos.Angle;

            // El signo de ProjectPosition.Angle se verifica empíricamente con el punto de prueba.
            double chosen = double.NaN;
            foreach (double candidate in new[] { original + theta, original - theta })
            {
                activeLoc.SetProjectPosition(_center, new ProjectPosition(basePos.EastWest, basePos.NorthSouth, basePos.Elevation, candidate));
                ProjectPosition now = activeLoc.GetProjectPosition(testAfter);
                if (Math.Abs(now.EastWest - testBefore.EastWest) < 1e-4 && Math.Abs(now.NorthSouth - testBefore.NorthSouth) < 1e-4)
                {
                    chosen = candidate;
                    break;
                }
            }

            if (double.IsNaN(chosen))
            {
                activeLoc.SetProjectPosition(_center, new ProjectPosition(basePos.EastWest, basePos.NorthSouth, basePos.Elevation, original));
                _report.Warnings.Add("No se pudo verificar la corrección del Norte Verdadero; se dejó el ángulo original.");
                _report.TrueNorthAfter = _report.TrueNorthBefore;
                return;
            }

            double delta = chosen - original;
            _report.TrueNorthAfter = FormatAngle(chosen);

            // Aplicar el mismo ajuste a las demás ubicaciones (emplazamientos) del proyecto
            foreach (ProjectLocation loc in _doc.ProjectLocations)
            {
                if (loc.Id == activeLoc.Id) continue;
                try
                {
                    ProjectPosition p = loc.GetProjectPosition(_center);
                    loc.SetProjectPosition(_center, new ProjectPosition(p.EastWest, p.NorthSouth, p.Elevation, p.Angle + delta));
                }
                catch (Exception ex)
                {
                    _report.Notes.Add($"No se pudo ajustar la ubicación '{loc.Name}': {ex.Message}");
                }
            }
        }

        private static string FormatAngle(double radians) => $"{Compat.NormalizeDeg(Compat.RadToDeg(radians)):0.####}°";

        // ------------------------------------------------------------------ trabajo compartido

        private bool CheckoutIfWorkshared(CandidateSet set)
        {
            if (!_doc.IsWorkshared) return true;

            var all = set.AllRotatable().ToList();
            all.AddRange(set.Pinned);
            var unique = all.GroupBy(Compat.IdValue).Select(g => g.First()).ToList();

            ICollection<ElementId> owned;
            try
            {
                owned = WorksharingUtils.CheckoutElements(_doc, unique);
            }
            catch (Exception ex)
            {
                _report.AbortReason = ex.Message;
                return false;
            }

            var ownedSet = new HashSet<long>(owned.Select(Compat.IdValue));
            var notOwned = new HashSet<long>();
            foreach (ElementId id in unique)
            {
                if (ownedSet.Contains(Compat.IdValue(id))) continue;
                notOwned.Add(Compat.IdValue(id));
                _report.Fail(Compat.IdValue(id), Compat.Describe(_doc.GetElement(id)), "Propiedad de otro usuario (trabajo compartido)");
            }

            if (notOwned.Count == 0) return true;

            set.Model.RemoveAll(id => notOwned.Contains(Compat.IdValue(id)));
            set.Annotations.RemoveAll(id => notOwned.Contains(Compat.IdValue(id)));
            set.SectionViews.RemoveAll(id => notOwned.Contains(Compat.IdValue(id)));
            set.ElevationMarkers.RemoveAll(id => notOwned.Contains(Compat.IdValue(id)));
            set.Spatial.RemoveAll(id => notOwned.Contains(Compat.IdValue(id)));
            set.Unrotatable.RemoveAll(id => notOwned.Contains(Compat.IdValue(id)));
            set.Pinned.RemoveAll(id => notOwned.Contains(Compat.IdValue(id)));
            _report.Warnings.Add($"{notOwned.Count} elementos pertenecen a otros usuarios y no se giraron.");
            return true;
        }

        // ------------------------------------------------------------------ transacciones

        /// <summary>Ejecuta una acción en una transacción con el preprocesador de fallos. Devuelve true si se confirmó.</summary>
        private bool RunTransaction(string name, Action action)
        {
            _failures.Reset();
            using (var t = new Transaction(_doc, name))
            {
                t.Start();
                _failures.Attach(t);
                try
                {
                    action();
                    TransactionStatus status = t.Commit();
                    if (status == TransactionStatus.Committed)
                    {
                        _report.Warnings.AddRange(_failures.Warnings);
                        return true;
                    }
                    return false;
                }
                catch (RevitException ex)
                {
                    _failures.Errors.Add(ex.Message);
                    if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                    return false;
                }
                catch (Exception ex)
                {
                    _failures.Errors.Add(ex.Message);
                    if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                    return false;
                }
            }
        }

        private string LastErrors()
        {
            if (_failures == null || _failures.Errors.Count == 0) return string.Empty;
            return "Revit informó: " + string.Join(" | ", _failures.Errors.Distinct().Take(5));
        }

        private string LastErrorsShort()
        {
            if (_failures == null || _failures.Errors.Count == 0) return "(sin detalle)";
            return _failures.Errors.Distinct().First();
        }
    }
}
