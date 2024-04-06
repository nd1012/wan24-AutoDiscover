using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    /// <summary>
    /// Configuration changed event throttle
    /// </summary>
    public sealed class ConfigChangeEventThrottle : EventThrottle
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public ConfigChangeEventThrottle() : base(timeout: 300) { }

        /// <inheritdoc/>
        protected override void HandleEvent(in DateTime raised, in int raisedCount) => RaiseOnConfigChange();

        /// <summary>
        /// Delegate for the <see cref="OnConfigChange"/> event
        /// </summary>
        public delegate void ConfigChange_Delegate();
        /// <summary>
        /// Raised on configuration changes
        /// </summary>
        public static event ConfigChange_Delegate? OnConfigChange;
        /// <summary>
        /// Raise the <see cref="OnConfigChange"/> event
        /// </summary>
        private static void RaiseOnConfigChange() => OnConfigChange?.Invoke();
    }
}
