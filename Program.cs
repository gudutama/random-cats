using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

class Program
{
    static string cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME") ?? "";
    static string apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY") ?? "";
    static string apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET") ?? "";

    static async Task Main(string[] args)
    {
        string channelName = "glupiikotiki";
        int maxPages = 100;
        int currentPage = 1;
        string currentUrl = $"https://t.me/s/{channelName}";

        List<string> mediaUrls = new List<string>();
        HashSet<string> processedTgUrls = new HashSet<string>();

        Console.WriteLine("Проверяем память...");
        if (File.Exists("cats.json"))
        {
            string oldCats = await File.ReadAllTextAsync("cats.json");
            mediaUrls = JsonSerializer.Deserialize<List<string>>(oldCats) ?? new List<string>();
            Console.WriteLine($"В базе уже есть готовых ссылок: {mediaUrls.Count}");
        }

        if (File.Exists("history.json"))
        {
            string oldHistory = await File.ReadAllTextAsync("history.json");
            var historyList = JsonSerializer.Deserialize<List<string>>(oldHistory) ?? new List<string>();
            processedTgUrls = new HashSet<string>(historyList);
            Console.WriteLine($"Парсер помнит старых постов из ТГ: {processedTgUrls.Count}");
        }

        Console.WriteLine("\nНастраиваем связь с облаком Cloudinary...");
        Account account = new Account(cloudName, apiKey, apiSecret);
        Cloudinary cloudinary = new Cloudinary(account);

        Console.WriteLine("Начинаем проверку новых постов...");

        using (HttpClient client = new HttpClient())
        {
            while (currentPage <= maxPages)
            {
                try
                {
                    Console.WriteLine($"\n[Страница {currentPage}] Анализируем: {currentUrl}");
                    string htmlCode = await client.GetStringAsync(currentUrl);

                    bool foundNewFilesOnThisPage = false;

                    // --- ШАГ 1: СОБИРАЕМ ПРЕВЬЮ ВИДЕО ---
                    // В HTML Telegram превью всегда в теге:
                    // <i class="tgme_widget_message_video_thumb" style="background-image:url('ТУТ_ПРЕВЬЮ')">
                    HashSet<string> videoPreviews = new HashSet<string>();
                    string videoThumbPattern = @"tgme_widget_message_video_thumb[^>]+background-image:url\('([^']+)'\)";
                    MatchCollection thumbMatches = Regex.Matches(htmlCode, videoThumbPattern);
                    foreach (Match m in thumbMatches)
                    {
                        videoPreviews.Add(m.Groups[1].Value);
                    }
                    Console.WriteLine($"Найдено превью видео (будут пропущены): {videoPreviews.Count}");

                    // --- ШАГ 2: КАЧАЕМ ВИДЕО ---
                    string videoTagPattern = @"<video[^>]+src=""([^""]+\.mp4[^""]*)""";
                    MatchCollection videoTags = Regex.Matches(htmlCode, videoTagPattern);

                    foreach (Match tagMatch in videoTags)
                    {
                        string videoUrl = tagMatch.Groups[1].Value;

                        if (!processedTgUrls.Contains(videoUrl))
                        {
                            foundNewFilesOnThisPage = true;
                            processedTgUrls.Add(videoUrl);
                            Console.WriteLine($"Найдено НОВОЕ видео, отправляем в облако...");
                            string cloudUrl = await UploadToCloudinaryAsync(client, cloudinary, videoUrl);
                            if (cloudUrl != null) mediaUrls.Add(cloudUrl);
                        }
                    }

                    // --- ШАГ 3: КАЧАЕМ ФОТО, ПРОПУСКАЯ ПРЕВЬЮ ---
                    string imgPattern = @"background-image:url\('(https://[^']+)'\)";
                    MatchCollection imgMatches = Regex.Matches(htmlCode, imgPattern);

                    foreach (Match match in imgMatches)
                    {
                        string tgUrl = match.Groups[1].Value;

                        if (videoPreviews.Contains(tgUrl))
                        {
                            Console.WriteLine($"Пропускаем превью видео.");
                            continue;
                        }

                        if (!processedTgUrls.Contains(tgUrl))
                        {
                            foundNewFilesOnThisPage = true;
                            processedTgUrls.Add(tgUrl);
                            Console.WriteLine($"Найдено НОВОЕ фото, отправляем в облако...");
                            string cloudUrl = await UploadToCloudinaryAsync(client, cloudinary, tgUrl);
                            if (cloudUrl != null) mediaUrls.Add(cloudUrl);
                        }
                    }

                    // --- УМНЫЙ ТОРМОЗ ---
                    if (foundNewFilesOnThisPage == false && processedTgUrls.Count > 0)
                    {
                        Console.WriteLine("\n[ВНИМАНИЕ] На этой странице все файлы старые.");
                        Console.WriteLine("Похоже, мы скачали все новинки. Останавливаем парсер!");
                        break;
                    }

                    // --- ИЩЕМ СЛЕДУЮЩУЮ СТРАНИЦУ ---
                    string beforePattern = @"data-before=""(\d+)""";
                    Match beforeMatch = Regex.Match(htmlCode, beforePattern);

                    if (beforeMatch.Success)
                    {
                        string beforeId = beforeMatch.Groups[1].Value;
                        currentUrl = $"https://t.me/s/{channelName}?before={beforeId}";
                    }
                    else
                    {
                        break;
                    }

                    // --- АВТОСОХРАНЕНИЕ ---
                    var autoOptions = new JsonSerializerOptions { WriteIndented = true };
                    await File.WriteAllTextAsync("cats.json", JsonSerializer.Serialize(mediaUrls, autoOptions));
                    await File.WriteAllTextAsync("history.json", JsonSerializer.Serialize(processedTgUrls.ToList(), autoOptions));
                    Console.WriteLine($"[Автосохранение] Прогресс страницы {currentPage} сохранен!");

                    currentPage++;
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка парсинга страницы: {ex.Message}");
                    break;
                }
            }
        }

        Console.WriteLine($"\nГОТОВО! Всего ссылок в базе для сайта/бота: {mediaUrls.Count}");

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync("cats.json", JsonSerializer.Serialize(mediaUrls, options));
        await File.WriteAllTextAsync("history.json", JsonSerializer.Serialize(processedTgUrls.ToList(), options));

        Console.WriteLine("Файлы cats.json и history.json успешно обновлены!");
    }

    static async Task<string> UploadToCloudinaryAsync(HttpClient client, Cloudinary cloudinary, string fileUrl)
    {
        if (!fileUrl.StartsWith("http"))
            return null;

        try
        {
            byte[] fileBytes = await client.GetByteArrayAsync(fileUrl);
            bool isVideo = fileUrl.Contains(".mp4");
            string extension = isVideo ? ".mp4" : ".jpg";
            string fileName = Guid.NewGuid().ToString() + extension;

            using (var stream = new MemoryStream(fileBytes))
            {
                var fileDesc = new FileDescription(fileName, stream);

                if (isVideo)
                {
                    var uploadResult = await cloudinary.UploadAsync(new VideoUploadParams() { File = fileDesc });
                    if (uploadResult.Error == null)
                    {
                        Console.WriteLine($" -> Успех (Видео)! {uploadResult.SecureUrl}");
                        return uploadResult.SecureUrl.ToString();
                    }
                    Console.WriteLine($" -> Ошибка облака: {uploadResult.Error.Message}");
                    return null;
                }
                else
                {
                    var uploadResult = await cloudinary.UploadAsync(new ImageUploadParams() { File = fileDesc });
                    if (uploadResult.Error == null)
                    {
                        Console.WriteLine($" -> Успех (Фото)! {uploadResult.SecureUrl}");
                        return uploadResult.SecureUrl.ToString();
                    }
                    Console.WriteLine($" -> Ошибка облака: {uploadResult.Error.Message}");
                    return null;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($" -> Ошибка скачивания файла: {ex.Message}");
            return null;
        }
    }
}
