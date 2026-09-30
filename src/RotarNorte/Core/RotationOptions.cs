using Autodesk.Revit.DB;

namespace RotarNorte.Core
{
    /// <summary>Punto alrededor del cual se gira el modelo.</summary>
    public enum RotationCenterMode
    {
        ProjectBasePoint,
        InternalOrigin,
        SurveyPoint,
        Custom
    }

    /// <summary>Qué hacer con las vistas de planta cuyo norte es el Norte de Proyecto.</summary>
    public enum PlanViewMode
    {
        /// <summary>
        /// La región de recorte gira junto con el modelo: cada vista y cada plano se ven
        /// exactamente igual que antes. Es la opción recomendada.
        /// </summary>
        KeepAppearance,

        /// <summary>
        /// La vista conserva su orientación en pantalla y la región de recorte se reajusta
        /// para seguir abarcando el mismo contenido: el modelo se ve girado en las vistas.
        /// </summary>
        ShowNewOrientation
    }

    /// <summary>Opciones elegidas por el usuario para la rotación segura.</summary>
    public class RotationOptions
    {
        /// <summary>Ángulo de giro en grados. Positivo = antihorario (visto en planta).</summary>
        public double AngleDegrees { get; set; }

        public RotationCenterMode CenterMode { get; set; } = RotationCenterMode.ProjectBasePoint;

        /// <summary>Punto de giro cuando CenterMode == Custom (coordenadas internas).</summary>
        public XYZ CustomCenter { get; set; }

        public PlanViewMode PlanViews { get; set; } = PlanViewMode.KeepAppearance;

        /// <summary>Ajusta el ángulo a Norte Verdadero para que las coordenadas compartidas no cambien.</summary>
        public bool PreserveTrueNorth { get; set; } = true;

        /// <summary>Desancla temporalmente los elementos anclados (vínculos, DWG, rejillas...) y los vuelve a anclar.</summary>
        public bool TemporarilyUnpin { get; set; } = true;

        /// <summary>Gira las anotaciones (textos, cotas, etiquetas, líneas de detalle...) de las vistas de planta.</summary>
        public bool RotatePlanAnnotations { get; set; } = true;

        /// <summary>Gira las marcas de sección y de alzado.</summary>
        public bool RotateSectionsAndElevations { get; set; } = true;

        /// <summary>Gira las cajas de sección y las cámaras de las vistas 3D.</summary>
        public bool Rotate3DViews { get; set; } = true;

        /// <summary>Si es true, se hace todo el trabajo y luego se deshace: sirve para auditar sin tocar el modelo.</summary>
        public bool DryRun { get; set; }

        /// <summary>
        /// Si es true, ante un error de Revit se intenta aplicar la resolución automática que propone Revit.
        /// Si es false (recomendado), cualquier error cancela toda la operación y el modelo queda intacto.
        /// </summary>
        public bool AutoResolveErrors { get; set; }

        /// <summary>Máximo de elementos problemáticos que se intentan aislar por bisección antes de desistir.</summary>
        public int MaxIsolatedFailures { get; set; } = 25;

        public double AngleRadians => Compat.DegToRad(AngleDegrees);
    }
}
