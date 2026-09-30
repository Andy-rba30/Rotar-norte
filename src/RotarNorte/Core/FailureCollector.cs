using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RotarNorte.Core
{
    /// <summary>
    /// Preprocesador de fallos de Revit:
    ///  - Los avisos (warnings) se eliminan para que no aparezcan cuadros de diálogo.
    ///  - Los errores cancelan la transacción (o, si el usuario lo pidió, se intenta la resolución
    ///    automática que propone Revit).
    /// Todos los mensajes quedan registrados para el informe.
    /// </summary>
    internal class FailureCollector : IFailuresPreprocessor
    {
        private readonly bool _autoResolve;

        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public readonly HashSet<long> FailingElementIds = new HashSet<long>();

        public FailureCollector(bool autoResolveErrors)
        {
            _autoResolve = autoResolveErrors;
        }

        public void Reset()
        {
            Warnings.Clear();
            Errors.Clear();
            FailingElementIds.Clear();
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool rollback = false;

            foreach (FailureMessageAccessor f in accessor.GetFailureMessages())
            {
                string text;
                try { text = f.GetDescriptionText(); } catch { text = "(sin descripción)"; }

                FailureSeverity severity = f.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    Warnings.Add(text);
                    try { accessor.DeleteWarning(f); } catch { /* ignorar */ }
                    continue;
                }

                // Error o DocumentCorruption
                Errors.Add(text);
                try
                {
                    foreach (ElementId id in f.GetFailingElementIds())
                        FailingElementIds.Add(Compat.IdValue(id));
                }
                catch { /* ignorar */ }

                if (_autoResolve && severity == FailureSeverity.Error && f.HasResolutions())
                {
                    try
                    {
                        accessor.ResolveFailure(f);
                        continue;
                    }
                    catch { /* cae en rollback */ }
                }

                rollback = true;
            }

            return rollback ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }

        /// <summary>Aplica este preprocesador a una transacción.</summary>
        public void Attach(Transaction t)
        {
            FailureHandlingOptions opts = t.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(this);
            opts.SetClearAfterRollback(true);
            opts.SetForcedModalHandling(true);
            t.SetFailureHandlingOptions(opts);
        }
    }
}
