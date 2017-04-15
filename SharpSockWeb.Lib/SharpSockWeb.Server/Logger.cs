using System;
using System.IO;

namespace SharpSockWeb.Server
{
    public enum LogLevel
    {
        Error = 0,
        Warning,
        Info,
        Connection,
        DataLoad,
        Server,
    }
    public static class Logger
    {
        private static readonly object sLocker = new object();

        private static readonly ConsoleColor[] sLogColors =
        {
            ConsoleColor.Red,ConsoleColor.Yellow,
            ConsoleColor.Blue,ConsoleColor.Green,
            ConsoleColor.Cyan,ConsoleColor.Magenta
        };

        private static readonly string[] sLogNames =
        {
            "[Error] ","[Warning] ",
            "[Info] ", "[Connection] ",
            "[DataLoad] ","[Server] "
        };

        public static bool LogDateTime { get; }

        public static void InitConsole(string title)
        {
            Console.Title = title;
            Console.ForegroundColor = ConsoleColor.White;
        }
        public static void Write(LogLevel logLevel, string format, params object[] objects)
        {
            lock (sLocker)
            {
                if (LogDateTime)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write("[{0}] ", DateTime.Now);
                }
                Console.ForegroundColor = sLogColors[(int)logLevel];
                Console.Write(sLogNames[(int)logLevel]);
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(format, objects);
            }
        }
        public static void Exception(Exception ex)
        {
            string exception = ex.ToString();
            string message = string.Format("[{0}]-----------------{1}{2}{1}", DateTime.Now, Environment.NewLine, exception);

            lock (sLocker)
            {
                File.AppendAllText("EXCEPTIONS.txt", message);
            }

            Write(LogLevel.Error, "An exception was logged{0}{1}", Environment.NewLine, exception);
        }
    }
}