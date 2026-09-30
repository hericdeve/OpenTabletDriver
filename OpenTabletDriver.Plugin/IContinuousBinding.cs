using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Plugin
{
    public interface IContinuousBinding : IStateBinding
    {
        /// <summary>
        /// Invoked on every device report while the binding is active and held.
        /// </summary>
        /// <param name="tablet">The tablet that this report is from.</param>
        /// <param name="report">The current device report.</param>
        void Update(TabletReference tablet, IDeviceReport report);
    }
}
