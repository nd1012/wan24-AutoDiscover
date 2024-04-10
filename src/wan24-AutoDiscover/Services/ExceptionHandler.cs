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
        /// Internal server error code
        /// </summary>
        private const int INTERNAL_SERVER_ERROR_CODE = (int)HttpStatusCode.InternalServerError;

        /// <summary>
        /// Internal server error message bytes
        /// </summary>
        private static readonly byte[] InternalServerErrorMessage = "Internal server error".GetBytes();

        /// <summary>
        /// Constructor
        /// </summary>
        public ExceptionHandler() { }

        /// <inheritdoc/>
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is BadHttpRequestException badRequest)
            {
                if (Logging.Trace)
                    Logging.WriteTrace($"http handling bad request exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = badRequest.StatusCode;
                await httpContext.Response.BodyWriter.WriteAsync((badRequest.Message ?? "Bad request").GetBytes(), cancellationToken).DynamicContext();
            }
            else
            {
                Logging.WriteError($"http handling exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = INTERNAL_SERVER_ERROR_CODE;
                await httpContext.Response.BodyWriter.WriteAsync(InternalServerErrorMessage, cancellationToken).DynamicContext();
            }
            return true;
        }
    }
}
