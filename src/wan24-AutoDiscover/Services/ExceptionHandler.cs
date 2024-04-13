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

        /// <summary>
        /// Constructor
        /// </summary>
        public ExceptionHandler() { }

        /// <inheritdoc/>
        public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (httpContext.Response.HasStarted) return ValueTask.FromResult(false);
            CancellationTokenSource cts = httpContext.RequestServices.GetRequiredService<CancellationTokenSource>();
            httpContext.Response.ContentType = TEXT_MIME_TYPE;
            if (exception is BadHttpRequestException badRequest)
            {
                if (Logging.Trace)
                    Logging.WriteTrace($"http handling bad request exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = badRequest.StatusCode;
                httpContext.Response.Body = new MemoryStream(badRequest.Message is null? BadRequestMessage : badRequest.Message.GetBytes());
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
                httpContext.Response.Body = new MemoryStream(MaintenanceMessage);
            }
            else
            {
                Logging.WriteError($"http handling exception for {httpContext.Connection.RemoteIpAddress}:{httpContext.Connection.RemotePort} request to \"{httpContext.Request.Method} {httpContext.Request.Path}\": {exception}");
                httpContext.Response.StatusCode = INTERNAL_SERVER_ERROR_STATUS_CODE;
                httpContext.Response.Body = new MemoryStream(InternalServerErrorMessage);
            }
            return ValueTask.FromResult(true);
        }
    }
}
