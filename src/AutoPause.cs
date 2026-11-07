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
        private static readonly string[] CallApps = new string[]
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

            string reason = null;
            if (Native.IsRemoteSession())
                reason = "Удалённый рабочий стол";
            else if (_settings.PauseOnFullscreen && Native.IsFullscreenWindow(foreground))
                reason = "Полноэкранное приложение";
            else if (_settings.PauseOnCall && IsCallApp(foreground))
                reason = "Звонок или показ экрана";
            else if (Native.IsBatterySaverOn())
                reason = "Экономия энергии";
            else if (_settings.PauseOnBattery && Native.IsOnBattery())
                reason = "Питание от батареи";
            else if (_settings.RespectReduceMotion && !Native.ClientAreaAnimationEnabled())
                reason = "Уменьшено движение";

            if (reason != null)
            {
                _clearSince = 0;
                if (reason == _reason) return;
                _reason = reason;
                Log.Debug("Auto pause: " + reason);
                return;
            }

            if (_reason == null) return;
            int delay = _settings.AutoPauseResumeDelaySeconds;
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

        private bool IsCallApp(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            string exe = Native.GetWindowProcessName(hwnd);
            if (string.IsNullOrEmpty(exe)) return false;
            for (int i = 0; i < CallApps.Length; i++)
                if (string.Equals(CallApps[i], exe, StringComparison.OrdinalIgnoreCase))
                    return true;
            string[] custom = _settings.CallApps;
            if (custom != null)
            {
                for (int i = 0; i < custom.Length; i++)
                    if (string.Equals(custom[i], exe, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }
    }
}
