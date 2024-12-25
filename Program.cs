using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SteamExplorer
{
    internal class Program
    {
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        private delegate bool ConsoleCtrlDelegate(int ctrlType);

        static Dictionary<string, string> translatedDirectoryNames = new Dictionary<string, string>();

        static bool translated = false;

        static async Task Main()
        {
            LoadEnvironmentVariables("params.env");
            SetConsoleCtrlHandler(new ConsoleCtrlDelegate(ConsoleCtrlHandler), true);
            WriteLineColored("SteamExplorer - консольное приложение, транслирующее id приложений Steam в названия и обратно", ConsoleColor.Cyan);
            WriteLineColored("Идёт загрузка базы данных из Steam. Ожидайте...");

            var apps = await GetAppNames();

            if (!apps.Any())
            {
                Console.WriteLine($"От API не было получено корректного ответа\nЗавершение работы...");
                return;
            }

            WriteLineColored("База данных загружена", ConsoleColor.Green);

            while (true)
            {
                Console.WriteLine("Выберите режим работы:\n" +
                    "1. Переименование папок с id в названия приложений (с возможностью восстановления)\n" +
                    "2. id в название\n" +
                    "3. Название в id\n" +
                    "q. Выход из приложения");

                switch (Console.ReadLine())
                {
                    case "1":
                        TranslateDirectoryNames(apps);
                        break;
                    case "2":
                        SteamIdToName(apps);
                        break;
                    case "3":
                        SteamNameToId(apps);
                        break;
                    case "q":
                        return;
                    default:
                        OnErrorInput();
                        break;
                }

                Console.Clear();
            }
        }

        static void SteamNameToId(Dictionary<long, string> apps)
        {
            Console.Clear();
            Console.Write("Введите название приложения Steam без кавычек (для выхода введите q): ");

            while (true)
            {
                string appName = Console.ReadLine();
                if (appName == "q") return;

                if (string.IsNullOrEmpty(appName))
                {
                    OnErrorInput();
                    continue;
                }

                var result = apps.Where(a => a.Value.ToLower().Contains(appName.ToLower()))
                    .Select(a => $"{a.Value}: {a.Key}")
                    .ToList();

                ShowSteamNameToIdResults(result);
            }
        }

        static void SteamIdToName(Dictionary<long, string> apps)
        {
            Console.Clear();
            Console.Write("Введите id приложения Steam (для выхода введите q): ");

            while (true)
            {
                string appIdName = Console.ReadLine();
                if (appIdName == "q") return;

                if (!long.TryParse(appIdName, out long appId))
                {
                    OnErrorInput();
                    continue;
                }

                var result = apps.FirstOrDefault(a => a.Key == appId);
                if (result.Key == 0)
                    WriteLineColored("id не был найден в базе данных Steam", ConsoleColor.Yellow);
                else
                    WriteLineColored($"{result.Key}: {result.Value}", ConsoleColor.Green);

                Console.ReadLine();
            }
        }

        private static void ShowSteamNameToIdResults(List<string> result)
        {
            if (result.Any())
                foreach (var str in result)
                    WriteLineColored(str, ConsoleColor.Green);
            else
                WriteLineColored("В базе данных не было найдено такого названия", ConsoleColor.Yellow);
        }

        static void TranslateDirectoryNames(Dictionary<long, string> apps)
        {
            Console.Clear();
            Console.Write("Введите путь к папке без кавычек: ");
            string parentDirectoryPath = Console.ReadLine();
            string[] directories = Directory.GetDirectories(parentDirectoryPath);

            translatedDirectoryNames = RenameDirectories(apps, parentDirectoryPath, directories);
            translated = true;

            WriteLineColored("Нажмите Enter для обратного переименования", ConsoleColor.Cyan);
            Console.ReadLine();
            RestoreDirectoryNames();
            WriteLineColored("Названия директорий возвращены", ConsoleColor.Green);
            WriteLineColored("Для продолжения нажмите Enter...");
            Console.ReadLine();
        }

        private static Dictionary<string, string> RenameDirectories(Dictionary<long, string> apps, string parentDirectoryPath, string[] directories)
        {
            Dictionary<string, string> translatedDirectoryNames = new Dictionary<string, string>();
            int appsFound = 0;

            foreach (string directory in directories)
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(directory);
                var root = directoryInfo.Root;

                if (long.TryParse(directoryInfo.Name, out long appId) && apps.TryGetValue(appId, out string appName))
                    try
                    {
                        appName = appName.Replace(":", "");

                        var newDirectoryName = $@"{directory} - {appName}";
                        translatedDirectoryNames[newDirectoryName] = directory;
                        Directory.Move(directory, newDirectoryName);
                        appsFound++;
                    }
                    catch (Exception ex)
                    {
                        WriteLineColored($"Произошла ошибка при попытке переименовать {appId} в {appName}: {ex.Message}", ConsoleColor.Red);
                    }
            }
            WriteLineColored($"Всего переименовано папок: {appsFound}", ConsoleColor.Green);
            return translatedDirectoryNames;
        }

        static void RestoreDirectoryNames()
        {
            foreach (var directory in translatedDirectoryNames)
                try
                {
                    if (Directory.Exists(directory.Key))
                        Directory.Move(directory.Key, directory.Value);
                    else
                        WriteLineColored($"Директория {directory.Key} не существует", ConsoleColor.Yellow);
                }
                catch (Exception ex)
                {
                    WriteLineColored($"Произошла ошибка при попытке обратного переименования {directory.Key}: {ex.Message}", ConsoleColor.Red);
                }

            translated = false;
        }

        static async Task<Dictionary<long, string>> GetAppNames()
        {
            var query = Environment.GetEnvironmentVariable("STEAM_APPS_GET_QUERY") ?? @"https://api.steampowered.com/ISteamApps/GetAppList/v0002/?format=json";

            HttpClient client = new HttpClient();
            var response = await client.GetAsync(query);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                WriteLineColored($"Возникла проблема с подключением к Steam API. Status code: {response.StatusCode}", ConsoleColor.Red);
                return new Dictionary<long, string>();
            }

            var appsDictionary = new Dictionary<long, string>();
            var jsonObject = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await response.Content.ReadAsStringAsync());

            if (jsonObject != null && jsonObject.ContainsKey("applist"))
            {
                var applist = jsonObject["applist"];
                var apps = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(applist.ToString())["apps"];

                foreach (var app in JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(apps.ToString()))
                {
                    long appId = app["appid"].GetInt64();
                    string name = app["name"].GetString();
                    appsDictionary[appId] = name;
                }
            }

            return appsDictionary;
        }

        private static void WriteLineColored(string str, ConsoleColor color = ConsoleColor.White)
        {
            var defaultColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(str);
            Console.ForegroundColor = defaultColor;
        }

        static void OnErrorInput()
        {
            WriteLineColored("Ошибка ввода. Попробуйте повторить попытку", ConsoleColor.Red);
            Thread.Sleep(2000);
        }

        private static bool ConsoleCtrlHandler(int ctrlType)
        {
            if (translated)
                RestoreDirectoryNames();

            return true;
        }

        static void LoadEnvironmentVariables(string filePath)
        {
            if (!File.Exists(filePath)) return;

            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                
                string key = parts[0].Trim();
                string value = parts[1].Trim();
                Environment.SetEnvironmentVariable(key, value);
                WriteLineColored($"Загружена переменная среды {key}", ConsoleColor.Magenta);
            }
        }
    }
}
