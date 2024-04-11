using System.Xml;
using wan24.Core;

namespace wan24.AutoDiscover
{
    /// <summary>
    /// XML response
    /// </summary>
    public class XmlResponse : DisposableBase
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public XmlResponse() : base(asyncDisposing: false)
        {
            XmlOutput = new(bufferSize: 1024)
            {
                AggressiveReadBlocking = false
            };
            XML = XmlWriter.Create(XmlOutput);
            XML.WriteStartElement(Constants.AUTODISCOVER_NODE_NAME, Constants.AUTO_DISCOVER_NS);
            XML.WriteStartElement(Constants.RESPONSE_NODE_NAME, Constants.RESPONSE_NS);
            XML.WriteStartElement(Constants.ACCOUNT_NODE_NAME);
            XML.WriteElementString(Constants.ACCOUNTTYPE_NODE_NAME, Constants.ACCOUNTTYPE);
            XML.WriteElementString(Constants.ACTION_NODE_NAME, Constants.ACTION);
            XML.Flush();
        }

        /// <summary>
        /// XML
        /// </summary>
        public XmlWriter XML { get; }

        /// <summary>
        /// XML output
        /// </summary>
        public BlockingBufferStream XmlOutput { get; }

        /// <summary>
        /// Finalize
        /// </summary>
        public virtual void FinalizeXmlOutput()
        {
            EnsureUndisposed();
            XML.WriteEndElement();
            XML.WriteEndElement();
            XML.WriteEndElement();
            XML.Flush();
            XmlOutput.IsEndOfFile = true;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            XML.Dispose();
            XmlOutput.Dispose();
        }
    }
}
