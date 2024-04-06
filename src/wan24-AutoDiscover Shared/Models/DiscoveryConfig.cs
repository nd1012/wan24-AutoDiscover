using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Mail;
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
        /// Current configuration
        /// </summary>
        public static DiscoveryConfig Current { get; set; } = null!;

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
        /// Known http proxies
        /// </summary>
        public IReadOnlySet<IPAddress> KnownProxies { get; init; } = new HashSet<IPAddress>();

        /// <summary>
        /// JSON file path which contains the email mappings list
        /// </summary>
        public string? EmailMappings { get; init; }

        /// <summary>
        /// Get the discovery configuration
        /// </summary>
        /// <param name="config">Configuration</param>
        /// <returns>Discovery configuration</returns>
        public virtual async Task<IReadOnlyDictionary<string, DomainConfig>> GetDiscoveryConfigAsync(IConfigurationRoot config)
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
            Dictionary<string, DomainConfig> discoveryDomains = new(
                Enumerable.Range(0, discovery.Count).Select(i => new KeyValuePair<string, DomainConfig>((string)keys[i], (DomainConfig)values[i]))
                );
            // Apply email mappings
            if (!string.IsNullOrWhiteSpace(EmailMappings))
                if (File.Exists(EmailMappings))
                {
                    Logging.WriteInfo($"Loading email mappings from \"{EmailMappings}\"");
                    FileStream fs = FsHelper.CreateFileStream(EmailMappings, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using (fs.DynamicContext())
                    {
                        EmailMapping[] mappings = await JsonHelper.DecodeAsync<EmailMapping[]>(fs).DynamicContext()
                            ?? throw new InvalidDataException("Invalid email mappings");
                        foreach(EmailMapping mapping in mappings)
                        {
                            if (!mapping.Email.Contains('@'))
                                continue;
                            string email = mapping.Email.ToLower();
                            if (
                                !MailAddress.TryCreate(mapping.Email, out MailAddress? emailAddress) ||
                                (emailAddress.User.Length == 1 && (emailAddress.User[0] == '*' || emailAddress.User[0] == '@')) ||
                                EmailMapping.GetLoginUser(mappings, email) is not string loginUser
                                )
                                continue;
                            string[] emailParts = mapping.Email.ToLower().Split('@', 2);
                            if (emailParts.Length != 2 || DomainConfig.GetConfig(string.Empty, emailParts) is not DomainConfig domain)
                                continue;
                            if (Logging.Debug)
                                Logging.WriteDebug($"Mapping email address \"{email}\" to login user \"{loginUser}\"");
                            domain.LoginNameMapping ??= [];
                            domain.LoginNameMapping[email] = loginUser;
                        }
                    }
                }
                else
                {
                    Logging.WriteWarning($"Email mappings file \"{EmailMappings}\" not found");
                }
            return discoveryDomains.ToFrozenDictionary();
        }
    }
}
