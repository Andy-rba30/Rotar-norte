using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RotarNorte
{
    /// <summary>Crea la pestaña "Rotar Norte" en la cinta de opciones.</summary>
    public class App : IExternalApplication
    {
        private const string TabName = "Rotar Norte";

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); } catch { /* ya existe */ }

            RibbonPanel panel = null;
            foreach (RibbonPanel p in app.GetRibbonPanels(TabName))
                if (p.Name == "Norte") { panel = p; break; }
            if (panel == null) panel = app.CreateRibbonPanel(TabName, "Norte");

            string asm = Assembly.GetExecutingAssembly().Location;

            var main = new PushButtonData("RotarNorteProyecto", "Rotar Norte\nde Proyecto", asm,
                "RotarNorte.Commands.RotarNorteProyectoCommand")
            {
                ToolTip = "Gira el Norte de Proyecto de forma segura.",
                LongDescription = "Gira el modelo, las anotaciones de planta, secciones, alzados, cajas de referencia, vínculos y DWG en una sola " +
                                  "operación, ajusta las regiones de recorte, cajas de sección y cámaras 3D, conserva las coordenadas compartidas " +
                                  "y vuelve a anclar lo que estaba anclado. Incluye modo de simulación con informe.",
                LargeImage = LoadImage("norte32.png"),
                Image = LoadImage("norte16.png")
            };

            var trueNorth = new PushButtonData("RotarNorteVerdadero", "Rotar Norte\nVerdadero", asm,
                "RotarNorte.Commands.RotarNorteVerdaderoCommand")
            {
                ToolTip = "Cambia solo el ángulo a Norte Verdadero, sin mover nada.",
                LongDescription = "Alternativa segura cuando se quiere corregir la orientación real del edificio: no se mueve ningún elemento ni vista.",
                LargeImage = LoadImage("verdadero32.png"),
                Image = LoadImage("verdadero16.png")
            };

            var straighten = new PushButtonData("EnderezarVista", "Enderezar\nvista", asm,
                "RotarNorte.Commands.EnderezarVistaCommand")
            {
                ToolTip = "Quita la rotación de la región de recorte de la vista de planta activa (o de las seleccionadas), conservando el contenido.",
                LargeImage = LoadImage("enderezar32.png"),
                Image = LoadImage("enderezar16.png")
            };

            panel.AddItem(main);
            panel.AddItem(trueNorth);
            panel.AddItem(straighten);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        private static BitmapImage LoadImage(string name)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("RotarNorte.Resources." + name))
                {
                    if (s == null) return null;
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = s;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
