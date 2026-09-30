using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RotarNorte.Core
{
    /// <summary>Un elemento que no se pudo girar, con la razón.</summary>
    public class ReportItem
    {
        public long Id { get; set; }
        public string Description { get; set; }
        public string Reason { get; set; }
        public override string ToString() => $"{Description} -> {Reason}";
    }

    /// <summary>Resultado de una rotación (o de una simulación).</summary>
    public class RotationReport
    {
        public bool Success { get; set; }
        public bool DryRun { get; set; }
        public string AbortReason { get; set; }
        public double AngleDegrees { get; set; }
        public string CenterDescription { get; set; }
        public TimeSpan Elapsed { get; set; }

        public int ModelElementsRotated { get; set; }
        public int AnnotationsRotated { get; set; }
        public int PinnedRestored { get; set; }
        public int PlanViewsRotated { get; set; }
        public int PlanViewsRefit { get; set; }
        public int SectionsRotated { get; set; }
        public int ElevationMarkersRotated { get; set; }
        public int SectionBoxesRotated { get; set; }
        public int CamerasRotated { get; set; }
        public int ViewportsRecentered { get; set; }
        public bool UsedElementByElementFallback { get; set; }

        public string TrueNorthBefore { get; set; }
        public string TrueNorthAfter { get; set; }

        public readonly SortedDictionary<string, int> RotatedByCategory = new SortedDictionary<string, int>();
        public readonly List<ReportItem> Skipped = new List<ReportItem>();
        public readonly List<ReportItem> Failed = new List<ReportItem>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Notes = new List<string>();

        public void Skip(long id, string description, string reason) =>
            Skipped.Add(new ReportItem { Id = id, Description = description, Reason = reason });

        public void Fail(long id, string description, string reason) =>
            Failed.Add(new ReportItem { Id = id, Description = description, Reason = reason });

        public void CountCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) category = "(sin categoría)";
            RotatedByCategory.TryGetValue(category, out int n);
            RotatedByCategory[category] = n + 1;
        }

        public string Summary()
        {
            if (!Success)
                return DryRun
                    ? "SIMULACIÓN: la rotación NO se pudo completar. " + AbortReason
                    : "La rotación NO se realizó. El modelo no fue modificado. " + AbortReason;

            var sb = new StringBuilder();
            sb.Append(DryRun ? "SIMULACIÓN completada (el modelo NO fue modificado). " : "Rotación completada. ");
            sb.Append($"{ModelElementsRotated} elementos de modelo y {AnnotationsRotated} anotaciones giradas; ");
            sb.Append($"{PlanViewsRotated + PlanViewsRefit} vistas de planta, {SectionsRotated} secciones, ");
            sb.Append($"{ElevationMarkersRotated} marcas de alzado y {SectionBoxesRotated + CamerasRotated} vistas 3D ajustadas.");
            if (Failed.Count > 0) sb.Append($" {Failed.Count} elementos no se pudieron girar (ver informe).");
            return sb.ToString();
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("INFORME - ROTAR NORTE DE PROYECTO (SEGURO)");
            sb.AppendLine("==========================================");
            sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Modo: {(DryRun ? "SIMULACIÓN (sin cambios en el modelo)" : "EJECUCIÓN")}");
            sb.AppendLine($"Resultado: {(Success ? "OK" : "CANCELADO")}");
            if (!string.IsNullOrEmpty(AbortReason)) sb.AppendLine($"Motivo: {AbortReason}");
            sb.AppendLine($"Ángulo: {AngleDegrees:0.####}° (positivo = antihorario)");
            sb.AppendLine($"Centro de giro: {CenterDescription}");
            sb.AppendLine($"Duración: {Elapsed.TotalSeconds:0.0} s");
            sb.AppendLine();
            sb.AppendLine("RESUMEN");
            sb.AppendLine($"  Elementos de modelo girados: {ModelElementsRotated}");
            sb.AppendLine($"  Anotaciones de planta giradas: {AnnotationsRotated}");
            sb.AppendLine($"  Elementos desanclados y vueltos a anclar: {PinnedRestored}");
            sb.AppendLine($"  Vistas de planta (recorte girado con el modelo): {PlanViewsRotated}");
            sb.AppendLine($"  Vistas de planta (recorte reajustado): {PlanViewsRefit}");
            sb.AppendLine($"  Secciones/llamadas giradas: {SectionsRotated}");
            sb.AppendLine($"  Marcas de alzado giradas: {ElevationMarkersRotated}");
            sb.AppendLine($"  Cajas de sección 3D giradas: {SectionBoxesRotated}");
            sb.AppendLine($"  Cámaras 3D giradas: {CamerasRotated}");
            sb.AppendLine($"  Ventanas gráficas recentradas en planos: {ViewportsRecentered}");
            if (!string.IsNullOrEmpty(TrueNorthBefore))
                sb.AppendLine($"  Ángulo a Norte Verdadero: {TrueNorthBefore} -> {TrueNorthAfter}");
            if (UsedElementByElementFallback)
                sb.AppendLine("  * Se usó el modo de aislamiento de elementos problemáticos.");
            sb.AppendLine();

            if (RotatedByCategory.Count > 0)
            {
                sb.AppendLine("ELEMENTOS GIRADOS POR CATEGORÍA");
                foreach (var kv in RotatedByCategory.OrderByDescending(k => k.Value))
                    sb.AppendLine($"  {kv.Value,6}  {kv.Key}");
                sb.AppendLine();
            }

            if (Failed.Count > 0)
            {
                sb.AppendLine($"ELEMENTOS QUE NO SE PUDIERON GIRAR ({Failed.Count}) - revisar manualmente");
                foreach (var f in Failed) sb.AppendLine("  " + f);
                sb.AppendLine();
            }

            if (Warnings.Count > 0)
            {
                sb.AppendLine($"AVISOS DE REVIT ({Warnings.Count})");
                foreach (var w in Warnings.Distinct()) sb.AppendLine("  - " + w);
                sb.AppendLine();
            }

            if (Notes.Count > 0)
            {
                sb.AppendLine("NOTAS");
                foreach (var n in Notes) sb.AppendLine("  - " + n);
                sb.AppendLine();
            }

            if (Skipped.Count > 0)
            {
                sb.AppendLine($"ELEMENTOS OMITIDOS A PROPÓSITO ({Skipped.Count}) - se mueven con su anfitrión o no aplican");
                foreach (var g in Skipped.GroupBy(s => s.Reason).OrderByDescending(g => g.Count()))
                    sb.AppendLine($"  {g.Count(),6}  {g.Key}");
            }

            return sb.ToString();
        }
    }
}
