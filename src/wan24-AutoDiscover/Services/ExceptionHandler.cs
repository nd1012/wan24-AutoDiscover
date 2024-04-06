using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    /// <summary>
    /// Exception handler
    /// </summary>
    public sealed class ExceptionHandler : IExceptionHandler
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public ExceptionHandler() { }

        /// <inheritdoc/>
        public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is BadHttpRequestException badRequest)
            {
                if (Logging.Trace)
                    Logging.WriteTrace($"http handling bas request exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = badRequest.StatusCode;
            }
            else
            {
                Logging.WriteError($"http handling exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            return ValueTask.FromResult(true);
        }
    }
}
