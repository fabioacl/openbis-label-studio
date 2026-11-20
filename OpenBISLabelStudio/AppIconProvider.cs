using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace OpenBISLabelStudio
{
    /// <summary>
    /// Loads and manages lifecycle of the embedded custom window <see cref="Icon"/>.
    /// </summary>
    internal static class AppIconProvider
    {
        private const string IconResourceName = "OpenBISLabelStudio.Assets.app-icon.ico";

        /// <summary>
        /// Creates a new <see cref="Icon"/> instance from the embedded multi-resolution ICO resource,
        /// returning <c>null</c> cleanly if the resource is missing or invalid.
        /// </summary>
        public static Icon? TryCreateWindowIcon()
        {
            try
            {
                Assembly assembly = typeof(AppIconProvider).Assembly;
                using Stream? stream = assembly.GetManifestResourceStream(IconResourceName);
                if (stream == null)
                {
                    return null;
                }

                return new Icon(stream);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Assigns a dedicated window icon owned by <paramref name="form"/>.
        /// </summary>
        public static void AttachWindowIcon(Form form, ref Icon? ownedIcon)
        {
            if (form == null)
            {
                return;
            }

            Icon? nextIcon = TryCreateWindowIcon();
            if (nextIcon == null)
            {
                return;
            }

            Icon? previous = ownedIcon;
            ownedIcon = nextIcon;
            form.Icon = nextIcon;
            previous?.Dispose();
        }

        /// <summary>
        /// Detaches and disposes the owned <see cref="Icon"/> safely on form disposal.
        /// </summary>
        public static void DetachWindowIcon(Form form, ref Icon? ownedIcon)
        {
            if (form != null && !form.IsDisposed && ReferenceEquals(form.Icon, ownedIcon))
            {
                form.Icon = null;
            }

            ownedIcon?.Dispose();
            ownedIcon = null;
        }
    }
}
