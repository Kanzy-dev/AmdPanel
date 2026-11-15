using System.Text.Json;
using System.Text.Json.Serialization;
using AmdPanel.Core.Profiles.Exceptions;
using AmdPanel.Core.Profiles.Models;

namespace AmdPanel.Core.Profiles.Storage;

/// <summary>
/// Репозиторий профилей с atomic-write: сначала запись в .tmp, затем
/// File.Replace с созданием .bak копии. Это гарантирует, что при
/// внезапном отключении питания файл не обнулится - останется либо
/// старая версия, либо новая, но не частично записанный JSON.
/// </summary>
public interface IProfileRepository
{
    GlobalProfile Load(string profileKey);
    GlobalProfile? TryLoad(string profileKey);
    void Save(GlobalProfile profile);
    void Delete(string profileKey);
    IReadOnlyList<string> ListAllKeys();
    bool Exists(string profileKey);
}

public sealed class JsonProfileRepository : IProfileRepository
{
    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public JsonProfileRepository()
    {
        ProfilePaths.EnsureDirectories();
    }

    public bool Exists(string profileKey)
        => File.Exists(ProfilePaths.ProfileFilePath(profileKey));

    public GlobalProfile Load(string profileKey)
        => TryLoad(profileKey) ?? throw new ProfileNotFoundException(profileKey);

    public GlobalProfile? TryLoad(string profileKey)
    {
        string path = ProfilePaths.ProfileFilePath(profileKey);
        if (!File.Exists(path)) return null;

        // Если основной файл повреждён - пытаемся восстановить из .bak
        try
        {
            return ReadJson(path);
        }
        catch (ProfileCorruptedException)
        {
            string bak = path + ".bak";
            if (File.Exists(bak))
            {
                try { return ReadJson(bak); }
                catch { /* fallthrough to rethrow original */ }
            }
            throw;
        }
    }

    public void Save(GlobalProfile profile)
    {
        if (string.IsNullOrEmpty(profile.ProfileKey))
            throw new ArgumentException("ProfileKey не может быть пустым");

        ProfilePaths.EnsureDirectories();
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        string finalPath = ProfilePaths.ProfileFilePath(profile.ProfileKey);
        string tmpPath   = finalPath + ".tmp";
        string bakPath   = finalPath + ".bak";

        // 1) Сериализуем в JSON и пишем во временный файл
        string json = JsonSerializer.Serialize(profile, s_jsonOpts);
        File.WriteAllText(tmpPath, json);

        // 2) Atomic replace:
        //    - Если основной файл уже существует: File.Replace сохраняет старую версию в .bak
        //    - Если файла ещё нет: File.Move с overwrite=true
        if (File.Exists(finalPath))
        {
            // File.Replace(source, dest, backup) - атомарная операция NTFS
            // Сохраняет текущий finalPath как .bak, переименовывает .tmp → finalPath
            File.Replace(tmpPath, finalPath, bakPath, ignoreMetadataErrors: false);
        }
        else
        {
            File.Move(tmpPath, finalPath, overwrite: false);
        }
    }

    public void Delete(string profileKey)
    {
        string path = ProfilePaths.ProfileFilePath(profileKey);
        if (!File.Exists(path)) return;
        string bak = path + ".bak";
        // Сначала делаем .bak (на случай откатывания удаления), потом удаляем основной
        try { if (File.Exists(bak)) File.Delete(bak); File.Move(path, bak, overwrite: true); }
        catch { /* if move fails, just delete in place */ try { File.Delete(path); } catch { } }
    }

    public IReadOnlyList<string> ListAllKeys()
    {
        var dir = ProfilePaths.ProfilesDirectory;
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private static GlobalProfile ReadJson(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<GlobalProfile>(json, s_jsonOpts);
            if (profile is null)
                throw new ProfileCorruptedException(path, new InvalidOperationException("Null after deserialize"));
            return profile;
        }
        catch (JsonException ex)
        {
            throw new ProfileCorruptedException(path, ex);
        }
    }

    // Новые методы для интеграции с AppConfigService
    public void SaveProfiles(ProfileManager profileManager)
    {
        _configService.SaveProfiles(profileManager);
    }

    public ProfileManager LoadProfiles()
    {
        return _configService.LoadProfiles();
    }

    public void SaveAppSettings(AppSettings settings)
    {
        _configService.SaveAppSettings(settings);
    }

    public AppSettings LoadAppSettings()
    {
        return _configService.LoadAppSettings();
    }
}
