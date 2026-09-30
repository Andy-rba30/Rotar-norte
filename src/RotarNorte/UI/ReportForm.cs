using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using RotarNorte.Core;

namespace RotarNorte.UI
{
    /// <summary>Muestra el informe de una rotación y permite guardarlo.</summary>
    internal class ReportForm : Form
    {
        private readonly RotationReport _report;

        public ReportForm(RotationReport report)
        {
            _report = report;

            Text = report.DryRun ? "Rotar Norte - Informe de simulación" : "Rotar Norte - Informe";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 560);
            MinimumSize = new Size(520, 360);
            Font = new Font("Segoe UI", 9f);
            ShowInTaskbar = false;

            var summary = new Label
            {
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(12, 10, 12, 4),
                Text = report.Summary(),
                ForeColor = report.Success ? Color.FromArgb(0, 100, 0) : Color.FromArgb(160, 0, 0),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            var text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                Text = report.ToText(),
                BackColor = Color.White
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 8)
            };
            var close = new Button { Text = "Cerrar", Width = 100, Height = 28, DialogResult = DialogResult.OK };
            var save = new Button { Text = "Guardar informe...", Width = 140, Height = 28 };
            var copy = new Button { Text = "Copiar", Width = 100, Height = 28 };
            save.Click += OnSave;
            copy.Click += (s, e) => { try { Clipboard.SetText(text.Text); } catch { } };
            buttons.Controls.Add(close);
            buttons.Controls.Add(save);
            buttons.Controls.Add(copy);

            Controls.Add(text);
            Controls.Add(summary);
            Controls.Add(buttons);
            AcceptButton = close;
            CancelButton = close;

            text.Select(0, 0);
        }

        private void OnSave(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Guardar informe",
                Filter = "Texto (*.txt)|*.txt",
                FileName = $"RotarNorte_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dlg.FileName, _report.ToText()); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo guardar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
