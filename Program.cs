using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

class Program
{
    static async Task Main(string[] args)
    {
        // ⚠️ ВАЖНО: Не забудь снова вписать название своего канала!
        string channelName = "glupiikotiki";

        int maxPages = 50;
        int currentPage = 1;

        string currentUrl = $"https://t.me/s/{channelName}";

        // Я переименовал список в mediaUrls, так как теперь тут не только картинки
        List<string> mediaUrls = new List<string>();

        Console.WriteLine("Начинаем сбор фото и видео из канала...");

        using (HttpClient client = new HttpClient())
        {
            while (currentPage <= maxPages)
            {
                try
                {
                    Console.WriteLine($"[Страница {currentPage}] Анализируем: {currentUrl}");
                    string htmlCode = await client.GetStringAsync(currentUrl);

                    // --- ШАГ 1: ИЩЕМ КАРТИНКИ ---
                    string imgPattern = @"background-image:url\('([^']+)'\)";
                    MatchCollection imgMatches = Regex.Matches(htmlCode, imgPattern);

                    foreach (Match match in imgMatches)
                    {
                        string url = match.Groups[1].Value;
                        if (!mediaUrls.Contains(url))
                        {
                            mediaUrls.Add(url);
                        }
                    }

                    // --- ШАГ 2: ИЩЕМ ВИДЕО (.mp4) ---
                    string videoPattern = @"<video[^>]+src=""([^""]+\.mp4)""";
                    MatchCollection videoMatches = Regex.Matches(htmlCode, videoPattern);

                    foreach (Match match in videoMatches)
                    {
                        string url = match.Groups[1].Value;
                        if (!mediaUrls.Contains(url))
                        {
                            mediaUrls.Add(url);
                        }
                    }

                    // --- ШАГ 3: ИЩЕМ КНОПКУ ДЛЯ ПЕРЕХОДА НА СЛЕДУЮЩУЮ СТРАНИЦУ ---
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

                    currentPage++;
                    await Task.Delay(1000); // Пауза, чтобы не злить сервера Telegram
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                    break;
                }
            }
        }

        Console.WriteLine($"\nГОТОВО! Всего найдено медиафайлов (фото + видео): {mediaUrls.Count}");

        Console.WriteLine("Сохраняем обновленную базу в cats.json...");

        var options = new JsonSerializerOptions { WriteIndented = true };
        string jsonText = JsonSerializer.Serialize(mediaUrls, options);
        await File.WriteAllTextAsync("cats.json", jsonText);

        Console.WriteLine("Файл cats.json успешно обновлен! Двигатель готов на 100%.");
    }
}