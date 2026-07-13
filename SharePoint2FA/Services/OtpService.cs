using System;
using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Text;
using SharePoint2FA.Models;

namespace SharePoint2FA.Services
{
    public class OtpService
    {
        private static readonly MemoryCache _cache = new MemoryCache("SP2FA_OtpCache");

        public string GenerateOtp(string username)
        {
            var    cfg         = TwoFactorConfig.Current;
            string otp         = GenerateNumericOtp(cfg.OtpLength);
            string salt        = GenerateSalt();
            string hash        = HashOtp(otp, salt);
            var    existing    = _GetEntry(username);
            int    resendCount = existing != null ? existing.ResendCount + 1 : 0;

            var entry = new OtpEntry
            {
                HashedOtp      = hash,
                Salt           = salt,
                Expiry         = DateTime.UtcNow.AddMinutes(cfg.OtpExpiryMinutes),
                GeneratedAt    = DateTime.UtcNow,
                FailedAttempts = 0,
                ResendCount    = resendCount,
                MaskedPhone    = existing?.MaskedPhone ?? string.Empty
            };

            _SetEntry(username, entry, cfg.OtpExpiryMinutes + 2);
            return otp;
        }

        public void SetMaskedPhone(string username, string maskedPhone)
        {
            var entry = _GetEntry(username);
            if (entry != null)
            {
                entry.MaskedPhone = maskedPhone;
                double rem = (entry.Expiry - DateTime.UtcNow).TotalMinutes + 2;
                if (rem > 0) _SetEntry(username, entry, rem);
            }
        }

        public string GetMaskedPhone(string username)
            => _GetEntry(username)?.MaskedPhone;

        public int GetResendCount(string username)
            => _GetEntry(username)?.ResendCount ?? 0;

        public int GetResendCooldownRemaining(string username)
        {
            var entry = _GetEntry(username);
            if (entry == null) return 0;
            int    cooldown = TwoFactorConfig.Current.ResendCooldownSeconds;
            double elapsed  = (DateTime.UtcNow - entry.GeneratedAt).TotalSeconds;
            int    rem      = (int)Math.Ceiling(cooldown - elapsed);
            return rem > 0 ? rem : 0;
        }

        public bool CanResend(string username)
        {
            var cfg = TwoFactorConfig.Current;
            if (GetResendCount(username) >= cfg.MaxResends) return false;
            return GetResendCooldownRemaining(username) == 0;
        }

        public OtpValidationResult ValidateOtp(string username, string inputOtp)
        {
            if (string.IsNullOrWhiteSpace(inputOtp))
                return OtpValidationResult.Invalid;

            var entry = _GetEntry(username);
            if (entry == null) return OtpValidationResult.NotFound;

            if (entry.Expiry < DateTime.UtcNow)
            {
                _RemoveEntry(username);
                return OtpValidationResult.Expired;
            }

            if (entry.FailedAttempts >= TwoFactorConfig.Current.MaxAttempts)
            {
                _RemoveEntry(username);
                return OtpValidationResult.MaxAttemptsExceeded;
            }

            bool valid = SecureEquals(HashOtp(inputOtp.Trim(), entry.Salt), entry.HashedOtp);

            if (valid)
            {
                _RemoveEntry(username);
                return OtpValidationResult.Valid;
            }

            entry.FailedAttempts++;
            double rem = (entry.Expiry - DateTime.UtcNow).TotalMinutes + 2;
            if (rem > 0) _SetEntry(username, entry, rem);

            return entry.FailedAttempts >= TwoFactorConfig.Current.MaxAttempts
                ? OtpValidationResult.MaxAttemptsExceeded
                : OtpValidationResult.Invalid;
        }

        public int RemainingAttempts(string username)
        {
            var entry = _GetEntry(username);
            if (entry == null) return 0;
            return Math.Max(0, TwoFactorConfig.Current.MaxAttempts - entry.FailedAttempts);
        }

        // ── Crypto ────────────────────────────────────────────────────────────

        private static string GenerateNumericOtp(int length)
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                int    max   = (int)Math.Pow(10, length);
                byte[] buf   = new byte[4];
                int    value;
                do { rng.GetBytes(buf); value = Math.Abs(BitConverter.ToInt32(buf, 0)); }
                while (value > int.MaxValue - (int.MaxValue % max));
                return (value % max).ToString($"D{length}");
            }
        }

        private static string GenerateSalt()
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] buf = new byte[16];
                rng.GetBytes(buf);
                return Convert.ToBase64String(buf);
            }
        }

        private static string HashOtp(string otp, string salt)
        {
            using (var sha = new SHA256Managed())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(otp + "|" + salt);
                return Convert.ToBase64String(sha.ComputeHash(bytes));
            }
        }

        private static bool SecureEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= (a[i] ^ b[i]);
            return diff == 0;
        }

        // ── Cache ─────────────────────────────────────────────────────────────

        private static string CacheKey(string u) => "SP2FA_" + u.ToLowerInvariant().Trim();
        private static OtpEntry _GetEntry(string u) => _cache.Get(CacheKey(u)) as OtpEntry;
        private static void _SetEntry(string u, OtpEntry e, double ttl)
            => _cache.Set(CacheKey(u), e, new CacheItemPolicy
               { AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(ttl) });
        private static void _RemoveEntry(string u) => _cache.Remove(CacheKey(u));
    }
}
