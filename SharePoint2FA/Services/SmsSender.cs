using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using SharePoint2FA.Models;

namespace SharePoint2FA.Services
{
    /// <summary>
    /// Sends OTP via a REST SMS API.
    /// All settings read from web.config (TwoFactor: prefix).
    ///
    /// Payload format (JSON POST):
    ///   { "MobileNo": "...", "Username": "...", "Password": "...", "Body": "..." }
    ///
    /// Adapt field names in the json string below to match your SMS provider.
    ///
    /// Extra HTTP headers (e.g. Sec-Fetch-Site, Referer, X-API-Key) are configured
    /// via web.config key TwoFactor:SmsExtraHeaders in format:
    ///   HeaderName:Value,HeaderName2:Value2
    /// </summary>
    public class SmsSender
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public SmsResult SendOtp(string phoneNumber, string otp)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return SmsResult.Fail("Phone number is null or empty.");

            var cfg = TwoFactorConfig.Current;
            string msg = $"Your portal verification code is: {otp}. " +
                           $"Valid for {cfg.OtpExpiryMinutes} minutes. Do not share this code.";
            string phone = phoneNumber.TrimStart('+');

            try
            {
                // ── Build JSON payload ─────────────────────────────────────────
                // Adapt field names to match your SMS provider's API spec.
                string json = "{" +
                    $"\"MobileNo\":\"{J(phone)}\"," +
                    $"\"Username\":\"{J(cfg.SmsUsername)}\"," +
                    $"\"Password\":\"{J(cfg.SmsApiPassword)}\"," +
                    $"\"Body\":\"{J(msg)}\"" +
                    "}";

                // ── Build request ──────────────────────────────────────────────
                var request = new HttpRequestMessage(HttpMethod.Post, cfg.SmsApiUrl);

                // Authorization header
                request.Headers.Add("Authorization", cfg.SmsApiKey);

                // Extra headers from web.config TwoFactor:SmsExtraHeaders
                // Format: "HeaderName:Value,HeaderName2:Value2"
                // Example: "Sec-Fetch-Site:test,Referer:abc.com"
                if (!string.IsNullOrWhiteSpace(cfg.SmsExtraHeaders))
                {
                    foreach (string header in cfg.SmsExtraHeaders.Split(','))
                    {
                        int idx = header.IndexOf(':');
                        if (idx > 0)
                        {
                            string name = header.Substring(0, idx).Trim();
                            string value = header.Substring(idx + 1).Trim();
                            if (!string.IsNullOrEmpty(name))
                                request.Headers.TryAddWithoutValidation(name, value);
                        }
                    }
                }

                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                // ── Send ───────────────────────────────────────────────────────
                var rt = _http.SendAsync(request);
                rt.Wait();
                var response = rt.Result;

                var bt = response.Content.ReadAsStringAsync();
                bt.Wait();
                string body = bt.Result;
                int code = (int)response.StatusCode;

                // ── Handle response ────────────────────────────────────────────
                if (response.IsSuccessStatusCode)
                {
                    // Some APIs return HTTP 200 with Success:false in the JSON body.
                    // Check the inner Success field if present.
                    var successMatch = Regex.Match(body,
                        "\"Success\"\\s*:\\s*(true|false)",
                        RegexOptions.IgnoreCase);

                    bool innerSuccess = !successMatch.Success ||
                                        successMatch.Groups[1].Value.ToLower() == "true";

                    if (innerSuccess)
                    {
                        AuditLogger.Log(AuditLogger.EventType.SmsSent, null,
                            $"OTP sent to {AdUserService.MaskPhone(phoneNumber)}");
                        return SmsResult.Ok();
                    }

                    // Extract Msg field from response for meaningful error
                    var msgMatch = Regex.Match(body, "\"Msg\"\\s*:\\s*\"([^\"]+)\"");
                    string apiErr = msgMatch.Success ? msgMatch.Groups[1].Value : body;

                    AuditLogger.Log(AuditLogger.EventType.SmsFailure, null,
                        $"API Success:false — {apiErr}");
                    return SmsResult.Fail(apiErr);
                }

                string err = $"SMS API returned {code}: {body}";
                AuditLogger.Log(AuditLogger.EventType.SmsFailure, null, err);
                return SmsResult.Fail(err);
            }
            catch (Exception ex)
            {
                AuditLogger.Log(AuditLogger.EventType.SmsFailure, null,
                    $"SMS exception: {ex.Message}");
                return SmsResult.Fail(ex.Message);
            }
        }

        // ── JSON string escape helper ──────────────────────────────────────────
        private static string J(string v)
            => string.IsNullOrEmpty(v) ? "" :
               v.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
    }

    public class SmsResult
    {
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }

        public static SmsResult Ok()
            => new SmsResult { Success = true };

        public static SmsResult Fail(string err)
            => new SmsResult { Success = false, ErrorMessage = err };
    }
}