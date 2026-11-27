# TechDigest nasıl çalışıyor? — Yaparken öğrendiklerim

> Bu dosya hem projenin Türkçe teknik rehberi hem de bir **öğrenme günlüğü**.
> TechDigest'i C# öğrenirken, bir yapay zekâ asistanıyla (Claude) eşli programlama yaparak geliştirdim.
> Amacım sadece çalışan bir şey yapmak değil; her parçanın **neden** öyle olduğunu anlayıp kendi cümlelerimle
> anlatabilmekti. Kodun büyük kısmını birlikte yazdık — bu rehber, o kodu gerçekten anlamak için tuttuğum not defteri.

**Neden yaptım?** Bir gün Anthropic'in halka arzını ve Nvidia–Çin gelişmelerini geç öğrendiğimi fark ettim. Her sabah
BBC, NYT, Reuters gezmek yerine önemli olanın bana gelmesini istedim. Bir de IELTS'e hazırlanıyorum: haberleri
İngilizce okuyup altında Türkçe özet görmek ve her gün birkaç yeni kelime öğrenmek, iki ihtiyacı tek yerde çözüyor.

---

## İçindekiler

1. [Büyük resim](#1-büyük-resim)
2. [Klasör haritası — nereden okumaya başlamalı?](#2-klasör-haritası)
3. [Bir sabah adım adım](#3-bir-sabah-adım-adım)
4. [Kümeleme: aynı olayı anlatan haberleri bulmak](#4-kümeleme)
5. [Puan formülü: "önemli" ne demek?](#5-puan-formülü)
6. [Gemini: ücretsiz katmanla güvenilir çalışmak](#6-gemini)
7. [Mail ve site](#7-mail-ve-site)
8. [Workflow'lar: kim, ne zaman, neyi çalıştırıyor?](#8-workflowlar)
9. [Güvenlik kararları](#9-güvenlik-kararları)
10. [Öğrenme günlüğü: karşılaştığım gerçek sorunlar](#10-öğrenme-günlüğü)
11. [Mülakatta sorulabilecekler](#11-mülakatta-sorulabilecekler)
12. [Kendin dene](#12-kendin-dene)

---

## 1. Büyük resim

```
07:00 İstanbul
cron-job.org ──(GitHub API: workflow_dispatch)──► GitHub Actions
                                                    │
                                                    ▼
                                    TechDigest (.NET 10 konsol uygulaması)
                     RSS çek → temizle → kümele → puanla → Gemini → mail + JSON
                                                    │
                       ┌────────────────────────────┼─────────────────────────┐
                       ▼                            ▼                         ▼
                 Gmail SMTP (BCC)        archive dalına günün JSON'u   GitHub Pages sitesi
```

Hepsi ücretsiz: GitHub Actions (public repo), Gmail SMTP, Gemini'nin ücretsiz katmanı, GitHub Pages, cron-job.org.

## 2. Klasör haritası

Okuma sırası önerim (yukarıdan aşağı):

| Sıra | Dosya | Ne öğrenirsin? |
|---|---|---|
| 1 | [`Program.cs`](../src/TechDigest/Program.cs) | Uygulama nasıl başlıyor, bağımlılıklar nasıl kuruluyor |
| 2 | [`Pipeline/DigestPipeline.cs`](../src/TechDigest/Pipeline/DigestPipeline.cs) | Bütün akış tek yerde: 5 adım |
| 3 | [`Services/Fetching/FeedFetcher.cs`](../src/TechDigest/Services/Fetching/FeedFetcher.cs) | Paralel HTTP, XML okuma, güvenlik sınırları |
| 4 | [`Services/Ranking/StoryClusterer.cs`](../src/TechDigest/Services/Ranking/StoryClusterer.cs) | Projenin "beyni": kümeleme |
| 5 | [`Services/Ranking/StoryRanker.cs`](../src/TechDigest/Services/Ranking/StoryRanker.cs) | Puan formülü ve son seçim |
| 6 | [`Services/Enrichment/GeminiClient.cs`](../src/TechDigest/Services/Enrichment/GeminiClient.cs) | Dış API ile dayanıklı çalışma |
| 7 | [`Services/Rendering/EmailRenderer.cs`](../src/TechDigest/Services/Rendering/EmailRenderer.cs) | Mail HTML'i |
| 8 | [`site/app.js`](../site/app.js) | Framework'süz web arayüzü |
| 9 | [`config/*.json`](../src/TechDigest/config) | Kaynaklar, konular, yayıncı kademeleri — kod değiştirmeden ayar |
| 10 | [`tests/`](../tests/TechDigest.Tests) | Her parçanın nasıl test edildiği |

**Tasarım fikri:** Her adım ayrı bir sınıf, `DigestPipeline` sadece sırayı yönetiyor. HTTP istemcisi ve saat dışarıdan
verildiği için [`PipelineTests`](../tests/TechDigest.Tests/PipelineTests.cs) bütün akışı **internete çıkmadan** sahte
RSS ve sahte Gemini ile test edebiliyor.

## 3. Bir sabah adım adım

Gerçek bir günden rakamlarla (`DigestPipeline.RunAsync`):

| Adım | Ne oluyor | Örnek |
|---|---|---|
| 1. Çek | 14 kaynak paralel çekilir (`Task.WhenAll`), son 24 saat kalır | ~450 haber |
| 2a. Filtrele | Engelli yayıncılar ve tık tuzağı başlıklar atılır (`ContentFilter`) | ~22 haber elenir |
| 2b. Kümele | Aynı olayı anlatanlar tek `Story` olur (`StoryClusterer`) | ~290 olay |
| 2c. Puanla | Konu atanır, puan hesaplanır, konusuzlar düşer (`StoryRanker`) | ~240 olay |
| 2d. Aday | Her konudan en iyi 6, toplam en fazla 30 | 30 aday |
| 3. Gemini | Türkçe özet + anlamsal kopyalar + 5 kelime (tek istek) | 1–2 kopya daha birleşir |
| 4. Seç | En yüksek puan = manşet; konu başına 4, toplam 20 | 20 haber, 6 konu |
| 5. Gönder | Mail (BCC), `out/` dosyaları, arşiv JSON'u | — |

## 4. Kümeleme

**Problem:** FIFA siber saldırısını 40 site yazdı. Hepsini göstermek saçma; birini gösterip diğerlerini atmak da
yanlış — çünkü "40 site yazdı" bilgisi, haberin ne kadar önemli olduğunun en iyi kanıtı. Bu yüzden kopyaları
**atmıyoruz, grupluyoruz.**

**1. geçiş — kelime örtüşmesi** ([`TextTools.IsSameStory`](../src/TechDigest/Text/TextTools.cs)):
Başlıklar kelime kümesine çevrilir ("the", "says" gibi dolgu kelimeler atılır, "exports" → "export").
İki başlık şu durumda aynı olaydır:
- **Jaccard benzerliği** (ortak kelime / toplam farklı kelime) ≥ 0,6 **ya da**
- en az 4 ortak kelime var ve kısa başlığın kelimelerinin ≥ %60'ı uzun başlıkta geçiyor.

**2. geçiş — nadir kelimeler (IDF)** ([`StoryClusterer.IsSameEvent`](../src/TechDigest/Services/Ranking/StoryClusterer.cs)):
"FIFA says hackers accessed data" ile "FIFA confirms cyberattack" çok az kelime paylaşıyor ama aynı olay.
Çözüm: her kelimeye o günkü **nadirliğine** göre ağırlık vermek: `ağırlık = log((başlık sayısı + 1) / kelimenin geçtiği başlık sayısı)`.
"fifa" sadece o olayda geçtiği için ağır, "nvidia" her yerde geçtiği için hafif sayılır. Birleşme şartı:
- ortak kelimelerin ağırlığı, küçük başlığın toplam ağırlığının ≥ %45'i **ve** en az bir ortak kelime gerçekten nadir (ağırlık ≥ 3) **ya da**
- en az 3 ortak kelime ve oran ≥ %55.

**Yaptığım hata ve öğrendiğim:** İlk denemede kümelerin bütün kelimelerini tek havuzda birleştirip karşılaştırdık. Sonuç:
büyük küme her şeye benzemeye başladı ve alakasız Nasdaq haberlerini "yuttu". Doğrusu, kümeleri **üye başlık
çiftleri** üzerinden karşılaştırmak. Ayrıca tek geçiş yetmedi (A+B birleşince C'ye benzeyebiliyor) → değişiklik
kalmayana kadar tekrar ediyoruz.

## 5. Puan formülü

[`StoryRanker.Score`](../src/TechDigest/Services/Ranking/StoryRanker.cs):

```
puan = konu uygunluğu (0–9)
     + 4 × log2(yayıncı sayısı)          1 → 0 · 2 → 4 · 4 → 8 · 8 → 12 · 16 → 16
     + yayıncı kalitesi                   Top +4 · Trusted +2 · Normal 0 · Low −3
     + tazelik                            son 6 saat +2 · son 12 saat +1
```

- **Konu uygunluğu** ([`TopicMatcher`](../src/TechDigest/Services/Ranking/TopicMatcher.cs)): başlıkta "güçlü" kelime 3 puan,
  sadece açıklamada 1 puan, başlıkta "zayıf" kelime 1 puan; en fazla 9. Güçlü kelime yoksa konu atanmaz
  ("china" tek başına bir habere "Chips" demez).
- **Neden log2?** 2 kaynak ile 1 kaynak arasındaki fark büyük (biri doğruladı), 40 ile 39 arasındaki fark önemsiz.
  Logaritma bu "azalan getiriyi" modelliyor; yoksa 40 kaynaklı tek bir haber her şeyi ezerdi.

**Örnek hesap:**

| Haber | Uygunluk | Kapsama | Kalite | Tazelik | **Puan** |
|---|---|---|---|---|---|
| Reuters, 8 kaynak, 3 saatlik | 6 | 4 × log2(8) = 12 | +4 | +2 | **24** |
| İçerik çiftliği, anahtar kelime dolu, tek kaynak | 9 | 0 | −3 | +2 | **8** |

Eski sürüm sadece anahtar kelime sayıyordu — yani ikinci haber kazanıyordu. Bu formül projenin en önemli dersiydi:
**neyi ölçtüğün, neyi göstereceğini belirler.**

## 6. Gemini

[`GeminiClient`](../src/TechDigest/Services/Enrichment/GeminiClient.cs) + [`DigestEnricher`](../src/TechDigest/Services/Enrichment/DigestEnricher.cs)

- **Tek istek:** 30 adayın hepsi tek prompt'ta gider; cevap üç parça döner (özetler, kopya grupları, kelimeler).
  Ücretsiz katmanın limitine takılmamak için.
- **Yapılandırılmış çıktı:** İstekle birlikte bir JSON şeması (`responseSchema`) gönderiliyor; model ona uygun cevap veriyor.
  Yine de modele güvenmiyoruz: geçersiz id'ler atılıyor, metinler kısaltılıyor, kontrol karakterleri temizleniyor.
- **Model seçimi:** Model adı koda yazılmıyor. Anahtarın erişebildiği modeller listelenip en uygunu seçiliyor.
  Sıra: en yeni 3 "flash" → "lite" modeller (daha az yoğun) → eskiler → preview.
- **Hata türüne göre davranış:**

| Hata | Anlamı | Ne yapıyoruz |
|---|---|---|
| 503 / 500 | Sunucu yoğun, geçici | 10 sn, sonra 30 sn bekleyip aynı modeli tekrar dene |
| 429 | Bu modelin günlük kotası doldu | Beklemek işe yaramaz → hemen sıradaki model |
| 404 | Model emekliye ayrılmış | Model listesini çek, başka modele geç |
| 400 (şema) | Şema reddedildi | Şemasız bir kez dene |
| 401 / 403 | Anahtar yanlış | Dur, başka model denemek fayda etmez |

- Model başına 3 dk, toplamda 8 dk süre sınırı. Hiçbiri olmazsa **mail yine gider**, sadece Türkçe özetsiz.

## 7. Mail ve site

**Mail** ([`EmailRenderer`](../src/TechDigest/Services/Rendering/EmailRenderer.cs)): Mail HTML'i web HTML'i değil.
Gmail `<style>` içindeki çoğu şeyi siler, Outlook flexbox bilmez. Bu yüzden düzen **tablolarla** kurulu ve her stil
satır içi. `<style>` bloğu sadece destekleyen istemciler için bonus: karanlık mod (Apple Mail / iCloud), mobil
boşluklar, hafif animasyon. Dışarıdan gelen her metin `WebUtility.HtmlEncode` ile kodlanıyor.

**Site** ([`site/`](../site)): Framework yok — `index.html`, `styles.css`, `app.js`. Veriler `data/index.json` ve
`data/days/YYYY-MM-DD.json`'dan okunuyor.
- **Güvenlik:** Hiçbir veri `innerHTML` ile yazılmıyor; her şey `createElement` + `textContent`. Linkler sadece
  `http(s)` ise tıklanabilir. Content-Security-Policy satır içi script'i tamamen yasaklıyor.
- **Hareket:** Sayfa geçişlerinde View Transitions API, kartlarda kademeli belirme, 3D çevrilen kelime kartları.
  `prefers-reduced-motion` açıksa animasyonlar kapanıyor.
- **"Hide Turkish":** IELTS pratiği için Türkçe özetleri gizleyip önce İngilizce okuyabiliyorsun.

## 8. Workflow'lar

| Dosya | Ne zaman | Ne yapıyor |
|---|---|---|
| [`daily-digest.yml`](../.github/workflows/daily-digest.yml) | 07:00 (cron-job.org) + 08:00 yedek | Bülteni üretir, gönderir, `archive` dalına yazar, siteyi tetikler |
| [`pages.yml`](../.github/workflows/pages.yml) | Bülten sonrası / `site/` değişince | `site/` + arşiv → GitHub Pages |
| [`ci.yml`](../.github/workflows/ci.yml) | Her push / PR | Kilitli restore, derleme (uyarılar = hata), 86 test, `app.js` kontrolü |

**Yedek zamanlayıcı:** 08:00'de GitHub'ın kendi zamanlayıcısı da çalışır ama önce "bugün (İstanbul saatiyle)
başarılı bir *Send digest* çalışması var mı?" diye bakar; varsa kendini atlar. cron-job.org bir gün çalışmazsa
bülten yine gelir — sadece geç.

**Neden ayrı `archive` dalı?** Her gün bir JSON commit'lemek `main`'in geçmişini kirletirdi. Ayrı dal, kodu ve
veriyi birbirinden ayırıyor.

## 9. Güvenlik kararları

- **Sırlar** sadece GitHub Secrets'ta. Gemini anahtarı URL'de değil header'da gidiyor (loglara düşmesin).
- **Action'lar commit SHA ile sabit** (`actions/checkout@3d3c42e…`): bir etiketin altı değiştirilse bile bizim
  workflow'umuz etkilenmez. Dependabot haftalık güncelleme öneriyor (sadece küçük/yama sürümler).
- **NuGet kilit dosyaları + audit:** CI birebir aynı paketleri kuruyor; bilinen açığı olan bir paket derlemeyi
  durduruyor. Bu ayar açılınca MailKit'in getirdiği `System.Security.Cryptography.Pkcs`'te **yüksek seviyeli bir açık**
  çıktı → yamalı sürüme sabitlendi.
- **RSS'e güvenme:** Feed başına 5 MB / 200 haber sınırı, XML'de dış kaynak çözümleme kapalı, sadece http(s) linkler.

## 10. Öğrenme günlüğü

Projenin asıl değeri burada. Her biri gerçekten yaşandı:

1. **"07:00'de gelecek" dediğimiz mail 15:00'te geldi.** GitHub'ın ücretsiz zamanlayıcısı işleri 8–10 saat
   geciktirebiliyor. Loglara bakmadan bunu fark etmezdim. Çözüm: dış tetikleyici (cron-job.org) + kendini atlayan yedek.
   *Ders: "çalışıyor" ile "doğru zamanda çalışıyor" aynı şey değil; ölçmeden bilemezsin.*

2. **Gemini bir sabah HTTP 404 döndü.** Koda yazdığımız model emekliye ayrılmıştı. *Ders: dış servislerin
   adlarını/sürümlerini sabit yazma; keşfet ve yedeğin olsun.*

3. **503 ile 429 aynı şey değil.** İkisine de "bekle ve tekrar dene" diyorduk. 429 "kota bitti" demek — beklemek
   sadece zaman kaybı. *Ders: hata kodlarını tek torbaya atma, her birinin anlamını oku.*

4. **Log'da her satırın başına "▸" geldi.** Grup başlığını işaretlemek için görünmez bir karakter kullandık;
   .NET'in kültüre duyarlı `StartsWith`'i bu karakteri "yok sayıp" her satırı eşleşmiş kabul etti.
   `StringComparison.Ordinal` ile düzeldi. *Ders: metin karşılaştırmada kültür ayarı sürpriz yapabilir.*

5. **Testler yerelde geçti, GitHub'da kaldı.** Formatlayıcı `GITHUB_ACTIONS` ortam değişkenine göre farklı yazıyordu.
   *Ders: ortama bağlı davranışı parametre yap, ikisini de test et.*

6. **.NET 8'in desteği 10 Kasım 2026'da bitmişti.** Proje çalışıyordu ama artık güvenlik yaması almayacaktı → .NET 10.
   *Ders: "çalışıyor" ≠ "güvende".*

7. **Sahte veriyle güzel görünen tasarım, gerçek veriyle taştı.** "preliminary injunction" gibi uzun kelimeler kelime
   kartlarından taştı. *Ders: arayüzü gerçek veriyle test et.*

8. **"Test başarılı" ama ertesi sabah mail gelmedi (27 Kasım).** cron-job.org'da token'ı yeniledik, TEST RUN 204
   verdi, rahatladık. Ertesi sabah 07:00'de mail yoktu. Teşhis sırası: önce GitHub Actions'a baktım — o sabah **hiç
   çalışma başlamamıştı**, yani sorun kodda değil tetikleyicideydi. Sonra cron-job.org'un *History* sekmesi:
   **401 Unauthorized**. Sebep: TEST RUN, formdaki **kaydedilmemiş** değerlerle çalışıyor; SAVE'e basılmadığı için
   kayıtlı iş eski token'la kalmıştı. Token'ı yenileyip kaydettikten sonra zincirin tamamı ilk kez gerçek gönderimde
   uçtan uca çalıştı: mail → `archive` dalına commit → site güncellendi.
   *Ders: testin geçmesi, kayıtlı yapılandırmanın doğru olduğunu kanıtlamaz. Bir de: hata ararken zinciri baştan
   takip et — "çalışma başladı mı?" sorusu, sorunun kodda mı dışarıda mı olduğunu tek bakışta ayırdı.*

9. **AI ile çalışmak:** Asistan kodu hızlı yazıyor; ama hangi sorunun gerçek olduğunu loglardan görmek, "bu formül
   neden log2?" diye sormak ve sonucu kendi cümlelerimle anlatabilmek benim işim. Bu rehber o yüzden var.

## 11. Mülakatta sorulabilecekler

| Soru | Cevabın özü |
|---|---|
| Hangi haberin önemli olduğuna nasıl karar veriyorsun? | Olay kümeleme + kapsama (kaç yayıncı) + kaynak kalitesi + tazelik; anahtar kelime sayısı değil |
| Neden IDF ağırlığı? | Herkesin kullandığı kelimeler ("nvidia") ayırt edici değil; nadir kelimeler ("fifa") olayı tanımlar |
| Neden log2? | Azalan getiri: ilk doğrulama çok değerli, 40. kaynak az değerli |
| Gemini çökerse ne olur? | Bülten Türkçe özetsiz gider; hiçbir adım exception fırlatıp akışı durdurmaz |
| 429 ile 503 farkı? | 503 geçici yoğunluk (bekle), 429 kota (başka modele geç) |
| Kodu nasıl test ettin? | 86 xUnit testi; sahte `HttpMessageHandler` ile internetsiz uçtan uca boru hattı testi |
| Güvenlik için ne yaptın? | SHA sabitleme, kilit dosyası + NuGet audit, http(s)-only linkler, `textContent` + CSP, sırlar header'da |
| Neden mail HTML'inde tablo? | Mail istemcileri modern CSS'i desteklemiyor; tablo her yerde aynı görünür |
| GitHub zamanlayıcısı gecikirse? | Dış tetikleyici + "bugün gönderildi mi?" kontrolü olan yedek |
| En zor kısım neydi? | Kümelemenin hem kopyaları yakalaması hem alakasız haberleri birleştirmemesi (bkz. bölüm 4) |

## 12. Kendin dene

Küçükten büyüğe, kodu gerçekten anlamak için:

1. **Kaynak ekle:** [`sources.json`](../src/TechDigest/config/sources.json)'a bir RSS ekle (ör. Wired), sonra
   `dotnet run --project src/TechDigest -- --dry-run --verbose` ile kaç haber geldiğine bak.
2. **Formülle oyna:** `StoryRanker.Score`'da kapsama katsayısını 4'ten 1'e indir, `dotnet test` çalıştır. Hangi test
   kırılıyor ve neden? (`Coverage_BeatsKeywordStuffing`)
3. **Konu ekle:** `topics.json`'a "Space" (SpaceX, NASA, satellite…) konusu ekle; config testi neyi kontrol ediyor?
4. **Yeni özellik:** Haftalık mailde geçmiş IELTS kelimelerinden 5 soruluk mini quiz. Arşivdeki `vocabulary`
   alanları hazır — tasarımı sen yap.

---

*Bu rehberi proje büyüdükçe güncelliyorum. Bir şeyi yanlış anladıysam ya da daha iyi bir yolu varsa,
issue açman beni çok sevindirir.* — Yasin
