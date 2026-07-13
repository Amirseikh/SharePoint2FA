using System;

namespace SharePoint2FA.Models
{
    [Serializable]
    public class OtpEntry
    {
        public string   HashedOtp      { get; set; }
        public string   Salt           { get; set; }
        public DateTime Expiry         { get; set; }
        public int      FailedAttempts { get; set; }
        public DateTime GeneratedAt    { get; set; }
        public string   MaskedPhone    { get; set; }
        public int      ResendCount    { get; set; }
    }
}
