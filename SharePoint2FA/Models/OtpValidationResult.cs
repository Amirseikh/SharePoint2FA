namespace SharePoint2FA.Models
{
    public enum OtpValidationResult
    {
        Valid,
        Invalid,
        Expired,
        MaxAttemptsExceeded,
        NotFound
    }
}
