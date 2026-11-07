using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace Okna
{
    internal enum UiLanguage
    {
        Auto = 0,
        Russian = 1,
        English = 2
    }

    /// <summary>
    /// Russian is the source language: every user-visible string in the UI is
    /// written in Russian and this class maps it to English on demand. The
    /// mapping is keyed by the Russian text, so a missing entry simply stays
    /// Russian instead of turning into a blank control.
    /// </summary>
    internal static class Loc
    {
        private static readonly Dictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Window and cards.
                { "Окна", "Okna" },
                { "Инерционные окна для Windows", "Inertial windows for Windows" },
                { "Желеобразные окна", "Jelly windows" },
                { "Перетаскивание за заголовок", "Drag by the title bar" },
                { "Желейность", "Jelliness" },
                { "Покачивание при отпускании", "Wobble on release" },
                { "Боковое качание и упругое сжатие после движения", "Side sway and elastic squash after movement" },
                { "Гнуть окна по инерции", "Bend windows by inertia" },
                { "Края изгибаются при разгоне и торможении", "Edges bend on acceleration and braking" },
                { "Тряска при разворачивании", "Shake on maximize" },
                { "Качается при развороте, восстановлении и возврате", "Swings on maximize, restore and return" },
                { "Запускать с Windows", "Start with Windows" },
                { "Тихо стартовать вместе с системой", "Start silently with the system" },
                { "Запуск от имени администратора", "Run as administrator" },
                { "Запускать с правами администратора при входе в Windows", "Run with administrator privileges at Windows logon" },
                { "Эффект при закрытии", "Close effect" },
                { "Если программа остаётся в трее — окно скатывается туда", "If the app stays in the tray, the window rolls there" },
                { "Без эффекта", "No effect" },
                { "Осколки", "Shards" },
                { "Сгорание", "Burn" },
                { "Сворачивать в трубочку", "Roll into a tube" },
                { "Окно скатывается и уезжает в панель задач", "The window rolls away to the taskbar" },
                { "Куда скатывать", "Roll direction" },
                { "Авто — в сторону панели задач, остальное — принудительно", "Auto follows the taskbar; the rest force a side" },
                { "Авто", "Auto" },
                { "Вниз", "Down" },
                { "Вверх", "Up" },
                { "Влево", "Left" },
                { "Вправо", "Right" },
                { "Горячая клавиша", "Hotkey" },
                { "Нажмите на поле и введите сочетание; Backspace — выключить", "Click the field and press a combination; Backspace turns it off" },
                { "Пауза сама по себе", "Pauses by itself" },
                { "Эффекты уступают место, когда вы заняты другим", "Effects step aside while you are busy" },
                { "Звонки и показ экрана", "Calls and screen sharing" },
                { "Teams, Zoom, Discord, Slack и похожие впереди", "Teams, Zoom, Discord, Slack and similar in front" },
                { "Питание от батареи", "On battery" },
                { "Не тратить заряд на эффекты", "Do not spend battery on effects" },
                { "Уважать уменьшение движения", "Respect reduced motion" },
                { "Если анимации выключены в Windows — не мешать", "Stay out of the way when Windows animations are off" },
                { "Исключения", "Exceptions" },
                { "Здесь окна остаются обычными: программы, маски и классы окон", "Windows stay ordinary here: programs, masks and window classes" },
                { "Добавить файл…", "Add file…" },
                { "Из запущенных…", "From running…" },
                { "Убрать", "Remove" },
                { "Маска: steam* или class:Progman", "Mask: steam* or class:Progman" },
                { "Добавить маску", "Add mask" },
                { "Свои правила для программы", "Per-program rules" },
                { "Эффект закрытия и отключение изгиба, тряски, скатывания", "Close effect and disabling bend, shake and roll" },
                { "Для программы…", "For program…" },
                { "Настройки рядом с программой", "Settings next to the program" },
                { "Файл okna.ini вместо реестра — для флешки", "okna.ini instead of the registry — for a USB stick" },
                { "Экспорт…", "Export…" },
                { "Импорт…", "Import…" },
                { "Сбросить", "Reset" },
                { "Диагностика…", "Diagnostics…" },
                { "Готово", "Done" },
                { "живёт в трее", "lives in the tray" },
                { "Язык", "Language" },
                { "Язык интерфейса", "Interface language" },
                { "Русский", "Russian" },
                { "Системный", "System" },
                { "Язык изменится сразу", "The language changes immediately" },

                // Presets.
                { "Спокойно", "Calm" },
                { "Баланс", "Balanced" },
                { "Желе", "Jelly" },
                { "Экономно", "Economy" },
                { "Режим", "Preset" },

                // Tooltips.
                { "Включить или выключить эффект", "Turn the effect on or off" },
                { "0–200: насколько сильно окно отстаёт и изгибается; 200 — максимально вязко", "0–200: how strongly the window lags and bends; 200 is the most viscous" },
                { "Добавляет боковое покачивание в конце движения", "Adds a sideways wobble at the end of a movement" },
                { "Настоящая гнутость: окно на время перетаскивания замирает и изгибается", "A real bend: the window freezes and bends while it is dragged" },
                { "Окно качается, когда разворачивается на весь экран, восстанавливается или возвращается из панели задач", "The window swings when it is maximized, restored or brought back from the taskbar" },
                { "Закрывать окна без эффекта", "Close windows without an effect" },
                { "Окно рассыпается осколками, когда закрывается кнопкой или по Alt+F4; если программа остаётся в трее, окно скатывается в трубочку и уезжает в трей", "The window shatters when closed with the button or Alt+F4; if the app stays in the tray, the window rolls into the tray" },
                { "Окно прогорает снизу вверх: огонь, обугленный край, угли и дым; если программа остаётся в трее, окно скатывается в трубочку и уезжает в трей", "The window burns from the bottom up: fire, charred edge, embers and smoke; if the app stays in the tray, the window rolls into the tray" },
                { "При сворачивании окно скатывается в трубочку и уезжает в панель задач", "On minimize the window rolls into a tube and goes to the taskbar" },
                { "Нажмите и введите сочетание, например Ctrl+Alt+K", "Click and press a combination, for example Ctrl+Alt+K" },
                { "Хранить настройки в okna.ini рядом с Okna.exe", "Keep the settings in okna.ini next to Okna.exe" },
                { "Выбрать из окон, которые открыты прямо сейчас", "Pick from the windows that are open right now" },

                // Dynamic settings window text.
                { "работает", "working" },
                { "занята", "taken" },
                { "неизвестно", "unknown" },
                { "Не удалось записать okna.ini рядом с программой. Скопируйте Okna.exe в папку, где можно писать, или оставьте настройки в реестре.", "Could not write okna.ini next to the program. Copy Okna.exe to a writable folder or keep the settings in the registry." },
                { "Потяните это окно за заголовок — оно желейное  ·  Shift — обычное перетаскивание  ·  ", "Drag this window by its title bar — it is jelly  ·  Shift — ordinary dragging  ·  " },
                { " — пауза", " — pause" },
                { "Файл: ", "File: " },
                { "Сейчас настройки хранятся в реестре (HKCU\\Software\\Okna)", "Settings are currently kept in the registry (HKCU\\Software\\Okna)" },
                { "Вернуть все настройки к заводским?", "Reset every setting to the factory values?" },
                { "Настройки сохранены в ", "Settings saved to " },
                { "В файле нет настроек Окон.", "The file does not contain Okna settings." },
                { "Настройки загружены.", "Settings loaded." },
                { "Экспорт настроек", "Export settings" },
                { "Импорт настроек", "Import settings" },
                { "Настройки Окон (*.ini)|*.ini|Все файлы (*.*)|*.*", "Okna settings (*.ini)|*.ini|All files (*.*)|*.*" },
                { "Программы (*.exe)|*.exe", "Programs (*.exe)|*.exe" },
                { "Программы без эффектов", "Programs without effects" },
                { "сгорание", "burn" },
                { "осколки", "shards" },
                { "без эффекта", "no effect" },
                { " · без изгиба", " · no bend" },
                { " · без тряски", " · no shake" },
                { " · без скатывания", " · no roll" },

                // Tray menu and balloons.
                { "ОКНА", "OKNA" },
                { "Пауза на 30 минут", "Pause for 30 minutes" },
                { "Возобновить (ещё ", "Resume (" },
                { " мин)", " min left)" },
                { "Скатывать в трубочку", "Roll into a tube" },
                { "Желеобразные окна: вкл", "Jelly windows: on" },
                { "Желеобразные окна: выкл", "Jelly windows: off" },
                { "Скатывать в трубочку: вкл", "Roll into a tube: on" },
                { "Скатывать в трубочку: выкл", "Roll into a tube: off" },
                { "Настройки…", "Settings…" },
                { "Потрясти активное окно", "Shake the active window" },
                { "Автозапуск включён", "Start with Windows: on" },
                { "Автозапуск выключен", "Start with Windows: off" },
                { "Запуск от имени администратора: вкл", "Run as administrator: on" },
                { "Запуск от имени администратора: выкл", "Run as administrator: off" },
                { "Проверить обновления…", "Check for updates…" },
                { "Автообновление включено", "Automatic updates: on" },
                { "Автообновление выключено", "Automatic updates: off" },
                { "Открыть журнал", "Open log" },
                { "Выход", "Exit" },
                { "Окна — безопасный режим", "Okna — safe mode" },
                { "Окна — выключены", "Okna — off" },
                { "Окна — работают", "Okna — working" },
                { "Окна — ", "Okna — " },
                { "Эффект включён", "Effects on" },
                { "Эффект выключен", "Effects off" },
                { "Окна включены", "Okna is on" },
                { "Окна на паузе", "Okna is paused" },
                { "Эффекты снова работают.", "The effects are working again." },
                { "Пауза снята.", "The pause is over." },
                { "Эффекты вернутся через 30 минут.", "The effects will return in 30 minutes." },
                { "Пауза на 30 минут закончилась.", "The 30-minute pause is over." },
                { "Горячая клавиша занята", "The hotkey is taken" },
                { "не проверена", "not checked" },
                { "выключена", "off" },
                { "диспетчер ещё не создан", "the dispatcher is not created yet" },
                { "окно диспетчера ещё не создано", "the dispatcher window is not created yet" },
                { " — работает", " — working" },
                { "Не удалось проверить обновления", "Could not check for updates" },
                { "Обновлений нет", "No updates" },
                { "У вас последняя версия — ", "You have the latest version — " },
                { "Доступно обновление v", "Update v" },
                { "Нажмите, чтобы установить, или выберите «Обновить» в меню.", " is available. Click to install or choose Update in the menu." },
                { "Не удалось обновить", "Could not update" },
                { "Подробности в журнале. Новую версию можно скачать с GitHub вручную.", "Details are in the log. You can download the new version from GitHub." },
                { "Пропустить версию", "Skip this version" },
                { "Пропустить эту версию", "Skip this version" },
                { "Эта версия пропущена", "This version is skipped" },
                { "Заметки к выпуску", "Release notes" },
                { "Нажмите, чтобы посмотреть, что нового.", "Click to see what is new." },
                { "Перетаскивайте окна за заголовок. Shift — обычное перетаскивание.", "Drag windows by the title bar. Shift — ordinary dragging." },
                { "Нажмите ", "Press " },
                { " или выберите «Эффект выключен» в трее, чтобы вернуть.", " or choose Effects off in the tray to bring them back." },
                { "Клавиша ", "The key " },
                { " уже занята другой программой", " is already used by another program" },
                { ". Выберите другую в настройках или закройте программу, которая её использует.", ". Choose another one in the settings or close the program that uses it." },
                { "Не удалось открыть журнал", "Could not open the log" },
                { "Прошлые запуски завершились неудачно, поэтому эффекты выключены. Включите их в трее, когда всё будет в порядке.", "Previous starts failed, so the effects are turned off. Turn them on in the tray when everything is in order." },
                { "Окна уже запущены", "Okna is already running" },
                { "Окна столкнулись с ошибкой.", "Okna ran into a problem." },
                { "Неизвестная ошибка.", "Unknown error." },
                { "Подробности записаны в ", "Details were written to " },
                { "Что нового…", "What's new…" },
                { "Что нового", "What's new" },
                { "Позже", "Later" },
                { "Обновить", "Update" },
                { "Обновить до v", "Update to v" },
                { "Открыть на GitHub", "Open on GitHub" },
                { "Доступно обновление", "Update available" },
                { "Текущая версия: ", "Current version: " },
                { "Новая версия: ", "New version: " },
                { "Безопасный режим", "Safe mode" },
                { "Программа уже работает в трее.", "The program is already running in the tray." },
                { "Исключение", "Excluded" },
                { "Диагностика", "Diagnostics" },
                { "© Сергей", "© Sergey" },
                { "Обратная связь: @Link_Ser", "Feedback: @Link_Ser" },

                // Dialogs.
                { "Что открыто сейчас", "What is open now" },
                { "Выберите окна и добавьте программу или её класс", "Select windows and add the program or its window class" },
                { "Добавить программу", "Add program" },
                { "Добавить класс окна", "Add window class" },
                { "Закрыть", "Close" },
                { "(без имени)", "(no name)" },
                { "Правила для программы", "Program rules" },
                { "Программа", "Program" },
                { "Имя или маска: steam*, chrome.exe", "Name or mask: steam*, chrome.exe" },
                { "Что делать при закрытии", "What to do on close" },
                { "Другие эффекты для этой программы", "Other effects for this program" },
                { "Гнуть окно", "Bend the window" },
                { "Тряска при развороте", "Shake on maximize" },
                { "Сохранить", "Save" },
                { "Отмена", "Cancel" },
                { "Диагностика Окон", "Okna diagnostics" },
                { "Копировать", "Copy" },
                { "Сохранить…", "Save…" },
                { "Сохранить диагностику", "Save diagnostics" },
                { "Текстовый файл (*.txt)|*.txt", "Text file (*.txt)|*.txt" },

                // Engine status and pause reasons.
                { "Активно", "Active" },
                { "Пауза", "Paused" },
                { "Пауза на время", "Paused for now" },
                { "Ошибка", "Error" },
                { "Конфликт", "Conflict" },
                { "Перетаскивание", "Dragging" },
                { "Отменено", "Cancelled" },
                { "Успокаивается", "Settling" },
                { "Упруго", "Springy" },
                { "Покачивание", "Wobbling" },
                { "Удержание", "Holding" },
                { "Удалённый рабочий стол", "Remote desktop" },
                { "Полноэкранное приложение", "Fullscreen app" },
                { "Звонок или показ экрана", "Call or screen sharing" },
                { "Экономия энергии", "Battery saver" },
                { "Уменьшено движение", "Reduced motion" },
                { "Исключение: ", "Excluded: " },
                { "Нет", "None" },

                // Jelly labels.
                { "Сдержанно", "Restrained" },
                { "Очень желейно", "Very jelly" },
                { "Максимально вязко", "Maximally viscous" },

                // Diagnostics report.
                { "Окна ", "Okna " },
                { "Исполняемый файл: ", "Executable: " },
                { "Версия сборки: ", "Build version: " },
                { "Среда: .NET ", "Runtime: .NET " },
                { "Портативный режим: ", "Portable mode: " },
                { "Уменьшение движения: ", "Reduced motion: " },
                { "Питание: ", "Power: " },
                { ", экономия: ", ", battery saver: " },
                { "Удалённый сеанс: ", "Remote session: " },
                { "Горячая клавиша: ", "Hotkey: " },
                { "Статус: ", "Status: " },
                { "Автозапуск: ", "Autostart: " },
                { "Настройки:", "Settings:" },
                { "Экраны:", "Screens:" },
                { "Панель задач:", "Taskbars:" },
                { "Журнал (", "Log (" },
                { "нет", "no" },
                { "включено", "on" },
                { "выключено", "off" },
                { "выключен", "off" },
                { "батарея", "battery" },
                { "сеть", "mains" },
                { "да", "yes" },
                { "(файла нет)", "(file missing)" },
                { "(не удалось прочитать: ", "(could not read: " },

                // Updater errors.
                { "Пустой ответ GitHub", "Empty GitHub response" },
                { "В релизе нет тега", "The release has no tag" },
                { "Непонятный тег релиза: ", "Unreadable release tag: " },
                { "В релизе нет файла ", "The release has no file " },
                { "В релизе нет контрольной суммы ", "The release has no checksum " },
                { "Контрольная сумма не совпала", "The checksum did not match" },
                { "Пустая контрольная сумма", "Empty checksum" },
                { "Контрольная сумма неверного вида", "The checksum has the wrong shape" }
            };

        private static bool _english;

        public static bool IsEnglish
        {
            get { return _english; }
        }

        public static void Configure(int language)
        {
            if (language == (int)UiLanguage.English) _english = true;
            else if (language == (int)UiLanguage.Russian) _english = false;
            else
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                _english = !string.Equals(two, "ru", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Translates one known string; unknown text stays as it is.</summary>
        public static string T(string russian)
        {
            if (!_english || string.IsNullOrEmpty(russian)) return russian;
            string value;
            return English.TryGetValue(russian, out value) ? value : russian;
        }

        /// <summary>
        /// Engine statuses are built from a fixed word plus an optional
        /// "Excluded: name" suffix. Keeps the raw string for the paused check.
        /// </summary>
        public static string Status(string russian)
        {
            if (string.IsNullOrEmpty(russian)) return russian;
            if (!_english) return russian;
            if (russian.StartsWith("Исключение: ", StringComparison.Ordinal))
                return T("Исключение: ") + russian.Substring("Исключение: ".Length);
            return T(russian);
        }

        /// <summary>
        /// The hotkey status is "Ctrl+Alt+J — работает" or a full conflict
        /// sentence. The key label is not translatable, the suffix is.
        /// </summary>
        public static string HotkeyStatus(string russian)
        {
            if (string.IsNullOrEmpty(russian)) return russian;
            if (!_english) return russian;
            const string working = " — работает";
            if (russian.EndsWith(working, StringComparison.Ordinal))
                return russian.Substring(0, russian.Length - working.Length) +
                       " — " + T("работает");
            return T(russian);
        }

        /// <summary>
        /// Walks a control tree once and translates the static text. The
        /// original is kept in AccessibleDescription, so switching the
        /// language again restores the right source string.
        /// </summary>
        public static void Apply(Control root)
        {
            if (root == null) return;
            Translate(root);
            for (int i = 0; i < root.Controls.Count; i++)
                Apply(root.Controls[i]);
            ToolStrip strip = root as ToolStrip;
            if (strip != null) ApplyItems(strip.Items);
            MenuStrip menu = root as MenuStrip;
            if (menu != null) ApplyItems(menu.Items);
        }

        public static void ApplyItems(ToolStripItemCollection items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                ToolStripItem item = items[i];
                if (item == null) continue;
                string source = item.AccessibleDescription;
                if (string.IsNullOrEmpty(source))
                {
                    source = item.Text;
                    if (!string.IsNullOrEmpty(source)) item.AccessibleDescription = source;
                }
                if (!string.IsNullOrEmpty(source)) item.Text = T(source);
                ToolStripDropDownItem drop = item as ToolStripDropDownItem;
                if (drop != null) ApplyItems(drop.DropDownItems);
            }
        }

        private static void Translate(Control control)
        {
            if (control == null) return;
            // Editable fields hold user data, not interface labels.
            if (control is TextBox || control is ComboBox || control is ListBox) return;
            string source = control.AccessibleDescription;
            if (string.IsNullOrEmpty(source))
            {
                source = control.Text;
                if (!string.IsNullOrEmpty(source)) control.AccessibleDescription = source;
            }
            if (string.IsNullOrEmpty(source)) return;
            string translated = T(source);
            if (control.Text != translated) control.Text = translated;
        }
    }
}
