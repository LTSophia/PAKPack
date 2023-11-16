using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using AtlusFileSystemLibrary;
using AtlusFileSystemLibrary.Common.IO;
using AtlusFileSystemLibrary.FileSystems.PAK;

namespace PAKPack
{
    internal static class Program
    {
        public static IReadOnlyDictionary< string, ICommand > Commands { get; } = new Dictionary<string, ICommand>
        {
            { "pack", new PackCommand() },
            { "packoradd", new PackOrAddCommand() },
            { "unpack", new UnpackCommand() },
            { "replace", new ReplaceCommand() },
            { "add", new AddCommand() },
            { "list", new ListCommand() }
        };

        public static IReadOnlyDictionary< string, FormatVersion > FormatsByName { get; } = new Dictionary< string, FormatVersion >
        {
            { "v1", FormatVersion.Version1 },
            { "v2", FormatVersion.Version2 },
            { "v2be", FormatVersion.Version2BE },
            { "v3", FormatVersion.Version3 },
            { "v3be", FormatVersion.Version3BE }
        };

        public static IReadOnlyDictionary<FormatVersion, string> FormatVersionEnumToString { get; } = new Dictionary<FormatVersion, string>
        {
            { FormatVersion.Version1, "v1"},
            { FormatVersion.Version2, "v2" },
            { FormatVersion.Version2BE, "v2be" },
            { FormatVersion.Version3, "v3" },
            { FormatVersion.Version3BE, "v3be" }
        };
        // Define an extension method for type System.Process that returns the command 
        // line via WMI.
        private static string GetCommandLine(this Process process)
        {
            string cmdLine = null;
            using (var searcher = new ManagementObjectSearcher(
              $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
            {
                // By definition, the query returns at most 1 match, because the process 
                // is looked up by ID (which is unique by definition).
                using (var matchEnum = searcher.Get().GetEnumerator())
                {
                    if (matchEnum.MoveNext()) // Move to the 1st item.
                    {
                        cmdLine = matchEnum.Current["CommandLine"]?.ToString();
                    }
                }
            }
            if (cmdLine == null)
            {
                // Not having found a command line implies 1 of 2 exceptions, which the
                // WMI query masked:
                // An "Access denied" exception due to lack of privileges.
                // A "Cannot process request because the process (<pid>) has exited."
                // exception due to the process having terminated.
                // We provoke the same exception again simply by accessing process.MainModule.
                var dummy = process.MainModule; // Provoke exception.
            }
            return cmdLine;
        }
    
    private static void Main( string[] args )
        {
            if ( args.Length == 0 )
            {
                Console.WriteLine( "PAKPack 1.4 - A PAK pack/unpacker made by TGE (2018)\n" +
                                   "\n" +
                                   "Usage:\n" +
                                   "  PAKPack <command>\n" +
                                   "\n" +
                                   "Commands:\n" +
                                   "\n" +
                                   "    pack        Packs the given input into a PAK file and outputs it to the specified output path.\n" +
                                   "        Usage:\n" +
                                   "            pack <input directory path> <format> [output file path]\n" +
                                   "\n" +
                                   "    unpack      Unpacks the given input PAK file and outputs it to the specified output directory.\n" +
                                   "        Usage:\n" +
                                   "            unpack <input file path> [output directory path]\n" +
                                   "\n" +
                                   "    replace     Replaces the specified file(s) with the contents of the specified input\n" +
                                   "        Usage:\n" +
                                   "            replace <input pak file path> <file name to replace> <file path> [output file path]\n" +
                                   "            replace <input pak file path> <path to file directory> [output file path]\n" +
                                   "\n" +
                                   "    add         Adds or replaces the specified file(s) with the contents of the specified input\n" +
                                   "        Usage:\n" +
                                   "            add <input pak file path> <file name to add/replace> <file path> [output file path]\n" +
                                   "            add <input pak file path> <path to file directory> [output file path]\n" +
                                   "\n" +
                                   "    list        Lists the files contained in the pak file\n" +
                                   "        Usage:\n" +
                                   "            list <input pak file path>\n" +
                                   "\n" );
                return;
            }
            if ( !Commands.TryGetValue( args[ 0 ], out var command ) )
            {
                Console.WriteLine( "Invalid command specified." );
                return;
            }
            //Thread.Sleep(1);
            if (args[0] == "packoradd")
            {
                var process = CheckRunning.AlreadyRunning();
                foreach (var p in process)
                {
                    string gcl = GetCommandLine( p );
                    string[] pargs = Regex.Matches(gcl, @"[\""].+?[\""]|[^ ]+")
                                        .Cast<Match>()
                                        .Select(m => m.Value)
                                        .ToArray();
                    var newargs = new List<string>();
                    for (int i = 0; i < args.Length; i++)
                        newargs.Add(args[i]);
                    for (int i = 3; i < pargs.Length; i++)
                    {
                        newargs.Add(pargs[i]);
                    }
                    args = newargs.ToArray();
                }
                foreach(var p in process)
                    p.Kill();

            }
            if (command.Execute(args))
            {
                Console.WriteLine("Command executed successfully.");
            }
        }
    }
    internal class CheckRunning
    {
        public static List<Process> AlreadyRunning()
        {
            var otherProcess = new List<Process>();
            try
            {
                // Getting collection of process  
                Process currentProcess = Process.GetCurrentProcess();

                // Check with other process already running   
                foreach (var p in Process.GetProcesses())
                {
                    if (p.Id != currentProcess.Id) // Check running process   
                    {
                        if (p.ProcessName.Equals(currentProcess.ProcessName) && p.MainModule.FileName.Equals(currentProcess.MainModule.FileName))
                        {
                            otherProcess.Add(p);
                        }
                    }
                }
            }
            catch { }
            return otherProcess;
        }
    }
    internal interface ICommand
    {
        bool Execute( string[] args );
    }

    internal class PackCommand : ICommand
    {
        public bool Execute( string[] args )
        {

            if ( args.Length < 3 )
            {
                Console.WriteLine( "Expected at least 2 arguments" );
                return false;
            }
            var inputPath = args[ 1 ];
            if ( !Directory.Exists( inputPath ) )
            {
                Console.WriteLine( "Input directory doesn't exist" );
                return false;
            }

            var formatName = args[ 2 ];
            if ( !Program.FormatsByName.TryGetValue( formatName, out var format ) )
            {
                Console.WriteLine( "Invalid format specified." );
                return false;
            }

            var outputPath = inputPath;

            var tempFolder = inputPath + ".bak";
            
            File.Move(inputPath, tempFolder);

            if ( args.Length > 3 )
                 outputPath = args[ 3 ];

            using ( var pak = new PAKFileSystem( format ) )
            {
                foreach ( string file in Directory.EnumerateFiles(tempFolder, "*.*", SearchOption.AllDirectories ) )
                {
                    Console.WriteLine( $"Adding {file}" );

                    pak.AddFile( file.Substring(tempFolder.Length )
                                        .Trim( Path.DirectorySeparatorChar )
                                        .Replace( "\\", "/" ),
                                    file, ConflictPolicy.Ignore );
                }

                Console.WriteLine( $"Saving...." );
                pak.Save( outputPath );
            }

            return true;
        }
    }

    internal class UnpackCommand : ICommand
    {
        public bool Execute( string[] args )
        {
            if ( args.Length < 2 )
            {
                Console.WriteLine( "Expected at least 1 argument." );
                return false;
            }

            var inputPath = args[1];
            if ( !File.Exists( inputPath ) )
            {
                Console.WriteLine( "Input file doesn't exist." );
                return false;
            }

            var outputPath = inputPath;
            if ( args.Length > 2 )
                outputPath = args[2];
            var tempPath = Path.Combine(Path.GetDirectoryName(inputPath), Path.GetRandomFileName());

            if ( !PAKFileSystem.TryOpen( inputPath, out var pak ) )
            {
                return false;
            }

            using ( pak )
            {
                if (pak.EnumerateFileSystemEntries().Count() == 0)
                    return false;

                var firstPakFilename = pak.EnumerateFileSystemEntries().FirstOrDefault();

                if (!Regex.IsMatch(firstPakFilename, @"^[A-Z0-9\.\\/_-]+$", RegexOptions.IgnoreCase) || firstPakFilename.Length <= 4)
                    return false;

                Console.WriteLine($"PAK format version: {Program.FormatVersionEnumToString[pak.Version]}");

                Directory.CreateDirectory(tempPath);

                foreach ( string file in pak.EnumerateFiles() )
                {
                    var normalizedFilePath = file.Replace("../", ""); // Remove backwards relative path
                    if (!Regex.IsMatch(file, @"^[A-Z0-9\.\\/_-]+$", RegexOptions.IgnoreCase) || file.Length <= 4)
                        return false;
                    using (var stream = FileUtils.Create(Path.Combine(tempPath, normalizedFilePath)))
                    using (var inputStream = pak.OpenFile(file))
                    {
                        Console.WriteLine($"Extracting {normalizedFilePath}");
                        inputStream.CopyTo(stream);
                    }
                }
            }

            File.Move( inputPath, Path.GetFullPath(inputPath) + ".bak" );

            File.Delete( inputPath );

            Directory.Move( tempPath, outputPath );

            return true;
        }
    }

    internal abstract class AddOrReplaceCommand : ICommand
    {
        protected static bool Execute( string[] args, bool allowAdd )
        {
            if ( args.Length < 3 )
            {
                Console.WriteLine( "Expected at least 2 arguments." );
                return false;
            }

            var inputPath = args[1];
            if ( !File.Exists( inputPath ) )
            {
                Console.WriteLine( "Input file doesn't exist." );
                return false;
            }

            if ( !PAKFileSystem.TryOpen( inputPath, out var pak ) )
            {
                Console.WriteLine( "Invalid PAK file." );
                return false;
            }

            string outputPath = inputPath;

            if ( Directory.Exists( args[2] ) )
            {
                var directoryPath = args[2];

                if ( args.Length > 3 )
                {
                    outputPath = args[3];
                }

                using ( pak )
                {
                    foreach ( string file in Directory.EnumerateFiles( directoryPath, "*.*", SearchOption.AllDirectories ) )
                    {
                        Console.WriteLine( $"{( pak.Exists(file) ? "Replacing" : "Adding" )} {file}" );
                        pak.AddFile( file.Substring( directoryPath.Length )
                                         .Trim( Path.DirectorySeparatorChar )
                                         .Replace( "\\", "/" ),
                                     file, ConflictPolicy.Replace );
                    }

                    Console.WriteLine( "Saving..." );
                    pak.Save( outputPath );
                }
            }
            else
            {
                if ( args.Length > 4 )
                {
                    outputPath = args[4];
                }

                using ( pak )
                {
                    var entryName = args[2];
                    var entryExists = pak.Exists( entryName );

                    if ( !allowAdd && !entryExists )
                    {
                        Console.WriteLine( "Specified entry doesn't exist." );
                        return false;
                    }

                    var filePath = args[3];
                    if ( !File.Exists( filePath ) )
                    {
                        Console.WriteLine( "Specified replacement file doesn't exist." );
                        return false;
                    }

                    Console.WriteLine( $"{( entryExists ? "Replacing" : "Adding")} {entryName}" );
                    pak.AddFile( entryName, filePath, ConflictPolicy.Replace );

                    Console.WriteLine( "Saving..." );
                    pak.Save( outputPath );
                }
            }

            return true;
        }

        public abstract bool Execute( string[] args );
    }

    internal class ReplaceCommand : AddOrReplaceCommand
    {
        public override bool Execute( string[] args ) => Execute( args, false );
    }

    internal class AddCommand : AddOrReplaceCommand
    {
        public override bool Execute( string[] args ) => Execute( args, true );
    }

    internal class PackOrAddCommand : ICommand
    {
        public bool Execute(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Expected at least 2 arguments");
                return false;
            }
            var formatName = args[1];
            if (!Program.FormatsByName.TryGetValue(formatName, out var format))
            {
                Console.WriteLine("Invalid format specified.");
                return false;
            }
            var inputPaths = new List<string>();
            var directory = Directory.GetCurrentDirectory();
            for(int i = 2; i < args.Length; i++)
            {
                var path = string.Concat(args[i].Trim('\"').Split(Path.GetInvalidPathChars()).ToList().Select(r => string.Concat(r, Path.DirectorySeparatorChar)).ToArray()).TrimEnd(Path.DirectorySeparatorChar);
                Console.WriteLine($"{path}");
                if (File.Exists(path))
                {
                    directory = new FileInfo(path).Directory.FullName;
                    inputPaths.Add(path);
                }
                if (Directory.Exists(path))
                {
                    directory = Directory.GetParent(path).FullName;
                    foreach(var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
                    {
                        inputPaths.Add(file);
                    }
                }
                Console.WriteLine(directory);
            }

            var outputPath = Path.Combine(directory, Path.GetFileName(directory));


            using (var pak = new PAKFileSystem(format))
            {
                foreach (string file in inputPaths)
                {
                    Console.WriteLine($"Adding {file}");

                    pak.AddFile(file.Substring(directory.Length)
                                        .Trim(Path.DirectorySeparatorChar)
                                        .Replace("\\", "/"),
                                    file, ConflictPolicy.Ignore);
                }

                Console.WriteLine($"Saving...");
                pak.Save(outputPath);
            }

            return true;

        }
    }

    internal class ListCommand : ICommand
    {
        public bool Execute( string[] args )
        {
            if ( args.Length < 2 )
            {
                Console.WriteLine( "Expected 1 argument." );
                return false;
            }

            var inputPath = args[1];
            if ( !File.Exists( inputPath ) )
            {
                Console.WriteLine( "Input file doesn't exist." );
                return false;
            }

            if ( !PAKFileSystem.TryOpen( inputPath, out var pak ) )
            {
                Console.WriteLine( "Invalid PAK file." );
                return false;
            }

            using ( pak )
            {
                Console.WriteLine( $"PAK format version: {Program.FormatVersionEnumToString[pak.Version]}" );

                foreach ( string file in pak.EnumerateFiles() )
                    Console.WriteLine( file );
            }

            Console.WriteLine("\nPress any key to close...");

            Console.ReadKey();
            return true;
        }


    }
    
}
