using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using RBX_Alt_Manager.Classes;

namespace RBX_Alt_Manager.Forms
{
    internal sealed class QuickLoginForm : Form
    {
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly TextBox codeBox;
        private readonly Label status;
        private readonly Button copy;
        private readonly Button approve;
        private Task running = Task.CompletedTask;
        private bool disposingStarted;

        public QuickLoginForm()
        {
            Text = "Add account - Quick Login";
            ClientSize = new Size(540, 355);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(26, 26, 34);
            ForeColor = Color.Gainsboro;
            Font = new Font("Segoe UI", 10);

            Controls.Add(new Label { Text = "Approve sign-in from your Roblox account", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(22, 20) });
            Controls.Add(new Label
            {
                Text = "On a device where you are already signed in:\nRoblox > Settings > Quick Login. Enter this code and review the request.",
                Location = new Point(22, 53), Size = new Size(495, 55)
            });
            codeBox = new TextBox
            {
                ReadOnly = true, Text = "------", TextAlign = HorizontalAlignment.Center,
                Font = new Font("Consolas", 28, FontStyle.Bold), Location = new Point(22, 116), Size = new Size(495, 56),
                BackColor = Color.FromArgb(37, 37, 48), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(codeBox);
            status = new Label { Text = "Requesting a code...", Location = new Point(22, 183), Size = new Size(495, 50) };
            Controls.Add(status);

            copy = MakeButton("Copy code", 22, 237, 136);
            copy.Enabled = false;
            copy.Click += (sender, e) =>
            {
                try { Clipboard.SetText(codeBox.Text); }
                catch { status.Text = "Clipboard is busy. Select and copy the code above."; }
            };
            approve = MakeButton("Open approval page", 170, 237, 205);
            approve.Enabled = false;
            approve.Click += (sender, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://www.roblox.com/crossdevicelogin/ConfirmCode") { UseShellExecute = true }); }
                catch { status.Text = "Open roblox.com in your normal browser, then Settings > Quick Login."; }
            };
            Button cancel = MakeButton("Cancel", 387, 237, 130);
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            Button browser = MakeButton("Sign in with password / browser instead", 22, 292, 495);
            browser.DialogResult = DialogResult.Retry;
            Shown += async (sender, e) => { running = RunAsync(); await running; };
            FormClosing += (sender, e) => lifetime.Cancel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposingStarted)
            {
                disposingStarted = true;
                lifetime.Cancel();
                _ = running.ContinueWith(task => lifetime.Dispose(), TaskScheduler.Default);
            }
            base.Dispose(disposing);
        }

        private Button MakeButton(string text, int x, int y, int width)
        {
            var button = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(width, 37),
                BackColor = Color.FromArgb(47, 47, 62), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            Controls.Add(button);
            return button;
        }

        private async Task RunAsync()
        {
            using (var client = new QuickLoginClient())
            {
                try
                {
                    await client.StartAsync(lifetime.Token);
                    codeBox.Text = client.Code;
                    copy.Enabled = approve.Enabled = true;
                    while (!lifetime.IsCancellationRequested)
                    {
                        if (DateTimeOffset.UtcNow >= client.ExpiresAt)
                        {
                            status.Text = "This code has expired. Close this window and choose Add again.";
                            break;
                        }
                        string state = await client.GetStatusAsync(lifetime.Token);
                        if (state == "Cancelled" || state == "Expired")
                        {
                            status.Text = "The request was cancelled or expired. Close this window and choose Add again.";
                            break;
                        }
                        if (state == "Validated")
                        {
                            status.Text = "Approved. Verifying your account...";
                            QuickLoginAccount account = await client.CompleteAsync(lifetime.Token);
                            lifetime.Token.ThrowIfCancellationRequested();
                            string accountJson = new JObject { ["UserId"] = account.UserId, ["Name"] = account.Username }.ToString();
                            if (AccountManager.AddAccount(account.SecurityToken, AccountJSON: accountJson) == null)
                                throw new InvalidOperationException("The verified account could not be saved.");
                            client.AcceptSession();
                            DialogResult = DialogResult.OK;
                            Close();
                            break;
                        }
                        int seconds = Math.Max(0, (int)(client.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
                        status.Text = state == "UserLinked"
                            ? "Review and approve the request on your signed-in device."
                            : $"Waiting for approval. Code expires in {seconds / 60}:{seconds % 60:00}.";
                        await Task.Delay(2000, lifetime.Token);
                    }
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    if (!IsDisposed && !lifetime.IsCancellationRequested)
                        status.Text = ex is OperationCanceledException
                            ? "Roblox took too long to respond. Close this window and try again."
                            : ex is System.Net.Http.HttpRequestException
                            ? "Could not reach Roblox. Check your connection or try the browser option."
                            : ex.Message;
                    // Never log response bodies, login codes, private keys, or session cookies.
                }
                finally
                {
                    if (!IsDisposed) copy.Enabled = approve.Enabled = false;
                    await client.CleanupAsync();
                }
            }
        }
    }
}
