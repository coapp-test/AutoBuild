using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using CoApp.Toolkit.Collections;
using CoApp.Toolkit.Extensions;

namespace AutoBuilder
{
    [XmlRoot(ElementName = "CommandScript", Namespace = "http://coapp.org/automation/build")]
    public class CommandScript
    {
        [XmlArray(IsNullable = false)]
        public List<string> Commands;

        public CommandScript()
        {
            Commands = new List<string>();
        }
        public CommandScript(IEnumerable<string> lines)
        {
            Commands = new List<string>(lines);
        }

        public int Run(string path, string project, XDictionary<string, string> macros)
        {
            ProcessUtility _cmdexe = new ProcessUtility("cmd.exe");
            return Run(_cmdexe, path, project, macros);
        }

        public int Run(ProcessUtility exe, string path, string project, XDictionary<string, string> macros)
        {
            // Reset my working directory.
            Environment.CurrentDirectory = path;
                
            string tmpFile = Path.GetTempFileName();
            File.Move(tmpFile, tmpFile+".bat");
            tmpFile += ".bat";
            FileStream file = new FileStream(tmpFile,FileMode.Open);
            StreamWriter FS = new StreamWriter(file);
            macros.Default = null;

            foreach (string command in Commands)
            {
                FS.WriteLine(command.FormatWithMacros((input) =>
                {
                    string Default = null;
                    if (input.Contains("??"))
                    {
                        var parts = input.Split(new[] { '?' }, StringSplitOptions.RemoveEmptyEntries);
                        Default = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                        input = parts[0];
                    }
                    return (macros[input.ToLower()] ?? Default) ?? input;
                }
                ));
            }

            FS.Close();
            return exe.ExecNoStdInRedirect(@"/c """ + tmpFile + @"""");
        }

        public string Flatten(XDictionary<string, string> macros = null)
        {
            StringBuilder Out = new StringBuilder();
            if (macros == null)
                foreach (string s in Commands) 
                    Out.AppendLine(s);
            else
                foreach (string s in Commands)
                    Out.AppendLine(s.FormatWithMacros((input) =>
                                                          {
                                                              string Default = null;
                                                              if (input.Contains("??"))
                                                              {
                                                                  var parts = input.Split(new[] {'?'},
                                                                                          StringSplitOptions.
                                                                                              RemoveEmptyEntries);
                                                                  Default = parts.Length > 1
                                                                                ? parts[1].Trim()
                                                                                : string.Empty;
                                                                  input = parts[0];
                                                              }
                                                              return (macros[input.ToLower()] ?? Default) ?? String.Empty;
                                                          }));
            
            return Out.ToString();
        }

    }
}
