using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    /// <summary>
    /// <see cref="XmlResponse"/> instances
    /// </summary>
    public sealed class XmlResponseInstances(in int capacity) : InstancePool<XmlResponse>(capacity)
    {
    }
}
