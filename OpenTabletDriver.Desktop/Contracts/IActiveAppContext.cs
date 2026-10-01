namespace OpenTabletDriver.Desktop.Contracts
{
    /// <summary>
    /// Provides information about the currently focused desktop application window.
    /// </summary>
    public interface IActiveAppContext
    {
        /// <summary>
        /// Gets the window class identifier of the active application (e.g. "xournalpp", "obsidian", "krita").
        /// </summary>
        string? CurrentWindowClass { get; }

        /// <summary>
        /// Gets the title of the active application window.
        /// </summary>
        string? CurrentWindowTitle { get; }
    }
}
