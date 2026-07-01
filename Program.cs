using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace FibaksFetcher
{
    public class Win32
    {
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
    }

    public class Config
    {
        public string kargo_pencere_adi = "Surat Kargo Mock Acente Yazilimi";
        public bool firebase_aktif = false;
        public string firebase_url = "";
        public string firebase_auth = "";
        public bool sql_aktif = false;
        public string sql_connection_string = "";
        public string whatsapp_webhook_url = "http://localhost:5000/webhook";
        public int islem_arasi_gecikme_ms = 1000;
        public int akilli_bekleme_limit_ms = 5000;
        public int pencere_odaklama_ms = 500;
        public int siparis_input_index = 0;
        public string sorgula_buton_name = "Sorgula";
        public int adres_output_index = 1;

        public static Config Load(string path)
        {
            Config cfg = new Config();
            if (!File.Exists(path)) return cfg;
            
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                
                cfg.kargo_pencere_adi = GetVal(json, "kargo_pencere_adi");
                cfg.whatsapp_webhook_url = GetVal(json, "whatsapp_webhook_url");
                
                // Firebase nested parsing
                var fbMatch = Regex.Match(json, @"""firebase_ayarlari""\s*:\s*\{[^}]*""aktif""\s*:\s*([^,}\n]+)");
                if (fbMatch.Success) cfg.firebase_aktif = fbMatch.Groups[1].Value.ToLower().Contains("true");
                cfg.firebase_url = GetVal(json, "database_url");
                cfg.firebase_auth = GetVal(json, "auth_token");

                // SQL Server nested parsing
                var sqlMatch = Regex.Match(json, @"""sql_server_ayarlari""\s*:\s*\{[^}]*""aktif""\s*:\s*([^,}\n]+)");
                if (sqlMatch.Success) cfg.sql_aktif = sqlMatch.Groups[1].Value.ToLower().Contains("true");
                var connMatch = Regex.Match(json, @"""sql_server_ayarlari""\s*:\s*\{[^}]*""connection_string""\s*:\s*""([^""]+)""");
                if (connMatch.Success) cfg.sql_connection_string = connMatch.Groups[1].Value;
                
                int val;
                if (int.TryParse(GetVal(json, "islem_arasi_gecikme_ms"), out val)) cfg.islem_arasi_gecikme_ms = val;
                if (int.TryParse(GetVal(json, "akilli_bekleme_limit_ms"), out val)) cfg.akilli_bekleme_limit_ms = val;
                if (int.TryParse(GetVal(json, "pencere_odaklama_ms"), out val)) cfg.pencere_odaklama_ms = val;
                
                var siparisMatch = Regex.Match(json, @"""siparis_input""\s*:\s*\{[^}]*""index""\s*:\s*(\d+)");
                if (siparisMatch.Success) int.TryParse(siparisMatch.Groups[1].Value, out cfg.siparis_input_index);

                var buttonMatch = Regex.Match(json, @"""sorgula_buton""\s*:\s*\{[^}]*""name""\s*:\s*""([^""]+)""");
                if (buttonMatch.Success) cfg.sorgula_buton_name = buttonMatch.Groups[1].Value;

                var adresMatch = Regex.Match(json, @"""adres_output""\s*:\s*\{[^}]*""index""\s*:\s*(\d+)");
                if (adresMatch.Success) int.TryParse(adresMatch.Groups[1].Value, out cfg.adres_output_index);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HATA] Yapilandirma dosyasi okunamadi, varsayilanlar kullanilacak: " + ex.Message);
            }
            
            return cfg;
        }

        public static string GetVal(string json, string key)
        {
            var match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"?([^\",\\n}]+)\"?");
            if (match.Success) return match.Groups[1].Value.Trim().Trim('"');
            return "";
        }
    }

    class Program
    {
        static Config config;
        static string csvPath;
        static string resultsTxtPath;
        static Dictionary<string, string> addressCache = new Dictionary<string, string>();
        static string lastProcessedAddress = "";

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Fibaks Kargo Otomasyonu - C# Surumu";

            Console.WriteLine("==================================================");
            Console.WriteLine("     FIBAKS OTOMASYON - C# (.NET) SURUMU          ");
            Console.WriteLine("==================================================");

            // Klasor ve dosya yollarini belirle
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(baseDir, "config.json");
            if (!File.Exists(configPath))
            {
                configPath = Path.Combine(baseDir, "..", "config.json");
            }
            csvPath = Path.Combine(baseDir, "liste.csv");
            if (!File.Exists(csvPath))
            {
                csvPath = Path.Combine(baseDir, "..", "liste.csv");
            }
            resultsTxtPath = Path.Combine(baseDir, "sonuclar.txt");
            if (!File.Exists(resultsTxtPath))
            {
                resultsTxtPath = Path.Combine(baseDir, "..", "sonuclar.txt");
            }

            // Config yukle
            config = Config.Load(configPath);
            Console.WriteLine("Veri kaynagi  : " + Path.GetFullPath(csvPath));
            Console.WriteLine("Bulut Modu    : " + (config.firebase_aktif ? "AKTIF" : "KAPALI"));
            if (config.firebase_aktif)
            {
                Console.WriteLine("Firebase URL  : " + config.firebase_url);
            }
            Console.WriteLine("SQL Server    : " + (config.sql_aktif ? "AKTIF" : "KAPALI"));
            if (config.sql_aktif)
            {
                Console.WriteLine("SQL ConnStr   : " + config.sql_connection_string);
            }
            Console.WriteLine("Webhook Hedef : " + config.whatsapp_webhook_url);
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("  [ESC]   : Otomasyonu durdurmak/cikmak icin.");
            Console.WriteLine("--------------------------------------------------\n");

            // Servis Noktasi Yoneticisi Guvenlik Ayarlari (SSL/TLS icin)
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2

            while (true)
            {
                // ESC kontrolu
                if (Win32.GetAsyncKeyState(27) < 0)
                {
                    Console.WriteLine("\n[BILGI] Otomasyon kullanici tarafindan ESC tusu ile sonlandirildi.");
                    break;
                }

                string siparisNo = "";
                string activeKey = "";

                if (config.sql_aktif)
                {
                    // 1. Yerel SQL Server'dan Bekleyen Siparis Bulma
                    try
                    {
                        using (var conn = new System.Data.SqlClient.SqlConnection(config.sql_connection_string))
                        {
                            conn.Open();
                            string selectQuery = "SELECT TOP 1 SiparisNo FROM Orders WHERE KargoStatus = 'Bekliyor' ORDER BY KayitTarihi ASC";
                            using (var cmd = new System.Data.SqlClient.SqlCommand(selectQuery, conn))
                            {
                                object res = cmd.ExecuteScalar();
                                if (res != null)
                                {
                                    siparisNo = res.ToString().Trim();
                                }
                            }

                            if (!string.IsNullOrEmpty(siparisNo))
                            {
                                // Durumu "Isleniyor..." olarak guncelle
                                string updateQuery = "UPDATE Orders SET KargoStatus = 'Isleniyor...' WHERE SiparisNo = @sipNo";
                                using (var updateCmd = new System.Data.SqlClient.SqlCommand(updateQuery, conn))
                                {
                                    updateCmd.Parameters.AddWithValue("@sipNo", siparisNo);
                                    updateCmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  [HATA] Yerel SQL Server baglanti hatasi: " + ex.Message);
                        Thread.Sleep(5000);
                        continue;
                    }

                    if (string.IsNullOrEmpty(siparisNo))
                    {
                        Thread.Sleep(2000);
                        continue;
                    }

                    Console.WriteLine("\n[ISLEM] Siparis No (" + siparisNo + ") yerel SQL Server'dan cekildi, sorgulaniyor...");
                }
                else if (config.firebase_aktif)
                {
                    // 2. Firebase'den Bekleyen Sipariş Bulma
                    string getPrefix = !string.IsNullOrEmpty(config.firebase_auth) ? "?auth=" + config.firebase_auth : "";
                    string url = config.firebase_url.TrimEnd('/') + "/orders.json" + getPrefix;
                    
                    string jsonResponse = "";
                    try
                    {
                        jsonResponse = HttpGet(url);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  [HATA] Firebase bağlantı hatası: " + ex.Message);
                        Thread.Sleep(5000);
                        continue;
                    }

                    if (string.IsNullOrEmpty(jsonResponse) || jsonResponse == "null" || jsonResponse == "{}")
                    {
                        Thread.Sleep(3000);
                        continue;
                    }

                    // Regex ile kargoStatus = "Bekliyor" olan ilk kaydı ara
                    var match = Regex.Match(jsonResponse, @"""([^""]+)""\s*:\s*\{([^}]+)\}");
                    bool foundPending = false;
                    while (match.Success)
                    {
                        string key = match.Groups[1].Value;
                        string fields = match.Groups[2].Value;
                        string kargoStatus = Config.GetVal(fields, "kargoStatus");
                        
                        if (kargoStatus == "Bekliyor" || string.IsNullOrEmpty(kargoStatus))
                        {
                            activeKey = key;
                            siparisNo = Config.GetVal(fields, "siparisNo").Trim();
                            foundPending = true;
                            break;
                        }
                        match = match.NextMatch();
                    }

                    if (!foundPending)
                    {
                        Thread.Sleep(2000);
                        continue;
                    }

                    Console.WriteLine("\n[İŞLEM] Sipariş No (" + siparisNo + ") buluttan çekildi, sorgulanıyor...");
                    
                    // Firebase'de durumu "İşleniyor..." olarak güncelle
                    string updateUrl = config.firebase_url.TrimEnd('/') + "/orders/" + activeKey + ".json" + getPrefix;
                    try
                    {
                        HttpPostOrPatch(updateUrl, "PATCH", "{\"kargoStatus\":\"İşleniyor...\"}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  [HATA] Firebase durumu güncellenemedi: " + ex.Message);
                        continue;
                    }
                }
                else
                {
                    // 3. Yerel CSV'den Bekleyen Sipariş Bulma
                    if (!File.Exists(csvPath))
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    List<string[]> rows = ReadCsv(csvPath);
                    int pendingIndex = -1;
                    for (int i = 1; i < rows.Count; i++) // 0 başlık
                    {
                        string status = rows[i].Length > 2 ? rows[i][2] : "";
                        if (status == "Bekliyor" || string.IsNullOrEmpty(status))
                        {
                            pendingIndex = i;
                            break;
                        }
                    }

                    if (pendingIndex == -1)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    siparisNo = rows[pendingIndex][0].Trim();
                    Console.WriteLine("\n[İŞLEM] Sipariş No (" + siparisNo + ") yerel listeden sorgulanıyor...");
                    
                    // CSV'de durumu "İşleniyor..." yap
                    UpdateCsvRow(csvPath, siparisNo, "", "İşleniyor...", "Bekliyor");
                }

                // 3. Kargo Uygulamasını Odakla ve Pencere Bul
                FocusKargoApp(config.kargo_pencere_adi);
                Thread.Sleep(config.pencere_odaklama_ms);

                AutomationElement win = GetWindow(config.kargo_pencere_adi);
                if (win == null)
                {
                    Console.WriteLine("  [HATA] Kargo penceresi bulunamadi! Lutfen uygulamayi acin.");
                    SaveResult(siparisNo, activeKey, "", "Hata: Kargo Penceresi Bulunamadi", "Iptal");
                    Thread.Sleep(2000);
                    continue;
                }

                // 4. Elementleri Bul
                Console.WriteLine("  [DEBUG] Baglanilan Pencere Adi: '" + win.Current.Name + "'");
                AutomationElement txtSiparis = GetElement(win, config.siparis_input_index, ControlType.Edit);
                AutomationElement btnSorgula = GetElementByNameAndType(win, config.sorgula_buton_name, ControlType.Button);
                AutomationElement txtAdres = GetElement(win, config.adres_output_index, ControlType.Edit);

                if (txtSiparis == null || btnSorgula == null || txtAdres == null)
                {
                    Console.WriteLine("  [HATA] UIA Elementleri bulunamadi!");
                    if (txtSiparis == null) Console.WriteLine("    -> Siparis Giris Kutusu (Index: " + config.siparis_input_index + ") BULUNAMADI.");
                    if (btnSorgula == null) Console.WriteLine("    -> Sorgula Butonu (Adi: '" + config.sorgula_buton_name + "') BULUNAMADI.");
                    if (txtAdres == null) Console.WriteLine("    -> Adres Cikis Kutusu (Index: " + config.adres_output_index + ") BULUNAMADI.");
                    
                    SaveResult(siparisNo, activeKey, "", "Hata: Element Eslesmesi Basarisiz", "Iptal");
                    Thread.Sleep(2000);
                    continue;
                }

                // Önbellek (De-duplication) Kontrolü
                if (addressCache.ContainsKey(siparisNo))
                {
                    string cachedAddress = addressCache[siparisNo];
                    string phone = ExtractPhoneNumber(cachedAddress);
                    Console.WriteLine("  [BAŞARILI] Önbellekten kopyalandı (Tekrar sorgulanmadı).");
                    string wpRes = SendToWhatsAppWebhook(siparisNo, cachedAddress, phone);
                    SaveResult(siparisNo, activeKey, cachedAddress, "Başarılı (Önbellek)", wpRes);
                    continue;
                }

                // 5. Sipariş Numarasını Yaz
                if (!SetElementValue(txtSiparis, siparisNo))
                {
                    Console.WriteLine("  [HATA] Giriş alanına değer yazılamadı.");
                    SaveResult(siparisNo, activeKey, "", "Hata: Yazma Başarısız", "İptal");
                    continue;
                }

                // Giriş Doğrulaması (Cross-check: Readback)
                string writtenVal = GetElementValue(txtSiparis);
                if (writtenVal != siparisNo)
                {
                    Console.WriteLine("  [HATA] Doğrulama hatası! Yazılan (" + writtenVal + ") ile hedef (" + siparisNo + ") eşleşmiyor.");
                    SaveResult(siparisNo, activeKey, "", "Hata: Giriş Doğrulama Başarısız", "İptal");
                    continue;
                }

                // 6. Sorgula Butonuna Bas
                if (!InvokeElement(btnSorgula))
                {
                    Console.WriteLine("  [HATA] Sorgula butonuna basılamadı.");
                    SaveResult(siparisNo, activeKey, "", "Hata: Buton Tıklanamadı", "İptal");
                    continue;
                }

                // 7. Akıllı Dinamik Bekleme
                bool kopyalandi = false;
                string errorReason = "Zaman Aşımı / Sonuç Bulunamadı";
                string temizAdres = "";
                int elapsed = 0;
                int limit = config.akilli_bekleme_limit_ms;
                int step = 250;

                while (elapsed < limit)
                {
                    if (Win32.GetAsyncKeyState(27) < 0) break;

                    string rawText = GetElementValue(txtAdres);
                    if (!string.IsNullOrEmpty(rawText))
                    {
                        string rawLower = rawText.ToLower();
                        if (rawLower.Contains("kayit yok") || rawLower.Contains("kayıt yok") || rawLower.Contains("bulunamadı") || rawLower.Contains("bulunamadi"))
                        {
                            errorReason = "Kayıt Bulunamadı";
                            break;
                        }

                        if (rawText.Length > 10 && rawText != lastProcessedAddress)
                        {
                            temizAdres = rawText.Replace("\r", " ").Replace("\n", " ").Trim();
                            kopyalandi = true;
                            break;
                        }
                    }

                    Thread.Sleep(step);
                    elapsed += step;
                }

                // 8. Sonuçları Kaydet
                if (kopyalandi)
                {
                    string phone = ExtractPhoneNumber(temizAdres);
                    if (!string.IsNullOrEmpty(phone))
                    {
                        addressCache[siparisNo] = temizAdres;
                        lastProcessedAddress = temizAdres;
                        
                        // sonuclar.txt dosyasına yaz
                        try
                        {
                            File.AppendAllText(resultsTxtPath, siparisNo + " -> Telefon: " + phone + " | Adres: " + temizAdres + "\n", Encoding.UTF8);
                        } catch {}

                        Console.WriteLine("  [BAŞARILI] Telefon: " + phone + " | Adres kopyalandı.");
                        
                        // WhatsApp Webhook tetikle
                        string wpRes = SendToWhatsAppWebhook(siparisNo, temizAdres, phone);
                        SaveResult(siparisNo, activeKey, temizAdres, "Başarılı", wpRes);
                    }
                    else
                    {
                        Console.WriteLine("  [HATA] Adres bulundu ancak içinden telefon numarası ayıklanamadı.");
                        SaveResult(siparisNo, activeKey, temizAdres, "Başarılı", "Hata: Telefon Numarası Bulunamadı");
                    }
                }
                else
                {
                    Console.WriteLine("  [HATA] Sorgu tamamlanamadı (" + errorReason + ").");
                    SaveResult(siparisNo, activeKey, "", "Hata: " + errorReason, "İptal");
                }

                Thread.Sleep(config.islem_arasi_gecikme_ms);
            }
        }

        #region Yardımcı Fonksiyonlar

        static string HttpGet(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 5000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        static string HttpPostOrPatch(string url, string method, string json)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.ContentType = "application/json; charset=utf-8";
            request.Timeout = 5000;
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        static void FocusKargoApp(string titlePattern)
        {
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
            {
                if (proc.MainWindowTitle.Contains(titlePattern) || proc.ProcessName.Contains(titlePattern))
                {
                    IntPtr hWnd = proc.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        if (Win32.IsIconic(hWnd))
                        {
                            Win32.ShowWindow(hWnd, 9); // Restore
                        }
                        Win32.SetForegroundWindow(hWnd);
                        break;
                    }
                }
            }
        }

        static AutomationElement GetWindow(string titlePattern)
        {
            var root = AutomationElement.RootElement;
            var collection = root.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement win in collection)
            {
                if (win.Current.Name.Contains(titlePattern)) return win;
            }
            return null;
        }

        static AutomationElement GetElement(AutomationElement window, int index, ControlType type)
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, type);
            var collection = window.FindAll(TreeScope.Subtree, cond);
            if (index < collection.Count) return collection[index];
            return null;
        }

        static AutomationElement GetElementByNameAndType(AutomationElement window, string name, ControlType type)
        {
            var cond1 = new PropertyCondition(AutomationElement.ControlTypeProperty, type);
            var cond2 = new PropertyCondition(AutomationElement.NameProperty, name);
            var cond = new AndCondition(cond1, cond2);
            var el = window.FindFirst(TreeScope.Subtree, cond);
            if (el != null) return el;

            // Fallback 1: Kısmi isim eşleşmesi (case-insensitive)
            var allElements = window.FindAll(TreeScope.Subtree, cond1);
            foreach (AutomationElement item in allElements)
            {
                if (item.Current.Name.ToLower().Contains(name.ToLower()))
                {
                    return item;
                }
            }

            // Fallback 2: Penceredeki ilk eşleşen kontrol tipini seç
            if (allElements.Count > 0)
            {
                return allElements[0];
            }

            return null;
        }

        static bool SetElementValue(AutomationElement element, string value)
        {
            object pattern;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
            {
                ((ValuePattern)pattern).SetValue(value);
                return true;
            }
            return false;
        }

        static string GetElementValue(AutomationElement element)
        {
            object valPattern;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out valPattern))
            {
                return ((ValuePattern)valPattern).Current.Value;
            }
            object txtPattern;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out txtPattern))
            {
                return ((TextPattern)txtPattern).DocumentRange.GetText(-1);
            }
            return "";
        }

        static bool InvokeElement(AutomationElement element)
        {
            object pattern;
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
            {
                ((InvokePattern)pattern).Invoke();
                return true;
            }
            return false;
        }

        static string ExtractPhoneNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var match = Regex.Match(text, @"(?:\+90|0)?\s?\(?5[0-9]{2}\)?\s?[0-9]{3}\s?[0-9]{2}\s?[0-9]{2}");
            if (match.Success)
            {
                string clean = Regex.Replace(match.Value, @"[^\d]", "");
                if (clean.Length == 10) clean = "0" + clean;
                if (clean.Length == 12 && clean.StartsWith("90")) clean = "0" + clean.Substring(2);
                if (clean.Length == 11 && clean.StartsWith("05")) return clean;
            }
            return "";
        }

        static string SendToWhatsAppWebhook(string siparisNo, string adres, string telefon)
        {
            string url = config.whatsapp_webhook_url;
            if (string.IsNullOrEmpty(url) || url.Contains("placeholder-url"))
            {
                Console.WriteLine("  [BİLGİ] WhatsApp webhook adresi yapılandırılmamış, veri iletilmedi.");
                return "Gönderilmedi (Yapılandırılmamış)";
            }

            string body = "{\"SiparisNo\":\"" + siparisNo + "\",\"Adres\":\"" + adres + "\",\"Telefon\":\"" + telefon + "\"}";
            try
            {
                HttpPostOrPatch(url, "POST", body);
                Console.WriteLine("  [WP ENTEGRASYONU] Başarıyla WhatsApp veritabanı sistemine iletildi.");
                return "Gönderildi";
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [WP HATA] WhatsApp sistemine veri gönderilemedi: " + ex.Message);
                return "Hata: " + ex.Message;
            }
        }

        static void SaveResult(string siparisNo, string activeKey, string adres, string kargoStatus, string wpStatus)
        {
            if (config.sql_aktif)
            {
                // SQL Server Guncelle
                try
                {
                    using (var conn = new System.Data.SqlClient.SqlConnection(config.sql_connection_string))
                    {
                        conn.Open();
                        string updateQuery = "UPDATE Orders SET Adres = @adres, Telefon = @telefon, KargoStatus = @kargoStatus, WpStatus = @wpStatus, GuncellemeTarihi = GETDATE() WHERE SiparisNo = @sipNo";
                        using (var cmd = new System.Data.SqlClient.SqlCommand(updateQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@adres", adres);
                            cmd.Parameters.AddWithValue("@telefon", ExtractPhoneNumber(adres));
                            cmd.Parameters.AddWithValue("@kargoStatus", kargoStatus);
                            cmd.Parameters.AddWithValue("@wpStatus", wpStatus);
                            cmd.Parameters.AddWithValue("@sipNo", siparisNo);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    Console.WriteLine("  [VERITABANI] Yerel SQL Server basariyla guncellendi.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  [VERITABANI HATA] SQL Server guncellenirken hata olustu: " + ex.Message);
                }
            }
            else if (config.firebase_aktif && !string.IsNullOrEmpty(activeKey))
            {
                // Firebase Güncelle
                string getPrefix = !string.IsNullOrEmpty(config.firebase_auth) ? "?auth=" + config.firebase_auth : "";
                string url = config.firebase_url.TrimEnd('/') + "/orders/" + activeKey + ".json" + getPrefix;
                string body = "{\"kargoStatus\":\"" + kargoStatus + "\",\"wpStatus\":\"" + wpStatus + "\",\"adres\":\"" + adres + "\",\"telefon\":\"" + ExtractPhoneNumber(adres) + "\"}";
                try
                {
                    HttpPostOrPatch(url, "PATCH", body);
                    Console.WriteLine("  [BULUT] Firebase veritabanı başarıyla güncellendi.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  [BULUT HATA] Firebase güncellenirken hata oluştu: " + ex.Message);
                }
            }

            // Her halükarda yerel CSV yedekle
            UpdateCsvRow(csvPath, siparisNo, adres, kargoStatus, wpStatus);
        }

        #endregion

        #region CSV İşlemleri

        static List<string[]> ReadCsv(string path)
        {
            var list = new List<string[]>();
            if (!File.Exists(path)) return list;
            
            using (var reader = new StreamReader(path, Encoding.UTF8))
            {
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    var matches = Regex.Matches(line, @"\s*""([^""]*)""\s*;?|\s*([^;]*)\s*;?");
                    var fields = new List<string>();
                    foreach (Match m in matches)
                    {
                        if (m.Length == 0) continue;
                        string val = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                        fields.Add(val.Trim());
                    }
                    if (fields.Count > 0 && (fields.Count > 1 || !string.IsNullOrEmpty(fields[0])))
                    {
                        list.Add(fields.ToArray());
                    }
                }
            }
            return list;
        }

        static void SaveCsv(string path, List<string[]> rows)
        {
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                foreach (var row in rows)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < row.Length; i++)
                    {
                        string val = row[i];
                        sb.Append("\"" + val.Replace("\"", "\"\"") + "\"");
                        if (i < row.Length - 1) sb.Append(";");
                    }
                    writer.WriteLine(sb.ToString());
                }
            }
        }

        static void UpdateCsvRow(string path, string siparisNo, string adres, string kargoStatus, string wpStatus)
        {
            lock (csvPath)
            {
                try
                {
                    var rows = ReadCsv(path);
                    bool exists = false;
                    for (int i = 1; i < rows.Count; i++)
                    {
                        if (rows[i][0] == siparisNo)
                        {
                            if (adres != "") rows[i][1] = adres;
                            rows[i][2] = kargoStatus;
                            if (rows[i].Length > 3) rows[i][3] = wpStatus;
                            exists = true;
                            break;
                        }
                    }

                    if (!exists && rows.Count > 0)
                    {
                        rows.Add(new string[] { siparisNo, adres, kargoStatus, wpStatus });
                    }

                    SaveCsv(path, rows);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  [CSV UYARI] liste.csv dosyasına yazılamadı: " + ex.Message);
                }
            }
        }

        #endregion
    }
}
