using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.AccessControl;
using System.Xml.Serialization;
using System.Collections.Generic;
using CoApp.Toolkit.Collections;
using CoApp.Toolkit.Extensions;
using CoApp.Toolkit.Pipes;

namespace AutoBuilder
{
    public delegate void ProjectChangeHandler(ProjectData sender);
    public delegate void AltProjectChangeHandler(string sender);

    [XmlRoot(ElementName = "ProjectData", Namespace = "http://coapp.org/automation/build")]
    public class ProjectData
    {

        #region XML Serialization methods
        /*
        public string ToXML()
        {
            XmlSerializer S = new XmlSerializer(typeof(ProjectData));
            StringWriter TW = new StringWriter();
            S.Serialize(TW, this);
            return TW.ToString();
        }
        public static string ToXML(ProjectData obj)
        {
            XmlSerializer S = new XmlSerializer(typeof(ProjectData));
            StringWriter TW = new StringWriter();
            S.Serialize(TW, obj);
            return TW.ToString();
        }
        public static ProjectData FromXML(string XMLinput)
        {
            XmlSerializer S = new XmlSerializer(typeof(ProjectData));
            StringReader SR = new StringReader(XMLinput);
            return (ProjectData)S.Deserialize(SR);
        }
        public static ProjectData FromXML(Stream XMLinput)
        {
            XmlSerializer S = new XmlSerializer(typeof(ProjectData));
            return (ProjectData)S.Deserialize(XMLinput);
        }
        */
        #endregion

        //Inner classes
        public class CheckoutInfo
        {
            public List<string> PreCmd;
            public List<string> BuildCmd;
            public List<string> ArchiveCmd;

            public CheckoutInfo()
            {
                PreCmd = new List<string>();
                BuildCmd = new List<string>();
                ArchiveCmd = new List<string>();
            }
        }

        //Actual class data
        private BuildHistory History;
        private string Name;

        public event ProjectChangeHandler Changed;
        public event AltProjectChangeHandler Changed2;

        [NotPersistable]
        public bool Enabled
        {
            get { return _Enabled; }
            set
            {
                ChangedEvent();
                _Enabled = value;
            }
        }
        [Persistable]
        private bool _Enabled;

        [NotPersistable]
        public bool KeepCleanRepo
        {
            get { return _KeepCleanRepo; }
            set
            {
                ChangedEvent();
                _KeepCleanRepo = value;
            }
        }
        [Persistable]
        private bool _KeepCleanRepo;

        [NotPersistable]
        public bool AllowConcurrentBuilds
        {
            get { return _AllowConcurrentBuilds; }
            set
            {
                ChangedEvent();
                _AllowConcurrentBuilds = value;
            }
        }
        [Persistable]
        private bool _AllowConcurrentBuilds;

        [NotPersistable]
        public string RepoURL
        {
            get { return _RepoURL; }
            set
            {
                ChangedEvent();
                _RepoURL = value;
            }
        }
        [Persistable]
        private string _RepoURL;

        [NotPersistable]
        public string VersionControl
        {
            get { return _VersionControl; }
            set
            {
                ChangedEvent();
                _VersionControl = value;
            }
        }
        [Persistable]
        private string _VersionControl;

        [Persistable]
        public ObservableCollection<string> WatchRefs { get; private set; }

        [Persistable]
        public XDictionary<string, CheckoutInfo> BuildCheckouts { get; private set; }

        [Persistable]
        public XDictionary<string, CommandScript> Commands { get; private set; }

        [Persistable]
        public ObservableCollection<BuildTrigger> BuildTriggers { get; private set; }

        [Persistable]
        public ObservableCollection<string> PreBuild { get; private set; }

        [Persistable]
        public ObservableCollection<string> Build { get; private set; }

        [Persistable]
        public ObservableCollection<string> PostBuild { get; private set; }

        private void ChangedEvent()
        {
            if (Changed != null)
                Changed(this);
            if (Changed2 != null)
                Changed2(Name);
        }

        void CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ChangedEvent();
        }
        void CommandsChanged(IDictionary<string, CommandScript> dict)
        {
            ChangedEvent();
        }
        void CheckoutsChanged(IDictionary<string, CheckoutInfo> dict)
        {
            ChangedEvent();
        }

        public BuildHistory GetHistory()
        {
            return History;
        }

        /// <summary>
        /// Sets the internal name of this project.
        /// This is only used as an internal reference for lookup by the service.
        /// </summary>
        /// <param name="newName"></param>
        public void SetName(string newName)
        {
            Name = newName;
        }

        /// <summary>
        /// Will attempt to load the build history from a file.
        /// If the file cannot be found, the string will be assumed to contain Xml data and an attempt will be made to parse it for history data.
        /// </summary>
        /// <param name="XmlFile"></param>
        /// <returns>True if the History object has changed.</returns>
        public bool LoadHistory(string XmlFile)
        {
            /*
            if (File.Exists(XmlFile))
                return History.ImportHistory(new FileStream(XmlFile, FileMode.Open, FileAccess.Read));
            
            //This means we don't see a file by that name.  Maybe it's just an Xml string?
            return History.ImportHistory(XmlFile);
             */
            if (XmlFile == null || XmlFile.Equals(String.Empty))
            {
                if (History == null)
                {
                    History = new BuildHistory();
                    History.Builds.CollectionChanged += CollectionChanged;
                    return true;
                }
                return false;
            }
            
            if (File.Exists(XmlFile))
            {
                UrlEncodedMessage uem = new UrlEncodedMessage(File.ReadAllText(XmlFile), AutoBuild.SerialSeperator, true);
                History = uem.DeserializeTo<BuildHistory>() ?? new BuildHistory();
                History.Builds.CollectionChanged += CollectionChanged;
                return true;
            }

            History = new BuildHistory();
            UrlEncodedMessage UEM = new UrlEncodedMessage(XmlFile, AutoBuild.SerialSeperator, true);
            UEM.DeserializeTo(History);
            History.Builds.CollectionChanged += CollectionChanged;
            return true;
        }


        /// <summary>
        /// Loads a history directly.  This will do nothing if a history has already been loaded for this project.
        /// </summary>
        /// <param name="history">The BuildHistory object to attach to this project.</param>
        /// <returns>True if the history was attached.  False otherwise.</returns>
        public bool LoadHistory(BuildHistory history)
        {
            if (History != null)
                return false;
            History = history;
            History.Builds.CollectionChanged += CollectionChanged;
            return true;
        }

        //Default constructor.  Always good to have one of these.
        public ProjectData()
        {
            Enabled = false;
            KeepCleanRepo = true;
            RepoURL = String.Empty;
            WatchRefs = new ObservableCollection<string>();
            WatchRefs.CollectionChanged += CollectionChanged;
            BuildCheckouts = new XDictionary<string, CheckoutInfo>();
            BuildCheckouts.Changed += CheckoutsChanged;
            Commands = new XDictionary<string, CommandScript>();
            Commands.Changed += CommandsChanged;
            BuildTriggers = new ObservableCollection<BuildTrigger>();
            BuildTriggers.CollectionChanged += CollectionChanged;
            Build = new ObservableCollection<string>();
            Build.CollectionChanged += CollectionChanged;
            PreBuild = new ObservableCollection<string>();
            PreBuild.CollectionChanged += CollectionChanged;
            PostBuild = new ObservableCollection<string>();
            PostBuild.CollectionChanged += CollectionChanged;
            LoadHistory(String.Empty);
        }

        //A copy constructor, because I'm always annoyed when I can't find one.
        public ProjectData(ProjectData source)
        {
            Enabled = source.Enabled;
            KeepCleanRepo = source.KeepCleanRepo;
            RepoURL = source.RepoURL;
            WatchRefs = new ObservableCollection<string>(source.WatchRefs);
            BuildCheckouts = new XDictionary<string, CheckoutInfo>(source.BuildCheckouts);
            Commands = new XDictionary<string, CommandScript>(source.Commands);
            BuildTriggers = new ObservableCollection<BuildTrigger>(source.BuildTriggers);
            Build = new ObservableCollection<string>(source.Build);
            PreBuild = new ObservableCollection<string>(source.PreBuild);
            PostBuild = new ObservableCollection<string>(source.PostBuild);
            WatchRefs.CollectionChanged += CollectionChanged;
            BuildCheckouts.Changed += CheckoutsChanged;
            Commands.Changed += CommandsChanged;
            BuildTriggers.CollectionChanged += CollectionChanged;
            Build.CollectionChanged += CollectionChanged;
            PreBuild.CollectionChanged += CollectionChanged;
            PostBuild.CollectionChanged += CollectionChanged;
            LoadHistory(String.Empty);
        }

/*
        //And a stream constructor in case I ever feel I need it.
        public ProjectData(Stream XMLinput) : this(FromXML(XMLinput))
        {
            WatchRefs.CollectionChanged += CollectionChanged;
            BuildCheckouts.Changed += CheckoutsChanged;
            Commands.Changed += CommandsChanged;
            BuildTriggers.CollectionChanged += CollectionChanged;
            Build.CollectionChanged += CollectionChanged;
            PreBuild.CollectionChanged += CollectionChanged;
            PostBuild.CollectionChanged += CollectionChanged;
        }
*/
    }
}
