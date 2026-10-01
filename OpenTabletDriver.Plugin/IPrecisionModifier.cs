namespace OpenTabletDriver.Plugin
{
    /// <summary>
    /// Represents a modifier that alters the absolute cursor sensitivity or precision.
    /// </summary>
    public interface IPrecisionModifier
    {
        /// <summary>
        /// Whether precision mode is currently active.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Sensitivity scale multiplier (e.g. 0.3 for 30% speed / high precision, or >1.0 for speed mode).
        /// </summary>
        float Scale { get; }

        /// <summary>
        /// Whether the anchor reference point should automatically re-anchor when the pen lifts out of range.
        /// </summary>
        bool ReanchorOnLift { get; }
    }
}
