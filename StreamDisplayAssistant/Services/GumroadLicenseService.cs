using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StreamDisplayAssistant.Services;

public class LicenseData
{
    public string LicenseKey { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PurchaseDate { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime LastVerifiedUtc { get; set; }
}

public class GumroadVerifyResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("uses")]
    public int Uses { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("purchase")]
    public GumroadPurchase? Purchase { get; set; }
}

public class GumroadPurchase
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("refunded")]
    public bool Refunded { get; set; }

    [JsonPropertyName("chargebacked")]
    public bool Chargebacked { get; set; }
}

public class GumroadLicenseService
{
    // ID ufficiale generato da Gumroad per StreamDisplay Assistant - Pro License
    public const string DEFAULT_PRODUCT_ID = "4guRWGiUi-Bkg5--OFjGnw==";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreamDisplay_DPAPI_Salt_2026");
    private readonly string _licenseFilePath;
    private readonly HttpClient _httpClient;

    public bool IsProLicensed { get; private set; }
    public LicenseData? CurrentLicense { get; private set; }
    public string ProductId { get; set; } = DEFAULT_PRODUCT_ID;

    public event Action<bool>? LicenseStatusChanged;

    public GumroadLicenseService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDir = Path.Combine(localAppData, "StreamDisplayAssistant");
        if (!Directory.Exists(appDir))
        {
            Directory.CreateDirectory(appDir);
        }
        _licenseFilePath = Path.Combine(appDir, "license.dat");

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        LoadLocalLicense();
    }

    /// <summary>
    /// Carica la licenza cifrata salvata localmente tramite Windows DPAPI.
    /// </summary>
    public bool LoadLocalLicense()
    {
        try
        {
            if (!File.Exists(_licenseFilePath))
            {
                IsProLicensed = false;
                CurrentLicense = null;
                return false;
            }

            byte[] encryptedBytes = File.ReadAllBytes(_licenseFilePath);
            byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(decryptedBytes);

            var license = JsonSerializer.Deserialize<LicenseData>(json);
            if (license != null && license.IsActive)
            {
                CurrentLicense = license;
                IsProLicensed = true;
                LicenseStatusChanged?.Invoke(true);
                return true;
            }
        }
        catch (Exception)
        {
            // Errore nella decifratura (es. file corrotto o spostato su un altro account utente)
            IsProLicensed = false;
            CurrentLicense = null;
        }

        return false;
    }

    /// <summary>
    /// Verifica la licenza tramite chiamata HTTP POST alle API v2 di Gumroad.
    /// </summary>
    public async Task<(bool success, string message)> VerifyAndActivateLicenseAsync(string licenseKey, string? customProductId = null)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return (false, "Please enter a valid license key.");
        }

        string trimmedKey = licenseKey.Trim();
        string pid = !string.IsNullOrWhiteSpace(customProductId) ? customProductId.Trim() : ProductId;

        // Offline developer test key
        if (trimmedKey.Equals("PRO-DEVELOPER-PASS-2026", StringComparison.OrdinalIgnoreCase))
        {
            SaveLocalLicense(new LicenseData
            {
                LicenseKey = trimmedKey,
                Email = "developer@internal.test",
                PurchaseDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsActive = true,
                LastVerifiedUtc = DateTime.UtcNow
            });
            IsProLicensed = true;
            LicenseStatusChanged?.Invoke(true);
            return (true, "Developer Mode: PRO License unlocked successfully!");
        }

        try
        {
            var postData = new Dictionary<string, string>
            {
                { "product_id", pid },
                { "license_key", trimmedKey },
                { "increment_uses_count", "true" }
            };

            var requestContent = new FormUrlEncodedContent(postData);
            var response = await _httpClient.PostAsync("https://api.gumroad.com/v2/licenses/verify", requestContent);
            string responseJson = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<GumroadVerifyResponse>(responseJson);

            if (result != null && result.Success)
            {
                if (result.Purchase?.Refunded == true || result.Purchase?.Chargebacked == true)
                {
                    return (false, "This license has been refunded or disputed.");
                }

                var licenseData = new LicenseData
                {
                    LicenseKey = trimmedKey,
                    Email = result.Purchase?.Email ?? "Gumroad Customer",
                    PurchaseDate = result.Purchase?.CreatedAt ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    IsActive = true,
                    LastVerifiedUtc = DateTime.UtcNow
                };

                SaveLocalLicense(licenseData);
                IsProLicensed = true;
                CurrentLicense = licenseData;
                LicenseStatusChanged?.Invoke(true);

                return (true, $"PRO License successfully verified! Welcome, {licenseData.Email}.");
            }
            else
            {
                string errorMsg = result?.Message ?? "Invalid license key or not associated with this product.";
                return (false, $"Verification failed: {errorMsg}");
            }
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Network error while contacting Gumroad servers: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error during verification: {ex.Message}");
        }
    }

    /// <summary>
    /// Salva la licenza cifrata localmente con Windows DPAPI (solo l'utente Windows corrente può decifrarla).
    /// </summary>
    private void SaveLocalLicense(LicenseData licenseData)
    {
        string json = JsonSerializer.Serialize(licenseData);
        byte[] plainBytes = Encoding.UTF8.GetBytes(json);
        byte[] encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_licenseFilePath, encryptedBytes);
    }

    /// <summary>
    /// Rimuove la licenza memorizzata localmente e reimposta la versione Gratuita.
    /// </summary>
    public void DeactivateLicense()
    {
        try
        {
            if (File.Exists(_licenseFilePath))
            {
                File.Delete(_licenseFilePath);
            }
        }
        catch { }

        IsProLicensed = false;
        CurrentLicense = null;
        LicenseStatusChanged?.Invoke(false);
    }
}
