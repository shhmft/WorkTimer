using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace WorkTimer
{
    static class WorkTimerTests
    {
        static int checks;
        static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
            checks++;
        }
        static void Near(double expected, double actual, string message)
        { Check(Math.Abs(expected - actual) < 0.03, message + ": " + actual); }
        static object Call(TrayApp app, string method, params object[] args)
        {
            foreach (MethodInfo m in typeof(TrayApp).GetMethods(Hidden))
                if (m.Name == method && m.GetParameters().Length == args.Length)
                    return m.Invoke(app, args);
            throw new Exception("Missing method: " + method);
        }
        static T Field<T>(TrayApp app, string name)
        { return (T)typeof(TrayApp).GetField(name, Hidden).GetValue(app); }
        static Dictionary<DateTime, double> Map(TrayApp app, WorkProject project, DateTime now)
        { return (Dictionary<DateTime, double>)Call(app, "BuildDayMap", project, now); }
        static string Export(TrayApp app, WorkProject project, DateTime from, DateTime to)
        { return (string)Call(app, "BuildExport", project, from, to, 0.0, 0); }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--reload") return CheckRestart(args[1], Array.IndexOf(args, "--updated") >= 0);
            string testDir = Path.Combine(Path.GetTempPath(), "WorkTimerTests-" + Guid.NewGuid().ToString("N"));
            Store.DataDirectory = testDir;
            TrayApp app = null;
            try
            {
                Application.EnableVisualStyles();
                DateTime today = DateTime.Now.Date;
                DateTime morning = today.AddHours(8);
                File.WriteAllText(Store.SessionsFile,
                    morning.ToString("s") + "|" + morning.AddHours(1).ToString("s") + "\r\n", Encoding.UTF8);
                File.WriteAllText(Store.AdjustFile, today.ToString("yyyy-MM-dd") + "|15\r\n", Encoding.UTF8);
                File.WriteAllText(Store.SettingsFile, "widget=0\r\nhotkey=0\r\nupdates=0\r\n", Encoding.UTF8);
                app = new TrayApp();
                List<WorkProject> projects = Field<List<WorkProject>>(app, "projects");
                WorkProject first = projects[0];
                Check(first.Id == "default", "Legacy sessions belong to default work");
                Near(75, Map(app, first, today.AddHours(18))[today], "Legacy hours and adjustment preserved");

                WorkProject second = new WorkProject("second", "Проект; \"Дом\" | фасад");
                projects.Add(second);
                Call(app, "ToggleProject", first);
                first.RunStart = DateTime.Now.AddMinutes(-10);
                Call(app, "SelectProject", second);
                Check(first.RunStart.HasValue, "Selecting another work leaves first running");
                Call(app, "ToggleRun");
                second.RunStart = DateTime.Now.AddMinutes(-20);
                Check(first.RunStart.HasValue && second.RunStart.HasValue, "Two timers run independently");
                Check(app.WidgetCaptionPub == first.Name + ", " + second.Name,
                    "Widget caption lists all running works");
                Near(85, Map(app, first, DateTime.Now)[today], "First work counts only its own time");
                Near(20, Map(app, second, DateTime.Now)[today], "Second work excludes legacy hours");
                Call(app, "SaveNow");
                Call(app, "SaveNow");
                List<Segment> snapshot = Store.LoadSegments();
                Check(snapshot.Count == 3, "Repeated autosave does not duplicate running intervals");
                Check(snapshot.Exists(delegate(Segment s) { return s.ProjectId == second.Id; }), "Unselected live work is saved");

                Call(app, "ToggleProject", first);
                Check(!first.RunStart.HasValue && second.RunStart.HasValue, "Pause one work keeps the other running");
                Call(app, "SelectProject", first);
                Call(app, "StopSession");
                Check(app.WidgetCaptionPub == second.Name,
                    "Widget caption lists the remaining running work");
                Check(first.SessionMinutes == 0 && second.RunStart.HasValue, "Stop resets only selected session");
                Near(85, Map(app, first, DateTime.Now)[today], "Stop preserves daily hours");
                // While an idle work is selected, the background work must still autosave.
                typeof(TrayApp).GetField("lastSave", Hidden).SetValue(app, DateTime.Now.AddSeconds(-20));
                Call(app, "OnTick", null, EventArgs.Empty);
                Check((DateTime.Now - Field<DateTime>(app, "lastSave")).TotalSeconds < 1, "Background running work triggers autosave");
                Check(Field<System.Windows.Forms.Timer>(app, "timer").Interval == 1000, "Background running work keeps timer ticking");

                second.Adjustments[today] = 30;
                second.Adjustments[today.AddDays(-1)] = -5;
                Call(app, "SelectProject", second);
                Call(app, "StopSession");
                Near(50, Map(app, second, DateTime.Now)[today], "Per-work adjustment survives stop");
                Near(85, Map(app, first, DateTime.Now)[today], "Other work's adjustment stays independent");

                List<Segment> segments = Field<List<Segment>>(app, "segments");
                DateTime midnight = today.AddDays(-1);
                segments.Add(new Segment(midnight.AddMinutes(-30), midnight.AddMinutes(90), second.Id));
                Dictionary<DateTime, double> split = Map(app, second, DateTime.Now);
                Near(30, split[today.AddDays(-2)], "Midnight interval split before midnight");
                Near(85, split[today.AddDays(-1)], "Midnight interval split after midnight with own adjustment");
                Check(!Map(app, first, DateTime.Now).ContainsKey(today.AddDays(-1)), "Midnight interval stays within its work");

                string csv = Export(app, second, today.AddDays(-2), today);
                Check(csv.Contains("\"Проект; \"\"Дом\"\" | фасад\""), "Export quotes special characters in name");
                Check(csv.Contains("ИТОГО;;;2:45;2,75"), "Export total includes only selected work");
                Check(!Export(app, second, today, today).Contains(today.AddDays(-1).ToString("dd.MM.yyyy")), "Export respects date range");
                Check(Export(app, first, today, today).Contains("ИТОГО;;;1:25;1,42"), "First work exports independently");

                Call(app, "SaveNow");
                List<WorkProject> loaded = Store.LoadProjects();
                Store.LoadProjectAdjustments(loaded);
                WorkProject loadedSecond = loaded.Find(delegate(WorkProject p) { return p.Id == "second"; });
                Check(loadedSecond.Name == second.Name, "Name round trip supports Russian and delimiters");
                Near(30, loadedSecond.Adjustments[today], "Per-work adjustment round trip");
                Near(15, loaded[0].Adjustments[today], "Legacy adjustment round trip");
                Check(Store.LoadSettings()["project"] == second.Id, "Selected work is persisted");

                second.Name = "Дом на берегу";
                Call(app, "SaveNow");
                Near(50, Map(app, second, DateTime.Now)[today], "Renaming preserves history by stable ID");
                Check(Store.LoadProjects()[1].Name == second.Name, "Renamed work is saved");

                List<Segment> reloadedSegments = Store.LoadSegments();
                Check(reloadedSegments.Count == segments.Count, "Restart contains each stopped interval once");
                Check(reloadedSegments.FindAll(delegate(Segment s) { return s.ProjectId == second.Id; }).Count == 2,
                    "Restart preserves separate work history");
                using (System.Diagnostics.Process child = new System.Diagnostics.Process())
                {
                    child.StartInfo = new System.Diagnostics.ProcessStartInfo(
                        Assembly.GetExecutingAssembly().Location, "--reload \"" + testDir + "\"");
                    child.StartInfo.UseShellExecute = false;
                    child.StartInfo.CreateNoWindow = true;
                    child.StartInfo.RedirectStandardOutput = true;
                    child.StartInfo.RedirectStandardError = true;
                    child.Start();
                    string output = child.StandardOutput.ReadToEnd();
                    string error = child.StandardError.ReadToEnd();
                    child.WaitForExit();
                    Check(child.ExitCode == 0, "Fresh process reloads work selection, hours and adjustments: " + error);
                    Console.Write(output);
                }
                string updateDir = Path.Combine(testDir, "resume-fixture");
                Directory.CreateDirectory(updateDir);
                foreach (string name in new string[] { "sessions.csv", "adjustments.csv", "projects.csv", "settings.ini" })
                    File.Copy(Path.Combine(testDir, name), Path.Combine(updateDir, name));
                File.AppendAllText(Path.Combine(updateDir, "settings.ini"), "updateResume=default,second\r\n", Encoding.UTF8);
                using (System.Diagnostics.Process child = new System.Diagnostics.Process())
                {
                    child.StartInfo = new System.Diagnostics.ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                        "--reload \"" + updateDir + "\" --updated");
                    child.StartInfo.UseShellExecute = false; child.StartInfo.CreateNoWindow = true;
                    child.StartInfo.RedirectStandardOutput = true; child.StartInfo.RedirectStandardError = true;
                    child.Start(); string output = child.StandardOutput.ReadToEnd(); string error = child.StandardError.ReadToEnd();
                    child.WaitForExit(); Check(child.ExitCode == 0, "Update restart resumes independent timers: " + error);
                    Console.Write(output);
                }

                Call(app, "ToggleProject", first);
                Call(app, "ToggleProject", second);
                Call(app, "FlushForShutdown");
                Check(!first.RunStart.HasValue && !second.RunStart.HasValue, "Shutdown/sleep closes every running work");
                Check(app.WidgetCaptionPub == second.Name,
                    "Widget caption shows selected work when all timers are stopped");
                Check(Store.LoadSegments().Count == segments.Count, "Shutdown saves all closed intervals");

                // Render the actual form with representative parallel timers for visual inspection.
                first.Name = "Общая работа";
                first.RunStart = DateTime.Now.AddMinutes(-15);
                second.RunStart = DateTime.Now.AddMinutes(-5);
                WorkProject third = new WorkProject("third", "Дизайн интерьера");
                third.SessionMinutes = 25;
                projects.Add(third);
                IntPtr handle = app.Handle;
                Call(app, "OpenMainPub");
                Application.DoEvents();
                Call(app, "UpdateWindow");
                DateTime renderAt = DateTime.Now.AddMilliseconds(650);
                while (DateTime.Now < renderAt)
                { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                if (args.Length > 0)
                    using (Bitmap bitmap = new Bitmap(app.Width, app.Height))
                    {
                        app.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(args[0], System.Drawing.Imaging.ImageFormat.Png);
                    }
                using (SettingsDialog dialog = new SettingsDialog(app))
                {
                    dialog.Show(app);
                    Application.DoEvents();
                    Check(dialog.ClientSize.Height == 404, "Update setting fits settings window");
                    if (args.Length > 0)
                        using (Bitmap bitmap = new Bitmap(dialog.Width, dialog.Height))
                        {
                            dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save(Path.Combine(Path.GetDirectoryName(args[0]), "preview-update-settings.png"),
                                System.Drawing.Imaging.ImageFormat.Png);
                        }
                    dialog.Close();
                }
                Check(Field<ProjectList>(app, "projectList").Rows.Count == 3, "Visible work list reflects all works");
                Console.WriteLine("PASS: " + checks + " checks; isolated data: " + testDir);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
            finally
            {
                if (app != null) { Call(app, "ExitApp"); app.Dispose(); }
                // Delete only this run's uniquely named directory under the system temp directory.
                string absolute = Path.GetFullPath(testDir);
                string root = Path.GetFullPath(Path.GetTempPath());
                if (absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(absolute).StartsWith("WorkTimerTests-", StringComparison.Ordinal))
                    Directory.Delete(absolute, true);
            }
        }

        static int CheckRestart(string dir, bool updated)
        {
            Store.DataDirectory = dir;
            TrayApp app = null;
            try
            {
                Application.EnableVisualStyles();
                app = new TrayApp();
                List<WorkProject> projects = Field<List<WorkProject>>(app, "projects");
                DateTime today = DateTime.Now.Date;
                Check(projects.Count == 2, "Restart restores work list");
                Check(Field<WorkProject>(app, "currentProject").Id == "second", "Restart restores selection");
                Check(app.ProjectNamePub == "Дом на берегу", "Restart restores renamed work");
                Near(85, Map(app, projects[0], DateTime.Now)[today], "Restart restores first work hours");
                Near(50, Map(app, projects[1], DateTime.Now)[today], "Restart restores second work hours and adjustment");
                Near(85, Map(app, projects[1], DateTime.Now)[today.AddDays(-1)], "Restart restores midnight split");
                Check(projects[0].RunStart.HasValue == updated && projects[1].RunStart.HasValue == updated,
                    "Only update restart resumes running works");
                if (updated) Check(!Store.LoadSettings().ContainsKey("updateResume"), "Resume marker consumed once");
                Console.WriteLine("PASS: " + checks + " fresh-process restart checks");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
            finally { if (app != null) { Call(app, "ExitApp"); app.Dispose(); } }
        }
    }
}
