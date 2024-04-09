namespace wan24.AutoDiscover
{
    /// <summary>
    /// Version information
    /// </summary>
    public static class VersionInfo
    {
        /// <summary>
        /// Current version
        /// </summary>
        private static Version? _Current = null;

        /// <summary>
        /// Current version
        /// </summary>
        public static Version Current => _Current ??= new(Properties.Resources.VERSION);
    }
}
