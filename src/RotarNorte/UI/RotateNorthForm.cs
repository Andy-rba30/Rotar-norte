using System;
using System.Drawing;
using System.Windows.Forms;
using XYZ = Autodesk.Revit.DB.XYZ;
using RotarNorte.Core;

namespace RotarNorte.UI
{
    /// <summary>Qué pide el formulario al comando cuando se cierra con DialogResult.Retry.</summary>
    internal enum PickRequest
    {
        None,
        MeasureAngle,
        PickCenter
    }

    /// <summary>Diálogo principal: ángulo, centro de giro y opciones de la rotación segura.</summary>
    internal class RotateNorthForm : System.Windows.Forms.Form
    {
        private readonly NumericUpDown _angle;
        private readonly RadioButton _centerPbp, _centerOrigin, _centerSurvey, _centerCustom;
        private readonly Label _customCenterLabel;
        private readonly RadioButton _planKeep, _planShow;
        private readonly CheckBox _preserveTrueNorth, _unpin, _annotations, _sections, _views3d, _recreate, _dryRun;
        private readonly RadioButton _errorsCancel, _errorsResolve;
        private readonly Label _preview;

        public PickRequest Pick { get; private set; } = PickRequest.None;
        public XYZ CustomCenter { get; set; }
        public bool AlignToEast { get; private set; }

        public RotateNorthForm(string previewText)
        {
            Text = "Rotar Norte de Proyecto (seguro)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(600, 700);
            Font = new Font("Segoe UI", 9f);

            int y = 12;

            // ---- Ángulo
            var gAngle = new GroupBox { Text = "Ángulo de giro", Location = new Point(12, y), Size = new Size(576, 92) };
            var lblAngle = new Label { Location = new Point(12, 26), AutoSize = true, Text = "Grados (positivo = antihorario visto en planta):" };
            _angle = new NumericUpDown
            {
                Location = new Point(12, 48), Width = 140, DecimalPlaces = 4,
                Minimum = -360, Maximum = 360, Increment = 1, Value = 0
            };
            var measure = new Button { Text = "Medir con 2 puntos...", Location = new Point(170, 46), Size = new Size(170, 27) };
            measure.Click += (s, e) => { AlignToEast = false; Pick = PickRequest.MeasureAngle; DialogResult = DialogResult.Retry; Close(); };
            var measureEast = new Button { Text = "Alinear con eje horizontal...", Location = new Point(350, 46), Size = new Size(200, 27) };
            measureEast.Click += (s, e) => { AlignToEast = true; Pick = PickRequest.MeasureAngle; DialogResult = DialogResult.Retry; Close(); };
            var tip = new ToolTip();
            tip.SetToolTip(measure, "Elija dos puntos en una planta: se calcula el giro necesario para que esa dirección apunte al Norte de Proyecto (arriba).");
            tip.SetToolTip(measureEast, "Elija dos puntos en una planta: se calcula el giro necesario para que esa dirección quede horizontal (Este-Oeste).");
            gAngle.Controls.AddRange(new Control[] { lblAngle, _angle, measure, measureEast });
            y += 100;

            // ---- Centro
            var gCenter = new GroupBox { Text = "Centro de giro", Location = new Point(12, y), Size = new Size(576, 82) };
            _centerPbp = new RadioButton { Text = "Punto base del proyecto (recomendado)", Location = new Point(12, 22), AutoSize = true, Checked = true };
            _centerOrigin = new RadioButton { Text = "Origen interno", Location = new Point(300, 22), AutoSize = true };
            _centerSurvey = new RadioButton { Text = "Punto de levantamiento", Location = new Point(12, 48), AutoSize = true };
            _centerCustom = new RadioButton { Text = "Punto elegido:", Location = new Point(300, 48), AutoSize = true };
            var pickCenter = new Button { Text = "Elegir...", Location = new Point(400, 44), Size = new Size(70, 25) };
            pickCenter.Click += (s, e) => { Pick = PickRequest.PickCenter; DialogResult = DialogResult.Retry; Close(); };
            _customCenterLabel = new Label { Location = new Point(476, 49), AutoSize = true, Text = "(ninguno)" };
            gCenter.Controls.AddRange(new Control[] { _centerPbp, _centerOrigin, _centerSurvey, _centerCustom, pickCenter, _customCenterLabel });
            y += 90;

            // ---- Vistas de planta
            var gPlan = new GroupBox { Text = "Vistas de planta orientadas a Norte de Proyecto", Location = new Point(12, y), Size = new Size(576, 118) };
            _planKeep = new RadioButton
            {
                Text = "Conservar el aspecto actual de todas las vistas y planos (recomendado)",
                Location = new Point(12, 22), AutoSize = true, Checked = true
            };
            var planKeepInfo = new Label
            {
                Location = new Point(30, 42), Size = new Size(530, 30), ForeColor = SystemColors.GrayText,
                Text = "La región de recorte gira con el modelo: cada vista y cada plano se ven exactamente igual que antes. " +
                       "Luego puede enderezar las vistas que quiera con el botón \"Enderezar vista\"."
            };
            _planShow = new RadioButton
            {
                Text = "Mostrar la nueva orientación (la vista no gira; el recorte se reajusta para abarcar lo mismo)",
                Location = new Point(12, 74), AutoSize = true
            };
            var planShowInfo = new Label
            {
                Location = new Point(30, 94), Size = new Size(530, 18), ForeColor = SystemColors.GrayText,
                Text = "El modelo y las anotaciones aparecen girados en las vistas; las vistas a Norte Verdadero no cambian."
            };
            gPlan.Controls.AddRange(new Control[] { _planKeep, planKeepInfo, _planShow, planShowInfo });
            y += 126;

            // ---- Opciones
            var gOpt = new GroupBox { Text = "Opciones", Location = new Point(12, y), Size = new Size(576, 174) };
            _preserveTrueNorth = new CheckBox { Text = "Mantener el Norte Verdadero y las coordenadas compartidas (ajusta el ángulo a Norte Verdadero)", Location = new Point(12, 22), AutoSize = true, Checked = true };
            _unpin = new CheckBox { Text = "Desanclar temporalmente los elementos anclados (vínculos, DWG, rejillas...) y volver a anclarlos", Location = new Point(12, 46), AutoSize = true, Checked = true };
            _annotations = new CheckBox { Text = "Girar las anotaciones de las vistas de planta (textos, cotas, etiquetas, líneas de detalle, DWG de vista)", Location = new Point(12, 70), AutoSize = true, Checked = true };
            _sections = new CheckBox { Text = "Girar marcas de sección, llamadas de detalle y marcas de alzado", Location = new Point(12, 94), AutoSize = true, Checked = true };
            _views3d = new CheckBox { Text = "Girar cajas de sección y cámaras de las vistas 3D", Location = new Point(12, 118), AutoSize = true, Checked = true };
            _recreate = new CheckBox { Text = "Recrear giradas (con nuevo Id) las familias que Revit no permite girar: basadas en cara o plano vertical", Location = new Point(12, 142), AutoSize = true, Checked = true };
            gOpt.Controls.AddRange(new Control[] { _preserveTrueNorth, _unpin, _annotations, _sections, _views3d, _recreate });
            y += 182;

            // ---- Seguridad
            var gSafe = new GroupBox { Text = "Seguridad", Location = new Point(12, y), Size = new Size(576, 100) };
            _dryRun = new CheckBox { Text = "Solo simular: hacer todo el trabajo, generar el informe y deshacerlo (el modelo no cambia)", Location = new Point(12, 22), AutoSize = true };
            var lblErr = new Label { Location = new Point(12, 48), AutoSize = true, Text = "Si Revit informa de errores durante la operación:" };
            _errorsCancel = new RadioButton { Text = "Cancelar todo y dejar el modelo intacto (recomendado)", Location = new Point(30, 68), AutoSize = true, Checked = true };
            _errorsResolve = new RadioButton { Text = "Aplicar la resolución automática de Revit", Location = new Point(330, 68), AutoSize = true };
            gSafe.Controls.AddRange(new Control[] { _dryRun, lblErr, _errorsCancel, _errorsResolve });
            y += 108;

            // ---- Vista previa
            _preview = new Label
            {
                Location = new Point(12, y), Size = new Size(576, 40), ForeColor = SystemColors.GrayText,
                Text = previewText
            };
            y += 44;

            var run = new Button { Text = "Ejecutar", Location = new Point(388, y), Size = new Size(100, 30), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Location = new Point(496, y), Size = new Size(92, 30), DialogResult = DialogResult.Cancel };
            run.Click += (s, e) =>
            {
                if (_angle.Value == 0)
                {
                    MessageBox.Show(this, "Indique un ángulo distinto de cero.", "Rotar Norte", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                    return;
                }
                if (_centerCustom.Checked && CustomCenter == null)
                {
                    MessageBox.Show(this, "Elija el punto de giro o seleccione otro centro.", "Rotar Norte", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                    return;
                }
                Pick = PickRequest.None;
            };
            Controls.AddRange(new Control[] { gAngle, gCenter, gPlan, gOpt, gSafe, _preview, run, cancel });
            ClientSize = new Size(600, y + 42);
            AcceptButton = run;
            CancelButton = cancel;
        }

        public double AngleDegrees
        {
            get => (double)_angle.Value;
            set => _angle.Value = (decimal)Math.Max(-360, Math.Min(360, Compat.NormalizeDeg(value)));
        }

        public void SetCustomCenter(XYZ p)
        {
            CustomCenter = p;
            _centerCustom.Checked = true;
            _customCenterLabel.Text = p == null ? "(ninguno)" : $"X={p.X * 0.3048:0.###} m, Y={p.Y * 0.3048:0.###} m";
        }

        public void ApplyTo(RotationOptions o)
        {
            o.AngleDegrees = AngleDegrees;
            o.CenterMode = _centerOrigin.Checked ? RotationCenterMode.InternalOrigin
                         : _centerSurvey.Checked ? RotationCenterMode.SurveyPoint
                         : _centerCustom.Checked ? RotationCenterMode.Custom
                         : RotationCenterMode.ProjectBasePoint;
            o.CustomCenter = CustomCenter;
            o.PlanViews = _planShow.Checked ? PlanViewMode.ShowNewOrientation : PlanViewMode.KeepAppearance;
            o.PreserveTrueNorth = _preserveTrueNorth.Checked;
            o.TemporarilyUnpin = _unpin.Checked;
            o.RotatePlanAnnotations = _annotations.Checked;
            o.RotateSectionsAndElevations = _sections.Checked;
            o.Rotate3DViews = _views3d.Checked;
            o.RecreateUnrotatableFamilies = _recreate.Checked;
            o.DryRun = _dryRun.Checked;
            o.AutoResolveErrors = _errorsResolve.Checked;
        }

        public void LoadFrom(RotationOptions o)
        {
            AngleDegrees = o.AngleDegrees;
            _centerPbp.Checked = o.CenterMode == RotationCenterMode.ProjectBasePoint;
            _centerOrigin.Checked = o.CenterMode == RotationCenterMode.InternalOrigin;
            _centerSurvey.Checked = o.CenterMode == RotationCenterMode.SurveyPoint;
            if (o.CenterMode == RotationCenterMode.Custom) SetCustomCenter(o.CustomCenter);
            _planKeep.Checked = o.PlanViews == PlanViewMode.KeepAppearance;
            _planShow.Checked = !_planKeep.Checked;
            _preserveTrueNorth.Checked = o.PreserveTrueNorth;
            _unpin.Checked = o.TemporarilyUnpin;
            _annotations.Checked = o.RotatePlanAnnotations;
            _sections.Checked = o.RotateSectionsAndElevations;
            _views3d.Checked = o.Rotate3DViews;
            _recreate.Checked = o.RecreateUnrotatableFamilies;
            _dryRun.Checked = o.DryRun;
            _errorsCancel.Checked = !o.AutoResolveErrors;
            _errorsResolve.Checked = o.AutoResolveErrors;
        }
    }
}
