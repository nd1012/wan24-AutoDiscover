using System.Text;
using wan24.CLI;
using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    /// <summary>
    /// CLI API
    /// </summary>
    [CliApi("autodiscover")]
    public class CommandLineInterface
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public CommandLineInterface() { }

        /// <summary>
        /// Create service information
        /// </summary>
        [CliApi("systemd", IsDefault = true)]
        [StdOut("/etc/systemd/system/autodiscover.service")]
        public static async Task CreateSystemdServiceAsync()
        {
            Stream stdOut = Console.OpenStandardOutput();
            await using (stdOut.DynamicContext())
            using (StreamWriter writer = new(stdOut, Encoding.UTF8, leaveOpen: true))
                await writer.WriteLineAsync(new SystemdServiceFile().ToString().Trim()).DynamicContext();
        }
    }
}
