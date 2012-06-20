/**
  *    Copyright 2012 Tim Rogers
  *
  *   Licensed under the Apache License, Version 2.0 (the "License");
  *   you may not use this file except in compliance with the License.
  *   You may obtain a copy of the License at
  *
  *       http://www.apache.org/licenses/LICENSE-2.0
  *
  *   Unless required by applicable law or agreed to in writing, software
  *   distributed under the License is distributed on an "AS IS" BASIS,
  *   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
  *   See the License for the specific language governing permissions and
  *   limitations under the License.
  *
  */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using CoApp.Toolkit.Collections;
using CoApp.Toolkit.Pipes;
using Microsoft.Win32;

namespace AutoBuilder
{
    internal enum Errors
    {
        NoError = 0,
        NoCommand = -10,

    }

    public class AutoBuild : ServiceBase
    {
        public static readonly string SerialSeperator = "\n";
        public static readonly string DateTimeDirFormat = "yyyy-MM-dd_HH-mm-ss";

        private static AutoBuild _instance;
        public static AutoBuild_config MasterConfig { get; private set; }
        public static XDictionary<string, ProjectData> Projects { get; private set; }
        private static XDictionary<string, Timer> Waiting;
        private static Queue<string> RunQueue;
        private static List<string> Running;
        private static List<string> Cancellations;
        private static int CurrentJobs;
        public static List<Daemon> Daemons;
        public static Action<string> VerboseOut;
        public static bool WriteConsole;

        public static void WriteVerbose(string data)
        {
            if (VerboseOut != null)
                VerboseOut(data);
        }

        public static AutoBuild Instance
        {
            get
            {
                _instance = _instance ?? new AutoBuild();
                return _instance;
            }
        }

        public AutoBuild()
        {
            Daemons = new List<Daemon>();
            Waiting = new XDictionary<string, Timer>();
            RunQueue = new Queue<string>();
            Running = new List<string>();
            Cancellations = new List<string>();
            Projects = new XDictionary<string, ProjectData>();
            MasterConfig = new AutoBuild_config();
            ServiceName = "AutoBuild";
            EventLog.Log = "Application";
            EventLog.Source = ServiceName;
            // Events to enable
            CanHandlePowerEvent = false;
            CanHandleSessionChangeEvent = false;
            CanPauseAndContinue = false;
            CanShutdown = true;
            CanStop = true;
        }

        static void Main()
        {
            VerboseOut = s => Console.WriteLine(s);
            AutoBuild Manager = Instance;
            AutoBuild.WriteConsole = true;
            Manager.OnStart(new string[0]);
            ConsoleKeyInfo c = new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false);
            while (!(c.Key.Equals(ConsoleKey.Escape)))
            {
                Console.Out.Write('.');
                System.Threading.Thread.Sleep(1000);
                if (Console.KeyAvailable)
                    c = Console.ReadKey(true);
            }
            Manager.OnStop();


            ////uncomment this for release...
            //ServiceBase.Run(new AutoBuild());
        }

        #region Standard Service Control Methods

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        /// <summary>
        /// OnStart(): Put startup code here
        ///  - Start threads, get inital data, etc.
        /// </summary>
        /// <param name="args"></param>
        protected override void OnStart(string[] args)
        {
            base.OnStart(args);
            _instance = this;

            // Always double-check that we have an actual thread to work with...
            InitWorld();
            LoadQueue();
        }

        /// <summary>
        /// OnStop(): Put your stop code here
        /// - Stop threads, set final data, etc.
        /// </summary>
        protected override void OnStop()
        {
            // Start by halting any further builds from starting
            CurrentJobs += 10000;

            // We should halt any daemons we have running...
            foreach (var daemon in Daemons)
            {
                daemon.Stop();
            }

            // Save all current configuration info.
            SaveConfig();

            /*
            foreach (var proj in Projects.Keys)
            {
                SaveProject(proj);
            }
            */

            //Dump the current build queue to a pending list.
            SaveQueue();

            base.OnStop();
        }

        /// <summary>
        /// OnPause: Put your pause code here
        /// - Pause working threads, etc.
        /// </summary>
        protected override void OnPause()
        {
            // Presently not implemented.
            // This is disabled above in AutoBuild().
        }

        /// <summary>
        /// OnContinue(): Put your continue code here
        /// - Un-pause working threads, etc.
        /// </summary>
        protected override void OnContinue()
        {
            // Presently not implemented.
            // This is disabled above in AutoBuild().
        }

        protected override void OnShutdown()
        {
            OnStop();
            base.OnShutdown();
        }

        #endregion


        protected void InitWorld()
        {
            // Load the master config
            LoadConfig();

            // Find and load all projects
            DirectoryInfo ProjectRoot = new DirectoryInfo(MasterConfig.ProjectRoot);
            if (Directory.Exists(ProjectRoot.ToString()))
            {
                DirectoryInfo[] children = ProjectRoot.GetDirectories();
                foreach (DirectoryInfo child in children)
                {
                    LoadProject(child.Name);
                }
            }
            else
            {
                Directory.CreateDirectory(ProjectRoot.ToString());
            }
            //-initialize listeners
            foreach (string name in Projects.Keys)
            {
                InitProject(name);
            }

            // Sometime, I need to make this a switchable option.
            // I also need to provide a better plugin mechanism for new listeners.
            if (MasterConfig.UseGithubListener)
            {
                ListenAgent agent = new ListenAgent();
                agent.Logger = ((message, type) => WriteEvent(message, type, 0, 1));
                if (agent.Start())
                {
                    Daemons.Add(agent);
                }
                else
                {
                    agent.Stop();
                    WriteEvent("ListenAgent failed to start properly.", EventLogEntryType.Error, 0, 0);
                }
            }

            ////-start timers as appropriate
        }


        protected void MasterChanged(AutoBuild_config config)
        {
            SaveConfig();
        }

        protected void ProjectChanged(string project)
        {
            SaveProject(project);
        }


        private static void LoadQueue()
        {
            var regKey = Registry.LocalMachine.CreateSubKey(@"Software\CoApp\AutoBuild Service") ??
             Registry.LocalMachine.OpenSubKey(@"Software\CoApp\AutoBuild Service");
            if (regKey == null)
                throw new Exception("Unable to load registry key.");
            string configfile = (string)(regKey.GetValue("ConfigFile", null));
            string path = Path.GetDirectoryName(configfile);
            if (path == null)
                return;
            path = Path.Combine(path, "PreviouslyQueued.txt");

            if (!File.Exists(path))
                return;

            string[] queue = File.ReadAllLines(path);
            foreach (var s in queue)
            {
                try
                {
                    StandBy(s);
                }
                catch (Exception e)
                {
                }
            }
            // Clean up the file now that we've read it.
            File.Delete(path);
        }

        private static void SaveQueue()
        {
            var regKey = Registry.LocalMachine.CreateSubKey(@"Software\CoApp\AutoBuild Service") ??
             Registry.LocalMachine.OpenSubKey(@"Software\CoApp\AutoBuild Service");
            if (regKey == null)
                throw new Exception("Unable to load registry key.");
            string configfile = (string)(regKey.GetValue("ConfigFile", null));
            string path = Path.GetDirectoryName(configfile);
            if (path == null)
                return;
            StringBuilder SB = new StringBuilder();
            while (RunQueue.Count > 0)
            {
                SB.AppendLine(RunQueue.Dequeue());
            }
            foreach (var pair in Waiting)
            {
                pair.Value.Dispose();
                SB.AppendLine(pair.Key);
            }
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "PreviouslyQueued.txt"), SB.ToString());
        }

        /// <summary>
        /// Loads the master config file from disk.
        /// This will first check for a registry key to locate the config.conf.
        /// If the file cannot be opened or cannot be found, a default config will be loaded
        /// </summary>
        /// <returns>True if a config file was successfully loaded.  False if a default config had to be generated.</returns>
        public bool LoadConfig()
        {
            try
            {
                var regKey = Registry.LocalMachine.CreateSubKey(@"Software\CoApp\AutoBuild Service") ??
                             Registry.LocalMachine.OpenSubKey(@"Software\CoApp\AutoBuild Service");
                if (regKey == null)
                    throw new Exception("Unable to load registry key.");
                string configfile = (string)(regKey.GetValue("ConfigFile", null));
                if (configfile == null)
                {
                    configfile = @"C:\AutoBuild\config.conf";
                    regKey.SetValue("ConfigFile", configfile);
                }
                UrlEncodedMessage UEM = new UrlEncodedMessage(File.ReadAllText(configfile), AutoBuild.SerialSeperator, true);
                UEM.DeserializeTo(MasterConfig);
                MasterConfig.Changed += MasterChanged;
                return true;
            }
            catch (Exception e)
            {
                MasterConfig = MasterConfig ?? new AutoBuild_config();
                WriteEvent("Unable to load master config:\n" + e.Message + "\n\nDefault config loaded.", EventLogEntryType.Error, 0, 0);
                MasterConfig.Changed += MasterChanged;
                return false;
            }

        }

        /// <summary>
        /// Loads a project configuration from disk.
        /// </summary>
        /// <param name="projectName">The name of the project to load.</param>
        /// <param name="overwrite">If true, will reload the project config data even if the project already has a configuration loaded.  (False by default)</param>
        /// <returns>True if the project was loaded successfully.  False otherwise.</returns>
        public bool LoadProject(string projectName, bool overwrite = false)
        {
            if (Projects.ContainsKey(projectName) && !overwrite)
                return false;

            try
            {
                if (projectName == null)
                    throw new ArgumentException("ProjectName cannot be null.");

                Projects[projectName] = new ProjectData();
                if (!Projects.ContainsKey(projectName))
                    throw new ArgumentException("Project not found: " + projectName);

                string file = Path.Combine(MasterConfig.ProjectRoot, projectName, "config.conf");
                UrlEncodedMessage UEM = new UrlEncodedMessage(File.ReadAllText(file), AutoBuild.SerialSeperator, true);
                UEM.DeserializeTo(Projects[projectName]);
                Projects[projectName].SetName(projectName);
                string logPath = Path.Combine(MasterConfig.ProjectRoot, projectName, "Log.log");
                if (!File.Exists(logPath))
                    logPath = String.Empty;
                Projects[projectName].LoadHistory(logPath);
                Projects[projectName].Changed2 += ProjectChanged;
                return true;
            }
            catch (Exception e)
            {
                WriteEvent("Unable to load project config (" + projectName + "):\n" + e.Message, EventLogEntryType.Error, 0, 0);
                return false;
            }
        }

        /// <summary>
        /// Saves the master configuration data to disk.
        /// </summary>
        /// <returns>True if saved successfully.  False otherwise.</returns>
        public bool SaveConfig()
        {
            try
            {
                var regKey = Registry.LocalMachine.CreateSubKey(@"Software\CoApp\AutoBuild Service") ??
                             Registry.LocalMachine.OpenSubKey(@"Software\CoApp\AutoBuild Service");
                if (regKey == null)
                    throw new Exception("Unable to load registry key.");
                string configfile = (string)(regKey.GetValue("ConfigFile", null));
                if (configfile == null)
                {
                    configfile = @"C:\AutoBuild\config.conf";
                    regKey.SetValue("ConfigFile", configfile);
                }
                if (!Directory.Exists(Path.GetDirectoryName(configfile)))
                    Directory.CreateDirectory(Path.GetDirectoryName(configfile));
                File.WriteAllText(configfile, MasterConfig.Serialize(AutoBuild.SerialSeperator, true));
                return true;
            }
            catch (Exception e)
            {
                WriteEvent("Unable to save master config:\n" + e.Message, EventLogEntryType.Error, 0, 0);
                return false;
            }

        }

        /// <summary>
        /// Saves a project config to disk.
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <returns>True if saved successfully.  False otherwise.</returns>
        public bool SaveProject(string projectName)
        {
            try
            {
                if (projectName == null)
                    throw new ArgumentException("ProjectName cannot be null.");
                if (!Projects.ContainsKey(projectName))
                    throw new ArgumentException("Project not found: " + projectName);

                string path = Path.Combine(MasterConfig.ProjectRoot, projectName);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

//                File.WriteAllText(Path.Combine(path, "config.conf"), Projects[projectName].ToXML());
//                File.WriteAllText(Path.Combine(path, "log.xml"), Projects[projectName].GetHistory().ExportXml());
                File.WriteAllText(Path.Combine(path, "config.conf"), Projects[projectName].Serialize(AutoBuild.SerialSeperator, true));
                File.WriteAllText(Path.Combine(path, "log.log"), Projects[projectName].GetHistory().Serialize(AutoBuild.SerialSeperator, true));
                
                return true;
            }
            catch (Exception e)
            {
                WriteEvent("Unable to save project config (" + projectName + "):\n" + e.Message, EventLogEntryType.Error, 0, 0);
                return false;
            }

        }

        public static bool IsWaiting(string projectName)
        {
            return (Waiting.ContainsKey(projectName) || RunQueue.Contains(projectName));
        }
        public static bool IsRunning(string projectName)
        {
            return Running.Contains(projectName);
        }
        public static bool CancelQueue(string projectName)
        {
            bool canceled = true; //assume that there's nothing to remove
            if (Waiting.ContainsKey(projectName))
            {
                Waiting[projectName].Change(Timeout.Infinite, Timeout.Infinite);
                Waiting[projectName].Dispose();
                canceled = Waiting.Remove(projectName);
            }
            if (RunQueue.Contains(projectName) && !Cancellations.Contains(projectName))
                Cancellations.Add(projectName);
            return canceled;
        }

        protected void WriteEvent(string Message, EventLogEntryType EventType, int ID, short Category)
        {
            EventLog.WriteEntry(Message, EventType, ID, Category);
        }

        public void AddProject(string projectName, ProjectData project)
        {
            if (Projects.ContainsKey(projectName))
                throw new ArgumentException("A project with this name already exists: " + projectName);
            Projects[projectName] = project;
            Projects[projectName].LoadHistory(new BuildHistory());
            SaveProject(projectName); // save this so we don't run into other problems later
            Projects[projectName].Changed2 += ProjectChanged;
            InitProject(projectName);
        }

        public static void InitProject(string projectName)
        {
            if (projectName == null)
                throw new ArgumentException("ProjectName cannot be null.");
            if (!Projects.ContainsKey(projectName))
                throw new ArgumentException("Project not found: " + projectName);
            foreach (var trigger in Projects[projectName].BuildTriggers)
                trigger.Init();
        }

        private static void StartBuild(string projectName)
        {
            WriteVerbose("Starting project:  " + projectName);
            if (Projects[projectName].BuildCheckouts.Any())
            {
                foreach (var checkout in Projects[projectName].BuildCheckouts.Keys)
                {
                    BuildStatus build = new BuildStatus();
                    Projects[projectName].GetHistory().Append(build);
                    Directory.CreateDirectory(Path.Combine(MasterConfig.ProjectRoot, projectName, "Archive",
                                                           build.TimeStamp.ToString(DateTimeDirFormat)));
                    string RunLog = Path.Combine(MasterConfig.ProjectRoot, projectName, "Archive",
                             build.TimeStamp.ToString(DateTimeDirFormat), "run.log");
                    StreamWriter runStream = new StreamWriter(RunLog, true);
                    build.Append("Log for project [" + projectName + "] on reference [" + checkout + "]");
                    if (PreBuildActions(projectName, build, checkout, runStream) == 0)
                        if (BuildActions(projectName, build, checkout, runStream) == 0)
                            if (PostBuildActions(projectName, build, checkout, runStream) == 0)
                                build.ChangeResult("Success");
                            else
                                build.ChangeResult("Warning");
                        else
                            build.ChangeResult("Failed");
                    else
                        build.ChangeResult("Error");
                    runStream.Close();
                    build.Lock();
                    WriteVerbose("Project done: " + projectName + " \t Result: " + build.Result);
                    string BuildLog = Path.Combine(MasterConfig.ProjectRoot, projectName, "Archive",
                                     build.TimeStamp.ToString(DateTimeDirFormat), "Build.log");
                    File.WriteAllText(BuildLog, build.LogData);
                }
            }
            else
            {
                BuildStatus build = new BuildStatus();
                Projects[projectName].GetHistory().Append(build);
                Directory.CreateDirectory(Path.Combine(MasterConfig.ProjectRoot, projectName, "Archive",
                                                       build.TimeStamp.ToString(DateTimeDirFormat)));
                string RunLog = Path.Combine(MasterConfig.ProjectRoot, projectName, "Archive",
                                             build.TimeStamp.ToString(DateTimeDirFormat), "run.log");
                StreamWriter runStream = new StreamWriter(RunLog, true);
                build.Append("Log for project [" + projectName + "]");
                if (PreBuildActions(projectName, build) == 0)
                    if (BuildActions(projectName, build) == 0)
                        if (PostBuildActions(projectName, build) == 0)
                            build.ChangeResult("Success");
                        else
                            build.ChangeResult("Warning");
                    else
                        build.ChangeResult("Failed");
                else
                    build.ChangeResult("Error");
                runStream.Close();
                build.Lock();
                WriteVerbose("Project done: " + projectName + " \t Result: " + build.Result);
                string BuildLog = Path.Combine(MasterConfig.ProjectRoot, projectName, "Archive",
                                 build.TimeStamp.ToString(DateTimeDirFormat), "Build.log");
                File.WriteAllText(BuildLog, build.LogData);
            }
        }

        public static void StandBy(string projectName)
        {
            if (projectName == null)
                throw new ArgumentException("ProjectName cannot be null.");
            if (!Projects.ContainsKey(projectName))
                throw new ArgumentException("Project not found: " + projectName);

            Waiting[projectName] = Waiting[projectName] ?? new Timer(o =>
                                                                         {
                                                                             Waiting[projectName].Dispose();
                                                                             Waiting.Remove(projectName);
                                                                             Trigger(projectName);
                                                                         });
            Waiting[projectName].Change(MasterConfig.PreTriggerWait, Timeout.Infinite);
            
            if (Cancellations.Contains(projectName))
                Cancellations.Remove(projectName);
        }

        public static void Trigger(string projectName)
        {
            if (projectName == null)
                throw new ArgumentException("ProjectName cannot be null.");
            if (!Projects.ContainsKey(projectName))
                throw new ArgumentException("Project not found: " + projectName);

            if (!(RunQueue.Contains(projectName) || Running.Contains(projectName)) || Projects[projectName].AllowConcurrentBuilds)
                RunQueue.Enqueue(projectName);
            Task.Factory.StartNew(ProcessQueue);
        }

        public static void ProcessQueue()
        {
            if (RunQueue.Count > 0)
            {
                while (CurrentJobs < MasterConfig.MaxJobs && RunQueue.Count > 0)
                {
                    CurrentJobs += 1;
                    string proj = RunQueue.Dequeue();
                    if (Waiting[proj] == null)
                    {
                        if (Cancellations.Contains(proj))
                        {
                            Cancellations.Remove(proj);
                            CurrentJobs -= 1;
                            return;
                        }

                        Task.Factory.StartNew(() =>
                                                  {
                                                      Running.Add(proj);
                                                      StartBuild(proj);
                                                  }, TaskCreationOptions.AttachedToParent).ContinueWith(
                                                      antecedent =>
                                                      {
                                                          Running.Remove(proj);
                                                          CurrentJobs -= 1;
                                                          Task.Factory.StartNew(ProcessQueue);
                                                      });
                    }
                    else
                        CurrentJobs -= 1;
                }
            }
            
        }

        private static int doActions(string projectName, IEnumerable<string> commands, BuildStatus status = null, XDictionary<string, string> Macros = null, StreamWriter OutputStream = null)
        {
            if (projectName == null)
                throw new ArgumentException("ProjectName cannot be null.");
            if (!Projects.ContainsKey(projectName))
                throw new ArgumentException("Project not found: " + projectName);

            Macros = Macros ?? new XDictionary<string, string>();

            status = status ?? new BuildStatus();
            string ArchiveLoc = Path.Combine(MasterConfig.ProjectRoot, projectName, "Archive",
                                             status.TimeStamp.ToString(DateTimeDirFormat));

            if (!Directory.Exists(ArchiveLoc))
                Directory.CreateDirectory(ArchiveLoc);
            ProjectData proj = Projects[projectName];
            ProcessUtility _cmdexe = new ProcessUtility("cmd.exe");
            _cmdexe.ConsoleOut = WriteConsole;
            _cmdexe.AssignOutputStream(OutputStream);
            

            Func<string> getToolSwitches = () =>
            {
                string ret = String.Empty;
                foreach (
                    string s in
                        MasterConfig.VersionControlList[proj.VersionControl].Tool.
                            Switches)
                    if (s.Contains(" "))
                        ret += " \"" + s + "\"";
                    else
                        ret += " " + s;
                return ret;
            };

            Macros["project"] = projectName;
            Macros["vcstool"] = MasterConfig.VersionControlList[proj.VersionControl].Tool.Path;
            Macros["vcsswitches"] = getToolSwitches();
            Macros["keepclean"] = proj.KeepCleanRepo.ToString();
            string rootPath = MasterConfig.ProjectRoot + @"\" + projectName;
            Macros["projectroot"] = rootPath;
            Macros["repo_url"] = proj.RepoURL;
            Macros["build_datetime"] = status.TimeStamp.ToString(DateTimeDirFormat);
            Macros["archive"] = ArchiveLoc;
            Macros["output_store"] = MasterConfig.OutputStore;

            foreach (string command in commands)
            {
                StringBuilder std = new StringBuilder();
                _cmdexe.ResetStdOut(std);
                _cmdexe.ResetStdErr(std);

                status.Append("AutoBuild - Begin command:  " + command);
                Macros["currentcommand"] = command;

                CommandScript tmp;
                if (proj.Commands.ContainsKey(command))
                {
                    tmp = proj.Commands[command];
                }
                else if (MasterConfig.Commands.ContainsKey(command))
                {
                    tmp = MasterConfig.Commands[command];
                }
                else
                {
                    // Can't locate the specified command.  Bail with error.
                    status.Append("AutoBuild Error:  Unable to locate command script: " + command);
                    return (int)Errors.NoCommand;
                }

                int retVal = tmp.Run(_cmdexe, rootPath, new XDictionary<string, string>(Macros));
                status.Append(_cmdexe.StandardOut);
                if (retVal != 0)
                    return retVal;
            }

            return 0;
        }

        private static int PreBuildActions(string projectName, BuildStatus status = null, string checkoutRef = null, StreamWriter OutputStream = null)
        {
            if (projectName == null)
                throw new ArgumentException("ProjectName cannot be null.");
            if (!Projects.ContainsKey(projectName))
                throw new ArgumentException("Project not found: " + projectName);

            WriteVerbose("Start PreBuild: " + projectName);
            if (checkoutRef != null)
            {
                XDictionary<string, string> macros = new XDictionary<string, string>();
                macros["checkout"] = checkoutRef;
                return doActions(projectName, Projects[projectName].BuildCheckouts[checkoutRef].PreCmd, status, macros, OutputStream);
            }
            // else
            return doActions(projectName, Projects[projectName].PreBuild, status);
        }

        private static int PostBuildActions(string projectName, BuildStatus status = null, string checkoutRef = null, StreamWriter OutputStream = null)
        {
            if (projectName == null)
                throw new ArgumentException("ProjectName cannot be null.");
            if (!Projects.ContainsKey(projectName))
                throw new ArgumentException("Project not found: " + projectName);

            WriteVerbose("Start PostBuild: " + projectName);
            if (checkoutRef != null)
            {
                XDictionary<string, string> macros = new XDictionary<string, string>();
                macros["checkout"] = checkoutRef;
                return doActions(projectName, Projects[projectName].BuildCheckouts[checkoutRef].ArchiveCmd, status, macros, OutputStream);
            }
            // else
            return doActions(projectName, Projects[projectName].PostBuild, status);
        }

        private static int BuildActions(string projectName, BuildStatus status = null, string checkoutRef = null, StreamWriter OutputStream = null)
        {
            if (projectName == null)
                throw new ArgumentException("ProjectName cannot be null.");
            if (!Projects.ContainsKey(projectName))
                throw new ArgumentException("Project not found: " + projectName);

            WriteVerbose("Start Build: " + projectName);
            if (checkoutRef != null)
            {
                XDictionary<string, string> macros = new XDictionary<string, string>();
                macros["checkout"] = checkoutRef;
                return doActions(projectName, Projects[projectName].BuildCheckouts[checkoutRef].BuildCmd, status, macros, OutputStream);
            }
            // else
            return doActions(projectName, Projects[projectName].Build, status);
        }


    }
}
