using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using wan24.AutoDiscover.Services;

namespace wan24.AutoDiscover.Tests
{
    /// <summary>
    /// <see cref="ExceptionHandler"/> tests
    /// </summary>
    [TestClass]
    public sealed class ExceptionHandler_Test
    {
        /// <summary>
        /// A bad request exception results in its status code and message
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(5000)]
        public async Task TryHandleAsync_BadRequest()
        {
            using CancellationTokenSource cts = new();
            (bool handled, HttpContext context, string body) = await HandleAsync(
                new BadHttpRequestException("Request body too large.", (int)HttpStatusCode.RequestEntityTooLarge),
                cts
                );
            Assert.IsTrue(handled);
            Assert.AreEqual((int)HttpStatusCode.RequestEntityTooLarge, context.Response.StatusCode);
            Assert.AreEqual(ExceptionHandler.TEXT_MIME_TYPE, context.Response.ContentType);
            Assert.AreEqual("Request body too large.", body);
            Assert.AreEqual(body.Length, context.Response.ContentLength);
        }

        /// <summary>
        /// A canceled operation results in a service unavailable message
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(5000)]
        public async Task TryHandleAsync_Canceled()
        {
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();
            (bool handled, HttpContext context, string body) = await HandleAsync(new OperationCanceledException(), cts);
            Assert.IsTrue(handled);
            Assert.AreEqual((int)HttpStatusCode.ServiceUnavailable, context.Response.StatusCode);
            Assert.AreEqual("Temporary Not Available", body);
        }

        /// <summary>
        /// Any other exception results in an internal server error message
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(5000)]
        public async Task TryHandleAsync_Error()
        {
            using CancellationTokenSource cts = new();
            (bool handled, HttpContext context, string body) = await HandleAsync(new InvalidOperationException("Test"), cts);
            Assert.IsTrue(handled);
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
            Assert.AreEqual("Internal Server Error", body);
            Assert.AreEqual(body.Length, context.Response.ContentLength);
        }

        /// <summary>
        /// Handle an exception
        /// </summary>
        /// <param name="exception">Exception</param>
        /// <param name="cts">Service cancellation (registered as service)</param>
        /// <returns>If handled, the HTTP context and the written response body</returns>
        private static async Task<(bool Handled, HttpContext Context, string Body)> HandleAsync(Exception exception, CancellationTokenSource cts)
        {
            ServiceCollection services = new();
            services.AddSingleton(cts);
            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            using MemoryStream responseBody = new();
            DefaultHttpContext context = new()
            {
                RequestServices = serviceProvider
            };
            context.Response.Body = responseBody;
            bool handled = await new ExceptionHandler().TryHandleAsync(context, exception, CancellationToken.None);
            return (handled, context, Encoding.UTF8.GetString(responseBody.ToArray()));
        }
    }
}
