using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarGroups.App.Views
{
    /// <summary>
    /// Builds the shared controls the settings-style windows use.
    /// </summary>
    /// <remarks>
    /// The 1.x project expressed its dark flat look as a repeated block of
    /// property assignments in every designer file. Six windows with the same
    /// palette would have meant six copies of the same colours to keep in step.
    /// This is the single place the palette lives; the designer files reference
    /// it rather than restating it.
    /// </remarks>
    internal static class ControlsBuilder
    {
        internal static readonly Color Surface = Color.FromArgb(20, 20, 20);
        internal static readonly Color Panel = Color.FromArgb(24, 24, 24);
        internal static readonly Color Raised = Color.FromArgb(32, 32, 32);
        internal static readonly Color Border = Color.FromArgb(56, 56, 56);
        internal static readonly Color Foreground = Color.FromArgb(240, 240, 240);
        internal static readonly Color Muted = Color.FromArgb(160, 160, 160);
        internal static readonly Color Accent = Color.FromArgb(76, 194, 255);
        internal static readonly Color Danger = Color.FromArgb(255, 122, 122);
        internal static readonly Color Warning = Color.FromArgb(255, 196, 92);
        internal static readonly Color Good = Color.FromArgb(126, 214, 143);

        /// <summary>Applies the palette to a form.</summary>
        internal static void StyleForm(Form form, string title, int width = 760, int height = 640)
        {
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.BackColor = Surface;
            form.ForeColor = Foreground;
            form.ClientSize = new Size(width, height);
            form.MinimumSize = new Size(Math.Min(520, width), 400);
            form.StartPosition = FormStartPosition.CenterParent;
            form.Text = title;
        }

        internal static Button Button(string text, EventHandler onClick, int width = 96, bool primary = false)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = false,
                Size = new Size(width, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Raised,
                ForeColor = primary ? Color.FromArgb(16, 20, 24) : Foreground,
                UseVisualStyleBackColor = false,
                Margin = new Padding(4)
            };

            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(120, 214, 255)
                : Color.FromArgb(44, 44, 44);

            if (onClick != null) button.Click += onClick;
            return button;
        }

        internal static Label Heading(string text, int size = 11)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Foreground,
                Font = new Font("Segoe UI", size, FontStyle.Bold),
                Margin = new Padding(0, 12, 0, 4)
            };
        }

        internal static Label Body(string text, bool muted = false)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = muted ? Muted : Foreground,
                Font = new Font("Segoe UI", 9.5f),
                Margin = new Padding(0, 2, 0, 2)
            };
        }

        internal static CheckBox Check(string text, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                ForeColor = Foreground,
                Font = new Font("Segoe UI", 9.5f),
                Margin = new Padding(0, 6, 0, 6)
            };
        }

        internal static NumericUpDown Number(int value, int min, int max, int width = 70)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
                Width = width,
                BackColor = Raised,
                ForeColor = Foreground,
                Margin = new Padding(4, 4, 4, 4)
            };
        }

        internal static TextBox Text(string value, bool multiline = false, int width = 0)
        {
            var box = new TextBox
            {
                Text = value ?? string.Empty,
                Multiline = multiline,
                BackColor = Raised,
                ForeColor = Foreground,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f)
            };

            if (width > 0) box.Width = width;
            return box;
        }

        internal static ComboBox Dropdown(int width = 160)
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                BackColor = Raised,
                ForeColor = Foreground,
                FlatStyle = FlatStyle.Flat
            };
        }

        internal static TableLayoutPanel Columns(params int[] widths)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = widths.Length,
                AutoSize = true
            };

            foreach (int width in widths)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));

            return layout;
        }

        internal static GroupBox Section(string caption)
        {
            return new GroupBox
            {
                Text = caption,
                ForeColor = Muted,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 8, 12, 12),
                Margin = new Padding(0, 8, 0, 8)
            };
        }

        /// <summary>A coloured status strip, used for check results.</summary>
        internal static Label Status(string text, Core.Enums.HealthStatus status)
        {
            Label label = Body(text, muted: true);

            switch (status)
            {
                case Core.Enums.HealthStatus.Healthy:
                    label.ForeColor = Good;
                    break;
                case Core.Enums.HealthStatus.Warning:
                    label.ForeColor = Warning;
                    break;
                case Core.Enums.HealthStatus.Problem:
                    label.ForeColor = Danger;
                    break;
            }

            return label;
        }
    }
}