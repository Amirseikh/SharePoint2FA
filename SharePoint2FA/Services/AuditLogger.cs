using System;
using System.Diagnostics;

namespace SharePoint2FA.Services
{
    public static class AuditLogger
    {
        private const string Source  = "SP2FA";
        private const string LogName = "Application";

        public enum EventType
        {
            OtpGenerated      = 1001,
            OtpValidSuccess   = 1002,
            OtpValidFailed    = 1003,
            OtpExpired        = 1004,
            OtpMaxAttempts    = 1005,
            SmsSent           = 1006,
            SmsFailure        = 1007,
            TwoFactorBypassed = 1008,
            Error             = 9001
        }

        public static void Log(EventType type, string username, string detail)
        {
            try { if (!Models.TwoFactorConfig.Current.EnableAuditLog) return; }
            catch { return; }

            try
            {
                if (!EventLog.SourceExists(Source))
                    EventLog.CreateEventSource(Source, LogName);

                var level = type == EventType.Error || type == EventType.SmsFailure
                    ? EventLogEntryType.Error
                    : type == EventType.OtpValidFailed || type == EventType.OtpMaxAttempts
                        ? EventLogEntryType.Warning
                        : EventLogEntryType.Information;

                EventLog.WriteEntry(Source,
                    $"[SP2FA] Event: {type}\n" +
                    $"User:   {(string.IsNullOrEmpty(username) ? "(system)" : Clean(username))}\n" +
                    $"Detail: {detail}\n" +
                    $"Time:   {DateTime.UtcNow:u}",
                    level, (int)type);
            }
            catch { }
        }

        private static string Clean(string s)
            => s?.Replace("\r", "").Replace("\n", "").Trim() ?? "";
    }
}
