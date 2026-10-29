using System.Buffers;
using System.ComponentModel.DataAnnotations;
using wan24.ObjectValidation;

namespace wan24.AutoDiscover.Models
{
    /// <summary>
    /// Email mapping
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    public record class EmailMapping() : ValidatableRecordBase()
    {
        /// <summary>
        /// Email address
        /// </summary>
        [EmailAddress]
        public required string Email { get; init; }

        /// <summary>
        /// Target email addresses or user names
        /// </summary>
        [CountLimit(1, int.MaxValue), ItemStringLength(byte.MaxValue)]
        public required IReadOnlyList<string> Targets { get; init; }

        /// <summary>
        /// Get the login user from email mappings for an email address (redirections to other mapped email addresses are followed breadth-first, each
        /// mapping is visited once)
        /// </summary>
        /// <param name="mappings">Mappings (borrowed for the call)</param>
        /// <param name="email">Email address (compared case-insensitive; borrowed for the call)</param>
        /// <returns>Login user (the first target without <c>@</c>), or <see langword="null"/>, if the address isn't mapped or redirects to external
        /// targets only</returns>
        public static string? GetLoginUser(ReadOnlySpan<EmailMapping> mappings, ReadOnlySpan<char> email)
        {
            int index = IndexOf(mappings, email);
            if (index < 0) return null;
            int count = mappings.Length;
            // Breadth-first queue of mapping indexes (first half) and visited flags (second half)
            using IMemoryOwner<int> buffer = MemoryPool<int>.Shared.Rent(count << 1);
            Span<int> queue = buffer.Memory.Span[..count],
                visited = buffer.Memory.Span[count..(count << 1)];
            visited.Clear();
            queue[0] = index;
            visited[index] = 1;
            for (int head = 0, tail = 1; head < tail; head++)
            {
                IReadOnlyList<string> targets = mappings[queue[head]].Targets;
                for (int i = 0, len = targets.Count; i < len; i++)
                    if (!targets[i].Contains('@'))
                        return targets[i];
                for (int i = 0, len = targets.Count; i < len; i++)
                {
                    index = IndexOf(mappings, targets[i]);
                    if (index < 0 || visited[index] != 0) continue;
                    visited[index] = 1;
                    queue[tail++] = index;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the index of the mapping of an email address
        /// </summary>
        /// <param name="mappings">Mappings (borrowed for the call)</param>
        /// <param name="email">Email address (compared case-insensitive; borrowed for the call)</param>
        /// <returns>Mapping index or <c>-1</c>, if not found</returns>
        private static int IndexOf(ReadOnlySpan<EmailMapping> mappings, ReadOnlySpan<char> email)
        {
            for (int i = 0, len = mappings.Length; i < len; i++)
                if (email.Equals(mappings[i].Email, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }
}
