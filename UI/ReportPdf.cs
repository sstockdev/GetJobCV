using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GetJobCV.UI
{
    /// <summary>
    /// Prints the HTML report (<see cref="ReportHtml"/>) to a PDF with WebView2, the Edge
    /// engine built into Windows, so the PDF and the report share one design.
    /// </summary>
    internal static class ReportPdf
    {
        /// <summary>
        /// WebView2 keeps browser data in a folder. Its default is next to the exe, which
        /// may not be writable, so use the user's local app data.
        /// </summary>
        private static readonly string UserDataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GetJobCV", "WebView2");

        /// <summary>
        /// Renders <paramref name="html"/> in a hidden WebView2 on <paramref name="host"/>
        /// and saves it as a Letter-size PDF at <paramref name="path"/>.
        /// </summary>
        /// <exception cref="WebView2RuntimeNotFoundException">The WebView2 Runtime isn't installed</exception>
        /// <exception cref="IOException">The PDF couldn't be written</exception>
        public static async Task SaveAsync(Control host, string html, string path)
        {
            // WebView2 needs a window handle, so it lives on the form, hidden
            using WebView2 view = new() { Visible = false, Size = new Size(816, 1056) };
            host.Controls.Add(view);
            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, UserDataFolder);
                await view.EnsureCoreWebView2Async(environment);
                CoreWebView2 web = view.CoreWebView2;

                // The report loads nothing, and nothing in it should run
                web.Settings.IsScriptEnabled = false;
                web.Settings.AreDefaultContextMenusEnabled = false;

                TaskCompletionSource<bool> loaded = new();
                web.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
                web.NavigateToString(html);
                if (!await loaded.Task)
                    throw new IOException("The report page didn't load");

                CoreWebView2PrintSettings settings = web.Environment.CreatePrintSettings();
                settings.ShouldPrintBackgrounds = true;       // bars and chips are backgrounds
                settings.ShouldPrintHeaderAndFooter = false;  // no "about:blank" and page URL
                settings.MarginTop = settings.MarginBottom = 0.5;
                settings.MarginLeft = settings.MarginRight = 0.5;

                if (!await web.PrintToPdfAsync(path, settings))
                    throw new IOException("The PDF couldn't be written");
            }
            finally
            {
                host.Controls.Remove(view);
            }
        }
    }
}
