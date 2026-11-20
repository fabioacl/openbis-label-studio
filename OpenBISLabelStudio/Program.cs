using System;
using System.Text;
using OpenBISLabelStudio.Services;

namespace OpenBISLabelStudio
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--preview-worker", StringComparison.Ordinal))
            {
                Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                LabelPreviewRenderer.RunPreviewWorkerLoop(Console.In, Console.Out);
                return;
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            try
            {
                Application.Run(new Form1());
            }
            finally
            {
                LabelPreviewRenderer.ShutdownPreviewWorker();
            }
        }
    }
}