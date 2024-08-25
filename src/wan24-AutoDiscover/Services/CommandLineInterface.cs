using System.ComponentModel;
using System.Text;
using wan24.CLI;
using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    /// <summary>
    /// CLI API
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    [CliApi("autodiscover")]
    [DisplayText("wan24-AutoDiscover API")]
    [Description("wan24-AutoDiscover CLI API methods")]
    public sealed partial class CommandLineInterface()
    {
        /// <summary>
        /// Create service information
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        [CliApi("systemd")]
        [DisplayText("systemd service")]
        [Description("Create a systemd service file")]
        [StdOut("/etc/systemd/system/autodiscover.service")]
        public static async Task CreateSystemdServiceAsync(CancellationToken cancellationToken = default)
        {
            Stream stdOut = Console.OpenStandardOutput();
            await using (stdOut.DynamicContext())
            using (StreamWriter writer = new(stdOut, Encoding.UTF8, leaveOpen: true))
                await writer.WriteLineAsync(new SystemdServiceFile().ToString().Trim().AsMemory(), cancellationToken).DynamicContext();
        }
    }
}
