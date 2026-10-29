using System.Xml;
using wan24.ObjectValidation;

namespace wan24.AutoDiscover.Models
{
    /// <summary>
    /// Domain configuration
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    public record class DomainConfig() : ValidatableRecordBase()
    {
        /// <summary>
        /// Registered domains (key is the served domain name)
        /// </summary>
        public static IReadOnlyDictionary<string, DomainConfig> Registered { get; set; } = null!;

        /// <summary>
        /// Accepted domain names
        /// </summary>
        [CountLimit(1, int.MaxValue), ItemRegularExpression(@"^[a-z|-|\.]{1,256}$")]
        public IReadOnlyList<string>? AcceptedDomains { get; init; }

        /// <summary>
        /// Protocols
        /// </summary>
        [CountLimit(1, byte.MaxValue)]
        public required IReadOnlyList<Protocol> Protocols { get; init; }

        /// <summary>
        /// Login name mapping (key is the email address or alias, value the mapped login name)
        /// </summary>
        [RequiredIf(nameof(LoginNameMappingRequired), true)]
        public Dictionary<string, string>? LoginNameMapping { get; set; }

        /// <summary>
        /// If a successful login name mapping is required (if no mapping was possible, the email address will be used as login name)
        /// </summary>
        public bool LoginNameMappingRequired { get; init; }

        /// <summary>
        /// Create XML
        /// </summary>
        /// <param name="xml">XML</param>
        /// <param name="emailParts">Splitted email parts</param>
        public virtual void CreateXml(XmlWriter xml, ReadOnlyMemory<string> emailParts)
        {
            foreach (Protocol protocol in Protocols) protocol.CreateXml(xml, emailParts, this);
        }

        /// <summary>
        /// Get a domain configuration from <see cref="Registered"/> which matches an email address
        /// </summary>
        /// <param name="host">Hostname</param>
        /// <param name="emailParts">Splitted email parts</param>
        /// <returns>Domain configuration</returns>
        public static DomainConfig? GetConfig(string host, ReadOnlyMemory<string> emailParts) => GetConfig(Registered, host, emailParts);

        /// <summary>
        /// Get a domain configuration which matches an email address (lookup order: email domain, hostname, the first configuration which has the
        /// email domain in its <see cref="AcceptedDomains"/>, the configuration with an empty domain name as key)
        /// </summary>
        /// <param name="registered">Domain configurations to search (key is the served domain name)</param>
        /// <param name="host">Hostname (an empty string skips the hostname lookup)</param>
        /// <param name="emailParts">Splitted email parts (alias and domain)</param>
        /// <returns>Domain configuration</returns>
        public static DomainConfig? GetConfig(IReadOnlyDictionary<string, DomainConfig> registered, string host, ReadOnlyMemory<string> emailParts)
        {
            string domain = emailParts.Span[1];
            if (registered.TryGetValue(domain, out DomainConfig? config) || (host.Length != 0 && registered.TryGetValue(host, out config)))
                return config;
            // IReadOnlyDictionary offers no struct enumerator, so this loop allocates its enumerator
            foreach (KeyValuePair<string, DomainConfig> kvp in registered)
            {
                IReadOnlyList<string>? acceptedDomains = kvp.Value.AcceptedDomains;
                if (acceptedDomains is null) continue;
                for (int i = 0, len = acceptedDomains.Count; i < len; i++)
                    if (domain.Equals(acceptedDomains[i], StringComparison.OrdinalIgnoreCase))
                        return kvp.Value;
            }
            return registered.TryGetValue(string.Empty, out config) ? config : null;
        }
    }
}
