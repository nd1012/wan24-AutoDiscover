using wan24.AutoDiscover.Models;

namespace wan24.AutoDiscover.Tests
{
    /// <summary>
    /// <see cref="DomainConfig"/> tests
    /// </summary>
    [TestClass]
    public sealed class DomainConfig_Test
    {
        /// <summary>
        /// Registered domain configurations before the test
        /// </summary>
        private IReadOnlyDictionary<string, DomainConfig>? PreviousRegistered;

        /// <summary>
        /// Remember the registered domain configurations
        /// </summary>
        [TestInitialize]
        public void Initialize() => PreviousRegistered = DomainConfig.Registered;

        /// <summary>
        /// Restore the registered domain configurations
        /// </summary>
        [TestCleanup]
        public void Cleanup() => DomainConfig.Registered = PreviousRegistered!;

        /// <summary>
        /// The email domain name is the first lookup key
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetConfig_EmailDomainKey()
        {
            DomainConfig domain = Create(),
                hostConfig = Create();
            Dictionary<string, DomainConfig> registered = new() { { "example.org", domain }, { "host.example.net", hostConfig } };
            Assert.AreSame(domain, DomainConfig.GetConfig(registered, "host.example.net", Parts("alias", "example.org")));
        }

        /// <summary>
        /// The host name is used, if the email domain name isn't registered
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetConfig_HostKey()
        {
            DomainConfig hostConfig = Create();
            Dictionary<string, DomainConfig> registered = new() { { "example.org", Create() }, { "host.example.net", hostConfig } };
            Assert.AreSame(hostConfig, DomainConfig.GetConfig(registered, "host.example.net", Parts("alias", "unknown.example.com")));
        }

        /// <summary>
        /// The host name has precedence over the accepted domain names
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetConfig_HostKeyBeforeAcceptedDomains()
        {
            DomainConfig hostConfig = Create();
            Dictionary<string, DomainConfig> registered = new()
            {
                { "example.org", Create("alias.example.net") },
                { "host.example.net", hostConfig }
            };
            Assert.AreSame(hostConfig, DomainConfig.GetConfig(registered, "host.example.net", Parts("alias", "alias.example.net")));
        }

        /// <summary>
        /// An accepted domain name matches (ignoring the case)
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetConfig_AcceptedDomains()
        {
            DomainConfig accepting = Create("Other.Example.Net", "Alias.Example.Net");
            Dictionary<string, DomainConfig> registered = new() { { "example.org", accepting }, { string.Empty, Create() } };
            Assert.AreSame(accepting, DomainConfig.GetConfig(registered, string.Empty, Parts("alias", "alias.example.net")));
            Assert.AreSame(accepting, DomainConfig.GetConfig(registered, "unknown.host", Parts("alias", "ALIAS.EXAMPLE.NET")));
        }

        /// <summary>
        /// The first registered configuration which accepts the domain name wins
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetConfig_AcceptedDomains_FirstEntry()
        {
            DomainConfig first = Create("alias.example.net");
            Dictionary<string, DomainConfig> registered = new()
            {
                { "first.example.org", first },
                { "second.example.org", Create("alias.example.net") }
            };
            Assert.AreSame(first, DomainConfig.GetConfig(registered, string.Empty, Parts("alias", "alias.example.net")));
        }

        /// <summary>
        /// The configuration with the empty key is the fallback
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetConfig_EmptyKeyFallback()
        {
            DomainConfig fallback = Create();
            Dictionary<string, DomainConfig> registered = new() { { "example.org", Create("accepted.example.net") }, { string.Empty, fallback } };
            Assert.AreSame(fallback, DomainConfig.GetConfig(registered, string.Empty, Parts("alias", "unknown.example.com")));
            Assert.AreSame(fallback, DomainConfig.GetConfig(registered, "unknown.host", Parts("alias", "unknown.example.com")));
        }

        /// <summary>
        /// Without any match there is no configuration
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetConfig_NoMatch()
        {
            Dictionary<string, DomainConfig> registered = new() { { "example.org", Create("accepted.example.net") } };
            Assert.IsNull(DomainConfig.GetConfig(registered, string.Empty, Parts("alias", "unknown.example.com")));
            Assert.IsNull(DomainConfig.GetConfig(registered, "unknown.host", Parts("alias", "unknown.example.com")));
            Assert.IsNull(DomainConfig.GetConfig(new Dictionary<string, DomainConfig>(), "unknown.host", Parts("alias", "example.org")));
        }

        /// <summary>
        /// The overload without a dictionary uses the registered configurations
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetConfig_UsesRegistered()
        {
            DomainConfig domain = Create(),
                fallback = Create();
            DomainConfig.Registered = new Dictionary<string, DomainConfig>() { { "example.org", domain }, { string.Empty, fallback } };
            Assert.AreSame(domain, DomainConfig.GetConfig("host.example.net", Parts("alias", "example.org")));
            Assert.AreSame(fallback, DomainConfig.GetConfig("host.example.net", Parts("alias", "unknown.example.com")));
        }

        /// <summary>
        /// Create a domain configuration
        /// </summary>
        /// <param name="acceptedDomains">Accepted domain names (none for no accepted domain names)</param>
        /// <returns>Configuration</returns>
        private static DomainConfig Create(params ReadOnlySpan<string> acceptedDomains) => new()
        {
            AcceptedDomains = acceptedDomains.Length == 0 ? null : [.. acceptedDomains],
            Protocols = [new Protocol() { Type = "IMAP", Server = "imap.example.org", Port = 993 }]
        };

        /// <summary>
        /// Create email parts
        /// </summary>
        /// <param name="alias">Alias</param>
        /// <param name="domain">Domain name</param>
        /// <returns>Email parts</returns>
        private static ReadOnlyMemory<string> Parts(string alias, string domain) => new string[] { alias, domain };
    }
}
