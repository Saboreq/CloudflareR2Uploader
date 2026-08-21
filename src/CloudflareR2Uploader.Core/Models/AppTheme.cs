namespace CloudflareR2Uploader.Models
{
    /// <summary>Which palette the application renders with.</summary>
    public enum AppTheme
    {
        /// <summary>The Paper design's dark palette and the migration default for older settings.</summary>
        Dark = 0,

        Light = 1,

        /// <summary>Follows the Windows "app mode" preference and updates when it changes.</summary>
        System = 2
    }
}
