# Fibaks Sürat Kargo Entegrasyon Modülü - Teknik İnceleme Kılavuzu

Bu klasör, Fibaks ile Sürat Kargo Acente Arayüzü arasında veri akışı sağlayan otomasyon uygulamasının kodlarını ve ayarlarını içerir. Sürat Kargo Genel Merkez IT ekibinin incelemesi için sade ve anlaşılır şekilde hazırlanmıştır.

---

## Bilgi Güvenliği ve Ağ Mimarisi

Kargo bilgisayarının ağ güvenliğini korumak ve izole tutmak için uygulamanın çalışma mantığı şu şekildedir:

1. Dışarıyla Bağlantı Kurmaz (Zero Outbound):
Uygulama yerel veritabanı modunda çalışırken internete (bulut sistemlerine, dış web sitelerine veya uzak API'lere) hiçbir istek atmaz. Ağda dışarı giden (outbound) herhangi bir port açılmasına gerek yoktur.

2. Tamamen Yerel Ağda Çalışır (Offline LAN):
Sipariş bilgi akışı ERP bilgisayarı ile Kargo bilgisayarı arasındaki yerel ağ (kablosuz bağlantı veya doğrudan çekilen ethernet kablosu) üzerinden yerel SQL Server veritabanına bağlanarak taşınır. Bütün veri akışı şirket içi ağınızda kalır.

3. Ekstra Yükleme Gerektirmez (Zero Installation):
Uygulama, Windows'un kendi içinde kurulu gelen C# derleyicisi (csc.exe) ile kaynak koddan derlenir. Dışarıdan hiçbir kütüphane (.dll) veya NuGet paketi indirilmez. Bu sayede antivirüs kurallarına ve şirket güvenlik politikalarına tam uyum sağlar.

4. Standart Microsoft API'leri Kullanılır:
Kargo uygulamasındaki verileri okumak için Windows'un resmi UI Automation altyapısı kullanılır. Pano (Clipboard) kopyalaması veya ekran görüntüsü okuma gibi güvensiz yollar kullanılmaz, her şey bellek üzerinde güvenli yolla çözülür.

---

## Dosya Yapısı

* Program.cs: Otomasyon mantığını, UI Automation kodlarını ve yerel SQL Server bağlantısını içeren ana C# kaynak kod dosyası.
* config.json: Kargo penceresinin adı, işlemler arası bekleme süreleri ve yerel SQL Server bağlantı dizesi gibi ayarları barındıran dosya.
* compile.bat: C# kodunu tek tıkla FibaksFetcher.exe dosyasına derleyen araç.
* OTOMASYONU_EXE_ILE_BASLAT.bat: Derlenen programı otomatik olarak yönetici yetkisi (UAC) talep ederek başlatan kısayol dosyası.

---

## Derleme ve Çalıştırma Adımları

1. Klasördeki compile.bat dosyasına çift tıklayarak derlemeyi başlatın. Windows C# derleyicisi kodları derleyip dizinde FibaksFetcher.exe adında bir program dosyası oluşturacaktır.
2. Derlenen programı başlatmak için OTOMASYONU_EXE_ILE_BASLAT.bat dosyasını çalıştırıp çıkan uyarıda Evet seçeneğini seçin. Program config.json dosyasındaki SQL Server bilgilerini okuyarak siparişleri kargo ekranında otomatik sorgulamaya başlayacaktır.
