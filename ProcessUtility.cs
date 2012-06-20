/*
 * This code was yanked directly from CoApp.Toolkit.Utility.ProcessUtility.
 * It was then modified to allow manual reset/reassignment of the StdOut and StdErr outputs.
 */

using System.ComponentModel;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using CoApp.Toolkit.Extensions;
using CoApp.Toolkit.Win32;

namespace AutoBuilder
{
    /// <summary>
    /// Wraps a Windows process
    /// </summary>
    /// <see cref="System.Diagnostics.Process"/>
    public class ProcessUtility
    {
        private Process currentProcess;
        /// <summary>
        /// Path to this process' executable binary
        /// </summary>
        public readonly string Executable;

        private StringBuilder sErr = new StringBuilder();
        private StringBuilder sOut = new StringBuilder();

        private StreamWriter AllOut = null;

        public void AssignOutputStream(StreamWriter SW)
        {
            AllOut = SW;
            AllOut.AutoFlush = true;
        }

        public void ResetStdOut(StringBuilder newOut = null)
        {
            sOut = newOut ?? new StringBuilder();
        }

        public void ResetStdErr(StringBuilder newErr = null)
        {
            sErr = newErr ?? new StringBuilder();
        }

        public bool ConsoleOut { get; set; }

        /// <summary>
        /// This process' exit code (or zero)
        /// </summary>
        /// <remarks>If it has not exited yet or doesn't exist, this will return 0.</remarks>
        public int ExitCode { get { return (currentProcess != null && currentProcess.HasExited) ? currentProcess.ExitCode : 0; } }

        private void CurrentProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            sErr.AppendLine(e.Data);
            if (ConsoleOut)
                Console.Out.WriteLine(e.Data);
            if (AllOut != null)
                AllOut.WriteLine(e.Data);
        }

        private void CurrentProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            sOut.AppendLine(e.Data);
            if (ConsoleOut)
                Console.Error.WriteLine(e.Data);
            if (AllOut != null)
                AllOut.WriteLine(e.Data);
        }

        private void CurrentProcess_Exited(object sender, EventArgs e)
        {
            Failed = currentProcess.ExitCode != 0;
        }

        /// <summary>
        /// True, if the exit code was non-zero. False otherwise.
        /// </summary>
        public bool Failed { get; set; }

        public bool IsRunning
        {
            get
            {
                return currentProcess != null ? !currentProcess.HasExited : false;
            }
        }

        /// <summary>
        /// Attempt to stop this process
        /// </summary>
        public void Kill()
        {
            if (IsRunning)
            {
                currentProcess.Kill();
            }
        }

        /// <summary>
        /// Writes something to this process' standard input
        /// </summary>
        /// <param name="text">Text to write to this process</param>
        public void SendToStandardIn(string text)
        {
            if (!string.IsNullOrEmpty(text) && IsRunning)
                currentProcess.StandardInput.Write(text);
        }

        /// <summary>
        /// Returns whatever this process just printed to standard output
        /// </summary>
        public string StandardOut
        {
            get
            {
                return sOut.ToString();
            }
        }

        /// <summary>
        /// Returns everything this process printed to standard error
        /// </summary>
        public string StandardError
        {
            get
            {
                return sErr.ToString();
            }
        }

        /// <summary>
        /// Creates a new ProcessUtility.
        /// </summary>
        /// <param name="filename">Path to the executable</param>
        /// <exception cref="ArgumentNullException">The filename is not permitted to be null</exception>
        public ProcessUtility(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentNullException("filename", "Filename not permitted to be null");
            }
            Executable = filename;
        }

        /// <summary>
        /// Wait indefinately until the associated process has exited.
        /// </summary>
        public void WaitForExit()
        {
            if (IsRunning)
                currentProcess.WaitForExit();
        }

        /// <summary>
        /// Wait for a specific number of milliseconds for the associated process to exit.
        /// </summary>
        /// <param name="milliseconds">Time to wait in milliseconds</param>
        public void WaitForExit(int milliseconds)
        {
            if (IsRunning)
                currentProcess.WaitForExit(milliseconds);
        }

        /// <summary>
        /// Safely attach to the console of the associated process
        /// </summary>
        public void AttachToConsoleForProcess()
        {
            if (!ConsoleExtensions.IsConsole)
            {
                Kernel32.AttachConsole(currentProcess.Id);
            }
        }

        /// <summary>
        /// Run the associated process asynchronously
        /// </summary>
        /// <param name="args">Command line parameters for the associated process</param>
        public void ExecAsync(string[] args)
        {
            var commandLine = new StringBuilder();
            foreach (var arg in args)
            {
                commandLine.AppendFormat(@"""{0}"" ", arg);
            }
            ExecAsync(commandLine.ToString());
        }

        /// <summary>
        /// Run the associated process asynchronously and redirect input/output
        /// </summary>
        /// <param name="arguments">Command line parameters as formatted string</param>
        /// <param name="args">Zero or more strings to format</param>
        public void ExecAsync(string arguments, params string[] args)
        {
            if (IsRunning)
                throw new InvalidAsynchronousStateException("Process is currently running.");

            Failed = false;

            currentProcess = new Process { StartInfo = { FileName = Executable, Arguments = string.Format(arguments, args), WorkingDirectory = Environment.CurrentDirectory, RedirectStandardError = true, RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, WindowStyle = ConsoleExtensions.IsConsole ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden } };

            currentProcess.ErrorDataReceived += CurrentProcess_ErrorDataReceived;
            currentProcess.OutputDataReceived += CurrentProcess_OutputDataReceived;
            currentProcess.Exited += CurrentProcess_Exited;

            currentProcess.Start();
            currentProcess.BeginErrorReadLine();
            currentProcess.BeginOutputReadLine();
        }

        /// <summary>
        /// Run the associated process asynchronously
        /// </summary>
        /// <param name="arguments">Command line parameters as formatted string</param>
        /// <param name="args">Zero or more strings to format</param>
        public void ExecAsyncNoRedirections(string arguments, params string[] args)
        {
            if (IsRunning)
                throw new InvalidAsynchronousStateException("Process is currently running.");

            Failed = false;

            currentProcess = new Process { StartInfo = { FileName = Executable, Arguments = string.Format(arguments, args), WorkingDirectory = Environment.CurrentDirectory, RedirectStandardError = false, RedirectStandardInput = false, RedirectStandardOutput = false, UseShellExecute = false, WindowStyle = ConsoleExtensions.IsConsole ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden } };

            currentProcess.Exited += CurrentProcess_Exited;

            currentProcess.Start();
        }

        /// <summary>
        /// Run the associated process asynchronously and redirect output
        /// </summary>
        /// <param name="arguments">Command line parameters as formatted string</param>
        /// <param name="args">Zero or more strings to format</param>
        public void ExecAsyncNoStdInRedirect(string arguments, params string[] args)
        {
            if (IsRunning)
                throw new InvalidAsynchronousStateException("Process is currently running.");

            Failed = false;

            currentProcess = new Process { StartInfo = { FileName = Executable, Arguments = string.Format(arguments, args), WorkingDirectory = Environment.CurrentDirectory, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, WindowStyle = ConsoleExtensions.IsConsole ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden } };
            currentProcess.ErrorDataReceived += CurrentProcess_ErrorDataReceived;
            currentProcess.OutputDataReceived += CurrentProcess_OutputDataReceived;
            currentProcess.Exited += CurrentProcess_Exited;

            currentProcess.Start();
            currentProcess.BeginErrorReadLine();
            currentProcess.BeginOutputReadLine();
        }

        /// <summary>
        /// Run the associated process synchronously
        /// </summary>
        /// <param name="args">Command line parameters for the associated process</param>
        /// <returns>The exit code of the associated process</returns>
        public int Exec(string[] args)
        {
            var commandLine = new StringBuilder();
            foreach (var arg in args)
            {
                commandLine.AppendFormat(@"""{0}"" ", arg);
            }
            return Exec(commandLine.ToString());
        }

        /// <summary>
        /// Run the associated process synchronously and redirect input/output
        /// </summary>
        /// <param name="arguments">Command line parameters as formatted string</param>
        /// <param name="args">Zero or more strings to format</param>
        /// <returns>The exit code of the associated process</returns>
        public int Exec(string arguments, params string[] args)
        {
            try
            {
                ExecAsync(arguments, args);
                WaitForExit();
            }
            catch (Exception e)
            {
                currentProcess = null;
                sErr.AppendFormat("Failed to execute program [{0}]\r\n   {1}", Executable, e.Message);
                return 100;
            }

            return currentProcess.ExitCode;
        }

        /// <summary>
        /// Run the associated process synchronously and redirect input/output
        /// </summary>
        /// <param name="arguments">Command line parameters as formatted string</param>
        /// <param name="args">Zero or more strings to format</param>
        /// <returns>The exit code of the associated process</returns>
        public int ExecNoStdInRedirect(string arguments, params string[] args)
        {
            try
            {
                ExecAsyncNoStdInRedirect(arguments, args);
                WaitForExit();
            }
            catch (Exception e)
            {
                currentProcess = null;
                sErr.AppendFormat("Failed to execute program [{0}]\r\n   {1}", Executable, e.Message);
                return 100;
            }

            return currentProcess.ExitCode;
        }

        /// <summary>
        /// Run the associated process synchronously
        /// </summary>
        /// <param name="arguments">Command line parameters as formatted string</param>
        /// <param name="args">Zero or more strings to format</param>
        public int ExecNoRedirections(string arguments, params string[] args)
        {
            try
            {
                ExecAsyncNoRedirections(arguments, args);
                WaitForExit();
            }
            catch (Exception e)
            {
                currentProcess = null;
                sErr.AppendFormat("Failed to execute program [{0}]\r\n   {1}", Executable, e.Message);
                return 100;
            }

            return currentProcess.ExitCode;
        }

        /// <summary>
        /// Run the associated process synchronously with a given input
        /// </summary>
        /// <param name="stdIn">Input to write to the associated process after starting</param>
        /// <param name="arguments">Command line parameters as formatted string</param>
        /// <param name="args">Zero or more strings to format</param>
        public int ExecWithStdin(string stdIn, string arguments, params string[] args)
        {
            try
            {
                ExecAsync(arguments, args);
                SendToStandardIn(stdIn);
                WaitForExit();
            }
            catch (Exception e)
            {
                currentProcess = null;
                sErr.AppendFormat("Failed to execute program [{0}]\r\n   {1}", Executable, e.Message);
                return 100;
            }

            return currentProcess.ExitCode;
        }
    }
}
