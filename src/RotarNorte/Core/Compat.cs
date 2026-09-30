using System;
using Autodesk.Revit.DB;

namespace RotarNorte.Core
{
    /// <summary>
    /// Pequeñas diferencias entre versiones del API de Revit.
    /// </summary>
    internal static class Compat
    {
        /// <summary>Valor numérico de un ElementId (IntegerValue hasta 2023, Value desde 2024).</summary>
        public static long IdValue(ElementId id)
        {
            if (id == null) return -1;
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        /// <summary>Crea un ElementId a partir de un entero.</summary>
        public static ElementId MakeId(long value)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }

        /// <summary>Grados a radianes.</summary>
        public static double DegToRad(double deg) => deg * Math.PI / 180.0;

        /// <summary>Radianes a grados.</summary>
        public static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

        /// <summary>Normaliza un ángulo en grados al rango (-180, 180].</summary>
        public static double NormalizeDeg(double deg)
        {
            while (deg <= -180.0) deg += 360.0;
            while (deg > 180.0) deg -= 360.0;
            return deg;
        }

        /// <summary>Nombre legible de la categoría de un elemento.</summary>
        public static string CategoryName(Element e)
        {
            try { return e?.Category?.Name ?? "(sin categoría)"; }
            catch { return "(sin categoría)"; }
        }

        /// <summary>Descripción corta de un elemento para los informes.</summary>
        public static string Describe(Element e)
        {
            if (e == null) return "(nulo)";
            string name;
            try { name = e.Name; } catch { name = ""; }
            return $"[{IdValue(e.Id)}] {CategoryName(e)}: {name}";
        }
    }
}
