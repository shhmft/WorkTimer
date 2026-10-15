using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WorkTimer
{
    class UpdateRelease
    {
        public Version Version;
        public string Url, Digest, Page;
        public long Size;
    }

    static class Updates
    {
        public const string Endpoint = "https://api.github.com/repos/shhmft/WorkTimer/releases/latest";
        public const string ReleasePrefix = "https://github.com/shhmft/WorkTimer/releases/";
        public static Version Current
        {
            get
            {
                Version version = typeof(Updates).Assembly.GetName().Version;
                return new Version(version.Major, version.Minor, version.Build);
            }
        }

        public static UpdateRelease Parse(string json)
        {
            JavaScriptSerializer parser = new JavaScriptSerializer();
            parser.MaxJsonLength = 1024 * 1024;
            Dictionary<string, object> data = parser.Deserialize<Dictionary<string, object>>(json);
            if ((bool)data["draft"] || (bool)data["prerelease"]) return null;
            string tag = (string)data["tag_name"];
            Version version;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out version) || version.Build < 0 ||
                version.Revision > 0) return null;
            version = new Version(version.Major, version.Minor, version.Build);
            if (version <= Current) return null;
            string page = (string)data["html_url"];
            if (!page.StartsWith(ReleasePrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Неверный адрес релиза.");
            System.Collections.IEnumerable assets = data["assets"] as System.Collections.IEnumerable;
            if (assets == null) throw new InvalidDataException("Нет списка файлов релиза.");
            foreach (object entry in assets)
            {
                Dictionary<string, object> asset = (Dictionary<string, object>)entry;
                if ((string)asset["name"] != "WorkTimer.exe") continue;
                string url = (string)asset["browser_download_url"];
                long size = Convert.ToInt64(asset["size"], CultureInfo.InvariantCulture);
                object value; string digest = asset.TryGetValue("digest", out value) ? value as string : null;
                if (!url.StartsWith(ReleasePrefix + "download/", StringComparison.OrdinalIgnoreCase) ||
                    size <= 0 || size > 100 * 1024 * 1024 || !ValidDigest(digest))
                    throw new InvalidDataException("У файла обновления нет корректной контрольной суммы SHA-256.");
                return new UpdateRelease { Version = version, Page = page, Url = url, Size = size, Digest = digest.Substring(7) };
            }
            throw new InvalidDataException("Windows-сборка нового релиза ещё не опубликована.");
        }

        static bool ValidDigest(string digest)
        {
            if (digest == null || !digest.StartsWith("sha256:") || digest.Length != 71) return false;
            for (int i = 7; i < digest.Length; i++) if (!Uri.IsHexDigit(digest[i])) return false;
            return true;
        }

        static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 on .NET 4
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "WorkTimer/" + Current;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 15000; request.ReadWriteTimeout = 15000;
            return request;
        }

        public static UpdateRelease Check()
        { return Parse(LatestJson()); }

        public static string LatestJson()
        {
            using (WebResponse response = Request(Endpoint).GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                char[] buffer = new char[4096]; int count;
                StringBuilder json = new StringBuilder();
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    json.Append(buffer, 0, count);
                    if (json.Length > 1024 * 1024) throw new InvalidDataException("Ответ сервера слишком большой.");
                }
                return json.ToString();
            }
        }

        public static string Hash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream file = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        public static void Verify(string path, long size, string digest)
        {
            if (new FileInfo(path).Length != size || !string.Equals(Hash(path), digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Файл обновления повреждён: контрольная сумма не совпала.");
        }

        public static string Download(UpdateRelease release)
        {
            string folder = Path.Combine(Store.Dir, "updates");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "WorkTimer-" + release.Version + "-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                using (WebResponse response = Request(release.Url).GetResponse())
                using (Stream input = response.GetResponseStream())
                using (FileStream file = new FileStream(path, FileMode.CreateNew))
                {
                    byte[] buffer = new byte[65536]; int count; long written = 0;
                    DateTime deadline = DateTime.UtcNow.AddMinutes(5);
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        written += count;
                        if (written > release.Size || DateTime.UtcNow > deadline)
                            throw new InvalidDataException("Загрузка обновления прервана.");
                        file.Write(buffer, 0, count);
                    }
                }
                Verify(path, release.Size, release.Digest);
                return path;
            }
            catch { if (File.Exists(path)) File.Delete(path); throw; }
        }

        public static string QuoteArg(string argument)
        {
            // Windows command line quoting; paths may contain spaces and trailing backslashes.
            StringBuilder result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c); slashes = 0;
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }

        public static void Replace(string stage, string target, string digest)
        {
            if (!string.Equals(Hash(stage), digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Контрольная сумма обновления не совпала.");
            string temp = target + ".update-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(stage, temp, false);
                if (!string.Equals(Hash(temp), digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Файл обновления повреждён при копировании.");
                File.Replace(temp, target, target + ".previous");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static void Apply(string[] args)
        {
            string target = args[2];
            bool replaced = false, stopped = false;
            try
            {
                int pid = int.Parse(args[1], CultureInfo.InvariantCulture);
                try
                {
                    using (Process parent = Process.GetProcessById(pid))
                        if (!parent.WaitForExit(30000)) throw new IOException("Таймер ещё не завершился.");
                }
                catch (ArgumentException) { } // The parent has already exited.
                stopped = true;
                Replace(args[3], target, args[4]);
                replaced = true;
                ProcessStartInfo launch = new ProcessStartInfo(target, "--updated");
                launch.UseShellExecute = false;
                Process.Start(launch);
            }
            catch (Exception ex)
            {
                Store.Log("установка обновления", ex);
                if (replaced)
                    try { File.Replace(target + ".previous", target, target + ".failed"); } catch { }
                if (stopped)
                    try
                    {
                        ProcessStartInfo launch = new ProcessStartInfo(target, "--updated --update-failed");
                        launch.UseShellExecute = false; Process.Start(launch);
                    }
                    catch { }
            }
        }
    }
}
