using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Serialization;
using System.Collections.Generic;
using CoApp.Toolkit.Collections;
using CoApp.Toolkit.Extensions;

namespace AutoBuilder
{
    public delegate void MasterConfigChangeHandler(AutoBuild_config sender);

    [XmlRoot(ElementName = "AutoBuild_config", Namespace = "http://coapp.org/automation/build")]
    public class AutoBuild_config
    {
        private const string DEFAULTROOT = @"C:\AutoBuild\Packages";
        private const string DEFAULTOUTPUT = @"C:\output";

#region XML Serialization methods
        /*
        public string ToXML()
        {
            XmlSerializer S = new XmlSerializer(typeof(AutoBuild_config));
            StringWriter TW = new StringWriter();
            S.Serialize(TW, this);
            return TW.ToString();
        }
        public static string ToXML(ProjectData obj)
        {
            XmlSerializer S = new XmlSerializer(typeof(AutoBuild_config));
            StringWriter TW = new StringWriter();
            S.Serialize(TW, obj);
            return TW.ToString();
        }
        public static AutoBuild_config FromXML(string XMLinput)
        {
            XmlSerializer S = new XmlSerializer(typeof(AutoBuild_config));
            StringReader SR = new StringReader(XMLinput);
            return (AutoBuild_config)S.Deserialize(SR);
        }
        public static AutoBuild_config FromXML(Stream XMLinput)
        {
            XmlSerializer S = new XmlSerializer(typeof(AutoBuild_config));
            return (AutoBuild_config)S.Deserialize(XMLinput);
        }
        */
#endregion

        //Actual class data
        public event MasterConfigChangeHandler Changed;

        [NotPersistable]
        public bool DefaultCleanRepo
        {
            get { return _DefaultCleanRepo; }
            set
            {
                ChangedEvent();
                _DefaultCleanRepo = value;
            }
        }
        [Persistable]
        private bool _DefaultCleanRepo;

        [NotPersistable]
        public bool UseGithubListener
        {
            get { return _UseGithubListener; }
            set
            {
                ChangedEvent();
                _UseGithubListener = value;
            }
        }
        [Persistable]
        private bool _UseGithubListener;

        [NotPersistable]
        public string ProjectRoot
        {
            get { return _ProjectRoot; }
            set
            {
                ChangedEvent();
                _ProjectRoot = value;
            }
        }
        [Persistable]
        private string _ProjectRoot;

        [NotPersistable]
        public string OutputStore
        {
            get { return _OutputStore; }
            set
            {
                ChangedEvent();
                _OutputStore = value;
            }
        }
        [Persistable]
        private string _OutputStore;

        [NotPersistable]
        public int MaxJobs
        {
            get { return _MaxJobs; }
            set
            {
                ChangedEvent();
                _MaxJobs = value;
            }
        }
        [Persistable]
        private int _MaxJobs;

        [NotPersistable]
        public int PreTriggerWait
        {
            get { return _PreTriggerWait; }
            set
            {
                ChangedEvent();
                _PreTriggerWait = value;
            }
        }
        [Persistable]
        private int _PreTriggerWait;

//        [XmlArray(IsNullable = false)]
        [Persistable]
        public XDictionary<string, VersionControl> VersionControlList { get; private set; }

//        [XmlArray(IsNullable = false)]
        [Persistable]
        public XDictionary<string, List<string>> DefaultCommands { get; private set; }

//        [XmlArray(IsNullable = false)]
        [Persistable]
        public XDictionary<string, CommandScript> Commands { get; private set; }

        [Persistable]
        public ObservableCollection<string> DefaultRefs { get; private set; }


        private void ChangedEvent()
        {
            if (Changed != null)
                Changed(this);
        }


        private void DefaultCommandsChanged(IDictionary<string, List<string>> dict)
        {
            ChangedEvent();
        }
        private void VCSChanged(IDictionary<string, VersionControl> dict)
        {
            ChangedEvent();
        }
        private void CommandsChanged(IDictionary<string, CommandScript> dict)
        {
            ChangedEvent();
        }
        private void CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ChangedEvent();
        }


        //Default constructor.  Always good to have one of these.
        public AutoBuild_config()
        {
            _DefaultCleanRepo = true;
            _UseGithubListener = true;
            _ProjectRoot = DEFAULTROOT;
            _OutputStore = DEFAULTOUTPUT;
            _PreTriggerWait = 60 * 1000;

            _MaxJobs = 4;
            VersionControlList = new XDictionary<string, VersionControl>();
            DefaultCommands = new XDictionary<string, List<string>>();
            Commands = new XDictionary<string, CommandScript>();
            DefaultRefs = new ObservableCollection<string>();

            VersionControlList.Changed += VCSChanged;
            DefaultCommands.Changed += DefaultCommandsChanged;
            Commands.Changed += CommandsChanged;
            DefaultRefs.CollectionChanged += CollectionChanged;
        }

        //A copy constructor, because I'm always annoyed when I can't find one.
        public AutoBuild_config(AutoBuild_config source)
        {
            _DefaultCleanRepo = source.DefaultCleanRepo;
            _UseGithubListener = source.UseGithubListener;
            _ProjectRoot = source.ProjectRoot;
            _OutputStore = source.OutputStore;
            _PreTriggerWait = source.PreTriggerWait;

            _MaxJobs = source.MaxJobs;
            VersionControlList = new XDictionary<string, VersionControl>(source.VersionControlList);
            DefaultCommands = new XDictionary<string, List<string>>(source.DefaultCommands);
            Commands = new XDictionary<string, CommandScript>(source.Commands);
            DefaultRefs = new ObservableCollection<string>(source.DefaultRefs);

            VersionControlList.Changed += VCSChanged;
            DefaultCommands.Changed += DefaultCommandsChanged;
            Commands.Changed += CommandsChanged;
            DefaultRefs.CollectionChanged += CollectionChanged;
        }

/*
        //And a stream constructor in case I ever feel I need it.
        public AutoBuild_config(Stream XMLinput) : this(FromXML(XMLinput))
        {
            VersionControlList.Changed += VCSChanged;
            DefaultCommands.Changed += DefaultCommandsChanged;
            Commands.Changed += CommandsChanged;
        }
*/


    }
}
