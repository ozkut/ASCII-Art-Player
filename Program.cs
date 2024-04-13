using System;
using System.IO;
using System.Linq;
using System.Drawing;
using FFMpegCore;
using static System.Net.Mime.MediaTypeNames;

namespace ASCII_Art_Player
{
    internal class Program
    {
        private static string framesPath, savePath;
        private static double frameDelay;
        private static System.Timers.Timer timer;
        private static StreamReader reader;
        private static int framesPlayed = 0;
        private static DateTime lastFrameEndTime;

        static void Main(string[] args)
        {
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
            Console.CancelKeyPress += delegate { Exit(); };

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--frame-delay":
                    case "-fd":
                        if (args.Length >= 2 || double.TryParse(args[i + 1], out frameDelay))
                            i++;
                        else
                        {
                            Console.Error.WriteLine("Please enter a valid number!");
                            Exit();
                        }
                        if (args.Length == 2)
                            Default();
                        break;
                    case "--create":
                    case "-c":
                        AskForCreationPaths();
                        break;
                    case "--play":
                    case "-p":
                        GetAsciiSaveLocation();
                        break;
                    default:
                        Default();
                        break;
                }
            }

            if (GetYesOrNo("Play sound alongside ASCII file? (must be WAV format)"))
                GetSoundPath();
            PlayFile();

            Console.ReadKey();
        }

        private static void Default()
        {
            if (GetYesOrNo("Open existing ASCII file?"))
                GetAsciiSaveLocation();
            else
                AskForCreationPaths();
        }

        private static void AskForCreationPaths()
        {
            framesPath = GetInput("WARNING: This folder may get very big in size depending on the length and resolution of the video!\nPath to folder that contains frames (leave blank for Desktop): ").Trim('"');
            if (framesPath != string.Empty)
            {
                while (!Directory.Exists(framesPath))
                {
                    Console.Error.WriteLine("File can't be found!");
                    framesPath = GetInput("File path of folder containing frames: ").Trim('"');
                }
            }
            else
                framesPath = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "frames")).FullName;

            savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), GetInput("Name of ascii file: ") + ".txt");
            if (File.Exists(savePath))
            {
                if (GetYesOrNo($"The ascii file at {savePath} already exists! Keep existing file?"))
                    Exit();
            }
            ConvertVideoToFrames();
            GenerateAsciiArt();
        }

        private static void ConvertVideoToFrames()
        {
            string inputPath = GetInput("Video path:").Trim('"');
            string outputPattern = Path.Combine(framesPath, "frame%04d.png");
            Action<double> progressHandler = new(progress => { LoadingBar(progress, "Extracting frames "); });
            FFMpegArguments.FromFileInput(inputPath)
                           .OutputToFile(outputPattern, true, 
                           options => options.Resize(Console.WindowWidth, Console.WindowHeight))
                                             .NotifyOnProgress(progressHandler, FFProbe.Analyse(inputPath).Duration)
                                             .ProcessSynchronously();
            frameDelay = 1000 / FFProbe.Analyse(inputPath).PrimaryVideoStream!.FrameRate;
            Console.WriteLine("\nExtraction complete!");
        }

        private static void GenerateAsciiArt()
        {
            string[] pixelTypes = new string[]
            {
                " .-+*#&%$",
                " *"
            };

            sbyte option = 0;
            while (!sbyte.TryParse(GetInput($"Character set 1 = '{pixelTypes[0]}' (recommended for colored videos)\nCharacter set 2 = '{pixelTypes[1]}' (recommended for black and white videos)\nChoose 1 or 2: "), out option))
            {
                if (sbyte.TryParse(GetInput("Please choose 1 or 2!"), out option))
                {
                    if (option == 1 || option == 2)
                        break;
                } 
            }

            string pixels = pixelTypes[option - 1];
            FileSystemInfo[] frames = new DirectoryInfo(framesPath).GetFileSystemInfos().OrderBy(x => x.Name).ToArray();

            using StreamWriter writer = new(savePath);
            writer.WriteLine(frameDelay);
            int frameIndex = 1;
            for (int i = 0; i < frames.Length - 1; i++)
            {
                Bitmap image = new(frames[frameIndex].FullName);

                //stretch to console window dimensions?
                //if (image.Height != Console.WindowHeight || image.Width != Console.WindowWidth)
                //    image = new(image, new(Console.WindowWidth - 3, Console.WindowHeight));

                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        Color color = image.GetPixel(x, y);
                        double brightness = Math.Sqrt(color.R * color.R * 0.241 + color.G * color.G * 0.691 + color.B * color.B * 0.068);
                        double index = brightness / 255 * (pixels.Length - 1);
                        char pixel = pixels[(int)Math.Round(index)];
                        writer.Write(pixel);
                        //writer.Write(pixel);//makes image not look good but was used to correct the aspect ratio
                    }
                    writer.WriteLine();
                }
                image.Dispose();
                LoadingBar(frameIndex + 1, frames.Length, $"Loading file:{frames[frameIndex].Name} ");
                frameIndex++;
            }
            Console.WriteLine();
        }

        private static void LoadingBar(int progress, int total, string text)
        {
            Console.CursorVisible = false;
            const int loadingBarLength = 20;

            //draw the start and end position of the progress bar
            Console.CursorLeft = 0;
            Console.Write('[');
            Console.CursorLeft = loadingBarLength + 1;
            Console.Write(']');
            Console.CursorLeft = 1;

            float correctLoadingBarLength = loadingBarLength - 0.9f;
            float loadingIndex = correctLoadingBarLength / total;

            int loadPosition = 1;
            //draw the complete part of the progress bar
            for (int i = 0; i < loadingIndex * progress; i++)
            {
                Console.CursorLeft = loadPosition++;
                Console.Write('#');
            }

            //draw the incomplete part of the progress bar
            for (int j = loadPosition; j <= loadingBarLength; j++)
            {
                Console.CursorLeft = loadPosition++;
                Console.Write('-');
            }

            float percentage = (float)progress / total * 100.0f;//display percantage complete
            Console.CursorLeft = loadingBarLength + 3;
            Console.Write($"{percentage:0}% [{progress}/{total}] ");//display how many files are complete out of the number total files
            Console.Write(text);
        }

        private static void LoadingBar(double progress, string text)
        {
            Console.CursorVisible = false;
            const int loadingBarLength = 20;

            //write text before the loading bar
            Console.CursorLeft = 0;
            Console.Write(text);

            //draw the start and end position of the progress bar
            Console.Write('[');
            Console.CursorLeft = text.Length + loadingBarLength + 1;
            Console.Write(']');
            Console.CursorLeft = text.Length;

            float correctLoadingBarLength = loadingBarLength - 0.9f;
            float loadingIndex = correctLoadingBarLength / 100;

            int loadPosition = text.Length + 1;
            //draw the complete part of the progress bar
            for (int i = 0; i < loadingIndex * progress; i++)
            {
                Console.CursorLeft = loadPosition++;
                Console.Write('#');
            }

            //draw the incomplete part of the progress bar
            for (int j = loadPosition; j <= text.Length + loadingBarLength; j++)
            {
                Console.CursorLeft = loadPosition++;
                Console.Write('-');
            }

            Console.CursorLeft = text.Length + loadingBarLength + 3;
            Console.Write($"{progress:0.0}%");
        }

        private static void GetAsciiSaveLocation()
        {
            savePath = GetInput("File path of ascii file: ").Trim('"');
            while (!File.Exists(savePath))
            {
                Console.Error.WriteLine("File can't be found!");
                savePath = GetInput("File path of ascii file: ").Trim('"');
            }
        }

        private static void GetSoundPath()
        {
            string soundFilePath = GetInput("File path of sound file: ").Trim('"');
            while (!File.Exists(soundFilePath))
            {
                Console.Error.WriteLine("File can't be found!");
                soundFilePath = GetInput("File path of sound file: ").Trim('"');
            }
            PlaySound(soundFilePath);
        }

        private static bool GetYesOrNo(string message)
        {
            while (true)
            {
                switch (GetInput($"{message} [Y/N] ").ToLower())
                {
                    case "y":
                        return true;
                    case "n":
                        return false;
                    default:
                        Console.Error.WriteLine("Please select Y or N");
                        break;
                }
            }
        }

        private static void PlayFile()
        {
#pragma warning disable CA1416
            Console.SetWindowPosition(0, 0);
            Console.SetWindowSize(Console.LargestWindowWidth, Console.LargestWindowHeight);
#pragma warning restore CA1416

            Console.SetCursorPosition(0, 0);
            Console.Write(new string(' ', Console.BufferWidth));
            Console.SetCursorPosition(0, 0);

            reader = new(savePath);
            frameDelay = double.Parse(reader.ReadLine()!);
            timer = new System.Timers.Timer(frameDelay - 5);
            timer.Start();
            DateTime startTime = DateTime.Now;
            timer.Elapsed += (sender, e) => DrawFrame(sender, e, startTime);

            while (true)
            {
                System.Threading.Thread.Sleep(1000/*750*/);
                if (reader.EndOfStream)
                    Exit();
            }
        }

        private static void DrawFrame(object? sender, System.Timers.ElapsedEventArgs e, DateTime startTime)
        {
            timer.Stop();
            //Draw frame
            Console.SetCursorPosition(0, 0);
            for (int i = 0; i < (Console.WindowHeight / 2) - 10 + 1; i++) 
                Console.Write($"\n{reader.ReadLine()}");

            //Display debug info
            double secondsPassed = (DateTime.Now - startTime).TotalSeconds;
            TimeSpan timePassed = TimeSpan.Zero;
            if (lastFrameEndTime != DateTime.Now)
                timePassed = DateTime.Now - lastFrameEndTime;
            Console.SetCursorPosition(0, 1);
            Console.WriteLine($"FPS:{framesPlayed / secondsPassed:0.0} " +
                              $"Frametime:{timePassed.TotalMilliseconds:0.0}ms " +
                              $"Accuracy:{frameDelay / timePassed.TotalMilliseconds * 100:0.0}% " +
                              $"Time:{secondsPassed:0.0}s " +
                              $"Frames:{framesPlayed} ");
            framesPlayed++;
            lastFrameEndTime = DateTime.Now;
            timer.Start();
        }

        private static void PlaySound(string audioLocation)
        {
#pragma warning disable CA1416
            System.Media.SoundPlayer player = new(audioLocation);
            player.LoadAsync();
            player.Play();
#pragma warning restore CA1416
        }

        private static string GetInput(string message)
        {
            Console.Write(message);
            return Console.ReadLine()!;
        }

        private static void Exit() => Environment.Exit(Environment.ExitCode);
    }
}