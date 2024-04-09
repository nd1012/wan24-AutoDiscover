using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using wan24.AutoDiscover.Models;
using wan24.CLI;
using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    // Postfix
    public sealed partial class CommandLineInterface
    {
        /// <summary>
        /// Regular expression to match a Postfix email mapping (<c>$1</c> contains the email address, <c>$2</c> contains the comma separated targets)
        /// </summary>
        private static readonly Regex RX_POSTFIX = RX_POSTFIX_Generator();

        /// <summary>
        /// Parse Postfix email mappings
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        [CliApi("postfix")]
        [DisplayText("Postfix email mapping")]
        [Description("Parse the email mapping from a Postfix hash text file")]
        [StdIn("/etc/postfix/virtual")]
        [StdOut("/home/autodiscover/postfix.json")]
        public static async Task ParsePostfixEmailMappingsAsync(CancellationToken cancellationToken = default)
        {
            HashSet<EmailMapping> mappings = [];
            Stream stdIn = Console.OpenStandardInput();
            await using (stdIn.DynamicContext())
            {
                using StreamReader reader = new(stdIn, Encoding.UTF8, leaveOpen: true);
                while (await reader.ReadLineAsync(cancellationToken).DynamicContext() is string line)
                {
                    if (!RX_POSTFIX.IsMatch(line)) continue;
                    string[] info = RX_POSTFIX.Replace(line, "$1\t$2").Split('\t', 2);
                    mappings.Add(new()
                    {
                        Email = info[0].ToLower(),
                        Targets = new List<string>((from target in info[1].Split(',') select target.Trim()).Distinct())
                    });
                }
            }
            Stream stdOut = Console.OpenStandardOutput();
            await using (stdOut.DynamicContext())
                await JsonHelper.EncodeAsync(mappings, stdOut, prettify: true, cancellationToken).DynamicContext();
        }

        /// <summary>
        /// Regular expression to match a Postfix email mapping (<c>$1</c> contains the email address, <c>$2</c> contains the comma separated targets)
        /// </summary>
        /// <returns>Regular expression</returns>
        [GeneratedRegex(@"^\s*([^\*\@#][^\s]+)\s*([^\s]+)\s*$", RegexOptions.Compiled)]
        private static partial Regex RX_POSTFIX_Generator();
    }
}
