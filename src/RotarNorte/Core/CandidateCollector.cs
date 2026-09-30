using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;

namespace RotarNorte.Core
{
    /// <summary>Conjunto de elementos que participan en la rotación.</summary>
    public class CandidateSet
    {
        /// <summary>Elementos de modelo (muros, suelos, familias, vínculos, DWG, rejillas, planos de referencia, cajas de referencia, grupos...).</summary>
        public readonly List<ElementId> Model = new List<ElementId>();

        /// <summary>Anotaciones de vistas de planta (textos, cotas, etiquetas, líneas de detalle, regiones, DWG de vista...).</summary>
        public readonly List<ElementId> Annotations = new List<ElementId>();

        /// <summary>Vistas de sección y llamadas de detalle cuya marca vive en una planta.</summary>
        public readonly List<ElementId> SectionViews = new List<ElementId>();

        /// <summary>Marcas de alzado.</summary>
        public readonly List<ElementId> ElevationMarkers = new List<ElementId>();

        /// <summary>Habitaciones, espacios y áreas: se trasladan por su punto de ubicación.</summary>
        public readonly List<ElementId> Spatial = new List<ElementId>();

        /// <summary>Elementos que estaban anclados y hay que volver a anclar.</summary>
        public readonly List<ElementId> Pinned = new List<ElementId>();

        /// <summary>Vistas de planta a procesar (recorte).</summary>
        public readonly List<ElementId> PlanViews = new List<ElementId>();

        /// <summary>Vistas 3D a procesar (caja de sección y cámara).</summary>
        public readonly List<ElementId> Views3D = new List<ElementId>();

        public int TotalToRotate => Model.Count + Annotations.Count + SectionViews.Count + ElevationMarkers.Count + Spatial.Count;

        public IEnumerable<ElementId> AllRotatable()
        {
            foreach (var id in Model) yield return id;
            foreach (var id in Annotations) yield return id;
            foreach (var id in SectionViews) yield return id;
            foreach (var id in ElevationMarkers) yield return id;
            foreach (var id in Spatial) yield return id;
        }
    }

    /// <summary>
    /// Decide qué elementos hay que girar explícitamente y cuáles se mueven solos con su anfitrión.
    /// Girar dos veces un elemento anfitrionado (por ejemplo una puerta y también su muro) es una de
    /// las causas clásicas de que "cosas desaparezcan" al rotar el norte; por eso se filtra con cuidado.
    /// </summary>
    internal class CandidateCollector
    {
        private readonly Document _doc;
        private readonly RotationOptions _options;
        private readonly RotationReport _report;

        /// <summary>Categorías sin geometría de modelo o que gestiona el propio add-in por otra vía: se omiten sin listarlas.</summary>
        private static readonly HashSet<string> SilentCategoryNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "OST_Levels", "OST_ProjectBasePoint", "OST_SharedBasePoint", "OST_InternalOrigin", "OST_IOS_GeoSite",
            "OST_Views", "OST_Sheets", "OST_Cameras", "OST_Viewports", "OST_Schedules", "OST_ProjectInformation",
            "OST_Materials", "OST_SketchLines", "OST_Phases", "OST_SunPath", "OST_ColorFillLegends",
            "OST_Legends", "OST_LegendComponents", "OST_LightingFixtureSource",
            // Las cajas de sección de las vistas 3D se giran a través de View3D.SetSectionBox, nunca como elemento
            "OST_SectionBox",
        };

        private static readonly HashSet<string> ExcludedCategoryNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "OST_Constraints",
            // Piezas de muro cortina: se mueven con el muro / sistema
            "OST_CurtainWallPanels", "OST_CurtainWallMullions", "OST_CurtainGrids", "OST_CurtainGridsWall",
            "OST_CurtainGridsRoof", "OST_CurtainGridsSystem", "OST_CurtainGridsCurtaSystem",
            // Componentes de escaleras y barandillas: se mueven con la escalera / barandilla
            "OST_StairsRuns", "OST_StairsLandings", "OST_StairsSupports", "OST_StairsStringerCarriage",
            "OST_StairsCutMarks", "OST_StairsTrisers", "OST_StairsSketchRunLines", "OST_StairsSketchLandingCenterLines",
            "OST_RailingTopRail", "OST_RailingHandRail", "OST_RailingSupport", "OST_RailingTermination",
            "OST_RailingRail", "OST_RailingBalusterRail",
            // Barridos anfitrionados
            "OST_Fascia", "OST_Gutter", "OST_EdgeSlab", "OST_Cornices", "OST_Reveals",
            // Armaduras y aislamientos: van con su anfitrión
            "OST_Rebar", "OST_AreaRein", "OST_PathRein", "OST_FabricAreas", "OST_FabricReinforcement",
            "OST_Coupler", "OST_DuctInsulations", "OST_DuctLinings", "OST_PipeInsulations",
            // Otros anfitrionados / derivados
            "OST_BuildingPad", "OST_MassFloor", "OST_Parts", "OST_DividedSurface", "OST_DividedPath",
            "OST_Assemblies", "OST_HostFinishes", "OST_ToposolidSubdivision", "OST_ToposolidSubdivisions",
        };

        public CandidateCollector(Document doc, RotationOptions options, RotationReport report)
        {
            _doc = doc;
            _options = options;
            _report = report;
        }

        public CandidateSet Collect()
        {
            var set = new CandidateSet();
            var excludedIds = CollectHostedIds();

            CollectModelElements(set, excludedIds);
            CollectViews(set);

            return set;
        }

        // ------------------------------------------------------------------ modelo

        /// <summary>Ids de paneles y montantes de muros cortina y de vigas de sistemas de vigas: se mueven con su anfitrión.</summary>
        private HashSet<long> CollectHostedIds()
        {
            var ids = new HashSet<long>();

            void AddGrid(CurtainGrid grid)
            {
                if (grid == null) return;
                try { foreach (var id in grid.GetPanelIds()) ids.Add(Compat.IdValue(id)); } catch { }
                try { foreach (var id in grid.GetMullionIds()) ids.Add(Compat.IdValue(id)); } catch { }
            }

            foreach (Wall wall in new FilteredElementCollector(_doc).OfClass(typeof(Wall)))
            {
                try { AddGrid(wall.CurtainGrid); } catch { }
            }
            foreach (CurtainSystem cs in new FilteredElementCollector(_doc).OfClass(typeof(CurtainSystem)))
            {
                try { foreach (CurtainGrid g in cs.CurtainGrids) AddGrid(g); } catch { }
            }
            foreach (RoofBase roof in new FilteredElementCollector(_doc).OfClass(typeof(RoofBase)))
            {
                try
                {
                    CurtainGridSet grids = (roof as FootPrintRoof)?.CurtainGrids ?? (roof as ExtrusionRoof)?.CurtainGrids;
                    if (grids != null) foreach (CurtainGrid g in grids) AddGrid(g);
                }
                catch { }
            }
            foreach (BeamSystem bs in new FilteredElementCollector(_doc).OfClass(typeof(BeamSystem)))
            {
                try { foreach (var id in bs.GetBeamIds()) ids.Add(Compat.IdValue(id)); } catch { }
            }

            return ids;
        }

        private void CollectModelElements(CandidateSet set, HashSet<long> hostedIds)
        {
            var collector = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent();

            foreach (Element e in collector)
            {
                string reason = WhySkipModelElement(e, hostedIds);
                if (reason != null)
                {
                    if (reason != IgnoreSilently) _report.Skip(Compat.IdValue(e.Id), Compat.Describe(e), reason);
                    continue;
                }

                if (e.Pinned)
                {
                    if (!_options.TemporarilyUnpin)
                    {
                        _report.Skip(Compat.IdValue(e.Id), Compat.Describe(e), "Elemento anclado (opción de desanclar desactivada)");
                        continue;
                    }
                    set.Pinned.Add(e.Id);
                }

                if (e is SpatialElement)
                    set.Spatial.Add(e.Id);
                else
                    set.Model.Add(e.Id);
            }
        }

        private const string IgnoreSilently = "\0";

        /// <summary>Devuelve null si el elemento debe girarse; si no, la razón para omitirlo.</summary>
        private string WhySkipModelElement(Element e, HashSet<long> hostedIds)
        {
            if (e == null) return IgnoreSilently;
            if (e is ElementType) return IgnoreSilently;

            // Todo lo que pertenece a una vista (componentes de leyenda, detalles...) no es geometría de modelo.
            try
            {
                if (e.ViewSpecific) return IgnoreSilently;
                if (e.OwnerViewId != null && e.OwnerViewId != ElementId.InvalidElementId) return IgnoreSilently;
            }
            catch { }

            // Clases que no se giran nunca
            if (e is View || e is ViewSheet || e is Viewport) return IgnoreSilently;
            if (e is BasePoint) return IgnoreSilently;
            string typeName = e.GetType().Name;
            if (typeName == "InternalOrigin" || typeName == "SectionBox" || typeName == "LegendComponent") return IgnoreSilently;
            if (e is Level) return IgnoreSilently;
            if (e is Sketch || e is SketchPlane) return IgnoreSilently;
            if (e is MEPSystem) return IgnoreSilently;
            if (e is Zone) return IgnoreSilently;
            if (e is ProjectInfo || e is Phase || e is DesignOption) return IgnoreSilently;
            if (e is AssemblyInstance) return "Ensamblaje: sigue a sus miembros";
            if (e is ElevationMarker) return IgnoreSilently;     // se recogen aparte
            if (e is WallFoundation) return "Cimentación de muro: se mueve con el muro";
            if (e is Autodesk.Revit.DB.Structure.StructuralConnectionHandler) return "Conexión de acero nativa: se mueve con la estructura";
            if (e is HostedSweep) return "Barrido anfitrionado: se mueve con su anfitrión";
            if (e is ColorFillLegend) return IgnoreSilently;

            Category cat = null;
            try { cat = e.Category; } catch { }
            if (cat == null) return IgnoreSilently;

            string bic = BuiltInName(cat);
            if (bic != null)
            {
                if (SilentCategoryNames.Contains(bic)) return IgnoreSilently;
                if (bic.StartsWith("OST_IOS", StringComparison.Ordinal)
                    && bic != "OST_IOSModelGroups" && bic != "OST_IOSAttachedDetailGroups")
                    return IgnoreSilently;

                // Las exclusiones por categoría solo valen para elementos de sistema (barridos, tramos, armaduras,
                // conexiones de acero nativas...). Una familia cargable de esas mismas categorías (por ejemplo una
                // conexión "BIMS" en Structural Connections) es independiente y se decide por su anfitrión, más abajo.
                if (!(e is FamilyInstance))
                {
                    if (ExcludedCategoryNames.Contains(bic)) return $"Categoría {cat.Name}: se mueve con su anfitrión";
                    if (bic.StartsWith("OST_StructConnection", StringComparison.Ordinal))
                        return "Conexión de acero nativa: se mueve con la estructura";
                    if (bic.IndexOf("Analytical", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Modelo analítico: lo recalcula Revit";
                    if (bic.StartsWith("OST_Rebar", StringComparison.Ordinal))
                        return "Armadura: se mueve con su anfitrión";
                }
            }

            if (hostedIds.Contains(Compat.IdValue(e.Id)))
                return "Panel/montante de muro cortina o viga de sistema: se mueve con su anfitrión";

            if (e.GroupId != null && e.GroupId != ElementId.InvalidElementId)
                return "Miembro de grupo: gira con el grupo";

            if (e is FamilyInstance fi)
            {
                try
                {
                    if (fi.SuperComponent != null) return "Familia anidada: se mueve con su familia principal";
                    Element host = fi.Host;
                    if (host != null && !(host is Level) && !(host is ReferencePlane) && !(host is Grid))
                        return $"Familia anfitrionada en {Compat.CategoryName(host)} [{Compat.IdValue(host.Id)}]: se mueve con su anfitrión";
                }
                catch { }
            }

            if (e is Wall w)
            {
                try { if (w.IsStackedWallMember) return "Miembro de muro apilado: se mueve con el muro apilado"; } catch { }
            }

            if (e is Opening op)
            {
                try { if (op.Host != null) return "Hueco anfitrionado: se mueve con su anfitrión"; } catch { }
            }

            if (e is Railing r)
            {
                try { if (r.HasHost) return "Barandilla anfitrionada: se mueve con la escalera/rampa"; } catch { }
            }

            if (e is TopographySurface ts)
            {
                try { if (ts.IsSiteSubRegion) return "Subregión de topografía: se mueve con la superficie"; } catch { }
            }

            if (cat.Name != null && cat.Name.IndexOf("Subdivisi", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Subdivisión: se mueve con su anfitrión";

            // Debe tener algo de geometría
            BoundingBoxXYZ bb = null;
            try { bb = e.get_BoundingBox(null); } catch { }
            if (bb == null && e.Location == null) return IgnoreSilently;

            return null;
        }

        private static string BuiltInName(Category cat)
        {
            try
            {
                long v = Compat.IdValue(cat.Id);
                if (v >= 0) return null;                 // categoría de usuario
                var bic = (BuiltInCategory)v;
                return Enum.IsDefined(typeof(BuiltInCategory), bic) ? bic.ToString() : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ vistas

        private void CollectViews(CandidateSet set)
        {
            // Vistas de planta
            foreach (ViewPlan vp in new FilteredElementCollector(_doc).OfClass(typeof(ViewPlan)))
            {
                if (vp.IsTemplate) continue;
                if (!IsPlanType(vp.ViewType)) continue;
                set.PlanViews.Add(vp.Id);

                if (_options.RotatePlanAnnotations)
                    CollectAnnotations(vp, set);
            }

            // Secciones y llamadas de detalle
            if (_options.RotateSectionsAndElevations)
            {
                foreach (ViewSection vs in new FilteredElementCollector(_doc).OfClass(typeof(ViewSection)))
                {
                    if (vs.IsTemplate) continue;
                    if (vs.ViewType == ViewType.Elevation) continue;            // van con la marca de alzado

                    try
                    {
                        if (vs.GetPrimaryViewId() != ElementId.InvalidElementId) continue;   // vista dependiente
                        ElementId parent = vs.GetCalloutParentId();
                        if (parent != ElementId.InvalidElementId && !(_doc.GetElement(parent) is ViewPlan))
                            continue;                                                        // llamada dentro de una sección: se mueve con ella
                    }
                    catch { }

                    if (vs.Pinned)
                    {
                        if (!_options.TemporarilyUnpin)
                        {
                            _report.Skip(Compat.IdValue(vs.Id), Compat.Describe(vs), "Sección anclada (opción de desanclar desactivada)");
                            continue;
                        }
                        set.Pinned.Add(vs.Id);
                    }
                    set.SectionViews.Add(vs.Id);
                }

                foreach (ElevationMarker em in new FilteredElementCollector(_doc).OfClass(typeof(ElevationMarker)))
                {
                    if (em.Pinned)
                    {
                        if (!_options.TemporarilyUnpin)
                        {
                            _report.Skip(Compat.IdValue(em.Id), Compat.Describe(em), "Marca de alzado anclada (opción de desanclar desactivada)");
                            continue;
                        }
                        set.Pinned.Add(em.Id);
                    }
                    set.ElevationMarkers.Add(em.Id);
                }
            }

            // Vistas 3D
            if (_options.Rotate3DViews)
            {
                foreach (View3D v3 in new FilteredElementCollector(_doc).OfClass(typeof(View3D)))
                {
                    if (v3.IsTemplate) continue;
                    set.Views3D.Add(v3.Id);
                }
            }
        }

        public static bool IsPlanType(ViewType vt) =>
            vt == ViewType.FloorPlan || vt == ViewType.CeilingPlan ||
            vt == ViewType.EngineeringPlan || vt == ViewType.AreaPlan;

        private void CollectAnnotations(View view, CandidateSet set)
        {
            var collector = new FilteredElementCollector(_doc)
                .OwnedByView(view.Id)
                .WhereElementIsNotElementType();

            foreach (Element e in collector)
            {
                if (e.OwnerViewId != view.Id) continue;
                if (e is View || e is Sketch || e is SketchPlane || e is ColorFillLegend || e is SpatialElement) continue;
                if (e is ElementType) continue;
                if (e.GroupId != null && e.GroupId != ElementId.InvalidElementId) continue;   // miembro de grupo de detalle

                Category cat = null;
                try { cat = e.Category; } catch { }
                if (cat == null) continue;
                string bic = BuiltInName(cat);
                if (bic == "OST_SketchLines" || bic == "OST_Cameras" || bic == "OST_Viewports") continue;
                if (bic != null && bic.StartsWith("OST_IOS", StringComparison.Ordinal) && bic != "OST_IOSAttachedDetailGroups") continue;

                if (e is FamilyInstance fi)
                {
                    try { if (fi.SuperComponent != null) continue; } catch { }
                }

                BoundingBoxXYZ bb = null;
                try { bb = e.get_BoundingBox(view); } catch { }
                if (bb == null && e.Location == null) continue;

                if (e.Pinned)
                {
                    if (!_options.TemporarilyUnpin)
                    {
                        _report.Skip(Compat.IdValue(e.Id), Compat.Describe(e), "Anotación anclada (opción de desanclar desactivada)");
                        continue;
                    }
                    set.Pinned.Add(e.Id);
                }

                set.Annotations.Add(e.Id);
            }
        }
    }
}
