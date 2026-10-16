using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace S4ClothesRenderer;

public sealed class MainForm : Form
{
    private const string Host = "https://app.s4clothes/";
    private readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(14, 14, 20) };
    private readonly Api api;

    public MainForm()
    {
        api = new Api(Send, OnUi, PickFolder, PickFile);
        Text = api.Title;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Color.FromArgb(14, 14, 20);
        MinimumSize = new Size(1100, 700);
        Size = new Size(1440, 900);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(web);
        Load += async (_, _) => await Init();
        FormClosing += (_, _) => api.Runner.Stop();
    }

    private async Task Init()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.Folder, "WebView2"));
            await web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this, "Microsoft Edge WebView2 Runtime is required.\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        var core = web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = System.Diagnostics.Debugger.IsAttached;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = System.Diagnostics.Debugger.IsAttached;
        core.Settings.IsZoomControlEnabled = false;
        core.AddWebResourceRequestedFilter(Host + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnRequest;
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Uri.StartsWith("http")) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true });
        };
        core.Navigate(Host);
    }

    private async void OnRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var uri = new Uri(e.Request.Uri);
            MemoryStream body = null;
            if (e.Request.Content != null)
            {
                body = new MemoryStream();
                e.Request.Content.CopyTo(body);
                body.Position = 0;
            }
            var response = await Task.Run(() => api.Handle(uri.AbsolutePath, uri.Query, body));
            var headers = "Content-Type: " + response.ContentType + "\r\nCache-Control: no-store";
            e.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(response.Body, response.Status, response.Status == 200 ? "OK" : "Error", headers);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Send(string type, object data)
    {
        if (IsDisposed || !IsHandleCreated) return;
        var message = JsonSerializer.Serialize(new { type, data }, Json.Options);
        BeginInvoke(() =>
        {
            if (web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(message);
        });
    }

    private string OnUi(Func<string> action) => (string)Invoke(action);

    private string PickFolder(string initial, bool create)
    {
        using var dialog = new FolderBrowserDialog { ShowNewFolderButton = create, UseDescriptionForTitle = true };
        if (Directory.Exists(initial)) dialog.InitialDirectory = initial;
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : "";
    }

    private string PickFile(string initial)
    {
        using var dialog = new OpenFileDialog { Filter = "refs.json|*.json", FileName = "refs.json" };
        if (Directory.Exists(initial)) dialog.InitialDirectory = initial;
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : "";
    }
}
