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
        private const int INTERNAL_SERVER_ERROR_STATUS_CODE = (int)HttpStatusCode.InternalServerError;
        /// <summary>
        /// Maintenance code
        /// </summary>
        private const int MAINTENANCE_STATUS_CODE = (int)HttpStatusCode.ServiceUnavailable;
        /// <summary>
        /// Text MIME type
        /// </summary>
        public const string TEXT_MIME_TYPE = "text/plain";

        /// <summary>
        /// Internal server error message bytes
        /// </summary>
        private static readonly byte[] InternalServerErrorMessage = "Internal server error".GetBytes();
        /// <summary>
        /// Maintenance message bytes
        /// </summary>
        private static readonly byte[] MaintenanceMessage = "Temporary not available".GetBytes();

        /// <summary>
        /// Constructor
        /// </summary>
        public ExceptionHandler() { }

        /// <inheritdoc/>
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            CancellationTokenSource cts = httpContext.RequestServices.GetRequiredService<CancellationTokenSource>();
            httpContext.Response.ContentType = TEXT_MIME_TYPE;
            if (exception is BadHttpRequestException badRequest)
            {
                if (Logging.Trace)
                    Logging.WriteTrace($"http handling bad request exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = badRequest.StatusCode;
                await httpContext.Response.BodyWriter.WriteAsync((badRequest.Message ?? "Bad request").GetBytes(), cancellationToken).DynamicContext();
            }
            else if (exception is OperationCanceledException)
            {
                if (cts.IsCancellationRequested)
                {
                    Logging.WriteInfo($"http handling operation canceled exception due shutdown for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\"");
                }
                else
                {
                    Logging.WriteWarning($"http handling operation canceled exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                }
                httpContext.Response.StatusCode = MAINTENANCE_STATUS_CODE;
                await httpContext.Response.BodyWriter.WriteAsync(MaintenanceMessage, cancellationToken).DynamicContext();
            }
            else
            {
                Logging.WriteError($"http handling exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = INTERNAL_SERVER_ERROR_STATUS_CODE;
                await httpContext.Response.BodyWriter.WriteAsync(InternalServerErrorMessage, cancellationToken).DynamicContext();
            }
            return true;
        }
    }
}
