﻿using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using SevenZip;
using MoltenCoreLauncher.Utils;

namespace MoltenCoreLauncher;

public static class LauncherActions
{
    public const string TMP_ARCHIVE_NAME_GAME = "__tmp__client.rar";

    public static void UpdateThisLauncher(bool weAreOnMacOs, GitHubReleaseInfo releaseInfo)
    {
        // C# can only create temp files with a fixed suffix, but we might want to have ".exe" at the end
        var rawTempFilePath = Path.GetTempFileName();
        try { File.Delete(rawTempFilePath); } catch {/*ignore*/}
        var tempFilePath = Path.ChangeExtension(rawTempFilePath, weAreOnMacOs ? "MoltenCoreLauncher" : "MoltenCoreLauncher.exe");

        var selectedAsset = releaseInfo.Assets!.Single(a => a.Name.Contains("MoltenCoreLauncher") && (a.Name.Contains(".exe") != weAreOnMacOs));
        var downloadUrl = selectedAsset.DownloadUrl;

        Console.WriteLine($"Downloading new version: {downloadUrl}");
        Console.WriteLine($"Into file: {tempFilePath}");

        DownloadFile(downloadUrl, tempFilePath);

        Console.WriteLine("Going to start tmp file in copy mode to replace the following path");
        var ourPath = Process.GetCurrentProcess().MainModule!.FileName!;
        Console.WriteLine($"Replace: '{ourPath}'");
        Thread.Sleep(TimeSpan.FromSeconds(3));
        var process = Process.Start(new ProcessStartInfo{
            FileName = tempFilePath,
            Arguments = $"--copy-self-to \"{ourPath}\"",
            UseShellExecute = true,
        });
        Thread.Sleep(TimeSpan.FromMilliseconds(200));
        if (process == null || process.HasExited)
        {
            throw new Exception("The tmp updater has exited prematurely");
        }

        Environment.Exit(0);
    }

    public static bool CheckGameIntegrity(string fullGamePath, bool macBuild)
    {
        // Allow custom clients by just checking the size and not the checksum
        if (macBuild)
        {
            throw new NotImplementedException("MacOs Build");
        }
        else
        {
            var wowClassicExe = new FileInfo(Path.Combine(fullGamePath, "_classic_era_/WowClassic.exe"));
            if (!wowClassicExe.Exists)
                return false;
            return wowClassicExe.Length > (20 * 1024 * 1024); // should be more than 20 MiB
        }
    }

    public static void CreateDesktopShortcut()
    {
        try
        {
            string linkName = "熔火之心";
            string description = "点击这里开始启动游戏";

            var desktopPath = WindowsShellApi.GetDesktopPath();
            string shortcutPath = Path.Combine(desktopPath, $"{linkName}.lnk");
            if (File.Exists(shortcutPath))
            {
                Console.WriteLine("Replacing existing Desktop Icon");
            }

            string? target = Process.GetCurrentProcess().MainModule?.FileName;
            if (target == null)
            {
                return; // I dont know where I am
            }

            var workingDirectory = Path.GetDirectoryName(target)!;

            Console.WriteLine("Creating Desktop Icon");
            WindowsShellApi.CreateShortcut(shortcutPath, description, target, workingDirectory);
        }
        catch
        {
            // Ignore
        }
    }

    private static void DownloadFile(string url, string outFilename)
    {
        using (var client = new FileDownloader(url, outFilename))
        {
            client.InitialInfo += (totalFileSize) => {
                Console.WriteLine($"Download size {UtilHelper.ToHumanFileSize(totalFileSize ?? -1)}");
            };

            ProgressBarPrinter progressBar = new ProgressBarPrinter("Download");
            client.ProgressChangedFixedDelay += (totalFileSize, totalBytesDownloaded, bytePerSec) => {
                double progress = ((double)totalBytesDownloaded) / (totalFileSize ?? 1);
                string dataRatePerSec = UtilHelper.ToHumanFileSize(bytePerSec);
                progressBar.UpdateState(progress, $"{dataRatePerSec}/s".PadRight(11));
            };

            client.DownloadDone += () => {
                progressBar.Done();
            };

            client.StartDownload().Wait();
        }
    }

    private static string CreateMd5Checksum(string filePath)
    {
        using (var stream = File.OpenRead(filePath))
        {
            using (var md5 = MD5.Create())
            {
                md5.ComputeHash(stream);
                return BitConverter.ToString(md5.Hash!).Replace("-", String.Empty).ToLower();
            }
        }
    }

    public static void PrepareGameConfigWtf(string gamePath)
    {
        var configWtfPath = Path.Combine(gamePath, "_classic_era_", "WTF", "Config.wtf");
        var dirName = Path.GetDirectoryName(configWtfPath);
        Directory.CreateDirectory(dirName!);

        List<string> configContent;
        if (File.Exists(configWtfPath))
            configContent = File.ReadAllLines(configWtfPath).ToList();
        else
            configContent = new List<string>();

        var newLine = "SET portal \"127.0.0.1:1119\"";
        bool wasChanged = false;

        var currentPortalLine = configContent.FindIndex(l => l.StartsWith("SET portal "));
        if (currentPortalLine != -1)
        {
            if (configContent[currentPortalLine] != newLine)
            {
                configContent[currentPortalLine] = newLine;
                wasChanged = true;
            }
        }
        else
        {
            configContent.Add(newLine);
            wasChanged = true;
        }

        if (wasChanged)
        {
            File.WriteAllLines(configWtfPath, configContent, Encoding.UTF8);
        }
    }

    public static void StartGameViaArctium(string gamePath, string arctiumPath)
    {
        bool weAreOnMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        var executableName = weAreOnMacOs
            ? "Arctium WoW Launcher"
            : "Arctium WoW Launcher.exe";

        var executablePath = Path.Combine(arctiumPath, executableName);

        Console.WriteLine("Starting WoW...");
        var process = Process.Start(new ProcessStartInfo{
            FileName = executablePath,
            WorkingDirectory = arctiumPath,
            Arguments = $"--staticseed --version=ClassicEra --path \"{Path.Combine(gamePath, "_classic_era_")}\"",
            UseShellExecute = true,
        })!;

        Thread.Sleep(1);
        if (process.HasExited)
        {
            Console.WriteLine("Error: Somehow ArctiumLauncher exited prematurely");
        }
    }

    public static void DownloadAndApplyMacOsPatches(string gamePath)
    {
        throw new NotImplementedException("TODO MacOS support");
    }

    public static void StartHermesProxyAndWaitTillEnd(string hermesPath)
    {
        bool weAreOnMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        var executableName = weAreOnMacOs
            ? "HermesProxy"
            : "HermesProxy.exe";

        var executablePath = Path.Combine(hermesPath, executableName);
        
        Console.WriteLine("Starting HermesProxy...");
        var process = Process.Start(new ProcessStartInfo{
            FileName = executablePath,
            WorkingDirectory = hermesPath,
            Arguments = "--no-version-check",
        });
        process?.WaitForExit();

        Console.WriteLine("Hermes Proxy has exited?");
        Console.WriteLine("Please report any errors to https://gitee.com/EverCore/hermesproxy");
        Thread.Sleep(2);
        Console.WriteLine("Press any key to close");
        Console.ReadLine();
    }

    public static bool ContainsValidGameZip()
    {
        string downloadedFile = Path.Combine("__tmp__client.rar");
        if (!File.Exists(downloadedFile))
            return false;

        long existingFileLength = new FileInfo(downloadedFile).Length;
        return existingFileLength == 8004342849;
    }

    public static void DownloadGameClientZip((string? provider, string downloadUrl) downloadSource)
    {
        if (downloadSource.provider != null)
            Console.WriteLine($"Downloading from {downloadSource.provider}: url \"{downloadSource.downloadUrl}\"");
        else
            Console.WriteLine($"Download url \"{downloadSource.downloadUrl}\"");

        Console.WriteLine("Placing temporary download file alongside launcher");
        Thread.Sleep(TimeSpan.FromSeconds(1));
        DownloadFile(downloadSource.downloadUrl, Path.Combine(TMP_ARCHIVE_NAME_GAME));
    }

    public static void ExtractGameClient(string gamePath, bool onlyData)
    {
        bool weAreOnMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        if (weAreOnMacOs)
        {
            // TODO: How to unrar a file on macos?
            Console.WriteLine($"Please extract {TMP_ARCHIVE_NAME_GAME} into {gamePath} and skip the 'World of Warcraft' folder name");
            Thread.Sleep(TimeSpan.FromSeconds(1));
            Console.ReadLine();
            Environment.Exit(1);
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "MoltenCoreLauncher.7z.dll";

        using (Stream stream = assembly.GetManifestResourceStream(resourceName)!)
        {
            try
            {
                using (var file = File.Open("7z.dll", FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(file);
                }
            }
            catch(Exception e)
            {
                // Maybe the file is somehow already in use
                Console.WriteLine("Failed to write 7z.dll");
                Console.WriteLine(e);
            }
        }

        SevenZipBase.SetLibraryPath("7z.dll");
        string downloadedFile = Path.Combine(TMP_ARCHIVE_NAME_GAME);
        Console.WriteLine($"Extracting archive into {gamePath}");
        using (var archiveFile = new SevenZipExtractor(downloadedFile))
        {
            var decompressProgress = new ProgressBarPrinter("Decompress");
            bool ShouldBeDecompressed(ArchiveFileInfo entry) => !entry.IsDirectory;
            string ToPath(string path) => path.ReplaceFirstOccurrence("World of Warcraft", gamePath);

            long totalSize = 0;
            long totalCount = 0;
            foreach (var entry in archiveFile.ArchiveFileData)
            {
                if (ShouldBeDecompressed(entry))
                {
                    totalSize += (long) entry.Size;
                    totalCount++;
                }
            }
            Console.WriteLine($"Total size to decompress {UtilHelper.ToHumanFileSize(totalSize)}");

            long alreadyDecompressedSize = 0;
            long alreadyDecompressedCount = 0;
            foreach (var entry in archiveFile.ArchiveFileData)
            {
                if (ShouldBeDecompressed(entry))
                {
                    var destName = ToPath(entry.FileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destName)!);
                    using (var fStream = File.Open(destName, FileMode.Create, FileAccess.Write))
                    {
                        archiveFile.ExtractFile(entry.FileName, fStream);
                    }
                    alreadyDecompressedSize += (long) entry.Size;
                    alreadyDecompressedCount++;
                    decompressProgress.UpdateState((alreadyDecompressedSize / (double)(totalSize)), $"{alreadyDecompressedCount}/{totalCount}".PadLeft(3+1+3));
                }
            }
            decompressProgress.Done();
        }
    }

    public static void RemoveTempGameClientZip()
    {
        string downloadedFile = Path.Combine(TMP_ARCHIVE_NAME_GAME);

        Console.WriteLine("Removing temporary archive");
        Thread.Sleep(TimeSpan.FromSeconds(2));
#if DEBUG
        Console.WriteLine($">>>DEBUG<<< Does not remove {downloadedFile}");
#else
        File.Delete(downloadedFile);
#endif
    }

    public static void ClearAndDownloadHermesProxy(string downloadUrl, string hermesPath)
    {
        var files = Directory.GetFiles(hermesPath);
        foreach (string file in files)
        {
            File.Delete(file);
        }

        var directories = Directory.GetDirectories(hermesPath);
        foreach (string directory in directories)
        {
            if (!directory.Contains("AccountData")) // we want to keep our AccountData
                Directory.Delete(directory, true);
        }


        var tempFilePath = "__tmp__hermes.zip";
        DownloadFile(downloadUrl, tempFilePath);

        ZipFile.ExtractToDirectory(tempFilePath, hermesPath);
        File.Delete(tempFilePath);
    }

    public static void ClearAndDownloadArctiumLauncher(string downloadUrl, string arctiumPath)
    {
        Directory.Delete(arctiumPath, true);

        var tempFilePath = "__tmp__arctium.zip";
        DownloadFile(downloadUrl, tempFilePath);

        ZipFile.ExtractToDirectory(tempFilePath, arctiumPath);
        File.Delete(tempFilePath);
    }

    public static void PrepareHermesProxyData(string hermesPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var allResources = assembly.GetManifestResourceNames();

        var baseCsvResources = allResources.Where(r => r.StartsWith("MoltenCoreLauncher.HermesDataPatches.") && !r.StartsWith("MoltenCoreLauncher.HermesDataPatches.Hotfix")).ToList();
        var hotfixCsvResources = allResources.Where(r => r.StartsWith("MoltenCoreLauncher.HermesDataPatches.Hotfix")).ToList();
        PatchCsvFiles(baseCsvResources, "MoltenCoreLauncher.HermesDataPatches.", Path.Combine(hermesPath, "CSV"));
        PatchCsvFiles(hotfixCsvResources, "MoltenCoreLauncher.HermesDataPatches.Hotfix.", Path.Combine(hermesPath, "CSV", "Hotfix"));

        var modifiedByUsFilePath = Path.Combine(hermesPath, "CSV", "_MoltenCoreLauncherModified.txt");
        if (!File.Exists(modifiedByUsFilePath))
            File.WriteAllText(modifiedByUsFilePath, "The CSV files where updated by MoltenCoreLauncher");

        void PatchCsvFiles(List<string> patchResources, string replace, string csvDirPath)
        {
            foreach (var patchResource in patchResources)
            {
                var fileName = patchResource.ReplaceFirstOccurrence(replace, "");
                var dstFilePath = Path.Combine(csvDirPath, fileName);

                List<string> patches = new List<string>();
                using (Stream stream = assembly.GetManifestResourceStream(patchResource)!)
                using (StreamReader reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream)
                    {
                        var currentLine = reader.ReadLine();
                        if (currentLine == null)
                            break;
                        patches.Add(currentLine);
                    }
                }

                if (!File.Exists(dstFilePath))
                {
                    Console.WriteLine($"Unable to patch '{dstFilePath}' ({patchResource})");
                    Thread.Sleep(TimeSpan.FromSeconds(5));
                }
                
                var allLinesInCsv = File.ReadAllLines(dstFilePath).ToList();

                bool wasPatched = false;
                foreach (var patch in patches)
                {
                    if (patch.StartsWith("REMOVE!"))
                    {
                        var clearPatch = patch.Substring("REMOVE!".Length);
                        int amountRemoved = allLinesInCsv.RemoveAll((l) => l == clearPatch);
                        if (amountRemoved > 0)
                            wasPatched = true;
                    }
                    if (!allLinesInCsv.Contains(patch))
                    {
                        allLinesInCsv.Add(patch);
                        wasPatched = true;
                    }
                }

                if (wasPatched)
                {
                    Console.WriteLine($"Applying patch to '{dstFilePath}' ({patchResource})");
                    File.WriteAllLines(dstFilePath, allLinesInCsv);
                }
            }
        }
    }

    public static void PrepareHermesProxyConfig(string hermesPath, string realmlist)
    {
        var hermesConfigPath = Path.Combine(hermesPath, "HermesProxy.config");
        XmlDocument doc = new XmlDocument();
        doc.Load(hermesConfigPath);

        const string modifiedAttrName = "MoltenCoreLauncherModified";
        var configNode = doc.DocumentElement!.SelectSingleNode("/configuration")!;
        XmlNode? alreadyModifiedByUsMarker = configNode.SelectSingleNode(modifiedAttrName);
        if (alreadyModifiedByUsMarker == null)
        {
            XmlNode appSettings = configNode.SelectSingleNode("appSettings")!;

            XmlNode serverAddrNode = appSettings.SelectSingleNode("add[@key='ServerAddress']")!;
            serverAddrNode.Attributes!["value"]!.Value = realmlist;

            XmlNode packetLogNode = appSettings.SelectSingleNode("add[@key='PacketsLog']")!;
            packetLogNode.Attributes!["value"]!.Value = "false";

            var modifiedMarker = doc.CreateElement(modifiedAttrName);
            modifiedMarker.SetAttribute("comment", "This file might get overwritten when new Hermes update is applied");
            configNode.InsertBefore(modifiedMarker, configNode.FirstChild);

            doc.Save(hermesConfigPath);
        }
    }
}
