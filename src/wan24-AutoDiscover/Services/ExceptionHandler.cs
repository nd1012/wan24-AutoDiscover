using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    /// <summary>
    /// Exception handler
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    public sealed class ExceptionHandler() : IExceptionHandler
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
        /// Bad request message bytes
        /// </summary>
        private static readonly byte[] BadRequestMessage = "Bad Request".GetBytes();
        /// <summary>
        /// Internal server error message bytes
        /// </summary>
        private static readonly byte[] InternalServerErrorMessage = "Internal Server Error".GetBytes();
        /// <summary>
        /// Maintenance message bytes
        /// </summary>
        private static readonly byte[] MaintenanceMessage = "Temporary Not Available".GetBytes();

        /// <inheritdoc/>
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (httpContext.Response.HasStarted) return false;
            CancellationTokenSource cts = httpContext.RequestServices.GetRequiredService<CancellationTokenSource>();
            httpContext.Response.ContentType = TEXT_MIME_TYPE;
            ReadOnlyMemory<byte> body;
            if (exception is BadHttpRequestException badRequest)
            {
                if (Logging.Trace)
                    Logging.WriteTrace($"http handling bad request exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = badRequest.StatusCode;
                // The framework message describes why the request was rejected (encoded on the error path only)
                body = badRequest.Message.Length == 0 ? BadRequestMessage : badRequest.Message.GetBytes();
            }
            else if (exception is OperationCanceledException)
            {
                if (cts.IsCancellationRequested)
                {
                    if (Logging.Info)
                        Logging.WriteInfo($"http handling operation canceled exception due shutdown for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\"");
                }
                else if (Logging.Warning)
                {
                    Logging.WriteWarning($"http handling operation canceled exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                }
                httpContext.Response.StatusCode = MAINTENANCE_STATUS_CODE;
                body = MaintenanceMessage;
            }
            else
            {
                if (Logging.Error)
                    Logging.WriteError($"http handling exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = INTERNAL_SERVER_ERROR_STATUS_CODE;
                body = InternalServerErrorMessage;
            }
            httpContext.Response.ContentLength = body.Length;
            await httpContext.Response.Body.WriteAsync(body, cancellationToken).DynamicContext();
            return true;
        }
    }
}
