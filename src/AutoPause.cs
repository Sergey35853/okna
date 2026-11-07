using System;

namespace Okna
{
    /// <summary>
    /// Decides when the effects should step aside on their own: a full-screen
    /// game or video, a remote desktop session, a call or screen-sharing app
    /// in front, battery power, or Windows' "reduce motion" switch. The master
    /// switch in the settings stays untouched; this is a temporary pause that
    /// ends by itself.
    /// </summary>
    internal sealed class AutoPause
    {
        internal static readonly string[] BuiltInCallApps = new string[]
        {
            "Teams.exe", "ms-teams.exe", "ms-teamsupdate.exe",
            "Zoom.exe", "zoom.exe",
            "webex.exe", "Webex.exe", "CiscoWebexStart.exe", "atmgr.exe", "ptoneclk.exe",
            "Discord.exe", "discord.exe",
            "Slack.exe", "slack.exe",
            "Skype.exe", "skype.exe",
            "WhatsApp.exe", "whatsapp.exe",
            "Telegram.exe", "telegram.exe",
            "FaceTime.exe", "Viber.exe", "viber.exe",
            "GoogleMeet.exe", "meet.exe"
        };

        private readonly AppSettings _settings;
        private string _reason;
        private double _nextCheck;
        private double _clearSince;

        public AutoPause(AppSettings settings)
        {
            _settings = settings;
        }

        /// <summary>Human readable reason, or null when nothing is in the way.</summary>
        public string Reason
        {
            get { return _reason; }
        }

        public bool Paused
        {
            get { return _reason != null; }
        }

        public void Update(IntPtr foreground, double now)
        {
            if (now < _nextCheck) return;
            _nextCheck = now + 0.4;

            bool remote = Native.IsRemoteSession();
            bool full = _settings != null && _settings.PauseOnFullscreen && Native.IsFullscreenWindow(foreground);
            bool call = _settings != null && _settings.PauseOnCall && IsCallApp(foreground);
            bool saver = Native.IsBatterySaverOn();
            bool battery = _settings != null && _settings.PauseOnBattery && Native.IsOnBattery();
            bool motion = _settings != null && _settings.RespectReduceMotion && !Native.ClientAreaAnimationEnabled();

            string reason = DecideReason(remote, full, call, saver, battery, motion);

            if (reason != null)
            {
                _clearSince = 0;
                if (reason == _reason) return;
                _reason = reason;
                Log.Debug("Auto pause: " + reason);
                return;
            }

            if (_reason == null) return;
            int delay = _settings == null ? 0 : _settings.AutoPauseResumeDelaySeconds;
            if (delay <= 0)
            {
                _reason = null;
                _clearSince = 0;
                Log.Debug("Auto pause off");
                return;
            }
            if (_clearSince <= 0) _clearSince = now;
            if (now - _clearSince >= delay)
            {
                _reason = null;
                _clearSince = 0;
                Log.Debug("Auto pause off (delayed)");
            }
        }

        /// <summary>Testable decision (same priority/strings as runtime). Conditions are pre-evaluated by caller/settings as appropriate.</summary>
        internal static string DecideReason(bool remote, bool fullscreenActive, bool callActive, bool batterySaver, bool onBattery, bool reduceMotion)
        {
            if (remote) return "Удалённый рабочий стол";
            if (fullscreenActive) return "Полноэкранное приложение";
            if (callActive) return "Звонок или показ экрана";
            if (batterySaver) return "Экономия энергии";
            if (onBattery) return "Питание от батареи";
            if (reduceMotion) return "Уменьшено движение";
            return null;
        }

        internal static bool IsCallAppName(string name, string[] customCallApps)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < BuiltInCallApps.Length; i++)
                if (string.Equals(BuiltInCallApps[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            if (customCallApps != null)
            {
                for (int i = 0; i < customCallApps.Length; i++)
                    if (!string.IsNullOrEmpty(customCallApps[i]) &&
                        string.Equals(customCallApps[i], name, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        private bool IsCallApp(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            string exe = Native.GetWindowProcessName(hwnd);
            return IsCallAppName(exe, _settings == null ? null : _settings.CallApps);
        }
    }
}
