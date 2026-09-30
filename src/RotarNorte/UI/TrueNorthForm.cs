using System;
using System.Drawing;
using System.Windows.Forms;

namespace RotarNorte.UI
{
    /// <summary>Diálogo para cambiar el ángulo a Norte Verdadero (no mueve nada).</summary>
    internal class TrueNorthForm : Form
    {
        private readonly NumericUpDown _angle;

        public double NewAngleDegrees => (double)_angle.Value;

        public TrueNorthForm(double currentDegrees)
        {
            Text = "Rotar Norte Verdadero";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, 230);
            Font = new Font("Segoe UI", 9f);

            var info = new Label
            {
                Location = new Point(14, 12),
                Size = new Size(412, 96),
                Text = "Cambia únicamente el ángulo entre el Norte de Proyecto y el Norte Verdadero.\n" +
                       "Ningún elemento ni vista se mueve: solo cambian las vistas cuya orientación es " +
                       "\"Norte verdadero\", el recorrido solar y las coordenadas compartidas.\n\n" +
                       "Si lo que quiere es corregir la orientación real del edificio sin tocar la " +
                       "maquetación de planos, esta es la opción correcta."
            };

            var lbl = new Label { Location = new Point(14, 120), AutoSize = true, Text = "Ángulo a Norte Verdadero (grados, positivo = antihorario):" };
            _angle = new NumericUpDown
            {
                Location = new Point(14, 142),
                Width = 140,
                DecimalPlaces = 4,
                Minimum = -360,
                Maximum = 360,
                Increment = 1,
                Value = (decimal)Math.Max(-360, Math.Min(360, currentDegrees))
            };
            var current = new Label { Location = new Point(164, 145), AutoSize = true, Text = $"(actual: {currentDegrees:0.####}°)" };

            var ok = new Button { Text = "Aplicar", Location = new Point(236, 186), Size = new Size(90, 30), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Location = new Point(336, 186), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };

            Controls.AddRange(new Control[] { info, lbl, _angle, current, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
