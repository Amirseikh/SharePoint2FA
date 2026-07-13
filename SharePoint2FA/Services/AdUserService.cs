using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Text.RegularExpressions;
using SharePoint2FA.Models;

namespace SharePoint2FA.Services
{
    public class AdUserService
    {
        // ── Phone Lookup Result ───────────────────────────────────────────────

        public enum PhoneLookupStatus { Found, NotFound, InvalidNumber }

        public class PhoneLookupResult
        {
            public string            Phone  { get; set; }
            public PhoneLookupStatus Status { get; set; }
        }

        // ── Phone Lookup ──────────────────────────────────────────────────────

        /// <summary>
        /// Looks up mobile number from Active Directory.
        ///
        /// Priority 1: SP2FA:ADPhoneAttribute (default: "mobile")
        ///   → If found and valid → return it
        ///   → If found but invalid, or empty → check fallback
        ///
        /// Priority 2: SP2FA:ADFallbackPhoneAttribute (default: "extensionAttribute13")
        ///   → If found and valid → return it
        ///   → If found but invalid → return InvalidNumber
        ///   → If empty → return NotFound
        ///
        /// Validation: must be a Saudi mobile number starting with 5 after country code.
        /// </summary>
        public PhoneLookupResult GetPhoneNumberWithStatus(string username)
        {
            string samAccount   = StripDomain(username);
            string attr         = TwoFactorConfig.Current.ADPhoneAttribute;
            string fallbackAttr = TwoFactorConfig.Current.ADFallbackPhoneAttribute;

            try
            {
                SearchResult result = QueryAD(samAccount, new[] { attr, fallbackAttr, "displayName" });

                if (result == null)
                    return new PhoneLookupResult { Status = PhoneLookupStatus.NotFound };

                // ── Priority 1: primary attribute ─────────────────────────────
                string primaryRaw = GetProp(result, attr);

                if (!string.IsNullOrWhiteSpace(primaryRaw))
                {
                    string normalized = NormalizePhone(primaryRaw);
                    if (!string.IsNullOrEmpty(normalized))
                        return new PhoneLookupResult
                        {
                            Phone  = normalized,
                            Status = PhoneLookupStatus.Found
                        };

                    AuditLogger.Log(AuditLogger.EventType.Error, username,
                        $"[{attr}] value is not a valid mobile number — checking [{fallbackAttr}].");
                }
                else
                {
                    AuditLogger.Log(AuditLogger.EventType.Error, username,
                        $"[{attr}] is empty — checking [{fallbackAttr}].");
                }

                // ── Priority 2: fallback attribute ────────────────────────────
                string fallbackRaw = GetProp(result, fallbackAttr);

                if (string.IsNullOrWhiteSpace(fallbackRaw))
                {
                    AuditLogger.Log(AuditLogger.EventType.Error, username,
                        $"[{fallbackAttr}] is also empty — no mobile number found.");
                    return new PhoneLookupResult { Status = PhoneLookupStatus.NotFound };
                }

                string fallbackNormalized = NormalizePhone(fallbackRaw);
                if (!string.IsNullOrEmpty(fallbackNormalized))
                {
                    AuditLogger.Log(AuditLogger.EventType.Error, username,
                        $"Using [{fallbackAttr}]: {MaskPhone(fallbackNormalized)}");
                    return new PhoneLookupResult
                    {
                        Phone  = fallbackNormalized,
                        Status = PhoneLookupStatus.Found
                    };
                }

                AuditLogger.Log(AuditLogger.EventType.Error, username,
                    $"[{fallbackAttr}] value is also not a valid mobile number.");
                return new PhoneLookupResult { Status = PhoneLookupStatus.InvalidNumber };
            }
            catch (Exception ex)
            {
                AuditLogger.Log(AuditLogger.EventType.Error, username,
                    $"AD phone lookup failed: {ex.Message}");
                return new PhoneLookupResult { Status = PhoneLookupStatus.NotFound };
            }
        }

        // ── Display Name ──────────────────────────────────────────────────────

        public string GetDisplayName(string username)
        {
            string samAccount = StripDomain(username);
            try
            {
                SearchResult result = QueryAD(samAccount, new[] { "displayName" });
                return GetProp(result, "displayName") ?? samAccount;
            }
            catch { return samAccount; }
        }

        // ── Group Membership ──────────────────────────────────────────────────

        public bool IsInAnyGroup(string username, IEnumerable<string> groupNames)
        {
            string samAccount = StripDomain(username);
            try
            {
                SearchResult userResult = QueryAD(samAccount, new[] { "memberOf" });
                if (userResult == null) return false;

                var entry = GetDirectoryEntry();

                foreach (string groupName in groupNames)
                {
                    var gs = new DirectorySearcher(entry)
                    {
                        Filter = $"(&(objectClass=group)(cn={groupName}))"
                    };
                    gs.PropertiesToLoad.Add("distinguishedName");
                    SearchResult gr = gs.FindOne();
                    if (gr == null) continue;

                    string groupDn  = GetProp(gr, "distinguishedName");
                    var    memberOf = userResult.Properties["memberOf"];
                    for (int i = 0; i < memberOf.Count; i++)
                    {
                        if (string.Equals(memberOf[i]?.ToString(), groupDn,
                            StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ── Static Helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Strips domain prefix from username.
        /// Handles: DOMAIN\user, user@domain, 0#.w|domain\user, i:0#.w|domain\user
        /// </summary>
        public static string StripDomain(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return username;
            if (username.Contains("|"))
                username = username.Substring(username.LastIndexOf('|') + 1);
            if (username.Contains("\\"))
                return username.Substring(username.IndexOf('\\') + 1);
            if (username.Contains("@"))
                return username.Substring(0, username.IndexOf('@'));
            return username;
        }

        /// <summary>Returns last 4 digits masked: **** **** 1234</summary>
        public static string MaskPhone(string rawPhone)
        {
            if (string.IsNullOrWhiteSpace(rawPhone)) return "****";
            string digits = Regex.Replace(rawPhone, @"\D", "");
            if (digits.Length < 4) return "****";
            return $"**** **** {digits.Substring(digits.Length - 4)}";
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private SearchResult QueryAD(string samAccount, string[] properties)
        {
            var searcher = new DirectorySearcher(GetDirectoryEntry())
            {
                Filter = $"(&(objectClass=user)(sAMAccountName={samAccount}))"
            };
            searcher.PropertiesToLoad.AddRange(properties);
            return searcher.FindOne();
        }

        private DirectoryEntry GetDirectoryEntry()
        {
            string domain = TwoFactorConfig.Current.ADDomain;
            string adUser = TwoFactorConfig.Current.ADUsername;
            string adPass = TwoFactorConfig.Current.ADPassword;

            return string.IsNullOrEmpty(adUser)
                ? new DirectoryEntry($"LDAP://{domain}")
                : new DirectoryEntry($"LDAP://{domain}", adUser, adPass,
                    AuthenticationTypes.Secure);
        }

        private static string GetProp(SearchResult result, string name)
            => result?.Properties[name]?.Count > 0
               ? result.Properties[name][0]?.ToString()
               : null;

        /// <summary>
        /// Validates and normalizes a Saudi mobile number to +966XXXXXXXXX format.
        ///
        /// Accepted formats:
        ///   +966 5XXXXXXXX  → +9665XXXXXXXX
        ///   00966 5XXXXXXXX → +9665XXXXXXXX
        ///   966 5XXXXXXXX   → +9665XXXXXXXX
        ///   05XXXXXXXX      → +9665XXXXXXXX
        ///   5XXXXXXXX       → +9665XXXXXXXX
        ///
        /// Saudi mobile numbers: always start with 5, exactly 9 digits after country code.
        /// Returns null if the number is not a valid Saudi mobile number.
        /// </summary>
        private static string NormalizePhone(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string cleaned = Regex.Replace(raw, @"[\s\-\(\)\+]", "");

            if (cleaned.StartsWith("00966"))      cleaned = cleaned.Substring(5);
            else if (cleaned.StartsWith("966"))   cleaned = cleaned.Substring(3);
            else if (cleaned.StartsWith("05"))    cleaned = cleaned.Substring(1);
            // Already starts with "5" — keep as-is

            // Must be exactly 9 digits starting with 5
            if (!Regex.IsMatch(cleaned, @"^5[0-9]{8}$"))
                return null;

            return "+966" + cleaned;
        }
    }
}
