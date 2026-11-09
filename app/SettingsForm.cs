using System.Diagnostics;

namespace PadLink;

/// <summary>连接信息窗口：地址、在线设备、二维码（用 PowerShell 生成）。</summary>
internal sealed class SettingsForm : Form
{
    public SettingsForm(string baseUrl, int connected)
    {
        Text = "PadLink · 连接信息";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 340);
        Font = new Font("Segoe UI", 10f);

        var ip = TrayContext.LocalIP;
        var lblUrl = new Label
        {
            Text = $"手机浏览器打开：\n{baseUrl}",
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top, Height = 70, Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        };
        var lblConn = new Label
        {
            Text = $"局域网 IP：{ip}    当前连接设备：{connected}",
            Dock = DockStyle.Top, Height = 32, TextAlign = ContentAlignment.MiddleCenter,
        };
        var btnCopy = new Button { Text = "复制地址", Dock = DockStyle.Top, Height = 36 };
        btnCopy.Click += (s, e) => { Clipboard.SetText(baseUrl); lblConn.Text = "已复制 ✓"; };
        var btnBrowser = new Button { Text = "在浏览器打开", Dock = DockStyle.Top, Height = 36 };
        btnBrowser.Click += (s, e) => Process.Start(new ProcessStartInfo(baseUrl) { UseShellExecute = true });
        var btnScan = new Button { Text = "二维码（弹窗后用手机相机扫码）", Dock = DockStyle.Top, Height = 36 };
        btnScan.Click += (s, e) => ShowQr(baseUrl);

        Controls.Add(btnScan);
        Controls.Add(btnBrowser);
        Controls.Add(btnCopy);
        Controls.Add(lblConn);
        Controls.Add(lblUrl);
    }

    /// <summary>调用系统 PowerShell + QRCoder？不引第三方，用网页版二维码：打开默认浏览器显示 /static/qr.html</summary>
    private void ShowQr(string baseUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(baseUrl.TrimEnd('/') + "/static/qr.html?url=" + Uri.EscapeDataString(baseUrl)) { UseShellExecute = true });
        }
        catch { MessageBox.Show("打开失败，请手动在浏览器打开 " + baseUrl); }
    }
}
