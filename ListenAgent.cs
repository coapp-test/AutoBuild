using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CoApp.Toolkit.Collections;
using CoApp.Toolkit.Extensions;
using CoApp.Toolkit.Pipes;
using CoApp.Toolkit.Tasks;
using CoApp.Toolkit.Utility;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace AutoBuilder
{
    public class RequestHandler
    {
        public virtual Task Put(HttpListenerResponse response, string relativePath, byte[] data)
        {
            return null;
        }

        public virtual Task Get(HttpListenerResponse response, string relativePath, UrlEncodedMessage message)
        {
            return null;
        }

        public virtual Task Post(HttpListenerResponse response, string relativePath, UrlEncodedMessage message)
        {
            return null;
        }

        public virtual Task Head(HttpListenerResponse response, string relativePath, UrlEncodedMessage message)
        {
            return null;
        }
    }

    public delegate void Logger(string message, EventLogEntryType type);

    public class Listener
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly List<string> _hosts = new List<string>();
        private readonly List<int> _ports = new List<int>();
        private readonly Dictionary<string, RequestHandler> _paths = new Dictionary<string, RequestHandler>();
        private Task<HttpListenerContext> _current = null;

        public static Logger Logger;
        private static void WriteLog(string message, EventLogEntryType type = EventLogEntryType.Information)
        {
            if (Logger != null)
                Logger(message, type);
        }

        public Listener()
        { }

        Regex ipAddrRx = new Regex(@"^([1-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-5])(\.([0-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-5])){3}$");
        Regex hostnameRx = new Regex(@"(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*");

        public void AddHost(string host)
        {
            if (string.IsNullOrEmpty(host))
                return;

            host = host.ToLower();

            if (_hosts.Contains(host))
                return;

            if (host == "+" || host == "*" || ipAddrRx.IsMatch(host) || hostnameRx.IsMatch(host))
            {
                _hosts.Add(host);
                if (_current != null)
                    Restart();
                return;
            }
        }

        public void RemoveHost(string host)
        {
            if (string.IsNullOrEmpty(host))
                return;

            host = host.ToLower();

            if (_hosts.Contains(host))
            {
                _hosts.Remove(host);
                if (_current != null)
                    Restart();
            }
        }

        public void AddPort(int port)
        {
            if (port <= 0 || port > 65535)
                return;

            if (_ports.Contains(port))
                return;

            _ports.Add(port);
            if (_current != null)
                Restart();
        }

        public void RemovePort(int port)
        {
            if (_ports.Contains(port))
            {
                _ports.Remove(port);
                if (_current != null)
                    Restart();
            }
        }

        public void AddHandler(string path, RequestHandler handler)
        {
            if (string.IsNullOrEmpty(path))
                path = "/";
            path = path.ToLower();

            if (!path.StartsWith("/"))
                path = "/" + path;

            if (!path.EndsWith("/"))
                path = path + "/";

            if (_paths.ContainsKey(path))
                return;

            _paths.Add(path, handler);
            if (_current != null)
                Restart();
        }

        public void RemoveHandler(string path)
        {
            if (string.IsNullOrEmpty(path))
                path = "/";

            path = path.ToLower();

            if (!path.StartsWith("/"))
                path = "/" + path;

            if (!path.EndsWith("/"))
                path = path + "/";

            if (_paths.ContainsKey(path))
            {
                _paths.Remove(path);
                if (_current != null)
                    Restart();
            }
        }


        public void Restart()
        {
            try
            { Stop(); }
            catch
            { }

            try
            { Start(); }
            catch
            { }

        }

        public void Stop()
        {
            _listener.Stop();
            _current = null;
        }


        public void Start()
        {
            if (_current == null)
            {
                _listener.Prefixes.Clear();
                foreach (var host in _hosts)
                {
                    foreach (var port in _ports)
                    {
                        foreach (var path in _paths.Keys)
                        {
                            WriteLog("Adding `http://{0}:{1}{2}`".format(host, port, path));
                            _listener.Prefixes.Add("http://{0}:{1}{2}".format(host, port, path));
                        }
                    }
                }
            }

            _listener.Start();

            _current = Task.Factory.FromAsync<HttpListenerContext>(_listener.BeginGetContext, _listener.EndGetContext, _listener);

            _current.ContinueWith(antecedent =>
            {
                if (antecedent.IsCanceled || antecedent.IsFaulted)
                {
                    _current = null;
                    return;
                }
                Start(); // start a new listener.

                try
                {
                    var request = antecedent.Result.Request;
                    var response = antecedent.Result.Response;
                    var url = request.Url;
                    var path = url.AbsolutePath.ToLower();

                    var handlerKey = _paths.Keys.OrderByDescending(each => each.Length).Where(path.StartsWith).FirstOrDefault();
                    if (handlerKey == null)
                    {
                        // no handler
                        response.StatusCode = 404;
                        response.Close();
                        return;
                    }

                    var relativePath = path.Substring(handlerKey.Length);

                    if (string.IsNullOrEmpty(relativePath))
                        relativePath = "index";

                    var handler = _paths[handlerKey];
                    Task handlerTask = null;
                    var length = request.ContentLength64;

                    switch (request.HttpMethod)
                    {

                        case "PUT":
                            try
                            {
                                var putData = new byte[length];
                                var read = 0;
                                var offset = 0;
                                do
                                {
                                    read = request.InputStream.Read(putData, offset, (int)length - offset);
                                    offset += read;
                                } while (read > 0 && offset < length);

                                handlerTask = handler.Put(response, relativePath, putData);
                            }
                            catch (Exception e)
                            {
                                HandleException(e);
                                response.StatusCode = 500;
                                response.Close();
                            }
                            break;

                        case "HEAD":
                            try
                            {
                                handlerTask = handler.Head(response, relativePath, new UrlEncodedMessage(relativePath + "?" + url.Query));
                            }
                            catch (Exception e)
                            {
                                HandleException(e);
                                response.StatusCode = 500;
                                response.Close();
                            }
                            break;

                        case "GET":
                            try
                            {
                                handlerTask = handler.Get(response, relativePath, new UrlEncodedMessage(relativePath + "?" + url.Query));
                            }
                            catch (Exception e)
                            {
                                HandleException(e);
                                response.StatusCode = 500;
                                response.Close();
                            }
                            break;

                        case "POST":
                            try
                            {
                                var postData = new byte[length];
                                var read = 0;
                                var offset = 0;
                                do
                                {
                                    read = request.InputStream.Read(postData, offset, (int)length - offset);
                                    offset += read;
                                } while (read > 0 && offset < length);

                                handlerTask = handler.Post(response, relativePath, new UrlEncodedMessage(relativePath + "?" + Encoding.UTF8.GetString(postData)));
                            }
                            catch (Exception e)
                            {
                                HandleException(e);
                                response.StatusCode = 500;
                                response.Close();
                            }
                            break;
                    }

                    if (handlerTask != null)
                    {
                        handlerTask.ContinueWith((antecedent2) =>
                        {
                            if (antecedent2.IsFaulted && antecedent2.Exception != null)
                            {
                                var e = antecedent2.Exception.InnerException;
                                HandleException(e);
                                response.StatusCode = 500;
                            }

                            response.Close();
                        }, TaskContinuationOptions.AttachedToParent);
                    }
                    else
                    {
                        // nothing retured? must be unimplemented.
                        response.StatusCode = 405;
                        response.Close();
                    }
                }
                catch (Exception e)
                {
                    HandleException(e);
                }
            }, TaskContinuationOptions.AttachedToParent);
        }

        public static void HandleException(Exception e)
        {
            if (e is AggregateException)
                e = (e as AggregateException).Flatten().InnerExceptions[0];

            WriteLog("{0} -- {1}\r\n{2}".format(e.GetType(), e.Message, e.StackTrace), EventLogEntryType.Error);
        }
    }

    public static class HttpListenerResponseExtensions
    {
        public static void WriteString(this HttpListenerResponse response, string format, params string[] args)
        {
            var text = string.Format(format, args);
            var buffer = Encoding.UTF8.GetBytes(text);
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Flush();
        }
    }


    public class PostHandler : RequestHandler
    {
        public static Logger Logger;
        private static void WriteLog(string message, EventLogEntryType type = EventLogEntryType.Information)
        {
            if (Logger != null)
                Logger(message, type);
        }

        public PostHandler()
        { }

        public PostHandler(Logger logger)
        {
            Logger = logger;
        }

        public override Task Post(HttpListenerResponse response, string relativePath, UrlEncodedMessage message)
        {
            var payload = (string)message["payload"];
            if (payload == null)
            {
                response.StatusCode = 500;
                response.Close();
                return "".AsResultTask();
            }

            var result = Task.Factory.StartNew(() =>
            {
                try
                {
                    dynamic json = JObject.Parse(payload);
                    var jobj = JObject.Parse(payload);

                    WriteLog("MSG Process begin " + json.commits.Count);

                    string repository = (json.repository.name) ?? String.Empty;
                    string reference = json["ref"].ToString();

                    int count = json.commits.Count;
                    bool validTrigger = false;


                    for (int i = 0; i < count; i++)
                    {
                        string username = (json.commits[i].author.username ?? json.commits[i].author.name ?? new {Value = String.Empty}).Value;

                        if (!username.Equals((string)(AutoBuild.MasterConfig.VersionControlList["git"].Properties["username"]), StringComparison.CurrentCultureIgnoreCase))
                        {
                            validTrigger = true;
                        }
                    }

                    if (validTrigger)
                    {
                        AutoBuild.WriteVerbose("POST received: " + repository + " -- " + reference);

                        if (AutoBuild.Projects.ContainsKey(repository))
                        {
                            ProjectData project = AutoBuild.Projects[repository];
                            if (project.WatchRefs.IsNullOrEmpty() || project.WatchRefs.Contains(reference))
                                AutoBuild.StandBy(repository);
                        }
                        else
                        {
                            bool makeNew;
                            if (!Boolean.TryParse(AutoBuild.MasterConfig.VersionControlList["git"].Properties["NewFromHook"], out makeNew))
                                return;
                            if (makeNew)
                            {
                                /////Build new ProjectInfo info from commit message.
                                ProjectData project = new ProjectData();
                                project.SetName(repository);

                                project.Enabled = true;
                                project.KeepCleanRepo = AutoBuild.MasterConfig.DefaultCleanRepo;

                                // This section constructs the repo url to use...
                                string init_url = json.repository.url;
                                string proto = init_url.Substring(0, init_url.IndexOf("://") + 3);
                                init_url = init_url.Substring(proto.Length);
                                string host = init_url.Substring(0, init_url.IndexOf("/"));
                                string repo = init_url.Substring(init_url.IndexOf("/") + 1);
                                switch (((string)(AutoBuild.MasterConfig.VersionControlList["git"].Properties["url_style"])).ToLower())
                                {
                                    case "git":
                                        project.RepoURL = "git://" + host + "/" + repo;
                                        break;
                                    case "http":
                                        project.RepoURL = json.url;
                                        break;
                                    case "ssh":
                                        project.RepoURL = "git@" + host + ":" + repo;
                                        break;
                                    default:
                                        project.RepoURL = null;
                                        break;
                                }
                                // End repo url section

                                project.WatchRefs.AddRange(AutoBuild.MasterConfig.DefaultRefs);

                                if (!(AutoBuild.MasterConfig.DefaultCommands.IsNullOrEmpty()))
                                {

                                    if (project.WatchRefs.Count > 0)
                                    {
                                        foreach (string watchRef in project.WatchRefs)
                                        {
                                            string branch = watchRef.Substring(11);  //length of @"refs/heads/"
                                            project.BuildCheckouts[branch] = new ProjectData.CheckoutInfo();

                                            List<string> strings;
                                            //prebuild
                                            strings = AutoBuild.MasterConfig.DefaultCommands["prebuild"] ??
                                                      new List<string>();
                                            foreach (string s in strings)
                                            {
                                                project.BuildCheckouts[branch].PreCmd.Add(s);
                                            }
                                            //build
                                            project.BuildCheckouts[branch].BuildCmd.Add("Checkout"); // magic name
                                            strings = AutoBuild.MasterConfig.DefaultCommands["build"] ?? new List<string>();
                                            foreach (string s in strings)
                                            {
                                                project.BuildCheckouts[branch].BuildCmd.Add(s);
                                            }
                                            //postbuild
                                            strings = AutoBuild.MasterConfig.DefaultCommands["postbuild"] ??
                                                      new List<string>();
                                            foreach (string s in strings)
                                            {
                                                project.BuildCheckouts[branch].ArchiveCmd.Add(s);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        List<string> strings;
                                        //prebuild
                                        strings = AutoBuild.MasterConfig.DefaultCommands["prebuild"] ??
                                                  new List<string>();
                                        foreach (string s in strings)
                                        {
                                            project.PreBuild.Add(s);
                                        }
                                        //build
                                        strings = AutoBuild.MasterConfig.DefaultCommands["build"] ?? new List<string>();
                                        foreach (string s in strings)
                                        {
                                            project.Build.Add(s);
                                        }
                                        //postbuild
                                        strings = AutoBuild.MasterConfig.DefaultCommands["postbuild"] ??
                                                  new List<string>();
                                        foreach (string s in strings)
                                        {
                                            project.PostBuild.Add(s);
                                        }

                                    }
                                }

                                //We're obviously adding a git repo for this project, so assign that for the project's version control
                                project.VersionControl = "git";

                                //Add the new project with the new ProjectInfo
                                AutoBuild.Instance.AddProject(repository, project);

                                //Start the wait period.
                                AutoBuild.StandBy(repository);
                            }
                        }
                    }

                }
                catch (Exception e)
                {
                    WriteLog("Error processing payload: {0} -- {1}\r\n{2}".format(e.GetType(), e.Message, e.StackTrace), EventLogEntryType.Error);
                    Listener.HandleException(e);
                    response.StatusCode = 500;
                    response.Close();
                }
            }, TaskCreationOptions.AttachedToParent);

            result.ContinueWith(antecedent =>
            {
                if (result.IsFaulted)
                {
                    var e = antecedent.Exception.InnerException;
                    WriteLog("Error handling commit message: {0} -- {1}\r\n{2}".format(e.GetType(), e.Message, e.StackTrace), EventLogEntryType.Error);
                    Listener.HandleException(e);
                    response.StatusCode = 500;
                    response.Close();
                }
            }, TaskContinuationOptions.OnlyOnFaulted);

            return result;
        }


        public override Task Get(HttpListenerResponse response, string relativePath, UrlEncodedMessage message)
        {
            if ((message["status"] + message["build"] + message["publish"] + message["log"] + message["reload"] + message["reconfig"] + message["cancel"])
                .Equals(String.Empty))
            {
                response.StatusCode = 500;
                response.Close();
                return "".AsResultTask();
            }
            
            var result = Task.Factory.StartNew(() =>
            {
                try
                {
                    response.AddHeader("Content-Type", "text/plain");
                    if (message["reconfig"] != String.Empty)
                    {
                        //re-load global config request
                        if (!AutoBuild.Instance.LoadConfig())
                        {
                            response.WriteString("Failed to load new global config.  ");
                        }
                    }
                    if (message["reload"] != String.Empty)
                    {
                        //re-load project config request
                        string projName = message["reload"];
                        if (!AutoBuild.Instance.LoadProject(projName, true))
                        {
                            response.WriteString("Failed to reload project config: '{0}'.  ", projName);
                        }
                    }
                    if (message["status"] != String.Empty)
                    {
                        //status request
                        string projName = message["status"];
                        var proj = AutoBuild.Projects[projName];
                        if (proj != null)
                        {
                            string currentStatus = AutoBuild.IsRunning(projName)
                                                       ? "Running"
                                                       : AutoBuild.IsWaiting(projName)
                                                             ? "Waiting in queue"
                                                             : proj.GetHistory().Builds.Count > 0
                                                                   ? "{0}  at {1}".format(
                                                                       proj.GetHistory().Builds[
                                                                           proj.GetHistory().Builds.Count-1].Result,
                                                                       proj.GetHistory().Builds[
                                                                           proj.GetHistory().Builds.Count-1].TimeStamp
                                                                           .ToString("yyyy-MM-dd HH:mm:ss"))
                                                                   : "Project status unavailable.  Please build the project.";
                            response.WriteString("Status of project '{0}':  {1}", projName, currentStatus);
                        }
                        else
                        {
                            response.WriteString("Unable to return status for project '{0}':  No such project.  ", projName);
                        }
                    }
                    if (message["build"] != String.Empty)
                    {
                        //build request
                        string projName = message["build"];
                        if (AutoBuild.Projects[projName] != null)
                        {
                            if (AutoBuild.IsWaiting(projName))
                                response.WriteString("Project already scheduled to run: '{0}'.  ", projName);
                            else
                            {
                                AutoBuild.StandBy(projName);
                                response.WriteString("Project added to build queue: '{0}'.  ", projName);
                            }
                        }
                        else
                        {
                            response.WriteString("Unable to return status for project '{0}':  No such project.  ", projName);
                        }
                    }
                    if (message["cancel"] != String.Empty)
                    {
                        //logfile request
                        string projName = message["cancel"];
                        if (AutoBuild.Projects.ContainsKey(projName))
                        {
                            string msg = AutoBuild.CancelQueue(projName)
                                             ? "Pending builds canceled for project: '{0}'.  ".format(projName)
                                             : "Error cancelling pending builds: '{0}'.  ".format(projName);
                            response.WriteString(msg);
                        }
                        else
                        {
                            response.WriteString("Unable to cancel, project does not exist: '{0}'.  ", projName);
                        }
                    }
                    if (message["publish"] != String.Empty)
                    {
                        //publish finished packages
                        ProcessUtility _cmdexe = new ProcessUtility("cmd.exe");

                        int ret = AutoBuild.MasterConfig.Commands["MasterPublish"].Run(_cmdexe,
                                                                                       Environment.CurrentDirectory,
                                                                                       new XDictionary<string, string>());
                        if (ret == 0)
                            response.WriteString("Publish uploads completed successsfully.  ");
                        else
                            response.WriteString("Error occurred during publish upload.  ");
                    }
                    if (message["log"] != String.Empty)
                    {
                        //logfile request
                        string projName = message["log"];
                        var proj = AutoBuild.Projects[projName];
                        if (proj != null)
                        {
                            string msg;
                            if (proj.GetHistory().Builds.Count <= 0)
                                msg = "Project status unavailable.  Please build the project.";
                            else
                            {
                                try
                                {
                                    string logpath = Path.Combine(AutoBuild.MasterConfig.ProjectRoot, projName, "Archive",
                                                                  proj.GetHistory().Builds[
                                                                      proj.GetHistory().Builds.Count - 1].TimeStamp.ToString
                                                                      (AutoBuild.DateTimeDirFormat));
                                    string logfile = File.Exists(Path.Combine(logpath, "build.log")) ? Path.Combine(logpath, "build.log") : Path.Combine(logpath, "run.log");
                                    StreamReader reader = new StreamReader(new FileStream(logfile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                                    msg = reader.ReadToEnd();
                                }
                                catch (Exception ee)
                                {
                                    msg = "Error: Unable to open log file for project '{0}'.  ".format(projName);
                                }
                            }
                            response.WriteString(msg);
                        }
                        else
                        {
                            response.WriteString("Unable to return log for project '{0}':  No such project.  ", projName);
                        }
                    }

                }
                catch (Exception e)
                {
                    WriteLog("Error processing request: {0} -- {1}\r\n{2}".format(e.GetType(), e.Message, e.StackTrace), EventLogEntryType.Error);
                    Listener.HandleException(e);
                    response.StatusCode = 500;
                    response.Close();
                }
            }, TaskCreationOptions.AttachedToParent);

            result.ContinueWith(antecedent =>
            {
                if (result.IsFaulted)
                {
                    var e = antecedent.Exception.InnerException;
                    WriteLog("Error handling commit message: {0} -- {1}\r\n{2}".format(e.GetType(), e.Message, e.StackTrace), EventLogEntryType.Error);
                    Listener.HandleException(e);
                    response.StatusCode = 500;
                    response.Close();
                }
            }, TaskContinuationOptions.OnlyOnFaulted);

            return result;
        }


    }

    public class ListenAgent : Daemon
    {
        private bool initDone = false;
        private Listener listener;
        public Logger Logger;
        public string[] hosts { get; private set; }
        public int[] ports { get; private set; }
        public string postfix { get; private set; }

        public ListenAgent(string handle = null, string[] Hosts = null, int[] Ports = null, Logger logger = null)
        {
            initDone = false;
            postfix = handle ?? "trigger";
            hosts = Hosts ?? new string[] { "*" };
            ports = Ports ?? new int[] { 80 };
            Logger = logger;
        }

        /// <summary>
        /// Initializes the internal listener.
        /// </summary>
        /// <returns>True if successful.  False on error.</returns>
        public bool Init()
        {
            if (initDone)
                try
                { listener.Stop(); }
                catch (Exception e)
                { }

            try
            {
                Listener.Logger = Logger;
                listener = new Listener();
                foreach (var host in hosts)
                    listener.AddHost(host);

                foreach (var port in ports)
                    listener.AddPort(port);

                listener.AddHandler(postfix, new PostHandler(Logger));
                return true;
            }
            catch (Exception e)
            {
                Listener.HandleException(e);
                return false;
            }
        }

        public override bool Start()
        {
            if (!initDone)
                initDone = Init();

            try
            {
                listener.Start();
                return true;
            }
            catch (Exception e)
            {
                Listener.HandleException(e);
                return false;
            }
        }

        public override bool Stop()
        {
            try
            {
                listener.Stop();
                return true;
            }
            catch (Exception e)
            {
                Listener.HandleException(e);
                return false;
            }
        }

    } // End ListenAgent
}
