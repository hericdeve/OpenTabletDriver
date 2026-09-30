namespace OpenTabletDriver.Plugin
{
    public interface IPointerSuppressor
    {
        /// <summary>
        /// Whether the suppressor is currently active.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Whether pointer motion should be suppressed / anchored while active.
        /// </summary>
        bool SuppressMotion { get; }

        /// <summary>
        /// Whether tip contact pressure / clicks should be suppressed while active.
        /// </summary>
        bool SuppressTip { get; }
    }
}
