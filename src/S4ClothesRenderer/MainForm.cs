using System.Runtime.InteropServices;
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
        FormBorderStyle = FormBorderStyle.None;
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
        core.WebMessageReceived += OnWindowMessage;
        core.NavigationCompleted += (_, _) => SendWindowState();
        core.Navigate(Host);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x00040000 | 0x00020000 | 0x00010000 | 0x00080000;
            cp.ClassStyle |= 0x00020000;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0083 && m.WParam != IntPtr.Zero)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var round = 2;
        DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        var screen = Screen.FromHandle(Handle);
        var area = screen.WorkingArea;
        MaximizedBounds = new Rectangle(area.X - screen.Bounds.X, area.Y - screen.Bounds.Y, area.Width, area.Height);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SendWindowState();
    }

    private void SendWindowState() => Send("win", new { max = WindowState == FormWindowState.Maximized });

    private void OnWindowMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("action", out var action)) return;
        switch (action.GetString())
        {
            case "drag":
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 2, 0);
                break;
            case "resize":
                ReleaseCapture();
                SendMessage(Handle, 0xA1, root.GetProperty("edge").GetInt32(), 0);
                break;
            case "min":
                WindowState = FormWindowState.Minimized;
                break;
            case "max":
                OnLocationChanged(EventArgs.Empty);
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                break;
            case "close":
                Close();
                break;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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
