using System;
using System.IO;
using System.Linq;
using System.Threading;
using static System.Console;

namespace FileCheker
{
    class Program
    {
        static void Main(string[] args)
        {
            // To catch unhandled exceptions

            AppDomain.CurrentDomain.UnhandledException += (sender, exc) =>
            {
                WriteLine($"Unhandled exception: {exc.ExceptionObject}");
                Environment.Exit(1);
            };


            // Checking arguments...

            if (args.Length < 2)
            {
                WriteLine("Invalid count of arguments.\nUsage: filechecker.exe folder mask");
                return;
            }

            var path = Path.GetFullPath(args[0]);
            var mask = args[1];

            if (!Directory.Exists(path))
            {
                WriteLine($"Folder {path} does not exists.");
                return;
            }

            var invalidMaskChars = Path.GetInvalidFileNameChars().Where(x => x != '*' && x != '?');
            if (invalidMaskChars.Any(x => mask.Contains(x)))
            {
                WriteLine("Invalid characters in file mask");
                return;
            }

            WriteLine("Starting monitoring. Press any key to exit.");


            // Starting the process...

            try
            {
                Worker.StartMonitoring(path, mask);
            }
            catch (InvalidOperationException exc)
            {
                WriteLine(exc.Message);
                return;
            }


            // Wait for key press and then send request to stop the process...

            ReadKey();

            Worker.Stop();
        }
    }
}