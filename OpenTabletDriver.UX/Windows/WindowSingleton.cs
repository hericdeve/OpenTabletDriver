using System;
using Eto.Forms;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.UX.Windows
{
    public class WindowSingleton<T> where T : Window, new()
    {
        private readonly object sync = new();
        private T? window;

        public T GetWindow()
        {
            lock (sync)
            {
                if (window == null)
                {
                    window = new T();
                    window.Closed += HandleWindowClosed;
                }

                return window;
            }
        }

        public void Show()
        {
            var window = GetWindow();

            try
            {
                switch (window)
                {
                    case DesktopForm desktopForm:
                        desktopForm.Show();
                        break;
                    case Form form:
                        if (!form.Visible)
                            form.Show();
                        form.Focus();
                        break;
                    case Dialog dialog:
                        dialog.ShowModal();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        public void Close()
        {
            if (window != null)
                GetWindow().Close();
        }

        private void HandleWindowClosed(object? sender, EventArgs e)
        {
            window = null;
        }
    }
}
