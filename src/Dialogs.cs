using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Okna
{
    /// <summary>
    /// Picks programs from the windows that are open right now, either by
    /// executable name or by window class. Window classes matter for shell
    /// surfaces that have no useful executable of their own.
    /// </summary>
    internal sealed class WindowPicker : Form
    {
        private readonly AppSettings _settings;
        private readonly ListBox _list;

        private sealed class Entry
        {
            public string Exe;
            public string Class;
            public override string ToString()
            {
                string name = string.IsNullOrEmpty(Exe) ? "(без имени)" : Exe;
                return name + "   ·   " + Class;
            }
        }

        private WindowPicker(AppSettings settings)
        {
            _settings = settings;
            Text = "Программы без эффектов";
            ClientSize = new Size(520, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;
            DoubleBuffered = true;

            Label title = MakeLabel("Что открыто сейчас", 20, 16, Theme.FontCardTitle, Theme.Text);
            Controls.Add(title);
            Label sub = MakeLabel("Выберите окна и добавьте программу или её класс", 20, 40, Theme.FontSmall, Theme.Muted);
            Controls.Add(sub);

            _list = new ListBox();
            _list.Location = new Point(20, 68);
            _list.Size = new Size(480, 280);
            _list.SelectionMode = SelectionMode.MultiExtended;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.BackColor = Theme.CardAlt;
            _list.ForeColor = Theme.Text;
            _list.Font = Theme.FontSmall;
            _list.IntegralHeight = false;
            _list.DoubleClick += delegate { AddProgram(); };
            Controls.Add(_list);

            JellyButton addExe = new JellyButton();
            addExe.Text = "Добавить программу";
            addExe.Location = new Point(20, 362);
            addExe.Size = new Size(170, 34);
            addExe.Font = Theme.FontSmall;
            addExe.Accent = true;
            addExe.Click += delegate { AddProgram(); };
            Controls.Add(addExe);

            JellyButton addClass = new JellyButton();
            addClass.Text = "Добавить класс окна";
            addClass.Location = new Point(198, 362);
            addClass.Size = new Size(170, 34);
            addClass.Font = Theme.FontSmall;
            addClass.Click += delegate { AddClass(); };
            Controls.Add(addClass);

            JellyButton cancel = new JellyButton();
            cancel.Text = "Закрыть";
            cancel.Location = new Point(404, 362);
            cancel.Size = new Size(96, 34);
            cancel.Font = Theme.FontSmall;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            Load += delegate { Fill(); };
            Loc.Apply(this);
        }

        public static void ShowPicker(IWin32Window owner, AppSettings settings)
        {
            using (WindowPicker form = new WindowPicker(settings))
                form.ShowDialog(owner);
        }

        private void Fill()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch { processes = new Process[0]; }

            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    string title = p.MainWindowTitle;
                    if (string.IsNullOrEmpty(title)) continue;

                    string exe = null;
                    try { exe = Path.GetFileName(p.MainModule.FileName); }
                    catch { exe = p.ProcessName + ".exe"; }
                    if (string.IsNullOrEmpty(exe)) continue;

                    string cls = Native.GetWindowClass(p.MainWindowHandle);
                    string key = exe + "|" + cls;
                    if (!seen.Add(key)) continue;

                    Entry entry = new Entry();
                    entry.Exe = exe;
                    entry.Class = cls;
                    _list.Items.Add(entry);
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
            _list.EndUpdate();
        }

        private void AddProgram()
        {
            int added = 0;
            foreach (object item in _list.SelectedItems)
            {
                Entry entry = item as Entry;
                if (entry == null || string.IsNullOrEmpty(entry.Exe)) continue;
                _settings.AddExclude(entry.Exe);
                added++;
            }
            if (added == 0) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void AddClass()
        {
            int added = 0;
            foreach (object item in _list.SelectedItems)
            {
                Entry entry = item as Entry;
                if (entry == null || string.IsNullOrEmpty(entry.Class)) continue;
                _settings.AddExclude("class:" + entry.Class);
                added++;
            }
            if (added == 0) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Label MakeLabel(string text, int x, int y, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.AutoSize = true;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }
    }

    /// <summary>
    /// Per-program rules: the close effect plus switches that turn the bend,
    /// the shake and the roll off for this program only. The program name may
    /// be a glob mask ("steam*").
    /// </summary>
    internal sealed class CloseOverrideDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly ComboBox _program;
        private readonly JellySwitch _bendSwitch;
        private readonly JellySwitch _shakeSwitch;
        private readonly JellySwitch _rollSwitch;
        private int _effect;
        private bool _bend = true;
        private bool _shake = true;
        private bool _roll = true;
        private bool _loading;
        private bool _dirty;
        private string _loadedName;

        private CloseOverrideDialog(AppSettings settings, string presetProgram)
        {
            _settings = settings;
            Text = "Правила для программы";
            ClientSize = new Size(460, 390);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;
            DoubleBuffered = true;

            Controls.Add(MakeLabel("Программа", 20, 16, Theme.FontCardTitle, Theme.Text));
            Controls.Add(MakeLabel("Имя или маска: steam*, chrome.exe", 20, 40, Theme.FontSmall, Theme.Muted));

            _program = new ComboBox();
            _program.Location = new Point(20, 66);
            _program.Size = new Size(420, 28);
            _program.DropDownStyle = ComboBoxStyle.DropDown;
            _program.BackColor = Theme.CardAlt;
            _program.ForeColor = Theme.Text;
            _program.Font = Theme.FontSmall;
            Controls.Add(_program);

            FillPrograms();
            _program.SelectedIndexChanged += delegate { LoadProfileFor(_program.Text.Trim()); };
            _program.TextChanged += delegate
            {
                if (!_loading && _program.Text.Trim() != _loadedName) _dirty = false;
            };
            _program.Leave += delegate
            {
                // Do not reload the stored rule after the user has already
                // flipped a switch: that would silently undo the edit.
                if (!_dirty) LoadProfileFor(_program.Text.Trim());
            };

            Controls.Add(MakeLabel("Что делать при закрытии", 20, 108, Theme.FontCardTitle, Theme.Text));

            JellyButton none = EffectButton("Без эффекта", 20, 0);
            JellyButton shatter = EffectButton("Осколки", 170, 1);
            JellyButton burn = EffectButton("Сгорание", 320, 2);
            none.Click += delegate { _effect = 0; _dirty = true; UpdateEffectStyles(); };
            shatter.Click += delegate { _effect = 1; _dirty = true; UpdateEffectStyles(); };
            burn.Click += delegate { _effect = 2; _dirty = true; UpdateEffectStyles(); };
            Controls.Add(none);
            Controls.Add(shatter);
            Controls.Add(burn);

            Panel divider = new Panel();
            divider.Location = new Point(20, 180);
            divider.Size = new Size(420, 1);
            divider.BackColor = Theme.BorderSoft;
            Controls.Add(divider);

            Controls.Add(MakeLabel("Другие эффекты для этой программы", 20, 192,
                Theme.FontCardTitle, Theme.Text));

            Controls.Add(MakeLabel("Гнуть окно", 20, 222, Theme.FontLabel, Theme.Text));
            Controls.Add(MakeLabel("Тряска при развороте", 20, 252, Theme.FontLabel, Theme.Text));
            Controls.Add(MakeLabel("Скатывать в трубочку", 20, 282, Theme.FontLabel, Theme.Text));

            _bendSwitch = MakeSwitch(390, 218);
            _shakeSwitch = MakeSwitch(390, 248);
            _rollSwitch = MakeSwitch(390, 278);
            _bendSwitch.CheckedChanged += delegate { if (!_loading) _dirty = true; };
            _shakeSwitch.CheckedChanged += delegate { if (!_loading) _dirty = true; };
            _rollSwitch.CheckedChanged += delegate { if (!_loading) _dirty = true; };
            Controls.Add(_bendSwitch);
            Controls.Add(_shakeSwitch);
            Controls.Add(_rollSwitch);

            JellyButton ok = new JellyButton();
            ok.Text = "Сохранить";
            ok.Accent = true;
            ok.Location = new Point(340, 338);
            ok.Size = new Size(100, 34);
            ok.Font = Theme.FontSmall;
            ok.Click += delegate
            {
                if (string.IsNullOrEmpty(_program.Text.Trim())) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            JellyButton cancel = new JellyButton();
            cancel.Text = "Отмена";
            cancel.Location = new Point(232, 338);
            cancel.Size = new Size(100, 34);
            cancel.Font = Theme.FontSmall;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            _effect = settings.Burn ? 2 : (settings.Shatter ? 1 : 0);
            if (!string.IsNullOrEmpty(presetProgram))
            {
                _program.Text = presetProgram;
                LoadProfileFor(presetProgram);
            }
            UpdateEffectStyles();
            UpdateSwitchStyles();
            Loc.Apply(this);
        }

        private JellySwitch MakeSwitch(int x, int y)
        {
            JellySwitch s = new JellySwitch();
            s.Location = new Point(x, y);
            s.BackColor = Theme.Bg;
            return s;
        }

        private JellyButton EffectButton(string text, int x, int effect)
        {
            JellyButton b = new JellyButton();
            b.Text = text;
            b.Location = new Point(x, 132);
            b.Size = new Size(140, 34);
            b.Font = Theme.FontSmall;
            b.Tag = effect;
            return b;
        }

        private void UpdateEffectStyles()
        {
            foreach (Control control in Controls)
            {
                JellyButton b = control as JellyButton;
                if (b == null || !(b.Tag is int)) continue;
                bool accent = (int)b.Tag == _effect;
                if (b.Accent != accent)
                {
                    b.Accent = accent;
                    b.Invalidate();
                }
            }
        }

        private void UpdateSwitchStyles()
        {
            _loading = true;
            try
            {
                _bendSwitch.Checked = _bend;
                _shakeSwitch.Checked = _shake;
                _rollSwitch.Checked = _roll;
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>
        /// Shows the stored rule when the name already exists, so editing a
        /// program does not silently reset its switches.
        /// </summary>
        private void LoadProfileFor(string name)
        {
            if (_loading || string.IsNullOrEmpty(name)) return;
            int effect;
            bool bend;
            bool shake;
            bool roll;
            if (_settings.GetAppProfile(name, out effect, out bend, out shake, out roll))
            {
                _effect = effect;
                _bend = bend;
                _shake = shake;
                _roll = roll;
            }
            else
            {
                _effect = _settings.Burn ? 2 : (_settings.Shatter ? 1 : 0);
                _bend = true;
                _shake = true;
                _roll = true;
            }
            _loadedName = name;
            _dirty = false;
            UpdateEffectStyles();
            UpdateSwitchStyles();
        }

        private void FillPrograms()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Process[] processes = Process.GetProcesses();
                for (int i = 0; i < processes.Length; i++)
                {
                    Process p = processes[i];
                    try
                    {
                        if (p.MainWindowHandle == IntPtr.Zero) continue;
                        string exe = null;
                        try { exe = Path.GetFileName(p.MainModule.FileName); }
                        catch { exe = p.ProcessName + ".exe"; }
                        if (!string.IsNullOrEmpty(exe) && seen.Add(exe))
                            _program.Items.Add(exe);
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch { }
        }

        public static bool ShowFor(IWin32Window owner, AppSettings settings, string presetProgram,
            out string program, out int effect)
        {
            bool bend;
            bool shake;
            bool roll;
            return ShowFor(owner, settings, presetProgram, out program, out effect,
                out bend, out shake, out roll);
        }

        public static bool ShowFor(IWin32Window owner, AppSettings settings, string presetProgram,
            out string program, out int effect, out bool bend, out bool shake, out bool roll)
        {
            using (CloseOverrideDialog form = new CloseOverrideDialog(settings, presetProgram))
            {
                DialogResult result = form.ShowDialog(owner);
                program = form._program.Text.Trim();
                effect = form._effect;
                bend = form._bendSwitch.Checked;
                shake = form._shakeSwitch.Checked;
                roll = form._rollSwitch.Checked;
                return result == DialogResult.OK && program.Length > 0;
            }
        }

        private static Label MakeLabel(string text, int x, int y, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.AutoSize = true;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }
    }

    /// <summary>
    /// The "Диагностика" report: everything needed to understand a problem
    /// without asking the user to dig through the registry or the log.
    /// </summary>
    internal static class Diagnostics
    {
        public static string BuildReport(AppSettings settings, JellyEngine engine, string hotkeyStatus)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Loc.T("Окна ") + Updater.CurrentLabel);
            sb.AppendLine(Loc.T("Исполняемый файл: ") + Updater.ExePath);
            sb.AppendLine(Loc.T("Версия сборки: ") + typeof(Diagnostics).Assembly.GetName().Version);
            sb.AppendLine("Windows: " + Environment.OSVersion.VersionString +
                          " (" + (Environment.Is64BitOperatingSystem ? "64" : "32") + "-bit)");
            sb.AppendLine(Loc.T("Среда: .NET ") + Environment.Version);
            sb.AppendLine(Loc.T("Портативный режим: ") +
                          (settings.IsPortable ? settings.PortablePath : Loc.T("нет")));
            sb.AppendLine(Loc.T("Уменьшение движения: ") +
                          Loc.T(Native.ClientAreaAnimationEnabled() ? "выключено" : "включено"));
            sb.AppendLine(Loc.T("Питание: ") + Loc.T(Native.IsOnBattery() ? "батарея" : "сеть") +
                          Loc.T(", экономия: ") + Loc.T(Native.IsBatterySaverOn() ? "да" : "нет"));
            sb.AppendLine(Loc.T("Удалённый сеанс: ") + Loc.T(Native.IsRemoteSession() ? "да" : "нет"));
            sb.AppendLine(Loc.T("Горячая клавиша: ") + Loc.HotkeyStatus(hotkeyStatus));
            sb.AppendLine(Loc.T("Статус: ") + (engine == null
                ? Loc.T("нет")
                : Loc.Status(engine.PauseReason ?? engine.Status)));
            sb.AppendLine(Loc.T("Автозапуск: ") + AutoStartDescription());
            sb.AppendLine();

            sb.AppendLine(Loc.T("Настройки:"));
            sb.AppendLine("  Enabled=" + settings.Enabled + " Jelly=" + settings.Jelly +
                          " ReleaseWobble=" + settings.ReleaseWobble +
                          " Bend=" + settings.Bend + " MaxShake=" + settings.MaxShake);
            sb.AppendLine("  Close=" + (settings.Burn ? "burn" : (settings.Shatter ? "shatter" : "none")) +
                          " Roll=" + settings.Roll + " Direction=" + settings.RollDirection);
            sb.AppendLine("  Economy=" + settings.Economy + " PauseOnBattery=" + settings.PauseOnBattery +
                          " PauseOnCall=" + settings.PauseOnCall +
                          " RespectReduceMotion=" + settings.RespectReduceMotion);
            sb.AppendLine("  Hotkey=" + settings.HotkeyLabel + " AutoUpdate=" + settings.AutoUpdate);
            sb.AppendLine("  Exclude=" + string.Join(", ", settings.Exclude));
            sb.AppendLine("  CloseOverrides=" + string.Join(", ", settings.CloseOverrides));
            sb.AppendLine();

            sb.AppendLine(Loc.T("Экраны:"));
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                Screen s = screens[i];
                sb.AppendLine("  " + (s.Primary ? "* " : "  ") + s.DeviceName +
                              " bounds=" + s.Bounds + " work=" + s.WorkingArea +
                              " bpp=" + s.BitsPerPixel);
            }

            sb.AppendLine();
            sb.AppendLine(Loc.T("Панель задач:"));
            Native.RECT[] bars = TaskbarFinder.Rects();
            for (int i = 0; i < bars.Length; i++)
                sb.AppendLine("  " + bars[i].Left + "," + bars[i].Top + " " +
                              bars[i].Width + "x" + bars[i].Height);

            sb.AppendLine();
            sb.AppendLine(Loc.T("Журнал (") + Log.LogPath + "):");
            sb.AppendLine(LogTail(250));
            return sb.ToString();
        }

        private static string AutoStartDescription()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "Okna", null);
                return value == null ? Loc.T("выключен") : value.ToString();
            }
            catch
            {
                return Loc.T("неизвестно");
            }
        }

        private static string LogTail(int lines)
        {
            try
            {
                if (!File.Exists(Log.LogPath)) return "  " + Loc.T("(файла нет)");
                string[] all = File.ReadAllLines(Log.LogPath);
                int start = Math.Max(0, all.Length - lines);
                StringBuilder sb = new StringBuilder();
                for (int i = start; i < all.Length; i++)
                    sb.AppendLine("  " + all[i]);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "  " + Loc.T("(не удалось прочитать: ") + ex.Message + ")";
            }
        }

        public static void Show(IWin32Window owner, AppSettings settings, JellyEngine engine,
            string hotkeyStatus)
        {
            using (DiagnosticsForm form = new DiagnosticsForm(
                BuildReport(settings, engine, hotkeyStatus)))
                form.ShowDialog(owner);
        }
    }

    internal sealed class DiagnosticsForm : Form
    {
        private readonly TextBox _text;

        public DiagnosticsForm(string report)
        {
            Text = "Диагностика Окон";
            ClientSize = new Size(700, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;
            DoubleBuffered = true;

            _text = new TextBox();
            _text.Multiline = true;
            _text.ReadOnly = true;
            _text.ScrollBars = ScrollBars.Both;
            _text.WordWrap = false;
            _text.Location = new Point(16, 16);
            _text.Size = new Size(668, 480);
            _text.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _text.BackColor = Theme.CardAlt;
            _text.ForeColor = Theme.Text;
            _text.Font = new Font("Consolas", 9f);
            _text.Text = report;
            Controls.Add(_text);

            JellyButton copy = new JellyButton();
            copy.Text = "Копировать";
            copy.Location = new Point(16, 510);
            copy.Size = new Size(130, 34);
            copy.Font = Theme.FontSmall;
            copy.Click += delegate
            {
                try { Clipboard.SetText(_text.Text); } catch { }
            };
            Controls.Add(copy);

            JellyButton save = new JellyButton();
            save.Text = "Сохранить…";
            save.Location = new Point(156, 510);
            save.Size = new Size(130, 34);
            save.Font = Theme.FontSmall;
            save.Click += delegate { SaveReport(); };
            Controls.Add(save);

            JellyButton close = new JellyButton();
            close.Text = "Закрыть";
            close.Accent = true;
            close.Location = new Point(554, 510);
            close.Size = new Size(130, 34);
            close.Font = Theme.FontSmall;
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Loc.Apply(this);
        }

        private void SaveReport()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Loc.T("Сохранить диагностику");
                dialog.Filter = Loc.T("Текстовый файл (*.txt)|*.txt");
                dialog.FileName = "okna-diagnostics.txt";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, _text.Text, Encoding.UTF8); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Окна"); }
            }
        }
    }

    /// <summary>
    /// Release notes with the three choices that matter: install now, look
    /// again later, or stop being offered this exact version.
    /// </summary>
    internal sealed class UpdateDialog : Form
    {
        public bool Install;
        public bool Skip;
        public bool OpenPage;

        public UpdateDialog(UpdateInfo info)
        {
            Text = Loc.T("Доступно обновление");
            ClientSize = new Size(620, 480);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;
            DoubleBuffered = true;

            Label title = MakeLabel(
                Loc.T("Доступно обновление") + " v" + Updater.Format(info.Version),
                20, 18, Theme.FontCardTitle, Theme.Text);
            Controls.Add(title);

            Label current = MakeLabel(
                Loc.T("Текущая версия: ") + Updater.CurrentLabel,
                20, 48, Theme.FontSmall, Theme.Muted);
            Controls.Add(current);

            Label notesLabel = MakeLabel("Заметки к выпуску", 20, 80, Theme.FontLabel, Theme.Text);
            Controls.Add(notesLabel);

            TextBox notes = new TextBox();
            notes.Multiline = true;
            notes.ReadOnly = true;
            notes.ScrollBars = ScrollBars.Vertical;
            notes.WordWrap = true;
            notes.Location = new Point(20, 104);
            notes.Size = new Size(580, 296);
            notes.BackColor = Theme.CardAlt;
            notes.ForeColor = Theme.Text;
            notes.Font = Theme.FontSmall;
            notes.Text = string.IsNullOrEmpty(info.Notes) ? "" : info.Notes;
            Controls.Add(notes);

            JellyButton install = new JellyButton();
            install.Text = "Обновить";
            install.Accent = true;
            install.Location = new Point(20, 420);
            install.Size = new Size(130, 38);
            install.Font = Theme.FontSmall;
            install.Click += delegate { Install = true; Close(); };
            Controls.Add(install);

            JellyButton later = new JellyButton();
            later.Text = "Позже";
            later.Location = new Point(158, 420);
            later.Size = new Size(110, 38);
            later.Font = Theme.FontSmall;
            later.Click += delegate { Close(); };
            Controls.Add(later);

            JellyButton skip = new JellyButton();
            skip.Text = "Пропустить эту версию";
            skip.Location = new Point(276, 420);
            skip.Size = new Size(190, 38);
            skip.Font = Theme.FontSmall;
            skip.Click += delegate { Skip = true; Close(); };
            Controls.Add(skip);

            JellyButton page = new JellyButton();
            page.Text = "Открыть на GitHub";
            page.Location = new Point(474, 420);
            page.Size = new Size(126, 38);
            page.Font = Theme.FontSmall;
            page.Click += delegate { OpenPage = true; Close(); };
            Controls.Add(page);

            Loc.Apply(this);
        }

        private static Label MakeLabel(string text, int x, int y, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.AutoSize = true;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }
    }
}
