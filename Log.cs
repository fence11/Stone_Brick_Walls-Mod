using System;
using System.Linq;
using BepInEx.Logging;

namespace PieceManagerModTemplate
{
    // Writes "[Stone_Brick_Walls]: message" as-is to the BepInEx console and LogOutput.log,
    // bypassing BepInEx's own "[Level:Source]" prefix.
    internal static class Log
    {
        private const string Tag = "[Stone_Brick_Walls]: ";

        public static void Info(string message) => Write(message, ConsoleColor.Gray, false);
        public static void Warning(string message) => Write(message, ConsoleColor.Yellow, false);
        public static void Error(string message) => Write(message, ConsoleColor.Red, true);
        public static void Unknown(string where, Exception e) => Error($"Unknown error in {where}: {e}");

        private static void Write(string message, ConsoleColor color, bool isError)
        {
            string line = Tag + message;
            try
            {
                bool written = false;
                // BepInEx points System.Console.Out at its console window when the console is enabled.
                if (System.Console.Out != System.IO.TextWriter.Null)
                {
                    ConsoleColor previous = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = color;
                    System.Console.Out.WriteLine(line);
                    System.Console.ForegroundColor = previous;
                    written = true;
                }

                foreach (DiskLogListener disk in Logger.Listeners.OfType<DiskLogListener>())
                {
                    if (disk.LogWriter == null) continue;
                    disk.LogWriter.WriteLine(line);
                    written = true;
                }

                if (!written) Fallback(line, isError);
            }
            catch (Exception)
            {
                Fallback(line, isError); // logging must never crash the mod
            }
        }

        private static void Fallback(string line, bool isError)
        {
            if (isError) UnityEngine.Debug.LogError(line);
            else UnityEngine.Debug.Log(line);
        }
    }
}
