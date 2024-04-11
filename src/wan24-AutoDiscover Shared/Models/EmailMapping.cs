using System.ComponentModel.DataAnnotations;
using wan24.ObjectValidation;

namespace wan24.AutoDiscover.Models
{
    /// <summary>
    /// Email mapping
    /// </summary>
    public record class EmailMapping : ValidatableRecordBase
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public EmailMapping() : base() { }

        /// <summary>
        /// Emailaddress
        /// </summary>
        [EmailAddress]
        public required string Email { get; init; }

        /// <summary>
        /// Target email addresses or user names
        /// </summary>
        [CountLimit(1, int.MaxValue), ItemStringLength(byte.MaxValue)]
        public required IReadOnlyList<string> Targets { get; init; }

        /// <summary>
        /// Get the login user from email mappings for an email address
        /// </summary>
        /// <param name="mappings">Mappings</param>
        /// <param name="email">Email address</param>
        /// <returns>Login user</returns>
        public static string? GetLoginUser(IEnumerable<EmailMapping> mappings, string email)
        {
            if (mappings.FirstOrDefault(m => m.Email.Equals(email, StringComparison.OrdinalIgnoreCase)) is not EmailMapping mapping)
                return null;
            if (mapping.Targets.FirstOrDefault(t => !t.Contains('@')) is string loginName)
                return loginName;
            HashSet<string> seen = [email];
            Queue<string> emails = [];
            foreach (string target in mapping.Targets)
                emails.Enqueue(target.ToLower());
            while(emails.TryDequeue(out string? target))
            {
                if (
                    !seen.Add(target) || 
                    mappings.FirstOrDefault(m => m.Email.Equals(email, StringComparison.OrdinalIgnoreCase)) is not EmailMapping targetMapping
                    )
                    continue;
                if (targetMapping.Targets.FirstOrDefault(t => !t.Contains('@')) is string targetLoginName)
                    return targetLoginName;
                foreach (string subTarget in targetMapping.Targets)
                    emails.Enqueue(subTarget.ToLower());
            }
            return null;
        }
    }
}
