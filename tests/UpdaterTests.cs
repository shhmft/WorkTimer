using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace WorkTimer
{
    static class UpdaterTests
    {
        static int checks;
        static string NextTag { get { return "v" + new Version(Updates.Current.Major, Updates.Current.Minor + 1, 0); } }
        static void Check(bool value, string name)
        { if (!value) throw new Exception(name); checks++; }
        static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, name);
        }
        static Dictionary<string, object> Release(string tag)
        {
            return new Dictionary<string, object> {
                {"tag_name", tag}, {"draft", false}, {"prerelease", false},
                {"html_url", Updates.ReleasePrefix + "tag/" + tag},
                {"assets", new object[] { new Dictionary<string, object> {
                    {"name", "WorkTimer.exe"}, {"size", 123},
                    {"browser_download_url", Updates.ReleasePrefix + "download/" + tag + "/WorkTimer.exe"},
                    {"digest", "sha256:" + new string('a', 64)} } }}
            };
        }
        static UpdateRelease Parse(Dictionary<string, object> release)
        { return Updates.Parse(new JavaScriptSerializer().Serialize(release)); }
        static Dictionary<string, object> Asset(Dictionary<string, object> release)
        { return (Dictionary<string, object>)((object[])release["assets"])[0]; }
        static void WaitForFile(string path)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!File.Exists(path) && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(50);
            Check(File.Exists(path), "Updated process started");
        }

        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--echo") { Console.Write(args[1]); return 0; }
            string folder = Path.Combine(Path.GetTempPath(), "WorkTimerUpdaterTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); Store.DataDirectory = folder;
            try
            {
                Check(Parse(Release(NextTag)).Version == new Version(Updates.Current.Major, Updates.Current.Minor + 1, 0), "New stable release selected");
                Check(Parse(Release("v" + Updates.Current + ".0")) == null, "Same four-part version is not an update");
                Check(Parse(Release("v1.2.0")) == null, "No downgrade");
                Check(Parse(Release("v1.4.0-beta")) == null, "Prerelease tag ignored");
                Dictionary<string, object> r = Release(NextTag); r["draft"] = true;
                Check(Parse(r) == null, "Draft ignored");
                r = Release(NextTag); r["prerelease"] = true;
                Check(Parse(r) == null, "Prerelease flag ignored");
                r = Release(NextTag); r["html_url"] = "https://example.com/release";
                Reject(delegate { Parse(r); }, "External release page rejected");
                r = Release(NextTag); Asset(r)["browser_download_url"] = "https://example.com/WorkTimer.exe";
                Reject(delegate { Parse(r); }, "External asset rejected");
                r = Release(NextTag); Asset(r)["digest"] = null;
                Reject(delegate { Parse(r); }, "Missing checksum rejected");
                r = Release(NextTag); Asset(r)["digest"] = "sha256:" + new string('z', 64);
                Reject(delegate { Parse(r); }, "Invalid checksum rejected");
                r = Release(NextTag); Asset(r)["size"] = 101 * 1024 * 1024;
                Reject(delegate { Parse(r); }, "Oversized asset rejected");
                r = Release(NextTag); Asset(r)["name"] = "other.exe";
                Reject(delegate { Parse(r); }, "Missing Windows asset rejected");

                string stage = Path.Combine(folder, "stage.exe"), target = Path.Combine(folder, "target.exe");
                File.WriteAllText(stage, "new bytes"); File.WriteAllText(target, "old bytes");
                string digest = Updates.Hash(stage);
                Updates.Verify(stage, new FileInfo(stage).Length, digest); checks++;
                Reject(delegate { Updates.Verify(stage, 999, digest); }, "Size mismatch rejected");
                Reject(delegate { Updates.Verify(stage, new FileInfo(stage).Length, new string('a', 64)); }, "Corrupt download rejected");
                Updates.Replace(stage, target, digest);
                Check(File.ReadAllText(target) == "new bytes" && File.ReadAllText(target + ".previous") == "old bytes", "Atomic replacement keeps backup");
                File.WriteAllText(stage, "tampered");
                Reject(delegate { Updates.Replace(stage, target, digest); }, "Tampered staged file rejected");
                Check(File.ReadAllText(target) == "new bytes", "Rejected update keeps current executable");

                string special = "C:\\path with spaces\\a\"b\\";
                ProcessStartInfo echo = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                    "--echo " + Updates.QuoteArg(special));
                echo.UseShellExecute = false; echo.CreateNoWindow = true; echo.RedirectStandardOutput = true;
                using (Process process = Process.Start(echo))
                { string actual = process.StandardOutput.ReadToEnd(); process.WaitForExit(); Check(actual == special, "Windows argument quoting round trip"); }

                File.Copy(args[0], stage, true); File.Copy(args[0], target, true);
                string oldHash = Updates.Hash(target);
                Updates.Apply(new string[] { "--apply-update", int.MaxValue.ToString(), target, stage, Updates.Hash(stage) });
                WaitForFile(target + ".started");
                Check(File.ReadAllText(target + ".started") == "--updated", "Helper relaunches with resume flag");
                File.Delete(target + ".started");
                File.WriteAllText(stage, "invalid executable");
                Updates.Apply(new string[] { "--apply-update", int.MaxValue.ToString(), target, stage, Updates.Hash(stage) });
                WaitForFile(target + ".started");
                Check(Updates.Hash(target) == oldHash, "Failed launch restores previous executable");
                Check(File.ReadAllText(target + ".started").Contains("--update-failed"), "Failed update relaunches old app with explanation");
                if (args.Length > 1 && args[1] == "--live")
                {
                    Dictionary<string, object> live = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Updates.LatestJson());
                    UpdateRelease available = Updates.Parse(new JavaScriptSerializer().Serialize(live));
                    Check(available == null || available.Version > Updates.Current, "Public GitHub check needs no credentials");
                    foreach (object entry in (System.Collections.IEnumerable)live["assets"])
                    {
                        Dictionary<string, object> asset = (Dictionary<string, object>)entry;
                        if ((string)asset["name"] != "WorkTimer.exe") continue;
                        UpdateRelease download = new UpdateRelease { Version = new Version(1, 1, 1),
                            Url = (string)asset["browser_download_url"], Size = Convert.ToInt64(asset["size"]),
                            Digest = ((string)asset["digest"]).Substring(7) };
                        string file = Updates.Download(download);
                        Check(Updates.Hash(file) == download.Digest, "Actual GitHub asset download verifies checksum without installation");
                        break;
                    }
                }
                Console.WriteLine("PASS: " + checks + " Windows updater checks");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                // Our own fixtures only; processes may briefly keep their executable open.
                System.Threading.Thread.Sleep(100);
                string absolute = Path.GetFullPath(folder);
                if (absolute.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(absolute).StartsWith("WorkTimerUpdaterTests-", StringComparison.Ordinal))
                    Directory.Delete(absolute, true);
            }
        }
    }
}
