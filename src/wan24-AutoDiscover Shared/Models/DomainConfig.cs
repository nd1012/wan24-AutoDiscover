using System.Collections.Frozen;
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
        public static FrozenDictionary<string, DomainConfig> Registered { get; set; } = null!;

        /// <summary>
        /// Accepted domain names
        /// </summary>
        [ItemRegularExpression(@"^[a-z|-|\.]{1,256}$")]
        public HashSet<string>? AcceptedDomains { get; init; }

        /// <summary>
        /// Protocols
        /// </summary>
        [CountLimit(1, int.MaxValue)]
        public required virtual HashSet<Protocol> Protocols { get; init; }

        /// <summary>
        /// Create XML
        /// </summary>
        /// <param name="xml">XML</param>
        /// <param name="account">Account node</param>
        /// <param name="emailParts">Splitted email parts</param>
        public virtual void CreateXml(XmlDocument xml, XmlNode account, string[] emailParts)
        {
            foreach (Protocol protocol in Protocols) protocol.CreateXml(xml, account, emailParts);
        }
    }
}
