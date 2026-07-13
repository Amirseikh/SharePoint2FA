using System;
using System.Configuration;

namespace SharePoint2FA.Models
{
    /// <summary>
    /// Reads all SharePoint2FA settings from web.config appSettings.
    /// All keys use the prefix "TwoFactor:" to avoid conflicts with SharePoint's own keys.
    /// Singleton — call Reload() if you update web.config without iisreset.
    /// </summary>
    public class TwoFactorConfig
    {
        private static TwoFactorConfig _instance;
        private static readonly object _lock = new object();

        // ── Active Directory ──────────────────────────────────────────────────
        /// <summary>e.g. "yourdomain.local" or "YOURDOMAIN"</summary>
        public string ADDomain                 { get; private set; }
        /// <summary>Primary AD attribute for mobile number. Default: "mobile"</summary>
        public string ADPhoneAttribute         { get; private set; }
        /// <summary>Fallback AD attribute if primary is empty. Default: "extensionAttribute13"</summary>
        public string ADFallbackPhoneAttribute { get; private set; }
        /// <summary>Service account for AD queries. Leave empty to use app pool identity.</summary>
        public string ADUsername               { get; private set; }
        public string ADPassword               { get; private set; }

        // ── SMS API ───────────────────────────────────────────────────────────
        /// <summary>Full URL of your SMS provider REST endpoint.</summary>
        public string SmsApiUrl                { get; private set; }
        /// <summary>API key or Bearer token for your SMS provider.</summary>
        public string SmsApiKey                { get; private set; }
        public string SmsApiPassword           { get; private set; }
        public string SmsSenderId              { get; private set; }
        public string SmsUsername              { get; private set; }

        public string SmsExtraHeaders          { get; private set; }

        // ── OTP Behaviour ─────────────────────────────────────────────────────
        public int OtpLength                   { get; private set; }
        public int OtpExpiryMinutes            { get; private set; }
        public int MaxAttempts                 { get; private set; }
        public int MaxResends                  { get; private set; }
        public int ResendCooldownSeconds       { get; private set; }

        // ── Module Behaviour ──────────────────────────────────────────────────
        public string[] BypassPaths            { get; private set; }
        public string[] ExemptGroups           { get; private set; }
        public string[] ExemptUsers            { get; private set; }
        public bool     EnableAuditLog         { get; private set; }
        public string   TwoFactorPagePath      { get; private set; }

        // ── UI Messages ───────────────────────────────────────────────────────
        public string MsgNoMobileFound         { get; private set; }
        public string MsgInvalidMobile         { get; private set; }
        public string MsgSmsFailed             { get; private set; }
        public string MsgNoMobileTitle         { get; private set; }

        // ── Singleton ─────────────────────────────────────────────────────────
        public static TwoFactorConfig Current
        {
            get
            {
                if (_instance == null)
                    lock (_lock) { if (_instance == null) _instance = Load(); }
                return _instance;
            }
        }

        public static void Reload() { lock (_lock) { _instance = Load(); } }

        private static TwoFactorConfig Load()
        {
            var cfg = ConfigurationManager.AppSettings;
            return new TwoFactorConfig
            {
                // AD
                ADDomain                 = Require(cfg, "TwoFactor:ADDomain"),
                ADPhoneAttribute         = cfg["TwoFactor:ADPhoneAttribute"]         ?? "mobile",
                ADFallbackPhoneAttribute = cfg["TwoFactor:ADFallbackPhoneAttribute"] ?? "extensionAttribute13",
                ADUsername               = cfg["TwoFactor:ADUsername"]               ?? "",
                ADPassword               = cfg["TwoFactor:ADPassword"]               ?? "",

                // SMS
                SmsApiUrl      = Require(cfg, "TwoFactor:SmsApiUrl"),
                SmsApiKey      = cfg["TwoFactor:SmsApiKey"]      ?? "",
                SmsApiPassword = cfg["TwoFactor:SmsApiPassword"] ?? "",
                SmsSenderId    = cfg["TwoFactor:SmsSenderId"]    ?? "Portal",
                SmsUsername    = cfg["TwoFactor:SmsUsername"]    ?? "",
                SmsExtraHeaders = cfg["TwoFactor:SmsExtraHeaders"] ?? "",

                // OTP
                OtpLength             = Int(cfg, "TwoFactor:OtpLength",             6),
                OtpExpiryMinutes      = Int(cfg, "TwoFactor:OtpExpiryMinutes",      5),
                MaxAttempts           = Int(cfg, "TwoFactor:MaxAttempts",            3),
                MaxResends            = Int(cfg, "TwoFactor:MaxResends",             3),
                ResendCooldownSeconds = Int(cfg, "TwoFactor:ResendCooldownSeconds", 120),

                // Module
                BypassPaths     = List(cfg["TwoFactor:BypassPaths"] ??
                    "/_layouts/15/TwoFactor/,/_vti_bin/,/_api/,/_catalogs/,/_controltemplates/,/favicon.ico,/robots.txt"),
                ExemptGroups    = List(cfg["TwoFactor:ExemptGroups"] ?? ""),
                ExemptUsers     = List(cfg["TwoFactor:ExemptUsers"]  ?? ""),
                EnableAuditLog  = bool.TryParse(cfg["TwoFactor:EnableAuditLog"], out bool al) && al,
                TwoFactorPagePath = cfg["TwoFactor:TwoFactorPagePath"] ??
                    "/_layouts/15/TwoFactor/TwoFactorAuth.aspx",

                // Messages
                MsgNoMobileFound = cfg["TwoFactor:MsgNoMobileFound"] ??
                    "We could not find a mobile number associated with your account in Active Directory. " +
                    "Please contact your IT/AD team to register your mobile number so you can access the portal.",
                MsgInvalidMobile = cfg["TwoFactor:MsgInvalidMobile"] ??
                    "A number was found on your account but it does not appear to be a valid mobile number. " +
                    "Please contact your IT/AD team to correct your mobile number so you can access the portal.",
                MsgSmsFailed     = cfg["TwoFactor:MsgSmsFailed"] ??
                    "Failed to send verification code. Please try again or contact IT support.",
                MsgNoMobileTitle = cfg["TwoFactor:MsgNoMobileTitle"] ??
                    "Mobile Number Required",
            };
        }

        private static string Require(System.Collections.Specialized.NameValueCollection c, string key)
        {
            string v = c[key];
            if (string.IsNullOrWhiteSpace(v))
                throw new ConfigurationErrorsException(
                    $"[SharePoint2FA] Required appSetting '{key}' is missing in web.config.");
            return v;
        }

        private static int Int(System.Collections.Specialized.NameValueCollection c,
            string key, int def) => int.TryParse(c[key], out int v) ? v : def;

        private static string[] List(string value)
            => string.IsNullOrWhiteSpace(value)
               ? Array.Empty<string>()
               : value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
