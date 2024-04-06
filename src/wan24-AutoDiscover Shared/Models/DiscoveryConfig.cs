using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using wan24.Core;

namespace wan24.AutoDiscover.Models
{
    /// <summary>
    /// Discovery configuration
    /// </summary>
    public record class DiscoveryConfig
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public DiscoveryConfig() { }

        /// <summary>
        /// Logfile path
        /// </summary>
        [StringLength(short.MaxValue, MinimumLength = 1)]
        public string? LogFile { get; init; }

        /// <summary>
        /// Number of POX XML responses to pre-fork
        /// </summary>
        [Range(1, int.MaxValue)]
        public int PreForkResponses { get; init; } = 10;

        /// <summary>
        /// Dicovery configuration type name
        /// </summary>
        [StringLength(byte.MaxValue, MinimumLength = 1)]
        public string? DiscoveryTypeName { get; init; }

        /// <summary>
        /// Discovery configuration type
        /// </summary>
        [JsonIgnore]
        public Type DiscoveryType => DiscoveryTypeName is null
            ? typeof(Dictionary<string, DomainConfig>)
            : TypeHelper.Instance.GetType(DiscoveryTypeName)
                ?? throw new InvalidDataException($"Discovery type {DiscoveryTypeName.ToQuotedLiteral()} not found");

        /// <summary>
        /// Get the discovery configuration
        /// </summary>
        /// <param name="config">Configuration</param>
        /// <returns>Discovery configuration</returns>
        public FrozenDictionary<string, DomainConfig> GetDiscoveryConfig(IConfigurationRoot config)
        {
            Type discoveryType = DiscoveryType;
            if (!typeof(IDictionary).IsAssignableFrom(discoveryType))
                throw new InvalidDataException($"Discovery type must be an {typeof(IDictionary)}");
            if (!discoveryType.IsGenericType)
                throw new InvalidDataException($"Discovery type must be a generic type");
            // Validate discovery configuration type generic type arguments
            Type[] gt = discoveryType.GetGenericArguments();
            if (gt.Length != 2)
                throw new InvalidDataException($"Discovery type must be a generic type with two type arguments");
            if (gt[0] != typeof(string))
                throw new InvalidDataException($"Discovery types first generic type argument must be a {typeof(string)}");
            if (!typeof(DomainConfig).IsAssignableFrom(gt[1]))
                throw new InvalidDataException($"Discovery types second generic type argument must be a {typeof(DomainConfig)}");
            // Parse the discovery configuration
            IDictionary discovery = config.GetRequiredSection("DiscoveryConfig:Discovery").Get(discoveryType) as IDictionary
                ?? throw new InvalidDataException("Failed to get discovery configuration");
            object[] keys = new object[discovery.Count],
                values = new object[discovery.Count];
            discovery.Keys.CopyTo(keys, index: 0);
            discovery.Values.CopyTo(values, index: 0);
            return new Dictionary<string, DomainConfig>(
                Enumerable.Range(0, discovery.Count).Select(i => new KeyValuePair<string, DomainConfig>((string)keys[i], (DomainConfig)values[i]))
                ).ToFrozenDictionary();
        }
    }
}
