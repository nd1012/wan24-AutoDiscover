using System.ComponentModel.DataAnnotations;
using System.Xml;
using wan24.ObjectValidation;

// https://learn.microsoft.com/en-us/exchange/client-developer/web-service-reference/protocol-pox

namespace wan24.AutoDiscover.Models
{
    /// <summary>
    /// Protocol (POX)
    /// </summary>
    public record class Protocol : ValidatableRecordBase
    {
        /// <summary>
        /// <c>Protocol</c> node name
        /// </summary>
        private const string PROTOCOL_NODE_NAME = "Protocol";
        /// <summary>
        /// <c>Type</c> node name
        /// </summary>
        private const string TYPE_NODE_NAME = "Type";
        /// <summary>
        /// <c>Server</c> node name
        /// </summary>
        private const string SERVER_NODE_NAME = "Server";
        /// <summary>
        /// <c>Port</c> node name
        /// </summary>
        private const string PORT_NODE_NAME = "Port";
        /// <summary>
        /// <c>LoginName</c> node name
        /// </summary>
        private const string LOGINNAME_NODE_NAME = "LoginName";
        /// <summary>
        /// <c>SPA</c> node name
        /// </summary>
        private const string SPA_NODE_NAME = "SPA";
        /// <summary>
        /// <c>SSL</c> node name
        /// </summary>
        private const string SSL_NODE_NAME = "SSL";
        /// <summary>
        /// <c>AuthRequired</c> node name
        /// </summary>
        private const string AUTHREQUIRED_NODE_NAME = "AuthRequired";
        /// <summary>
        /// <c>ON</c>
        /// </summary>
        private const string ON = "on";
        /// <summary>
        /// <c>OFF</c>
        /// </summary>
        private const string OFF = "off";

        /// <summary>
        /// Constructor
        /// </summary>
        public Protocol() : base() { }

        /// <summary>
        /// Login name getter delegate
        /// </summary>
        public static LoginName_Delegate LoginName { get; set; } = DefaultLoginName;

        /// <summary>
        /// Type
        /// </summary>
        [Required]
        public required string Type { get; init; }

        /// <summary>
        /// Server
        /// </summary>
        [RegularExpression(@"^[a-z|-|\.]{1,256}$")]
        public required string Server { get; init; }

        /// <summary>
        /// Port
        /// </summary>
        [Range(1, ushort.MaxValue)]
        public int Port { get; init; }

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
        /// If the login name is the alias of the email address
        /// </summary>
        public bool LoginNameIsEmailAlias { get; init; } = true;

        /// <summary>
        /// Secure password authentication
        /// </summary>
        public bool SPA { get; init; }

        /// <summary>
        /// SSL
        /// </summary>
        public bool SSL { get; init; } = true;

        /// <summary>
        /// Authentication required
        /// </summary>
        public bool AuthRequired { get; init; } = true;

        /// <summary>
        /// Create XML
        /// </summary>
        /// <param name="xml">XML</param>
        /// <param name="account">Account node</param>
        /// <param name="emailParts">Splitted email parts</param>
        /// <param name="domain">Domain</param>
        public virtual void CreateXml(XmlDocument xml, XmlNode account, ReadOnlyMemory<string> emailParts, DomainConfig domain)
        {
            XmlNode protocol = account.AppendChild(xml.CreateElement(PROTOCOL_NODE_NAME, Constants.RESPONSE_NS))!;
            foreach (KeyValuePair<string, string> kvp in new Dictionary<string, string>()
            {
                {TYPE_NODE_NAME, Type },
                {SERVER_NODE_NAME, Server },
                {PORT_NODE_NAME, Port.ToString() },
                {LOGINNAME_NODE_NAME, LoginName(xml, account, emailParts, domain, this) },
                {SPA_NODE_NAME, SPA ? ON : OFF },
                {SSL_NODE_NAME, SSL ? ON : OFF },
                {AUTHREQUIRED_NODE_NAME, AuthRequired ? ON : OFF }
            })
                protocol.AppendChild(xml.CreateElement(kvp.Key, Constants.RESPONSE_NS))!.InnerText = kvp.Value;
        }

        /// <summary>
        /// Delegate for a login name resolver
        /// </summary>
        /// <param name="xml">XML</param>
        /// <param name="account">Account node</param>
        /// <param name="emailParts">Splitted email parts</param>
        /// <param name="domain">Domain</param>
        /// <param name="protocol">Protocol</param>
        /// <returns>Login name</returns>
        public delegate string LoginName_Delegate(XmlDocument xml, XmlNode account, ReadOnlyMemory<string> emailParts, DomainConfig domain, Protocol protocol);

        /// <summary>
        /// Default login name resolver
        /// </summary>
        /// <param name="xml">XML</param>
        /// <param name="account">Account node</param>
        /// <param name="emailParts">Splitted email parts</param>
        /// <param name="domain">Domain</param>
        /// <param name="protocol">Protocol</param>
        /// <returns>Login name</returns>
        public static string DefaultLoginName(XmlDocument xml, XmlNode account, ReadOnlyMemory<string> emailParts, DomainConfig domain, Protocol protocol)
        {
            string emailAddress = string.Join('@', emailParts),
                res = protocol.LoginNameIsEmailAlias
                    ? emailParts.Span[0]
                    : emailAddress;
            string? loginName = null;
            return (protocol.LoginNameMapping?.TryGetValue(emailAddress, out loginName) ?? false) ||
                (protocol.LoginNameIsEmailAlias && (protocol.LoginNameMapping?.TryGetValue(res, out loginName) ?? false)) ||
                (domain.LoginNameMapping?.TryGetValue(emailAddress, out loginName) ?? false) ||
                (protocol.LoginNameIsEmailAlias && (domain.LoginNameMapping?.TryGetValue(res, out loginName) ?? false)) ||
                (loginName is null && (domain.LoginNameMappingRequired || protocol.LoginNameMappingRequired))
                ? loginName ?? emailAddress
                : res;
        }
    }
}
