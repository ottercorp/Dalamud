using AutoUpdaterDotNET;
using Dalamud.Updater.Properties;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Configuration;
using Newtonsoft.Json.Linq;
using System.Security.Principal;
using System.Xml;

namespace Dalamud.Updater
{
    public partial class FormMain : Form
    {
        private string updateUrl = "https://dalamud-1253720819.cos.ap-nanjing.myqcloud.com/update.xml";

        // private List<string> pidList = new List<string>();
        private bool firstHideHint = true;
        private bool isThreadRunning = true;
        private bool dotnetDownloadFinished = false;
        private bool desktopDownloadFinished = false;
        private string dotnetDownloadPath;
        private string desktopDownloadPath;
        private DirectoryInfo runtimePath;
        private DirectoryInfo[] runtimePaths;
        private string RuntimeVersion = "5.0.6";
        private double injectDelaySeconds = 0;

        public static string GetAppSettings(string key, string def = null)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                var ele = settings[key];
                if (ele == null) return def;
                return ele.Value;
            }
            catch (ConfigurationErrorsException)
            {
                Console.WriteLine("Error reading app settings");
            }
            return def;
        }
        public static void AddOrUpdateAppSettings(string key, string value)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (settings[key] == null)
                {
                    settings.Add(key, value);
                }
                else
                {
                    settings[key].Value = value;
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            catch (ConfigurationErrorsException)
            {
                Console.WriteLine("Error writing app settings");
            }
        }
        private Version getVersion()
        {
            var rgx = new Regex(@"^\d+\.\d+\.\d+\.\d+$");
            var di = new DirectoryInfo(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName);
            var dirs = di.GetDirectories("*", SearchOption.TopDirectoryOnly).Where(dir => rgx.IsMatch(dir.Name)).ToArray();

            var version = new Version("0.0.0.0");
            foreach (var dir in dirs)
            {
                var newVersion = new Version(dir.Name);
                if (newVersion > version)
                {
                    version = newVersion;
                }
            }
            return version;
        }


        public FormMain()
        {
            InitializeComponent();
            InitializePIDCheck();
            InitializeDeleteShit();
            InitializeConfig();
            labelVersion.Text = string.Format("卫月版本 : {0}", getVersion());
            delayBox.Value = (decimal)this.injectDelaySeconds;
            string[] strArgs = Environment.GetCommandLineArgs();
            if (strArgs.Length >= 2 && strArgs[1].Equals("-startup"))
            {
                //this.WindowState = FormWindowState.Minimized;
                //this.ShowInTaskbar = false;
                if (firstHideHint)
                {
                    firstHideHint = false;
                    this.DalamudUpdaterIcon.ShowBalloonTip(2000, "自启动成功", "放心，我会在后台偷偷干活的。", ToolTipIcon.Info);
                }
            }
            try
            {
                var localVersion = getVersion();
                var remoteUrl = getUpdateUrl();
                XmlDocument remoteXml = new XmlDocument();
                remoteXml.Load(remoteUrl);
                foreach (XmlNode child in remoteXml.SelectNodes("/item/version"))
                {
                    if (child.InnerText != localVersion.ToString())
                    {
                        AutoUpdater.Start(remoteUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "程序启动版本检查失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        #region Initialize
        private void InitializeConfig()
        {
            if (GetAppSettings("AutoInject", "false") == "true")
            {
                this.checkBoxAutoInject.Checked = true;
            }
            if (GetAppSettings("AutoStart", "false") == "true")
            {
                this.checkBoxAutoStart.Checked = true;
            }
            if (GetAppSettings("Accelerate", "false") == "true")
            {
                this.checkBoxAcce.Checked = true;
            }
            var tempInjectDelaySeconds = GetAppSettings("InjectDelaySeconds", "0");
            if (tempInjectDelaySeconds != "0")
            {
                this.injectDelaySeconds = double.Parse(tempInjectDelaySeconds);
            }
        }

        private void InitializeDeleteShit()
        {
            var shitInjector = Path.Combine(Directory.GetCurrentDirectory(), "Dalamud.Injector.exe");
            if (File.Exists(shitInjector))
            {
                File.Delete(shitInjector);
            }
        }

        private void InitializePIDCheck()
        {
            var thread = new Thread(() => {
                while (this.isThreadRunning)
                {
                    try
                    {
                        var newPidList = Process.GetProcessesByName("ffxiv_dx11").Where(process =>
                        {
                            return !process.MainWindowTitle.Contains("FINAL FANTASY XIV");
                        }).ToList().ConvertAll(process => process.Id.ToString()).ToArray();
                        var newHash = String.Join(", ", newPidList).GetHashCode();
                        var oldPidList = this.comboBoxFFXIV.Items.Cast<Object>().Select(item => item.ToString()).ToArray();
                        var oldHash = String.Join(", ", oldPidList).GetHashCode();
                        if (oldHash != newHash && this.comboBoxFFXIV.IsHandleCreated)
                        {
                            this.comboBoxFFXIV.Invoke((MethodInvoker)delegate {
                                // Running on the UI thread
                                comboBoxFFXIV.Items.Clear();
                                comboBoxFFXIV.Items.AddRange(newPidList);
                                if (newPidList.Length > 0)
                                {
                                    if (!comboBoxFFXIV.DroppedDown)
                                        this.comboBoxFFXIV.SelectedIndex = 0;
                                    if (this.checkBoxAutoInject.Checked)
                                    {
                                        foreach (var pidStr in newPidList)
                                        {
                                            Thread.Sleep((int)(this.injectDelaySeconds * 1000));
                                            var pid = int.Parse(pidStr);
                                            if (this.Inject(pid))
                                            {
                                                this.DalamudUpdaterIcon.ShowBalloonTip(2000, "帮你注入了", $"帮你注入了进程{pid}，不用谢。", ToolTipIcon.Info);
                                            }
                                        }
                                    }
                                }
                            });
                        }
                    } catch
                    {

                    }
                    Thread.Sleep(1000);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        #endregion
        private void FormMain_Load(object sender, EventArgs e)
        {
            AutoUpdater.ApplicationExitEvent += AutoUpdater_ApplicationExitEvent;
            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.InstalledVersion = getVersion();
            labelVer.Text = $"v{Assembly.GetExecutingAssembly().GetName().Version}";
            UpdateRuntimePaths();
            new Thread(() => {
                Thread.Sleep(5000);
                if (runtimePaths.Any(p => !p.Exists))
                {
                    var choice = MessageBox.Show("运行卫月需要下载所需运行库，是否下载？", "下载运行库",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Information);
                    if (choice == DialogResult.Yes)
                        TryDownloadRuntime(runtimePath, RuntimeVersion);
                    else
                        return;
                }
            }).Start();
            
        }
        private void FormMain_Disposed(object sender, EventArgs e)
        {
            this.isThreadRunning = false;
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
            //this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            //this.ShowInTaskbar = false;
            //this.Visible = false;
            if (firstHideHint)
            {
                firstHideHint = false;
                this.DalamudUpdaterIcon.ShowBalloonTip(2000, "小玩意挺会藏", "哎我藏起来了，单击托盘图标呼出程序界面。", ToolTipIcon.Info);
            }
        }

        private void DalamudUpdaterIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                //if (this.WindowState == FormWindowState.Minimized)
                //{
                //    this.WindowState = FormWindowState.Normal;
                //    this.FormBorderStyle = FormBorderStyle.FixedDialog;
                //    this.ShowInTaskbar = true;
                //}
                if (this.WindowState == FormWindowState.Minimized)
                {
                    this.Show();
                    this.WindowState = FormWindowState.Normal;
                }
                this.Activate();
            }
        }

        private void 显示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //WindowState = FormWindowState.Normal;
            if (!this.Visible) this.Visible = true;
            this.Activate();
        }
        private void 退出ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Dispose();
            //this.Close();
            this.DalamudUpdaterIcon.Dispose();
            Application.Exit();
        }

        private void AutoUpdater_ApplicationExitEvent()
        {
            Text = @"Closing application...";
            Thread.Sleep(5000);
            Application.Exit();
        }


        private void AutoUpdaterOnParseUpdateInfoEvent(ParseUpdateInfoEventArgs args)
        {
            dynamic json = JsonConvert.DeserializeObject(args.RemoteData);
            args.UpdateInfo = new UpdateInfoEventArgs
            {
                CurrentVersion = json.version,
                ChangelogURL = json.changelog,
                DownloadURL = json.url,
                Mandatory = new Mandatory
                {
                    Value = json.mandatory.value,
                    UpdateMode = json.mandatory.mode,
                    MinimumVersion = json.mandatory.minVersion
                },
                CheckSum = new CheckSum
                {
                    Value = json.checksum.value,
                    HashingAlgorithm = json.checksum.hashingAlgorithm
                }
            };
        }

        private void OnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (args.Error == null)
            {
                if (args.IsUpdateAvailable)
                {
                    DialogResult dialogResult;
                    if (args.Mandatory.Value)
                    {
                        dialogResult =
                            MessageBox.Show(
                                $@"卫月框架 {args.CurrentVersion} 版本可用。当前版本为 {
                                        args.InstalledVersion
                                    }。这是一个强制更新，请点击确认来更新卫月框架。",
                                @"更新可用",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                    }
                    else
                    {
                        dialogResult =
                            MessageBox.Show(
                                $@"卫月框架 {args.CurrentVersion} 版本可用。当前版本为 {
                                        args.InstalledVersion
                                    }。您想要开始更新吗？", @"更新可用",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information);
                    }


                    if (dialogResult.Equals(DialogResult.Yes) || dialogResult.Equals(DialogResult.OK))
                    {
                        try
                        {
                            //You can use Download Update dialog used by AutoUpdater.NET to download the update.

                            if (AutoUpdater.DownloadUpdate(args))
                            {
                                this.Dispose();
                                this.DalamudUpdaterIcon.Dispose();
                                Application.Exit();
                            }
                        }
                        catch (Exception exception)
                        {
                            MessageBox.Show(exception.Message, exception.GetType().ToString(), MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }
                }
                else
                {
                    MessageBox.Show(@"没有可用更新，请稍后查看。", @"更新不可用",
                                   MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                if (args.Error is WebException)
                {
                    MessageBox.Show(
                        @"访问更新服务器出错，请检查您的互联网连接后重试。",
                        @"更新检查失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show(args.Error.Message,
                        args.Error.GetType().ToString(), MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            OnCheckForUpdateEvent(args);
        }
        public class ProgressEventArgsEx
        {
            public int Percentage { get; set; }
            public string Text { get; set; }
        }
        private async void TryDownloadRuntime(DirectoryInfo runtimePath, string RuntimeVersion)
        {
            new Thread(() =>
            {
                try
                {
                    DownloadRuntime(runtimePath, RuntimeVersion);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("运行库下载失败 :(", "下载运行库",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    return;
                }
            }).Start();
        }
        void client1_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            double bytesIn = double.Parse(e.BytesReceived.ToString());
            double totalBytes = double.Parse(e.TotalBytesToReceive.ToString());
            double percentage = bytesIn / totalBytes * 100;
            progressBar1.Invoke((MethodInvoker)delegate
            {
                progressBar1.Value = int.Parse(Math.Truncate(percentage).ToString());
            });
        }
        void client1_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show($"dotnet运行库下载失败\n{e.Error}", "下载运行库",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                buttonCheckRuntime.Invoke((MethodInvoker)delegate
                {
                    buttonCheckRuntime.Text = "重试下载";
                    buttonCheckRuntime.Enabled = true;
                });
                return;
            }
            ZipFile.ExtractToDirectory(dotnetDownloadPath, runtimePath.FullName);
            File.Delete(dotnetDownloadPath);
            dotnetDownloadFinished = true;
            if (dotnetDownloadFinished && desktopDownloadFinished)
            {
                buttonCheckRuntime.Invoke((MethodInvoker)delegate
                {
                    buttonCheckRuntime.Text = "下载完毕";
                    buttonCheckRuntime.Enabled = true;
                });
                MessageBox.Show("运行库已下载 :)", "下载运行库",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
            }
        }
        void client2_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            double bytesIn = double.Parse(e.BytesReceived.ToString());
            double totalBytes = double.Parse(e.TotalBytesToReceive.ToString());
            double percentage = bytesIn / totalBytes * 100;

            progressBar2.Invoke((MethodInvoker)delegate
            {
                progressBar2.Value = int.Parse(Math.Truncate(percentage).ToString());
            });
        }
        void client2_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show($"desktop运行库下载失败\n{e.Error}", "下载运行库",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                buttonCheckRuntime.Invoke((MethodInvoker)delegate
                {
                    buttonCheckRuntime.Text = "重试下载";
                    buttonCheckRuntime.Enabled = true;
                });
                return;
            }
            ZipFile.ExtractToDirectory(desktopDownloadPath, runtimePath.FullName);
            File.Delete(desktopDownloadPath);
            desktopDownloadFinished = true;
            if (dotnetDownloadFinished && desktopDownloadFinished)
            {
                buttonCheckRuntime.Invoke((MethodInvoker)delegate
                {
                    buttonCheckRuntime.Text = "下载完毕";
                    buttonCheckRuntime.Enabled = true;
                });
                MessageBox.Show("运行库已下载 :)", "下载运行库",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
            }
        }


        private void DownloadRuntime(DirectoryInfo runtimePath, string version)
        {
            if (!runtimePath.Exists)
            {
                runtimePath.Create();
            }
            else
            {
                runtimePath.Delete(true);
                runtimePath.Create();
            }


            WebClient client1 = new WebClient();
            client1.DownloadProgressChanged += new DownloadProgressChangedEventHandler(client1_DownloadProgressChanged);
            client1.DownloadFileCompleted += new AsyncCompletedEventHandler(client1_DownloadFileCompleted);

            WebClient client2 = new WebClient();
            client2.DownloadProgressChanged += new DownloadProgressChangedEventHandler(client2_DownloadProgressChanged);
            client2.DownloadFileCompleted += new AsyncCompletedEventHandler(client2_DownloadFileCompleted);


            var baseDotnetRuntimeUrl = this.checkBoxAcce.Checked ? "https://dotnetcli.azureedge.net" : "https://dotnetcli.blob.core.windows.net";
            var dotnetUrl = $"{baseDotnetRuntimeUrl}/dotnet/Runtime/{version}/dotnet-runtime-{version}-win-x64.zip";
            var desktopUrl = $"{baseDotnetRuntimeUrl}/dotnet/WindowsDesktop/{version}/windowsdesktop-runtime-{version}-win-x64.zip";

            dotnetDownloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            desktopDownloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            if (File.Exists(dotnetDownloadPath))
                File.Delete(dotnetDownloadPath);

            if (File.Exists(desktopDownloadPath))
                File.Delete(desktopDownloadPath);

            buttonCheckRuntime.Invoke((MethodInvoker)delegate
            {
                buttonCheckRuntime.Text = "正在下载";
                buttonCheckRuntime.Enabled = false;
            });

            dotnetDownloadFinished = false;
            new Thread(() =>
            {
                client1.DownloadFileAsync(new Uri(dotnetUrl), dotnetDownloadPath);
            }).Start();
            //ZipFile.ExtractToDirectory(downloadPath, runtimePath.FullName);

            desktopDownloadFinished = false;
            new Thread(() =>
            {
                client2.DownloadFileAsync(new Uri(desktopUrl), desktopDownloadPath);
            }).Start();
            //ZipFile.ExtractToDirectory(downloadPath, runtimePath.FullName);

            //File.Delete(downloadPath);
        }

        private void UpdateRuntimePaths()
        {
            runtimePath = new DirectoryInfo(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "XIVLauncher", "runtime"));
            runtimePaths = new DirectoryInfo[]
            {
                new DirectoryInfo(Path.Combine(runtimePath.FullName, "host", "fxr", RuntimeVersion)),
                new DirectoryInfo(Path.Combine(runtimePath.FullName, "shared", "Microsoft.NETCore.App", RuntimeVersion)),
                new DirectoryInfo(Path.Combine(runtimePath.FullName, "shared", "Microsoft.WindowsDesktop.App", RuntimeVersion)),
            };
        }
        private async void ButtonCheckRuntime_Click(object sender, EventArgs e)
        {
            UpdateRuntimePaths();
            if (runtimePaths.Any(p => !p.Exists))
            {
                var choice = MessageBox.Show("运行卫月需要下载所需运行库，是否下载？", "下载运行库",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information);
                if (choice == DialogResult.Yes)
                    TryDownloadRuntime(runtimePath, RuntimeVersion);
                else
                    return;
            } else
            {
                var choice = MessageBox.Show("运行库已存在，是否强制下载？", "下载运行库",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information);
                if (choice == DialogResult.Yes)
                    TryDownloadRuntime(runtimePath, RuntimeVersion);
            }
        }

        private string getUpdateUrl()
        {
            var _updateUrl = updateUrl;
            /*
            var OverwriteUpdate = GetAppSettings("OverwriteUpdate");
            if (OverwriteUpdate != null)
                _updateUrl = OverwriteUpdate;
            */
            if (this.checkBoxAcce.Checked)
                _updateUrl = updateUrl.Replace("/update", "/acce_update").Replace("ap-nanjing", "accelerate");
            return _updateUrl;
        }

        private void ButtonCheckForUpdate_Click(object sender, EventArgs e)
        {
            if (this.comboBoxFFXIV.SelectedItem != null)
            {
                var pid = int.Parse((string)this.comboBoxFFXIV.SelectedItem);
                var process = Process.GetProcessById(pid);
                if (isInjected(process))
                {
                    var choice = MessageBox.Show("经检测存在 ffxiv_dx11.exe 进程，更新卫月需要关闭游戏，需要帮您代劳吗？", "关闭游戏",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Information);
                    if (choice == DialogResult.Yes)
                    {
                        process.Kill();
                    }
                    else
                    {
                        return;
                    }
                }
            }
            AutoUpdater.Mandatory = true;
            AutoUpdater.InstalledVersion = getVersion();
            AutoUpdater.Start(getUpdateUrl());
        }

        private void comboBoxFFXIV_Clicked(object sender, EventArgs e)
        {

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://qun.qq.com/qqweb/qunpro/share?_wv=3&_wwv=128&inviteCode=CZtWN&from=181074&biz=ka&shareSource=5");
        }

        private string GeneratingDalamudStartInfo(Process process)
        {
            var ffxivDir = Path.GetDirectoryName(process.MainModule.FileName);
            var appDataDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            var xivlauncherDir = Path.Combine(appDataDir, "XIVLauncher");

            var gameVerStr = File.ReadAllText(Path.Combine(ffxivDir, "ffxivgame.ver"));

            var startInfo = JObject.FromObject(new
            {
                ConfigurationPath = Path.Combine(xivlauncherDir, "dalamudConfig.json"),
                PluginDirectory = Path.Combine(xivlauncherDir, "installedPlugins"),
                DefaultPluginDirectory = Path.Combine(xivlauncherDir, "devPlugins"),
                AssetDirectory = Path.Combine(xivlauncherDir, "dalamudAssets"),
                GameVersion = gameVerStr,
                Language = "ChineseSimplified",
                OptOutMbCollection = false,
                GlobalAccelerate = this.checkBoxAcce.Checked,
            });

            return startInfo.ToString();
        }

        private bool isInjected(Process process)
        {
            try
            {
                for (var j = 0; j < process.Modules.Count; j++)
                {
                    if (process.Modules[j].ModuleName == "Dalamud.dll")
                    {
                        return true;
                    }
                }
            } catch {
            
            }
            return false;
        }

        private bool Inject(int pid)
        {
            var process = Process.GetProcessById(pid);
            if(isInjected(process))
            {
                return false;
            }
            var version = getVersion();
            var dalamudPath = new DirectoryInfo(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, $"{version}"));
            var injectorFile = Path.Combine(dalamudPath.FullName, "Dalamud.Injector.exe");
            var dalamudStartInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes(GeneratingDalamudStartInfo(process)));
            var startInfo = new ProcessStartInfo(injectorFile, $"{pid} {dalamudStartInfo}");
            startInfo.WorkingDirectory = dalamudPath.FullName;
            Process.Start(startInfo);
            return true;
        }

        private void ButtonInject_Click(object sender, EventArgs e)
        {
            if(this.comboBoxFFXIV.SelectedItem != null
                && this.comboBoxFFXIV.SelectedItem.ToString().Length > 0)
            {
                var pidStr = this.comboBoxFFXIV.SelectedItem.ToString();
                if(int.TryParse(pidStr, out var pid))
                {
                    Inject(pid);
                }
                else
                {
                    MessageBox.Show("未能解析游戏进程ID", "找不到游戏",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("未选择游戏进程", "找不到游戏",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            }
        }
        private void SetAutoRun()
        {
            string strFilePath = Application.ExecutablePath;
            try
            {
                SystemHelper.SetAutoRun($"\"{strFilePath}\"" + " -startup", "DalamudAutoInjector", checkBoxAutoStart.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void checkBoxAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            SetAutoRun();
            AddOrUpdateAppSettings("AutoStart", checkBoxAutoStart.Checked ? "true" : "false");
        }

        private void checkBoxAutoInject_CheckedChanged(object sender, EventArgs e)
        {
            AddOrUpdateAppSettings("AutoInject", checkBoxAutoInject.Checked ? "true" : "false");
        }

        private void checkBoxAcce_CheckedChanged(object sender, EventArgs e)
        {
            AddOrUpdateAppSettings("Accelerate", checkBoxAcce.Checked ? "true" : "false");
        }

        private void delayBox_ValueChanged(object sender, EventArgs e)
        {
            this.injectDelaySeconds = (double)delayBox.Value;
            AddOrUpdateAppSettings("InjectDelaySeconds", this.injectDelaySeconds.ToString());
        }
    }
}
