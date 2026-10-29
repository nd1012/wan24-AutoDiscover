using wan24.AutoDiscover.Models;

namespace wan24.AutoDiscover.Tests
{
    /// <summary>
    /// <see cref="Protocol"/> tests
    /// </summary>
    [TestClass]
    public sealed class Protocol_Test
    {
        /// <summary>
        /// Email address
        /// </summary>
        private const string EMAIL = "alias@example.org";

        /// <summary>
        /// Splitted email address parts (alias and domain)
        /// </summary>
        private static readonly string[] EmailParts = ["alias", "example.org"];

        /// <summary>
        /// Without a mapping the login name is the alias
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void DefaultLoginName_Alias()
        {
            Protocol protocol = CreateProtocol(loginNameIsEmailAlias: true);
            Assert.AreEqual("alias", Protocol.DefaultLoginName(EmailParts, CreateDomain(protocol), protocol));
        }

        /// <summary>
        /// Without a mapping and without the alias option the login name is the email address
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void DefaultLoginName_EmailAddress()
        {
            Protocol protocol = CreateProtocol(loginNameIsEmailAlias: false);
            Assert.AreEqual(EMAIL, Protocol.DefaultLoginName(EmailParts, CreateDomain(protocol), protocol));
        }

        /// <summary>
        /// A protocol mapping of the email address wins over the alias
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void DefaultLoginName_ProtocolMappingByEmailAddress()
        {
            Protocol protocol = CreateProtocol(loginNameIsEmailAlias: true, new Dictionary<string, string>() { [EMAIL] = "user1" });
            Assert.AreEqual("user1", Protocol.DefaultLoginName(EmailParts, CreateDomain(protocol), protocol));
        }

        /// <summary>
        /// A domain mapping of the email address is used, if the protocol has no mapping
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void DefaultLoginName_DomainMappingByEmailAddress()
        {
            Protocol protocol = CreateProtocol(loginNameIsEmailAlias: false);
            DomainConfig domain = CreateDomain(protocol, new Dictionary<string, string>() { [EMAIL] = "user2" });
            Assert.AreEqual("user2", Protocol.DefaultLoginName(EmailParts, domain, protocol));
        }

        /// <summary>
        /// A required mapping which wasn't found results in the email address
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void DefaultLoginName_RequiredMappingMissing()
        {
            Protocol protocol = CreateProtocol(loginNameIsEmailAlias: true, new Dictionary<string, string>() { ["other@example.org"] = "user3" }, mappingRequired: true);
            Assert.AreEqual(EMAIL, Protocol.DefaultLoginName(EmailParts, CreateDomain(protocol), protocol));
        }

        /// <summary>
        /// Create a protocol
        /// </summary>
        /// <param name="loginNameIsEmailAlias">If the login name is the alias of the email address</param>
        /// <param name="mapping">Login name mapping</param>
        /// <param name="mappingRequired">If a login name mapping is required</param>
        /// <returns>Protocol</returns>
        private static Protocol CreateProtocol(bool loginNameIsEmailAlias, IReadOnlyDictionary<string, string>? mapping = null, bool mappingRequired = false)
            => new()
            {
                Type = "IMAP",
                Server = "imap.example.org",
                Port = 993,
                LoginNameIsEmailAlias = loginNameIsEmailAlias,
                LoginNameMapping = mapping,
                LoginNameMappingRequired = mappingRequired
            };

        /// <summary>
        /// Create a domain configuration
        /// </summary>
        /// <param name="protocol">Protocol</param>
        /// <param name="mapping">Login name mapping</param>
        /// <returns>Domain configuration</returns>
        private static DomainConfig CreateDomain(Protocol protocol, Dictionary<string, string>? mapping = null)
            => new()
            {
                Protocols = [protocol],
                LoginNameMapping = mapping
            };
    }
}
