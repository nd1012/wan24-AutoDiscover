using Microsoft.Extensions.Configuration;
using System.Text;
using wan24.AutoDiscover.Models;

namespace wan24.AutoDiscover.Tests
{
    /// <summary>
    /// <see cref="DiscoveryConfig"/> tests
    /// </summary>
    [TestClass]
    public sealed class DiscoveryConfig_Test
    {
        /// <summary>
        /// Configuration JSON document with one discovery domain
        /// </summary>
        private const string CONFIG_JSON = """
            {
              "DiscoveryConfig": {
                "Discovery": {
                  "example.org": {
                    "Protocols": [
                      { "Type": "IMAP", "Server": "imap.example.org", "Port": 993 }
                    ]
                  }
                }
              }
            }
            """;

        /// <summary>
        /// Registered domain configurations before the test
        /// </summary>
        private IReadOnlyDictionary<string, DomainConfig>? PreviousRegistered;
        /// <summary>
        /// Temporary folder of the test
        /// </summary>
        private string Root = string.Empty;

        /// <summary>
        /// Remember the registered domain configurations and create the temporary folder
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            PreviousRegistered = DomainConfig.Registered;
            Root = Path.Combine(Path.GetTempPath(), $"wan24AutoDiscoverTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        /// <summary>
        /// Restore the registered domain configurations and delete the temporary folder
        /// </summary>
        [TestCleanup]
        public void Cleanup()
        {
            DomainConfig.Registered = PreviousRegistered!;
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        /// <summary>
        /// A mapping is applied, if there is no registered configuration yet
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(10000)]
        public async Task GetDiscoveryConfigAsync_NoRegistered()
        {
            DomainConfig.Registered = null!;
            string file = await WriteMappingsAsync("""[{"Email":"a@example.org","Targets":["user1"]}]""");
            IReadOnlyDictionary<string, DomainConfig> result = await new DiscoveryConfig() { EmailMappings = file }.GetDiscoveryConfigAsync(CreateConfig());
            Assert.IsNotNull(result["example.org"].LoginNameMapping);
            Assert.AreEqual(1, result["example.org"].LoginNameMapping!.Count);
            Assert.AreEqual("user1", result["example.org"].LoginNameMapping!["a@example.org"]);
        }

        /// <summary>
        /// A previously registered configuration isn't modified
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(10000)]
        public async Task GetDiscoveryConfigAsync_RegisteredNotModified()
        {
            DomainConfig old = new()
            {
                Protocols = [new Protocol() { Type = "IMAP", Server = "old.example.org", Port = 993 }]
            };
            Dictionary<string, DomainConfig> registered = new() { { "example.org", old } };
            DomainConfig.Registered = registered;
            string file = await WriteMappingsAsync("""[{"Email":"a@example.org","Targets":["user1"]}]""");
            IReadOnlyDictionary<string, DomainConfig> result = await new DiscoveryConfig() { EmailMappings = file }.GetDiscoveryConfigAsync(CreateConfig());
            Assert.IsNull(old.LoginNameMapping);
            Assert.AreSame(registered, DomainConfig.Registered);
            Assert.AreNotSame(old, result["example.org"]);
            Assert.AreEqual("user1", result["example.org"].LoginNameMapping!["a@example.org"]);
        }

        /// <summary>
        /// A chained mapping is resolved to the local login user
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(10000)]
        public async Task GetDiscoveryConfigAsync_ChainedMapping()
        {
            DomainConfig.Registered = null!;
            string file = await WriteMappingsAsync(
                """[{"Email":"a@example.org","Targets":["b@example.org"]},{"Email":"b@example.org","Targets":["localuser"]}]"""
                );
            IReadOnlyDictionary<string, DomainConfig> result = await new DiscoveryConfig() { EmailMappings = file }.GetDiscoveryConfigAsync(CreateConfig());
            Dictionary<string, string> mapping = result["example.org"].LoginNameMapping!;
            Assert.AreEqual("localuser", mapping["a@example.org"]);
            Assert.AreEqual("localuser", mapping["b@example.org"]);
        }

        /// <summary>
        /// The mapped email address is stored lower case
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(10000)]
        public async Task GetDiscoveryConfigAsync_LowerCaseKey()
        {
            DomainConfig.Registered = null!;
            string file = await WriteMappingsAsync("""[{"Email":"Alias@Example.ORG","Targets":["user1"]}]""");
            IReadOnlyDictionary<string, DomainConfig> result = await new DiscoveryConfig() { EmailMappings = file }.GetDiscoveryConfigAsync(CreateConfig());
            Assert.AreEqual("user1", result["example.org"].LoginNameMapping!["alias@example.org"]);
        }

        /// <summary>
        /// Mappings to external targets only and mappings for unknown domains are skipped
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(10000)]
        public async Task GetDiscoveryConfigAsync_SkipsUnresolvable()
        {
            DomainConfig.Registered = null!;
            string file = await WriteMappingsAsync(
                """[{"Email":"a@example.org","Targets":["ext@other.org"]},{"Email":"b@unknown.example.com","Targets":["user1"]}]"""
                );
            IReadOnlyDictionary<string, DomainConfig> result = await new DiscoveryConfig() { EmailMappings = file }.GetDiscoveryConfigAsync(CreateConfig());
            Assert.AreEqual(1, result.Count);
            Assert.IsNull(result["example.org"].LoginNameMapping);
        }

        /// <summary>
        /// Domain names are looked up case-insensitive
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(10000)]
        public async Task GetDiscoveryConfigAsync_DomainKeysIgnoreCase()
        {
            DomainConfig.Registered = null!;
            IReadOnlyDictionary<string, DomainConfig> result = await new DiscoveryConfig().GetDiscoveryConfigAsync(CreateConfig());
            Assert.IsTrue(result.ContainsKey("Example.ORG"));
            string[] parts = ["alias", "EXAMPLE.org"];
            Assert.AreSame(result["example.org"], DomainConfig.GetConfig(result, string.Empty, parts));
        }

        /// <summary>
        /// Without mappings or with a missing mappings file the configuration is returned unchanged
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(10000)]
        public async Task GetDiscoveryConfigAsync_NoMappings()
        {
            DomainConfig.Registered = null!;
            IReadOnlyDictionary<string, DomainConfig> result = await new DiscoveryConfig().GetDiscoveryConfigAsync(CreateConfig());
            Assert.AreEqual("imap.example.org", result["example.org"].Protocols[0].Server);
            Assert.IsNull(result["example.org"].LoginNameMapping);
            result = await new DiscoveryConfig() { EmailMappings = Path.Combine(Root, "missing.json") }.GetDiscoveryConfigAsync(CreateConfig());
            Assert.IsNull(result["example.org"].LoginNameMapping);
        }

        /// <summary>
        /// Create the configuration root
        /// </summary>
        /// <returns>Configuration root</returns>
        private static IConfigurationRoot CreateConfig()
        {
            using MemoryStream ms = new(Encoding.UTF8.GetBytes(CONFIG_JSON));
            return new ConfigurationBuilder().AddJsonStream(ms).Build();
        }

        /// <summary>
        /// Write a mappings file into the temporary folder
        /// </summary>
        /// <param name="json">JSON</param>
        /// <returns>File path</returns>
        private async Task<string> WriteMappingsAsync(string json)
        {
            string file = Path.Combine(Root, "mappings.json");
            await File.WriteAllTextAsync(file, json);
            return file;
        }
    }
}
