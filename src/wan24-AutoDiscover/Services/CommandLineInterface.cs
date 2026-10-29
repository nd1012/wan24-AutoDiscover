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
        // The CLI API methods have no CancellationToken parameter: for a parameter without CliApiAttribute wan24-CLI passes
        // ParameterInfo.DefaultValue, which is null for a default struct value, and the compiled method invoker fails to unbox it

        /// <summary>
        /// Create service information
        /// </summary>
        /// <returns>Task</returns>
        [CliApi("systemd")]
        [DisplayText("systemd service")]
        [Description("Create a systemd service file")]
        [StdOut("/etc/systemd/system/autodiscover.service")]
        public static async Task CreateSystemdServiceAsync()
        {
            Stream stdOut = Console.OpenStandardOutput();
            await using (stdOut.DynamicContext())
            using (StreamWriter writer = new(stdOut, Encoding.UTF8, leaveOpen: true))
                await writer.WriteLineAsync(new SystemdServiceFile().ToString().Trim().AsMemory(), CancellationToken.None).DynamicContext();
        }
    }
}
