using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Xml;
using wan24.AutoDiscover.Models;
using wan24.AutoDiscover.Services;
using wan24.Core;

namespace wan24.AutoDiscover.Controllers
{
    /// <summary>
    /// Discovery controller
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    /// <param name="responses">Responses</param>
    [ApiController, Route("autodiscover")]
    public sealed class DiscoveryController(InstancePool<XmlResponse> responses) : ControllerBase()
    {
        /// <summary>
        /// Max. request length in bytes (a formatted POX request with an email address of the maximum length of 254 characters needs less than 1 KiB)
        /// </summary>
        private const int MAX_REQUEST_LEN = 4096;
        /// <summary>
        /// OK http status code
        /// </summary>
        private const int OK_STATUS_CODE = (int)HttpStatusCode.OK;
        /// <summary>
        /// Bad request http status code
        /// </summary>
        private const int BAD_REQUEST_STATUS_CODE = (int)HttpStatusCode.BadRequest;
        /// <summary>
        /// XML response MIME type
        /// </summary>
        private const string XML_MIME_TYPE = "application/xml";
        /// <summary>
        /// <c>EMailAddress</c> node name
        /// </summary>
        private const string EMAIL_NODE_NAME = "EMailAddress";

        /// <summary>
        /// Invalid request XML message bytes
        /// </summary>
        private static readonly byte[] InvalidRequestMessage = "Invalid Request XML".GetBytes();
        /// <summary>
        /// Missing email address message bytes
        /// </summary>
        private static readonly byte[] MissingEmailMessage = "Missing Email Address".GetBytes();
        /// <summary>
        /// Invalid email address message bytes
        /// </summary>
        private static readonly byte[] InvalidEmailMessage = "Invalid Email Address".GetBytes();
        /// <summary>
        /// Unknown domain name message bytes
        /// </summary>
        private static readonly byte[] UnknownDomainMessage = "Unknown Domain Name".GetBytes();

        /// <summary>
        /// Request XML reader settings (asynchronous reading)
        /// </summary>
        private static readonly XmlReaderSettings RequestXmlSettings = new()
        {
            Async = true
        };

        /// <summary>
        /// Responses
        /// </summary>
        private readonly InstancePool<XmlResponse> Responses = responses;

        /// <summary>
        /// Autodiscover (POX request body required; the request content type isn't checked, because clients send <c>text/xml</c> or
        /// <c>application/xml</c> - the body is parsed as XML)
        /// </summary>
        /// <returns>POX response</returns>
        [HttpPost("autodiscover.xml"), RequestSizeLimit(MAX_REQUEST_LEN), Produces(XML_MIME_TYPE)]
        public async Task AutoDiscoverAsync()
        {
            // Validate the request and try getting the email address
            if (Logging.Trace)
                Logging.WriteTrace($"POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}");
            string? emailAddress = null;// Full email address
            using (MemoryPoolStream ms = new())
            {
                await HttpContext.Request.Body.CopyToAsync(ms, HttpContext.RequestAborted).DynamicContext();
                ms.Position = 0;
                try
                {
                    using XmlReader requestXml = XmlReader.Create(ms, RequestXmlSettings);
                    while (await requestXml.ReadAsync().DynamicContext())
                    {
                        if (!requestXml.Name.Equals(EMAIL_NODE_NAME, StringComparison.OrdinalIgnoreCase))
                            continue;
                        emailAddress = await requestXml.ReadElementContentAsStringAsync().DynamicContext();
                        break;
                    }
                }
                catch(XmlException ex)
                {
                    if (Logging.Debug)
                        Logging.WriteDebug($"Parsing POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} failed: {ex}");
                    await RespondBadRequestAsync(InvalidRequestMessage).DynamicContext();
                    return;
                }
            }
            if(emailAddress is null)
            {
                await RespondBadRequestAsync(MissingEmailMessage).DynamicContext();
                return;
            }
            if (Logging.Trace)
                Logging.WriteTrace($"POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} for email address {emailAddress.ToQuotedLiteral()}");
            // @ splitted email alias and domain name (strings, because the domain configuration and login name mapping lookups use string keys)
            string[] emailParts = emailAddress.Split('@', 2);
            if (emailParts.Length != 2 || !MailAddress.TryCreate(emailAddress, out _))
            {
                await RespondBadRequestAsync(InvalidEmailMessage).DynamicContext();
                return;
            }
            // Generate the response
            if (DomainConfig.GetConfig(HttpContext.Request.Host.Host, emailParts) is not DomainConfig config)
            {
                await RespondBadRequestAsync(UnknownDomainMessage).DynamicContext();
                return;
            }
            if (Logging.Trace)
                Logging.WriteTrace($"Creating POX response for \"{emailAddress}\" request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}");
            HttpContext.Response.StatusCode = OK_STATUS_CODE;
            HttpContext.Response.ContentType = XML_MIME_TYPE;
            using XmlResponse responseXml = await Responses.GetOneAsync(HttpContext.RequestAborted).DynamicContext();// Response XML
            // A client abort ends the XML output, which releases the XML writer, if it waits for buffer space
            using CancellationTokenRegistration abortRegistration = HttpContext.RequestAborted.Register(static state => EndXmlOutput((XmlResponse)state!), responseXml);
            Task sendXmlOutput = responseXml.XmlOutput.CopyToAsync(HttpContext.Response.Body, HttpContext.RequestAborted);
            try
            {
                config.CreateXml(responseXml.XML, emailParts);
                responseXml.FinalizeXmlOutput();
            }
            catch (Exception ex)
            {
                // The exception is logged here, because disposing the XML writer of the ended output may throw another exception
                if (Logging.Error)
                    Logging.WriteError($"Creating the POX response for \"{emailAddress}\" request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} failed: {ex}");
                // Lets the copy task complete with the XML written so far (the response has been started already)
                EndXmlOutput(responseXml);
                throw;
            }
            finally
            {
                await sendXmlOutput.DynamicContext();
                if (Logging.Trace)
                    Logging.WriteTrace($"POX response for \"{emailAddress}\" request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} sent");
            }
        }

        /// <summary>
        /// Respond with a bad request message
        /// </summary>
        /// <param name="message">UTF-8 message (borrowed until the returned task completed)</param>
        /// <returns>Task which completes when the message was written to the response body</returns>
        private ValueTask RespondBadRequestAsync(ReadOnlyMemory<byte> message)
        {
            if (Logging.Trace)
                Logging.WriteTrace($"Invalid POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}: \"{Encoding.UTF8.GetString(message.Span)}\"");
            HttpContext.Response.StatusCode = BAD_REQUEST_STATUS_CODE;
            HttpContext.Response.ContentType = ExceptionHandler.TEXT_MIME_TYPE;
            HttpContext.Response.ContentLength = message.Length;
            return HttpContext.Response.Body.WriteAsync(message, HttpContext.RequestAborted);
        }

        /// <summary>
        /// End the XML output of a response (a waiting reader receives the end of the data, a writer which waits for buffer space fails with an
        /// <see cref="InvalidOperationException"/>)
        /// </summary>
        /// <param name="response">Response</param>
        private static void EndXmlOutput(XmlResponse response)
        {
            try
            {
                if (!response.XmlOutput.IsEndOfFile)
                    response.XmlOutput.IsEndOfFile = true;
            }
            catch (InvalidOperationException)
            {
                // The output has been ended concurrently, or the response has been disposed already (ObjectDisposedException)
            }
        }
    }
}
