<%@ Assembly Name="SharePoint2FA, Version=1.0.0.0, Culture=neutral, PublicKeyToken=YOUR_PUBLIC_KEY_TOKEN" %>
<%@ Page Language="C#"
    AutoEventWireup="true"
    CodeBehind="TwoFactorAuth.aspx.cs"
    Inherits="SharePoint2FA.Layouts.TwoFactor.TwoFactorAuthPage"
    EnableSessionState="True" %>
<%@ OutputCache Location="None" %>

<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="robots" content="noindex, nofollow" />
    <title>Two-Factor Authentication</title>
    <link rel="stylesheet" href="/_layouts/15/TwoFactor/TwoFactorAuth.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="sp2fa-page">
    <div class="sp2fa-card">

        <!-- Header -->
        <div class="sp2fa-header">
            <div class="sp2fa-shield">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor"
                     stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M12 2L3 7v5c0 5.25 3.75 10.15 9 11.25C17.25 22.15 21 17.25 21 12V7L12 2z"/>
                </svg>
            </div>
            <h1 class="sp2fa-title">Two-Factor Authentication</h1>
            <p class="sp2fa-subtitle">Secure Portal Access</p>
        </div>

        <!-- No phone / invalid number -->
        <asp:Panel ID="pnlNophone" runat="server" Visible="false" CssClass="sp2fa-step">
            <div class="sp2fa-alert sp2fa-alert--error" role="alert">
                <asp:Literal ID="litNoPhoneMessage" runat="server" />
            </div>
        </asp:Panel>

        <!-- Step 1: Send OTP -->
        <asp:Panel ID="pnlRequestOtp" runat="server" CssClass="sp2fa-step">
            <p class="sp2fa-welcome">
                Welcome, <strong><asp:Literal ID="litDisplayName" runat="server" /></strong>
            </p>
            <p class="sp2fa-info">
                To verify your identity, a one-time verification code
                will be sent to your registered mobile number:
            </p>
            <div class="sp2fa-phone-box">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor"
                     stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <rect x="5" y="2" width="14" height="20" rx="2"/>
                    <line x1="12" y1="18" x2="12" y2="18.01"/>
                </svg>
                <asp:Literal ID="litMaskedPhone" runat="server" />
            </div>
            <asp:Button ID="btnSendOtp" runat="server"
                Text="Send Verification Code"
                OnClick="btnSendOtp_Click"
                CssClass="sp2fa-btn sp2fa-btn--primary" />
            <asp:Label ID="lblSendError" runat="server"
                CssClass="sp2fa-error" role="alert" />
        </asp:Panel>

        <!-- Step 2: Verify OTP -->
        <asp:Panel ID="pnlVerifyOtp" runat="server" Visible="false" CssClass="sp2fa-step">
            <p class="sp2fa-info">
                Enter the 6-digit code sent to
                <strong><asp:Literal ID="litMaskedPhone2" runat="server" /></strong>
            </p>
            <div class="sp2fa-expiry">
                Code expires in
                <strong><asp:Literal ID="litExpiryMinutes" runat="server" /> minutes</strong>
            </div>
            <asp:TextBox ID="txtOtp" runat="server"
                MaxLength="6"
                CssClass="sp2fa-otp"
                autocomplete="one-time-code"
                inputmode="numeric"
                placeholder="_ _ _ _ _ _"
                aria-label="Verification code"
                aria-required="true" />
            <p class="sp2fa-otp-hint">Enter the 6-digit code from your SMS</p>
            <div class="sp2fa-actions">
                <asp:Button ID="btnVerify" runat="server"
                    Text="Verify"
                    OnClick="btnVerify_Click"
                    CssClass="sp2fa-btn sp2fa-btn--primary" />
                <asp:Button ID="btnResend" runat="server"
                    Text="Resend Code"
                    OnClick="btnResend_Click"
                    CssClass="sp2fa-btn sp2fa-btn--secondary"
                    CausesValidation="false" />
            </div>
            <span id="resendCountdown" class="sp2fa-info-sm"></span>
            <asp:Label ID="lblVerifyError"  runat="server" CssClass="sp2fa-error"    role="alert" />
            <asp:Label ID="lblAttemptsLeft" runat="server" CssClass="sp2fa-attempts" />
            <asp:Label ID="lblResendInfo"   runat="server" CssClass="sp2fa-info-sm"  />
        </asp:Panel>

        <!-- Locked out -->
        <asp:Panel ID="pnlLocked" runat="server" Visible="false" CssClass="sp2fa-step">
            <div class="sp2fa-alert sp2fa-alert--warning" role="alert">
                <strong>Too many failed attempts.</strong><br />
                Please wait a few minutes and try again,
                or contact your IT Helpdesk for assistance.
            </div>
        </asp:Panel>

        <!-- Footer -->
        <div class="sp2fa-footer">Secure Two-Factor Authentication</div>

    </div>
</div>

<script>
(function () {
    var otp = document.getElementById('txtOtp');
    if (otp) {
        otp.addEventListener('input', function () {
            this.value = this.value.replace(/[^0-9]/g, '').substring(0, 6);
        });
        setTimeout(function () { otp.focus(); }, 100);
    }

    var resendBtn     = document.getElementById('btnResend');
    var countdownSpan = document.getElementById('resendCountdown');

    if (resendBtn) {
        var cooldown = parseInt(resendBtn.getAttribute('data-cooldown'), 10) || 0;
        if (cooldown > 0) {
            resendBtn.disabled = true;
            var endTime = Date.now() + (cooldown * 1000);
            var tick = function () {
                var remaining = Math.ceil((endTime - Date.now()) / 1000);
                if (remaining <= 0) {
                    clearInterval(timer);
                    resendBtn.disabled = false;
                    resendBtn.removeAttribute('disabled');
                    resendBtn.classList.remove('aspNetDisabled');
                    if (countdownSpan) countdownSpan.textContent = '';
                } else {
                    if (countdownSpan)
                        countdownSpan.textContent = 'You can resend in ' + remaining + 's';
                }
            };
            tick();
            var timer = setInterval(tick, 1000);
        }
    }
})();
</script>
</form>
</body>
</html>
