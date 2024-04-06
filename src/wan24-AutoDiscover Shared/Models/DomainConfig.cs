﻿using System.Xml;
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
        public IReadOnlyDictionary<string, string>? LoginNameMapping { get; init; }

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
    }
}
