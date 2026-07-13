using System;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using SharePoint2FA.Models;
using SharePoint2FA.Services;

namespace SharePoint2FA.Layouts.TwoFactor
{
    public partial class TwoFactorAuthPage : Page
    {
        // ── Controls ──────────────────────────────────────────────────────────
        protected Panel   pnlRequestOtp;
        protected Panel   pnlVerifyOtp;
        protected Panel   pnlNophone;
        protected Panel   pnlLocked;

        protected Literal litDisplayName;
        protected Literal litMaskedPhone;
        protected Literal litMaskedPhone2;
        protected Literal litExpiryMinutes;
        protected Literal litNoPhoneMessage;

        protected TextBox txtOtp;
        protected Button  btnSendOtp;
        protected Button  btnVerify;
        protected Button  btnResend;

        protected Label lblSendError;
        protected Label lblVerifyError;
        protected Label lblAttemptsLeft;
        protected Label lblResendInfo;

        // ── Services ──────────────────────────────────────────────────────────
        private readonly OtpService    _otp     = new OtpService();
        private readonly AdUserService _ad      = new AdUserService();
        private readonly SmsSender     _sms     = new SmsSender();

        // ── Page Load ─────────────────────────────────────────────────────────
        protected void Page_Load(object sender, EventArgs e)
        {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            Response.Cache.SetExpires(DateTime.UtcNow.AddDays(-1));

            if (IsAlreadyVerified()) { RedirectToDestination(); return; }
            if (!IsPostBack) InitialisePageState();
        }

        // ── Event: Send OTP ───────────────────────────────────────────────────
        protected void btnSendOtp_Click(object sender, EventArgs e)
        {
            string username  = User.Identity.Name;
            var    cfg       = TwoFactorConfig.Current;

            // Check resend limit
            if (_otp.GetResendCount(username) >= cfg.MaxResends)
            {
                ShowPanel(pnlLocked);
                AuditLogger.Log(AuditLogger.EventType.OtpMaxAttempts, username, "Resend limit exceeded.");
                return;
            }

            // Check cooldown
            int cooldown = _otp.GetResendCooldownRemaining(username);
            if (cooldown > 0)
            {
                lblSendError.Text = $"Please wait {cooldown} second(s) before requesting a new code.";
                ShowVerifyPanel(_otp.GetMaskedPhone(username) ?? "");
                return;
            }

            // AD lookup
            string displayName = _ad.GetDisplayName(username);
            var    lookup      = _ad.GetPhoneNumberWithStatus(username);

            if (lookup.Status == AdUserService.PhoneLookupStatus.NotFound)
            {
                ShowNoPhonePanel(displayName, "not_found");
                return;
            }
            if (lookup.Status == AdUserService.PhoneLookupStatus.InvalidNumber)
            {
                ShowNoPhonePanel(displayName, "invalid");
                return;
            }

            // Generate and send OTP
            string otp        = _otp.GenerateOtp(username);
            string masked     = AdUserService.MaskPhone(lookup.Phone);
            _otp.SetMaskedPhone(username, masked);

            SmsResult result;
            try   { result = _sms.SendOtp(lookup.Phone, otp); }
            catch (Exception ex)
            {
                lblSendError.Text = cfg.MsgSmsFailed;
                AuditLogger.Log(AuditLogger.EventType.SmsFailure, username, ex.Message);
                return;
            }

            if (result.Success)
            {
                ShowVerifyPanel(masked);
                AuditLogger.Log(AuditLogger.EventType.SmsSent, username, $"OTP sent to {masked}");
            }
            else
            {
                lblSendError.Text = cfg.MsgSmsFailed;
                AuditLogger.Log(AuditLogger.EventType.SmsFailure, username, result.ErrorMessage);
            }
        }

        // ── Event: Verify OTP ─────────────────────────────────────────────────
        protected void btnVerify_Click(object sender, EventArgs e)
        {
            string username = User.Identity.Name;
            string input    = txtOtp.Text?.Trim() ?? "";
            var    result   = _otp.ValidateOtp(username, input);

            switch (result)
            {
                case OtpValidationResult.Valid:
                    Response.Cookies.Add(new HttpCookie(TwoFactorAuthModule.CookieName)
                    {
                        Value    = TwoFactorAuthModule.EncryptValue(username),
                        HttpOnly = true,
                        Secure   = Request.IsSecureConnection,
                        Expires  = DateTime.Now.AddHours(8)
                    });
                    AuditLogger.Log(AuditLogger.EventType.OtpValidSuccess, username, "Access granted.");
                    RedirectToDestination();
                    return;

                case OtpValidationResult.Invalid:
                    int rem = _otp.RemainingAttempts(username);
                    lblVerifyError.Text  = "Incorrect code. Please try again.";
                    lblAttemptsLeft.Text = $"{rem} attempt(s) remaining.";
                    AuditLogger.Log(AuditLogger.EventType.OtpValidFailed, username, $"{rem} attempts left.");
                    break;

                case OtpValidationResult.Expired:
                    lblVerifyError.Text = "Your code has expired. Please request a new one.";
                    ShowPanel(pnlRequestOtp);
                    break;

                case OtpValidationResult.MaxAttemptsExceeded:
                    ShowPanel(pnlLocked);
                    AuditLogger.Log(AuditLogger.EventType.OtpMaxAttempts, username, "Max attempts exceeded.");
                    break;

                case OtpValidationResult.NotFound:
                    lblVerifyError.Text = "Session expired. Please request a new code.";
                    ShowPanel(pnlRequestOtp);
                    break;
            }
        }

        // ── Event: Resend ─────────────────────────────────────────────────────
        protected void btnResend_Click(object sender, EventArgs e)
            => btnSendOtp_Click(sender, e);

        // ── Helpers ───────────────────────────────────────────────────────────

        private void InitialisePageState()
        {
            string username    = User.Identity.Name;
            string displayName = _ad.GetDisplayName(username);

            if (litDisplayName != null)
                litDisplayName.Text = Server.HtmlEncode(displayName);

            var lookup = _ad.GetPhoneNumberWithStatus(username);

            switch (lookup.Status)
            {
                case AdUserService.PhoneLookupStatus.Found:
                    if (litMaskedPhone != null)
                        litMaskedPhone.Text = Server.HtmlEncode(AdUserService.MaskPhone(lookup.Phone));
                    ShowPanel(pnlRequestOtp);
                    break;

                case AdUserService.PhoneLookupStatus.NotFound:
                    ShowNoPhonePanel(displayName, "not_found");
                    break;

                case AdUserService.PhoneLookupStatus.InvalidNumber:
                    ShowNoPhonePanel(displayName, "invalid");
                    break;
            }
        }

        private void ShowNoPhonePanel(string displayName, string reason)
        {
            pnlRequestOtp.Visible = false;
            pnlVerifyOtp.Visible  = false;
            pnlNophone.Visible    = true;
            pnlLocked.Visible     = false;

            var    cfg    = TwoFactorConfig.Current;
            string name   = Server.HtmlEncode(displayName);
            string title  = Server.HtmlEncode(cfg.MsgNoMobileTitle);
            string detail = reason == "invalid"
                ? Server.HtmlEncode(cfg.MsgInvalidMobile)
                : Server.HtmlEncode(cfg.MsgNoMobileFound);

            if (litNoPhoneMessage != null)
                litNoPhoneMessage.Text =
                    $"<strong>Hello {name},</strong><br /><br />" +
                    $"<strong>{title}</strong><br /><br />" +
                    $"{detail}";
        }

        private void ShowVerifyPanel(string maskedPhone)
        {
            pnlRequestOtp.Visible = false;
            pnlVerifyOtp.Visible  = true;
            pnlNophone.Visible    = false;
            pnlLocked.Visible     = false;

            if (litMaskedPhone2  != null) litMaskedPhone2.Text  = Server.HtmlEncode(maskedPhone);
            if (litExpiryMinutes != null) litExpiryMinutes.Text = TwoFactorConfig.Current.OtpExpiryMinutes.ToString();

            string username   = User.Identity.Name;
            int    resends    = _otp.GetResendCount(username);
            int    maxResends = TwoFactorConfig.Current.MaxResends;
            int    cooldown   = _otp.GetResendCooldownRemaining(username);

            if (lblResendInfo != null)
                lblResendInfo.Text = resends > 0 && cooldown == 0
                    ? $"Code resent. ({resends}/{maxResends} resends used)"
                    : $"({resends}/{maxResends} resends used)";

            if (btnResend != null)
            {
                btnResend.Enabled = (cooldown == 0) && (resends < maxResends);
                btnResend.Attributes["data-cooldown"] = cooldown.ToString();
            }
        }

        private void ShowPanel(Panel panel)
        {
            pnlRequestOtp.Visible = (panel == pnlRequestOtp);
            pnlVerifyOtp.Visible  = (panel == pnlVerifyOtp);
            pnlNophone.Visible    = (panel == pnlNophone);
            pnlLocked.Visible     = (panel == pnlLocked);
        }

        private bool IsAlreadyVerified()
        {
            try
            {
                var cookie = Request.Cookies[TwoFactorAuthModule.CookieName];
                if (cookie == null) return false;
                string dec = TwoFactorAuthModule.DecryptValue(cookie.Value);
                return !string.IsNullOrEmpty(dec) && dec == User.Identity.Name;
            }
            catch { return false; }
        }

        private void RedirectToDestination()
        {
            string url = Request.QueryString["ReturnUrl"];
            if (!string.IsNullOrEmpty(url))
            {
                url = Uri.UnescapeDataString(url);
                if (url.StartsWith("/") && !url.StartsWith("//"))
                {
                    Response.Redirect(url, endResponse: true);
                    return;
                }
            }
            Response.Redirect("/", endResponse: true);
        }
    }
}
