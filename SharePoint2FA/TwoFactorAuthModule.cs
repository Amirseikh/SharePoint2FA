using System;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Linq;
using SharePoint2FA.Models;
using SharePoint2FA.Services;

namespace SharePoint2FA
{
    /// <summary>
    /// IHttpModule registered in SharePoint's web.config.
    ///
    /// Flow:
    ///  1. PostAuthenticateRequest fires after Windows Auth authenticates the user.
    ///  2. Skip: anonymous, bypass paths, system accounts, exempt users/groups.
    ///  3. Check encrypted DPAPI cookie — if valid, let user through.
    ///  4. Otherwise redirect to 2FA page with ReturnUrl.
    ///  5. On successful OTP: ASPX sets encrypted cookie (8h expiry).
    ///  6. Subsequent requests: cookie check passes, no 2FA prompt.
    /// </summary>
    public class TwoFactorAuthModule : IHttpModule
    {
        internal const string CookieName = "SP2FA_Verified";

        public void Init(HttpApplication context)
        {
            context.PostAuthenticateRequest += OnPostAuthenticate;
        }

        private void OnPostAuthenticate(object sender, EventArgs e)
        {
            var app     = (HttpApplication)sender;
            var ctx     = app.Context;
            var request = ctx.Request;
            var user    = ctx.User;

            // 1. Skip anonymous
            if (user == null || !user.Identity.IsAuthenticated) return;

            string identity = user.Identity.Name;
            string rawPath  = request.Url.AbsolutePath;
            var    cfg      = TwoFactorConfig.Current;

            // 2. Skip bypass paths
            if (cfg.BypassPaths.Any(bp =>
                rawPath.StartsWith(bp.Trim(), StringComparison.OrdinalIgnoreCase)))
                return;

            // 3. Skip system/service accounts
            if (IsSystemAccount(identity)) return;

            // 4. Skip exempt users (SAM account names in SP2FA:ExemptUsers)
            if (cfg.ExemptUsers != null && cfg.ExemptUsers.Length > 0)
            {
                string sam = AdUserService.StripDomain(identity).ToLowerInvariant();
                if (Array.Exists(cfg.ExemptUsers,
                    u => u.Trim().ToLowerInvariant() == sam))
                    return;
            }

            // 5. Skip exempt groups (AD group names in SP2FA:ExemptGroups)
            if (cfg.ExemptGroups != null && cfg.ExemptGroups.Length > 0)
            {
                try
                {
                    if (new AdUserService().IsInAnyGroup(identity, cfg.ExemptGroups))
                        return;
                }
                catch { }
            }

            // 6. Check encrypted cookie
            try
            {
                var cookie = request.Cookies[CookieName];
                if (cookie != null)
                {
                    string decrypted = DecryptValue(cookie.Value);
                    if (!string.IsNullOrEmpty(decrypted) && decrypted == identity)
                        return;
                }
            }
            catch { }

            // 7. Redirect to 2FA page
            string returnUrl = Uri.EscapeDataString(request.Url.PathAndQuery);
            ctx.Response.Redirect(
                $"{cfg.TwoFactorPagePath}?ReturnUrl={returnUrl}",
                endResponse: true);
        }

        // ── DPAPI cookie helpers ───────────────────────────────────────────────

        internal static string EncryptValue(string value)
        {
            try
            {
                byte[] encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine);
                return Convert.ToBase64String(encrypted);
            }
            catch { return null; }
        }

        internal static string DecryptValue(string encrypted)
        {
            try
            {
                byte[] decrypted = ProtectedData.Unprotect(
                    Convert.FromBase64String(encrypted), null, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch { return null; }
        }

        // ── System account detection ──────────────────────────────────────────

        private static bool IsSystemAccount(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string lower = name.ToLowerInvariant();
            return lower.EndsWith("$")
                || lower.Contains("iusr")
                || lower.Contains("iis apppool\\")
                || lower == "nt authority\\system"
                || lower == "nt authority\\network service"
                || lower == "nt authority\\local service";
        }

        public void Dispose() { }
    }
}
