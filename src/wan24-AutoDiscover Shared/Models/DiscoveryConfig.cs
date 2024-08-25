using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Mail;
using System.Text.Json.Serialization;
using wan24.Core;
using wan24.ObjectValidation;

namespace wan24.AutoDiscover.Models
{
    /// <summary>
    /// Discovery configuration
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    public record class DiscoveryConfig() : ValidatableRecordBase()
    {
        /// <summary>
        /// Discovery configuration type
        /// </summary>
        protected Type? _DiscoveryType = null;

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
        /// Discovery configuration type name
        /// </summary>
        [StringLength(byte.MaxValue, MinimumLength = 1)]
        public string? DiscoveryTypeName { get; init; }

        /// <summary>
        /// Discovery configuration type
        /// </summary>
        [JsonIgnore]
        public virtual Type DiscoveryType => _DiscoveryType ??= string.IsNullOrWhiteSpace(DiscoveryTypeName)
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
        [StringLength(short.MaxValue, MinimumLength = 1)]
        public string? EmailMappings { get; init; }

        /// <summary>
        /// Watch email mappings list file changes for reloading the configuration?
        /// </summary>
        public bool WatchEmailMappings { get; init; } = true;

        /// <summary>
        /// Additional file paths to watch for an automatic configuration reload
        /// </summary>
        [CountLimit(1, byte.MaxValue), ItemStringLength(short.MaxValue)]
        public string[]? WatchFiles { get; init; }

        /// <summary>
        /// Command to execute (and optional arguments) before reloading the configuration
        /// </summary>
        [CountLimit(1, byte.MaxValue), ItemStringLength(short.MaxValue)]
        public string[]? PreReloadCommand { get; init; }

        /// <summary>
        /// Get the discovery configuration
        /// </summary>
        /// <param name="config">Configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Discovery configuration</returns>
        public virtual async Task<IReadOnlyDictionary<string, DomainConfig>> GetDiscoveryConfigAsync(IConfigurationRoot config, CancellationToken cancellationToken = default)
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
                throw new InvalidDataException($"Discovery types first generic type argument must be {typeof(string)}");
            if (!typeof(DomainConfig).IsAssignableFrom(gt[1]))
                throw new InvalidDataException($"Discovery types second generic type argument must be a {typeof(DomainConfig)}");
            // Parse the discovery configuration
            IDictionary discovery = config.GetRequiredSection("DiscoveryConfig:Discovery").Get(discoveryType) as IDictionary
                ?? throw new InvalidDataException("Failed to get discovery configuration from the \"DiscoveryConfig:Discovery section\"");
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
                    Logging.WriteInfo($"Loading email mappings from {EmailMappings.ToQuotedLiteral()}");
                    FileStream fs = FsHelper.CreateFileStream(EmailMappings, FileMode.Open, FileAccess.Read, FileShare.Read);
                    EmailMapping[] mappings;
                    await using (fs.DynamicContext())
                        mappings = await JsonHelper.DecodeAsync<EmailMapping[]>(fs, cancellationToken).DynamicContext()
                            ?? throw new InvalidDataException("Invalid email mappings");
                    foreach(EmailMapping mapping in mappings)
                    {
                        if (!mapping.Email.Contains('@'))
                        {
                            if (Logging.Debug)
                                Logging.WriteDebug($"Skipping invalid email address {mapping.Email.ToQuotedLiteral()}");
                            continue;
                        }
                        string email = mapping.Email.ToLower();
                        string[] emailParts = email.Split('@', 2);
                        if (
                            emailParts.Length != 2 ||
                            !MailAddress.TryCreate(email, out MailAddress? emailAddress) ||
                            (emailAddress.User.Length == 1 && (emailAddress.User[0] == '*' || emailAddress.User[0] == '@')) ||
                            EmailMapping.GetLoginUser(mappings, email) is not string loginUser ||
                            DomainConfig.GetConfig(string.Empty, emailParts) is not DomainConfig domain
                            )
                        {
                            if (Logging.Debug)
                                Logging.WriteDebug($"Mapping email address {email.ToQuotedLiteral()} to login user failed, because it seems to be a redirection to an external target, or no matching domain configuration was found");
                            continue;
                        }
                        if (Logging.Debug)
                            Logging.WriteDebug($"Mapping email address {email.ToQuotedLiteral()} to login user {loginUser.ToQuotedLiteral()}");
                        domain.LoginNameMapping ??= [];
                        if (Logging.Debug && domain.LoginNameMapping.ContainsKey(email))
                            Logging.WriteDebug($"Overwriting existing email address {email.ToQuotedLiteral()} mapping");
                        domain.LoginNameMapping[email] = loginUser;
                    }
                }
                else
                {
                    Logging.WriteWarning($"Email mappings file {EmailMappings.ToQuotedLiteral()} not found");
                }
            return discoveryDomains.ToFrozenDictionary();
        }
    }
}
