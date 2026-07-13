# SharePoint2FA

> SMS-based Two-Factor Authentication for SharePoint 2019 / Subscription Edition.
> No ADFS. No WSP packages. No third-party identity provider. Just a DLL in the bin folder.

---

## How it works

```
User enters AD credentials (Windows Auth)
        ↓
IIS authenticates against Active Directory
        ↓
HTTP Module intercepts authenticated request
        ↓
Checks encrypted DPAPI cookie → valid? → allow through
        ↓  no valid cookie
Redirect to 2FA page /_layouts/15/TwoFactor/TwoFactorAuth.aspx
        ↓
Page reads mobile from AD (primary attribute → fallback attribute)
        ↓
Validates as a Saudi mobile number (+966XXXXXXXXX)
        ↓
Generates 6-digit OTP (SHA-256 + salt) → sends via SMS REST API
        ↓
User enters OTP → validated → encrypted cookie set (8-hour expiry)
        ↓
User redirected to original destination
```

---

## Features

- SMS OTP via any REST API (configurable headers and payload)
- Active Directory phone number lookup with configurable primary + fallback attributes
- Saudi mobile number validation (05X, 5X, 966X, +966X formats)
- DPAPI-encrypted cookie session tracking (no ASP.NET session dependency)
- Configurable resend cooldown and max resend limits
- Personalised "no mobile number" message with user's display name
- All messages configurable from web.config (no rebuild needed)
- Bypass paths and exempt user/group lists
- Windows Event Log audit trail
- Bin deployment — no GAC, no SharePoint solution packages

---

## Project Structure

```
SharePoint2FA.sln
SharePoint2FA/
  Models/
    OtpEntry.cs                - OTP state object (hashed, salted, expiry)
    OtpValidationResult.cs     - Validation result enum
    TwoFactorConfig.cs         - Typed web.config reader (TwoFactor: prefix)
  Services/
    OtpService.cs              - OTP generation, validation, cooldown tracking
    AdUserService.cs           - AD phone lookup with fallback + Saudi validation
    SmsSender.cs               - SMS REST API integration
    AuditLogger.cs             - Windows Event Log writer
  Layouts/TwoFactor/
    TwoFactorAuth.aspx         - Standalone 2FA page (no SharePoint master page)
    TwoFactorAuth.aspx.cs      - Code-behind
    TwoFactorAuth.css          - Clean responsive styles
  Config/
    TwoFactorConfig.xml        - web.config additions reference
  TwoFactorAuthModule.cs       - IHttpModule — intercepts every request
DeployScripts/
  Deploy-SharePoint2FA.ps1     - One-command deployment script
```

---

## Quick Start

### 1. Build

```
1. Open SharePoint2FA.sln in Visual Studio 2019 or 2022
2. Add SharePoint DLL references:
   C:\Program Files\Common Files\Microsoft Shared\Web Server Extensions\16\ISAPI\
   → Microsoft.SharePoint.dll (Copy Local = False)
   → Microsoft.SharePoint.Security.dll (Copy Local = False)
3. Project Properties → Signing → Sign assembly → New → SharePoint2FA.snk
4. Build → Rebuild Solution (Release) → 0 errors
5. Get public key token:
   sn.exe -T bin\Release\SharePoint2FA.dll
6. Update PublicKeyToken in TwoFactorAuth.aspx line 1
7. Rebuild once more
```

### 2. Adapt SmsSender.cs for your SMS provider

The `SendOtp` method sends a JSON POST. Edit the payload fields and auth header to match your provider's API:

```csharp
// Inside SendOtp() — change field names to match your provider
string json = "{" +
    $"\"MobileNo\":\"{J(phone)}\"," +      // ← your provider's field name
    $"\"Username\":\"{J(cfg.SmsUsername)}\"," +
    $"\"Password\":\"{J(cfg.SmsApiPassword)}\"," +
    $"\"Body\":\"{J(msg)}\"" +
    "}";

// Auth header — change if your provider uses X-API-Key instead of Bearer:
request.Headers.Add("Authorization", cfg.SmsApiKey);
```

### 3. Deploy to WFE server

```powershell
.\DeployScripts\Deploy-SharePoint2FA.ps1 `
    -DllPath ".\SharePoint2FA\bin\Release\SharePoint2FA.dll" `
    -WebAppPort "80"
```

### 4. Update web.config

See `Config/TwoFactorConfig.xml` for all keys. Minimum required:

```xml
<!-- Active Directory -->
<add key="TwoFactor:ADDomain"    value="yourdomain.local" />
<add key="TwoFactor:ADUsername"  value="DOMAIN\svc_account" />
<add key="TwoFactor:ADPassword"  value="password" />

<!-- SMS Provider -->
<add key="TwoFactor:SmsApiUrl"   value="https://your-sms-api.com/send" />
<add key="TwoFactor:SmsApiKey"   value="YOUR_API_KEY" />

<!-- Module registration -->
<add name="TwoFactorAuthModule"
     type="SharePoint2FA.TwoFactorAuthModule, SharePoint2FA, Version=1.0.0.0,
           Culture=neutral, PublicKeyToken=YOUR_TOKEN"
     preCondition="managedHandler" />
```

---

## Configuration Reference

All settings use the `TwoFactor:` prefix in `web.config` `<appSettings>`.

| Key | Default | Description |
|-----|---------|-------------|
| `TwoFactor:ADDomain` | *(required)* | AD domain name |
| `TwoFactor:ADPhoneAttribute` | `mobile` | Primary AD attribute for mobile number |
| `TwoFactor:ADFallbackPhoneAttribute` | `extensionAttribute13` | Fallback AD attribute if primary is empty |
| `TwoFactor:ADUsername` | *(app pool)* | Service account for AD queries |
| `TwoFactor:SmsApiUrl` | *(required)* | SMS provider REST endpoint |
| `TwoFactor:SmsApiKey` | — | API key / Bearer token |
| `TwoFactor:OtpLength` | `6` | OTP digit count |
| `TwoFactor:OtpExpiryMinutes` | `5` | OTP validity window |
| `TwoFactor:MaxAttempts` | `3` | Failed attempts before OTP invalidated |
| `TwoFactor:MaxResends` | `3` | Maximum resend requests per session |
| `TwoFactor:ResendCooldownSeconds` | `120` | Seconds between resend requests |
| `TwoFactor:BypassPaths` | `/_layouts/15/TwoFactor/,...` | URL prefixes that skip 2FA |
| `TwoFactor:ExemptUsers` | — | SAM account names that skip 2FA |
| `TwoFactor:ExemptGroups` | — | AD group names whose members skip 2FA |
| `TwoFactor:EnableAuditLog` | `true` | Write events to Windows Event Log |
| `TwoFactor:MsgNoMobileFound` | *(default text)* | Message when no mobile found in AD |
| `TwoFactor:MsgInvalidMobile` | *(default text)* | Message when number fails validation |
| `TwoFactor:MsgSmsFailed` | *(default text)* | Message when SMS sending fails |

---

## Phone Number Lookup Logic

```
Check TwoFactor:ADPhoneAttribute (default: "mobile")
  ├─ Found + valid Saudi number  → USE IT ✅
  ├─ Found but invalid format    → check fallback attribute
  └─ Empty                       → check fallback attribute

Check TwoFactor:ADFallbackPhoneAttribute (default: "extensionAttribute13")
  ├─ Found + valid Saudi number  → USE IT ✅
  ├─ Found but invalid format    → show "invalid number" message ❌
  └─ Empty                       → show "no mobile found" message ❌
```

**Accepted Saudi number formats:**
`+966 5XXXXXXXX` · `00966 5XXXXXXXX` · `966 5XXXXXXXX` · `05XXXXXXXX` · `5XXXXXXXX`

All normalized to `+9665XXXXXXXX` before sending.

---

## Security Controls

| Threat | Control |
|---|---|
| OTP brute force | Max 3 attempts → OTP invalidated |
| OTP replay | Deleted from cache on first valid use |
| Timing attack | Constant-time `SecureEquals()` comparison |
| OTP plaintext storage | SHA-256 + random 16-byte salt — only hash stored |
| Cookie tampering | Windows DPAPI `ProtectedData.Protect()` |
| Open redirect | ReturnUrl validated to relative paths only |
| Phone number exposure | Last 4 digits shown only (`**** **** 1234`) |
| Resend spam | Configurable cooldown (default: 120 seconds) |

---

## Multi-WFE Farm Note

OTP storage uses in-process `MemoryCache` by default. In a multi-WFE farm, OTPs stored on WFE1 are invisible to WFE2.

**Options:**
1. Replace `_GetEntry/_SetEntry/_RemoveEntry` in `OtpService.cs` with a shared SQL store
2. Configure F5/load balancer source-IP persistence

---

## License

MIT — free to use, modify, and distribute.
