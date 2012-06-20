using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using CoApp.Toolkit.Collections;
using System.Collections.ObjectModel;
using CoApp.Toolkit.Extensions;


namespace AutoBuilder
{
    [XmlRoot(ElementName = "Tool", Namespace = "http://coapp.org/automation/build")]
    public class Tool
    {
        [XmlElement] 
        public string Name;
        [XmlElement] 
        public string Path;
        [XmlArray(IsNullable = false)] 
        public string[] Switches;

        public Tool(string name, string path, string[] switches)
        {
            Name = name;
            Path = File.Exists(path) ? path :
                File.Exists(System.IO.Path.GetFullPath(path)) ? System.IO.Path.GetFullPath(path) :
                null;
            Switches = switches;
        }

        protected Tool()
        {
            Name = String.Empty;
            Path = String.Empty;
            Switches =  new string[0];
        }
    }

    [XmlRoot(ElementName = "VersionControl", Namespace = "http://coapp.org/automation/build")]
    public class VersionControl
    {
        [XmlElement]
        public string Name;
        [XmlElement]
        public Tool Tool;
        [XmlElement]
        public XDictionary<string, string> Properties;

        public VersionControl(string name, Tool tool = null, IDictionary<string,string> properties = null)
        {
            if (name == null)
                throw new ArgumentNullException("name", "VersionControl.Name cannot be null.");
            Name = name;
            Tool = tool;
            Properties = properties == null
                              ? new XDictionary<string, string>()
                              : new XDictionary<string, string>(properties);
        }

        protected VersionControl()
        {
            Name = String.Empty;
            Tool = (Tool)Activator.CreateInstance(typeof(Tool), true);
            Properties = new XDictionary<string, string>();
        }
    }

    [XmlRoot(ElementName = "BuildTrigger", Namespace = "http://coapp.org/automation/build")]
    public abstract class BuildTrigger
    {
        [XmlAttribute] 
        public string Type;

        public abstract void Init();
    }

    public class BuildStatus : IEquatable<BuildStatus>
    {
        [Persistable]
        private bool Locked = true;

        [Persistable]
        public string Result;

        [Persistable] 
        public DateTime TimeStamp;

        [NotPersistable] 
        public string LogData;

        /// <summary>
        /// This will set the result for this BuildStatus iff the status is not locked and Result is currently set to null.
        /// </summary>
        /// <param name="NewResult"></param>
        /// <returns>True if the Result has changed.  False otherwise.</returns>
        public bool SetResult(string NewResult)
        {
            if (Locked)
                return false;
            if (Result == null)
            {
                Result = NewResult;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Changes the current rusult unless this BuildStatus is locked.
        /// </summary>
        /// <param name="NewResult"></param>
        /// <returns></returns>
        public string ChangeResult(string NewResult)
        {
            if (Locked)
                return null;
            string prev = Result;
            Result = NewResult;
            return prev;
        }

        public void Lock()
        {
            Locked = true;
        }
        
        public void Append(string data)
        {
            LogData = LogData == null ? data : LogData + Environment.NewLine + data;
        }

        public BuildStatus()
        {
            Locked = false;
            TimeStamp = DateTime.UtcNow;
        }

        public bool Equals(BuildStatus other)
        {
            return ((other.TimeStamp.Equals(TimeStamp)) && (other.Result.Equals(Result, StringComparison.CurrentCultureIgnoreCase)));
        }
    }

    [XmlRoot(ElementName = "BuildHistory", Namespace = "http://coapp.org/automation/build")]
    public class BuildHistory
    {
        [XmlArray(IsNullable = false)] 
        public ObservableCollection<BuildStatus> Builds;

        /// <summary>
        /// This will populate the Builds list from an XML input stream.
        /// NOTE: This will only make changes to Builds if Builds is empty or null.
        /// </summary>
        /// <param name="XmlStream">Stream containing XML data</param>
        /// <returns>True if Builds was altered.</returns>
        /*
        public bool ImportHistory(Stream XmlStream)
        {
            if (!(Builds == null || Builds.Count <= 0))
                return false;
            try
            {
                XmlSerializer S = new XmlSerializer(typeof (BuildHistory));
                StreamReader SR = new StreamReader(XmlStream);
                Builds = ((BuildHistory) S.Deserialize(SR)).Builds;
                Builds.Sort((A, B) => (A.TimeStamp.CompareTo(B.TimeStamp)));
                return true;
            }
            catch(Exception e)
            {
                return false;
            }
        }
        */

        /// <summary>
        /// This will populate the Builds list from an XML input stream.
        /// NOTE: This will only make changes to Builds if Builds is empty or null.
        /// </summary>
        /// <param name="XmlString">Stream containing XML data</param>
        /// <returns>True if Builds was altered.</returns>
        public bool ImportHistory(string XmlString)
        {
            /*
            if (!(Builds == null || Builds.Count <= 0))
                return false;
            try
            {
                XmlSerializer S = new XmlSerializer(typeof(BuildHistory));
                StringReader SR = new StringReader(XmlString);
                Builds = ((BuildHistory)S.Deserialize(SR)).Builds;
                Builds.Sort((A, B) => (A.TimeStamp.CompareTo(B.TimeStamp)));
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
            */
            return false;
        }

        public string ExportXml()
        {
            XmlSerializer S = new XmlSerializer(typeof(BuildHistory));
            StringWriter TW = new StringWriter();
            S.Serialize(TW, this);
            return TW.ToString();
        }

        /// <summary>
        /// This will add a BuildStatus to the history.
        /// </summary>
        /// <param name="status"></param>
        public bool Append(BuildStatus status)
        {
            if (Builds.Contains(status))
                return false;
            Builds = Builds ?? new ObservableCollection<BuildStatus>();
            Builds.Add(status);
            return true;
        }

        /// <summary>
        /// This will add a BuildStatus to the history.
        /// NOTE:  This will also lock the BuildStatus against further changes!
        /// </summary>
        /// <param name="status"></param>
        public bool AppendAndLock(BuildStatus status)
        {
            if (Builds.Contains(status))
                return false;
            status.Lock();
            Builds = Builds ?? new ObservableCollection<BuildStatus>();
            Builds.Add(status);
            return true;
        }

        public BuildHistory()
        {
            Builds = new ObservableCollection<BuildStatus>();
        }

        /*
        public BuildHistory(string Xml)
        {
            //ImportHistory(Xml);
        }
        
        
        public BuildHistory(Stream Xml)
        {
            ImportHistory(Xml);
        }
        */
    }

    public static class XDictionaryExtensions
    {
        
        /// <summary>
        /// Finds the first key which contains a value with the provided object.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dict">the dictionary to look through</param>
        /// <param name="searchValue">object to look for in the dictionary</param>
        /// <returns>The first key found which contains the searchValue in its value.  Returns null if no match is found.</returns>
        public static object FindKey<TKey, TValue>(this XDictionary<TKey, TValue> dict, object searchValue)
        {
            foreach (KeyValuePair<TKey, TValue> pair in dict)
            {
                dynamic val = pair.Value;
                if (val is IEnumerable<TValue>)
                    if (val.Contains(searchValue))
                        return pair.Key;
                if (val.Equals(searchValue))
                    return pair.Key;
            }
            return null;
        }

        /// <summary>
        /// Finds all keys which contain the searchValue in their value.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dict">dictionary to look through</param>
        /// <param name="searchValue">object to serch for</param>
        /// <returns>An enumerable set containing all keys located, or null if no matches were found.</returns>
        public static IEnumerable<TKey> FindKeys<TKey, TValue>(this XDictionary<TKey, TValue> dict, TValue searchValue)
        {
            List<TKey> found = new List<TKey>();

            foreach (KeyValuePair<TKey, TValue> pair in dict)
            {
                dynamic val = pair.Value;
                if (val is IEnumerable<TValue>)
                    if (val.Contains(searchValue))
                        found.Add(pair.Key);
                if (val.Equals(searchValue))
                    found.Add(pair.Key);
            }
            
            return found.Count <= 0 ? null : found;
        }
    }

    public abstract class Daemon
    {
        public abstract bool Start();
        public abstract bool Stop();
    }

}
