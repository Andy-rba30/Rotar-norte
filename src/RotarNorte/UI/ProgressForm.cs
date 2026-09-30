using System;
using System.Drawing;
using System.Windows.Forms;

namespace RotarNorte.UI
{
    /// <summary>Ventana de progreso mínima (no modal) para operaciones largas.</summary>
    internal class ProgressForm : Form
    {
        private readonly Label _label;

        public ProgressForm(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(480, 90);
            Font = new Font("Segoe UI", 9f);

            _label = new Label
            {
                AutoSize = false,
                Location = new Point(16, 16),
                Size = new Size(448, 30),
                Text = "Preparando..."
            };
            var bar = new ProgressBar
            {
                Location = new Point(16, 52),
                Size = new Size(448, 20),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };
            Controls.Add(_label);
            Controls.Add(bar);
        }

        public void Report(string text)
        {
            if (IsDisposed) return;
            _label.Text = text;
            Refresh();
            Application.DoEvents();
        }
    }
}
