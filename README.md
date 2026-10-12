# Eco Leaf — ekologik briketlar sayti (PostgreSQL + Render)

ASP.NET Core (.NET 10) + PostgreSQL. Sayt va admin panel **alohida sahifalar**:

| Manzil | Nima |
|---|---|
| `/` | Ommaviy sayt + foydalanuvchi kabineti (`wwwroot/index.html`) |
| `/admin` | Admin panel (`wwwroot/admin.html`) — saytda hech qanday havola yo'q |
| `/healthz` | Render uchun sog'liq tekshiruvi |

## Papka tuzilmasi

```
Web_sayt/
├── Program.cs                  # sozlamalar (DB, cookie-auth, rate limit)
├── Web_sayt.csproj
├── Dockerfile                  # Render uchun
├── render.yaml                 # Render Blueprint (web + PostgreSQL)
├── appsettings.json
├── Controllers/
│   ├── AuthController.cs       # ro'yxatdan o'tish / kirish / chiqish
│   ├── PublicController.cs     # mahsulotlar, statistika, reklama, izohlar
│   ├── OrdersController.cs     # foydalanuvchi buyurtmalari + Telegram
│   └── AdminController.cs      # admin API (faqat Admin roli)
├── Data/
│   ├── AppDbContext.cs         # jadvallar
│   └── DbInitializer.cs        # jadval yaratish, boshlang'ich ma'lumot, admin
├── Models/Entities.cs
├── Services/
│   ├── SecurityHelpers.cs      # PBKDF2 parol, telefon normalizatsiyasi
│   ├── TelegramService.cs
│   └── SettingsService.cs
└── wwwroot/
    ├── index.html
    └── admin.html
```

## Eskidan nimalar o'zgardi

- **Excel (EPPlus) → PostgreSQL.** Render'da fayl tizimi vaqtinchalik, shuning uchun `database.xlsx` va `admin_data.json` har deployda yo'qolardi. Endi hamma narsa bazada.
- **Hamma narsa `localStorage` da edi** (mahsulotlar, foydalanuvchilar, buyurtmalar, admin paroli!). Shu sababli har bir brauzer o'z "saytini" ko'rardi va admin parolini brauzer konsolidan o'qish mumkin edi. Endi barcha ma'lumot serverda.
- **Admin alohida** (`/admin`), cookie-sessiya bilan himoyalangan; admin API'lari faqat Admin roliga ochiq.
- **Parollar** PBKDF2-SHA256 (tuzli) bilan xeshlanadi. Admin paroli kodda emas, `ADMIN_PASSWORD` environment variable orqali beriladi.
- **Buyurtmalar** bazaga saqlanadi va Telegramga yuboriladi (Telegram ishlamasa ham buyurtma yo'qolmaydi). Admin buyurtma holatini o'zgartira oladi.
- Login endi **telefon raqam** bo'yicha (frontend va backend avval turlicha edi: frontendda login, backendda telefon).
- Qo'shildi: mahsulotni tahrirlash, izohlarni boshqarish, hisobotlar (kunlik/oylik), reklama (rasm bilan), login urinishlariga limit (daqiqasiga 10 ta), JSON eksport.
- Olib tashlandi: `ExcelDbService`, `AdminPanelService`, eski `HomeController`, eski `TelegramService`.

## Render'ga joylash

1. Loyihani GitHub'ga yuklang (`Web_sayt` papkasi repo ildizida bo'lsin).
2. Render → **New → Blueprint** → repo'ni tanlang. `render.yaml` o'qiladi va PostgreSQL baza hamda web servis birga yaratiladi.
3. Render so'raydigan ikki qiymatni kiriting (yoki keyin Dashboard → Environment da):
   - `Telegram__BotToken` — @BotFather bergan token
   - `Telegram__ChatId` — buyurtmalar tushadigan chat ID
4. Deploy tugagach: `https://<nom>.onrender.com/admin`
   - Login: `admin`
   - Parol: Dashboard → servis → **Environment** → `ADMIN_PASSWORD` (Render o'zi yaratgan)
   - Kirgach **"Parolni o'zgartirish"** bo'limidan o'z parolingizni qo'ying.

Qo'lda sozlamoqchi bo'lsangiz: Render'da PostgreSQL yarating, **Web Service → Docker** tanlang va `DATABASE_URL` ga bazaning *Internal Database URL* ini kiriting. Boshqa o'zgaruvchilar quyida.

### Environment variable'lar

| Nomi | Majburiy | Izoh |
|---|---|---|
| `DATABASE_URL` | ha | Render PostgreSQL manzili (Blueprint o'zi ulaydi) |
| `ADMIN_USERNAME` | yo'q | Birinchi ishga tushishda yaratiladigan admin logini (standart: `admin`) |
| `ADMIN_PASSWORD` | tavsiya | Admin paroli. Berilmasa, tasodifiy parol yaratilib **logga** yoziladi |
| `Telegram__BotToken` | tavsiya | Buyurtma xabarlari uchun |
| `Telegram__ChatId` | tavsiya | Buyurtma xabarlari uchun |
| `ADMIN_FORCE_RESET` | yo'q | Admin parolini unutsangiz: `true` qiling + yangi `ADMIN_PASSWORD` bering + qayta deploy; kirgach o'chiring |

> Admin hisobi faqat **birinchi marta** yaratiladi. Keyin `ADMIN_PASSWORD` ni o'zgartirish ta'sir qilmaydi (panel ichidan o'zgartirgan parol ustun) — `ADMIN_FORCE_RESET` bundan mustasno.

## Lokal ishga tushirish

```bash
# 1) PostgreSQL (masalan Docker bilan)
docker run --name ecoleaf-pg -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=ecoleaf -p 5432:5432 -d postgres:16

# 2) Ilovani ishga tushirish
dotnet run
```

`appsettings.json` dagi `ConnectionStrings:Default` lokal bazaga qaratilgan. Admin parolini berish uchun:
`ADMIN_PASSWORD=MeningParolim123 dotnet run` (Windows PowerShell: `$env:ADMIN_PASSWORD="MeningParolim123"; dotnet run`).

## Muhim eslatmalar

- **Telegram tokenni almashtiring.** Agar eski token (`HomeController` ichida yoki GitHub'da) hech qachon ochiq repoda bo'lgan bo'lsa, @BotFather orqali `/revoke` qilib yangisini oling.
- **Eski foydalanuvchilar ko'chirilmadi.** Eski `database.xlsx` dagi parollar tuzsiz SHA-256 bilan, `localStorage` dagilari esa ochiq matnda edi — yangi tizim formatiga mos emas. Foydalanuvchilar qayta ro'yxatdan o'tadi. Kerak bo'lsa, ularni bir martalik skript bilan ko'chirib berishim mumkin.
- **Jadvallar `EnsureCreated` bilan yaratiladi** (migratsiyasiz). Kelajakda jadval tuzilmasini o'zgartirsangiz, EF Core Migrations'ga o'ting.
- **Render bepul rejalari cheklangan:** bepul web servis ishlatilmay qolsa uxlab qoladi (birinchi so'rov sekin), bepul PostgreSQL esa muddat bilan cheklangan bo'lishi mumkin. Joriy shartlarni Render narxlar sahifasida tekshiring; haqiqiy do'kon uchun pullik baza rejasini oling va bazadan zaxira nusxa (backup) yoqing. Admin paneldagi *Nastroyka → Eksport* ham qo'shimcha zaxira beradi.
- Cookie shifrlash kalitlari bazada saqlanadi, shuning uchun qayta deploydan keyin foydalanuvchilar tizimdan chiqib ketmaydi.
