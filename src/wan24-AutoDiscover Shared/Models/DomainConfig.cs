﻿using System.Net.Http;
using System.Xml;
using wan24.ObjectValidation;

namespace wan24.AutoDiscover.Models
{
    /// <summary>
    /// Domain configuration
    /// </summary>
    public record class DomainConfig
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public DomainConfig() { }

        /// <summary>
        /// Registered domains (key is the served domain name)
        /// </summary>
        public static IReadOnlyDictionary<string, DomainConfig> Registered { get; set; } = null!;

        /// <summary>
        /// Accepted domain names
        /// </summary>
        [ItemRegularExpression(@"^[a-z|-|\.]{1,256}$")]
        public IReadOnlyList<string>? AcceptedDomains { get; init; }

        /// <summary>
        /// Protocols
        /// </summary>
        [CountLimit(1, int.MaxValue)]
        public required IReadOnlyList<Protocol> Protocols { get; init; }

        /// <summary>
        /// Login name mapping (key is the email address or alias, value the mapped login name)
        /// </summary>
        [RequiredIf(nameof(LoginNameMappingRequired), true)]
        public Dictionary<string, string>? LoginNameMapping { get; set; }

        /// <summary>
        /// If a successfule login name mapping is required (if no mapping was possible, the email address will be used as login name)
        /// </summary>
        public bool LoginNameMappingRequired { get; init; }

        /// <summary>
        /// Create XML
        /// </summary>
        /// <param name="xml">XML</param>
        /// <param name="account">Account node</param>
        /// <param name="emailParts">Splitted email parts</param>
        public virtual void CreateXml(XmlDocument xml, XmlNode account, string[] emailParts)
        {
            foreach (Protocol protocol in Protocols) protocol.CreateXml(xml, account, emailParts, this);
        }

        /// <summary>
        /// Get a domain configuration which matches an email address
        /// </summary>
        /// <param name="host">Hostname</param>
        /// <param name="emailParts">Splitted email parts</param>
        /// <returns>Domain configuration</returns>
        public static DomainConfig? GetConfig(string host, string[] emailParts)
            => !Registered.TryGetValue(emailParts[1], out DomainConfig? config) &&
                (host.Length == 0 || !Registered.TryGetValue(host, out config)) &&
                !Registered.TryGetValue(
                    Registered.Where(kvp => kvp.Value.AcceptedDomains?.Contains(emailParts[1], StringComparer.OrdinalIgnoreCase) ?? false)
                        .Select(kvp => kvp.Key)
                        .FirstOrDefault() ?? string.Empty,
                    out config)
                ? null
                : config;
    }
}
