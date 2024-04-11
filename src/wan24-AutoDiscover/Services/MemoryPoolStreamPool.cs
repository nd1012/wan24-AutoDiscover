using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    /// <summary>
    /// <see cref="PooledMemoryStream"/> pool
    /// </summary>
    public sealed class MemoryPoolStreamPool : DisposableObjectPool<PooledMemoryStream>
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="capacity">Capacity</param>
        public MemoryPoolStreamPool(in int capacity)
            : base(
                  capacity, 
                  () =>
                  {
                      if (Logging.Trace)
                          Logging.WriteTrace("Creating a new memory pool stream");
                      return new();
                  }
                  )
            => ResetOnRent = false;
    }
}
