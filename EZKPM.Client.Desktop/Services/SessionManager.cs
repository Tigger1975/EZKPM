using System;
using System.Linq;
using Avalonia.Threading;

namespace EZKPM.Client.Desktop.Services
{
    public static class SessionManager
    {
        private static DispatcherTimer _idleTimer;
        private static DispatcherTimer _minimizedTimer;
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5); // 5 minutes FA 14
        private static readonly TimeSpan MinimizedTimeout = TimeSpan.FromMinutes(1); // 1 minute when minimized

        // FA 14: Sensitive Session is always locked by default until explicitly authenticated
        public static bool IsLocked { get; private set; } = true;
        public static bool RequiresStartupAuth { get; private set; } = false;

        public static event Action OnSessionLocked;
        public static event Action OnSessionUnlocked;

        public static void Initialize()
        {
            try
            {
                // Initial lock check: If app started >5 minutes after Windows login (explorer start for this session)
                try
                {
                    var currentSessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
                    var explorer = System.Diagnostics.Process.GetProcessesByName("explorer")
                                    .FirstOrDefault(p => p.SessionId == currentSessionId);
                    
                    if (explorer != null)
                    {
                        if ((DateTime.Now - explorer.StartTime).TotalMinutes > 5)
                        {
                            RequiresStartupAuth = true;
                            Program.LogDebug("App started >5min nach Anmeldung. Sofortige App-Start Authentifizierung erforderlich.");
                        }
                        else
                        {
                            Program.LogDebug("App started <5min nach Anmeldung. App startet, aber sensible Daten bleiben gesperrt.");
                        }
                    }
                }
                catch { }

                _idleTimer = new DispatcherTimer { Interval = IdleTimeout };
                _idleTimer.Tick += (s, e) => LockSession("UI Session locked due to inactivity.");
                _idleTimer.Start();

                _minimizedTimer = new DispatcherTimer { Interval = MinimizedTimeout };
                _minimizedTimer.Tick += (s, e) => 
                {
                    _minimizedTimer.Stop();
                    LockSession("UI Session locked due to being minimized for 1 minute.");
                };
            }
            catch (Exception ex)
            {
                Program.LogDebug($"Exception in SessionManager.Initialize: {ex}");
            }
        }

        public static void LockSession(string reason = "")
        {
            IsLocked = true;
            _idleTimer?.Stop();
            _minimizedTimer?.Stop();
            if (!string.IsNullOrEmpty(reason))
            {
                Program.LogDebug(reason);
            }
            OnSessionLocked?.Invoke();
        }

        public static void HandleWindowStateChanged(Avalonia.Controls.WindowState state, bool isTray = false)
        {
            if (isTray)
            {
                LockSession("UI Session locked due to tray minimization.");
                return;
            }

            if (state == Avalonia.Controls.WindowState.Minimized)
            {
                _minimizedTimer?.Start();
            }
            else
            {
                _minimizedTimer?.Stop();
                RegisterActivity();
            }
        }

        public static void RegisterActivity()
        {
            if (!IsLocked && _idleTimer != null)
            {
                _idleTimer.Stop();
                _idleTimer.Start();
            }
        }

        public static bool EnsureAuthenticated(string reason)
        {
            if (!IsLocked)
                return true;

            bool success = WindowsAuthService.PromptForCredentials(reason);
            if (success)
            {
                IsLocked = false;
                RegisterActivity();
                OnSessionUnlocked?.Invoke();
                return true;
            }
            
            return false;
        }
    }
}
