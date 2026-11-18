# Operations / Operasional

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

How to configure, back up and observe the v0.13 server. It runs as **one process on one machine**. Nothing here has been deployed to the internet or run across several hosts.

Cara mengatur, mencadangkan, dan memantau server v0.13. Server berjalan sebagai **satu proses pada satu mesin**. Belum ada bagian yang di-deploy ke internet atau dijalankan pada beberapa host.

## What is tested and what is not / Yang teruji dan yang belum

| Part / Bagian | Status |
| --- | --- |
| Accounts, sessions, profiles, match results on embedded SQLite / Akun, sesi, profil, hasil pertandingan pada SQLite tertanam | Tested / Teruji |
| The same SQL against PostgreSQL (`Database: postgres`) / SQL yang sama terhadap PostgreSQL | Tested against PostgreSQL 17.12: store, full server / Teruji terhadap PostgreSQL 17.12: store, server penuh |
| In-memory matchmaking queue / Antrean matchmaking dalam memori | Tested / Teruji |
| Redis matchmaking queue (`Queue: redis`) / Antrean Redis | Tested against Garnet 2.2.12 (Redis protocol, with Lua). **Not run against Redis itself** / Teruji terhadap Garnet 2.2.12 (protokol Redis, dengan Lua). **Belum dijalankan terhadap Redis itu sendiri** |
| Orleans lobby grain, one silo in the server process / Grain lobby Orleans, satu silo di proses server | Tested / Teruji |
| Orleans across hosts, match grains / Orleans lintas host, grain pertandingan | Not implemented / Belum ada |
| SQLite backup and restore / Backup dan restore SQLite | Tested, including the scripts / Teruji, termasuk skripnya |
| `pg_dump` / `pg_restore` paths in the scripts / Jalur `pg_dump` / `pg_restore` | Run once in a recovery drill / Dijalankan sekali dalam latihan pemulihan |
| Keeper NPC model voice (`Npc`) / Suara model untuk Juru Kunci | Tested against one Azure OpenAI deployment (gpt-5-mini) / Teruji terhadap satu deployment Azure OpenAI (gpt-5-mini) |
| Load: 32 rooms × 10 players on loopback / Beban: 32 ruang × 10 pemain di loopback | Measured on one machine; see [PERFORMANCE.md](PERFORMANCE.md) / Diukur pada satu mesin |
| HTTPS, public deployment, multi-host failure tests, recovery drills / HTTPS, deployment publik, uji kegagalan multi-host, latihan pemulihan | Not done / Belum dilakukan |

The PostgreSQL and Redis-protocol rows were tested on one development machine with local servers (PostgreSQL 17.12, Garnet 2.2.12), not in a deployment. / Baris PostgreSQL dan protokol Redis diuji pada satu mesin pengembangan dengan server lokal, bukan pada deployment.

Treat the untested rows as a starting point that needs its own test pass before use. / Perlakukan baris yang belum teruji sebagai titik awal yang memerlukan pengujian tersendiri sebelum dipakai.

## Configuration / Konfigurasi

Settings live in the `Platform` section of `src/BattleNusantara.Server/appsettings.json`, and can be overridden by environment variables such as `Platform__RequireAccount=true`.

Pengaturan berada di bagian `Platform` pada `src/BattleNusantara.Server/appsettings.json`, dan dapat ditimpa dengan variabel lingkungan seperti `Platform__RequireAccount=true`.

| Key / Kunci | Default | Meaning / Arti |
| --- | --- | --- |
| `Database` | `sqlite` | `sqlite` or `postgres` |
| `ConnectionString` | `Data Source=data/nusantara.db` | A relative SQLite path is resolved against the server's content root / Jalur SQLite relatif dihitung dari content root server |
| `Queue` | `memory` | `memory` or `redis` |
| `Redis` | `localhost:6379` | Used only when `Queue` is `redis` / Hanya dipakai bila `Queue` adalah `redis` |
| `RequireAccount` | `false` | When true, guests cannot create, join or watch rooms / Bila true, tamu tidak dapat membuat, bergabung, atau menonton ruang |
| `PartySize` | `2` | Players a matchmade room starts with; clamped to 2–10 (MOBA 2–5) / Jumlah pemain per ruang matchmaking |
| `TicketSeconds` | `120` | How long a queue ticket waits before it is closed / Lama tiket antrean menunggu |
| `AuthPerMinute` | `30` | Register and login attempts per client address per minute / Percobaan daftar dan masuk per alamat per menit |
| `SiloPort`, `GatewayPort` | `11111`, `30000` | Orleans ports on this machine / Port Orleans di mesin ini |

Never put a real database password in `appsettings.json`; use an environment variable or a secret store. / Jangan menaruh kata sandi basis data di `appsettings.json`; gunakan variabel lingkungan atau penyimpan rahasia.

## Accounts and security / Akun dan keamanan

- Names are 3–20 letters, digits or underscores and unique ignoring case. Passwords are 8–128 characters, stored only as PBKDF2-SHA256 with 600,000 iterations and a per-password salt. / Nama 3–20 huruf, angka, atau garis bawah dan unik tanpa membedakan huruf besar-kecil. Kata sandi 8–128 karakter, hanya disimpan sebagai PBKDF2-SHA256 dengan 600.000 iterasi dan salt per kata sandi.
- A session is a random 256-bit token, valid for seven days; the database stores only its SHA-256. An account keeps at most five sessions. Signing out revokes the token on the server. / Sesi adalah token acak 256-bit yang berlaku tujuh hari; basis data hanya menyimpan SHA-256-nya. Satu akun memiliki paling banyak lima sesi. Keluar mencabut token di server.
- The client stores the token (never the password) in local storage and sends it only to the server that issued it. / Klien menyimpan token (bukan kata sandi) di local storage dan hanya mengirimkannya ke server yang menerbitkannya.
- The player's identity in a room comes from the authenticated connection, never from a value the client sends. / Identitas pemain di ruang berasal dari koneksi yang terotentikasi, bukan dari nilai kiriman klien.
- **The server speaks plain HTTP.** On anything but a trusted LAN, put it behind HTTPS; otherwise passwords and tokens cross the network unencrypted. / **Server memakai HTTP biasa.** Di luar LAN tepercaya, letakkan di belakang HTTPS; jika tidak, kata sandi dan token melintas tanpa enkripsi.
- Not implemented: password reset, e-mail, account deletion, per-account lockout (only the per-address rate limit), moderation. In the browser host all players share the Blazor server's address, so they also share its rate limit. / Belum ada: reset kata sandi, e-mail, penghapusan akun, penguncian per akun (hanya batas per alamat), moderasi. Pada host browser semua pemain memakai alamat server Blazor, sehingga berbagi batas yang sama.

## Matchmaking

A signed-in player joins the queue for a mode. When `PartySize` players wait for the same mode, the lobby creates a room, seats them and sends each a `Matched` message. One Orleans grain makes every matchmaking decision, so decisions are never concurrent. A ticket closes when the player cancels or disconnects, after `TicketSeconds`, or when the same account starts searching elsewhere. There is no skill rating: the queue is first come, first served.

Pemain yang sudah masuk bergabung ke antrean sebuah mode. Saat `PartySize` pemain menunggu mode yang sama, lobby membuat ruang, mendudukkan mereka, dan mengirim pesan `Matched`. Satu grain Orleans mengambil semua keputusan matchmaking, sehingga tidak pernah terjadi bersamaan. Tiket ditutup saat pemain membatalkan atau terputus, setelah `TicketSeconds`, atau saat akun yang sama mulai mencari di tempat lain. Belum ada peringkat kemampuan: antrean dilayani sesuai urutan datang.

## Keeper NPC voice / Suara Juru Kunci

`GET /api/npc/ask?topic=<id>&lang=id|en` answers one of the three authored Keeper topics (`kalatidha`, `gua`, `prasasti`). By default the reply is the authored text (`"source": "authored"`). With a model configured, the server asks it to reword that text in character (`"source": "llm"`). The player never sends free text, so nothing a player types reaches the prompt.

`GET /api/npc/ask?topic=<id>&lang=id|en` menjawab salah satu dari tiga topik naskah Juru Kunci. Secara bawaan jawabannya adalah teks naskah (`"source": "authored"`). Bila model dikonfigurasi, server memintanya menyusun ulang teks itu sesuai karakter (`"source": "llm"`). Pemain tidak pernah mengirim teks bebas, sehingga tidak ada ketikan pemain yang masuk ke prompt.

`POST /api/npc/talk` with `{ "npc": "keeper" | "captain", "text": "...", "lang": "id" | "en" }` answers a typed question of up to 160 characters (400 Bad Request otherwise). Without a model it is routed by keyword to an authored answer or the NPC's authored "I don't know". With a model the question is sent as the user message only, and each distinct question is cached (256 entries). Both endpoints share the `PerMinute` limit and the daily cap. / `POST /api/npc/talk` menjawab pertanyaan bebas hingga 160 karakter; tanpa model diarahkan lewat kata kunci.

| Key / Kunci (`Platform:Npc:`) | Default | Meaning / Arti |
| --- | --- | --- |
| `Provider` | `none` | `none` or `azure-openai` (through Semantic Kernel) |
| `Endpoint`, `Deployment`, `ApiKey` | empty | All three are required. Set the key with `Platform__Npc__ApiKey`, never in a file in the repository / Ketiganya wajib. Isi kunci lewat `Platform__Npc__ApiKey`, jangan di berkas repositori |
| `ReasoningEffort` | empty | For reasoning models; `minimal` cut gpt-5-mini from about 5 s to about 2 s per reply / Untuk model penalaran; `minimal` memangkas gpt-5-mini dari sekitar 5 dtk menjadi sekitar 2 dtk |
| `TimeoutSeconds` | `8` | A slower reply is dropped and the authored answer is used / Jawaban yang lebih lambat dibuang |
| `MaxTokens` | `1200` | Output cap per call, hidden reasoning tokens included / Batas keluaran per panggilan |
| `Variants` | `3` | Replies cached per topic and language; after that no more calls are made / Jawaban yang disimpan per topik dan bahasa; setelah itu tidak ada panggilan lagi |
| `DailyCalls` | `200` | Hard cap per UTC day / Batas keras per hari UTC |
| `PerMinute` | `12` | Questions per client address per minute / Pertanyaan per alamat per menit |

With the defaults a server makes at most 3 topics × 2 languages × 3 variants = 18 successful calls until it restarts; failed calls are bounded by `DailyCalls`. A reply is rejected, and the authored text used, if it is empty, longer than 360 characters, or contains a link or markup. Replies are cached in memory only. The model's wording is not reviewed by a person before players see it; leave the provider at `none` if that is not acceptable.

Dengan nilai bawaan, server melakukan paling banyak 18 panggilan berhasil sampai dimulai ulang; panggilan gagal dibatasi `DailyCalls`. Jawaban ditolak, dan teks naskah dipakai, bila kosong, lebih dari 360 karakter, atau memuat tautan atau markup. Jawaban hanya disimpan di memori. Susunan kata dari model tidak ditinjau manusia sebelum dilihat pemain; biarkan provider `none` bila hal itu tidak dapat diterima.

## Backup and restore / Backup dan restore

```powershell
./scripts/backup.ps1                       # writes backups/nusantara-<UTC time>.db, keeps the newest 14
./scripts/restore.ps1 -File backups/nusantara-20261116-101500.db -Force   # stop the server first
```

The backup uses SQLite's online backup API, so it is consistent while the server runs, and the result is one self-contained file that is verified (integrity check and schema version) before the script reports success. Restore verifies the file before touching the live database and refuses to run without `-Force`. **Stop the server before restoring.** Backups are not encrypted and contain password hashes: store them accordingly. Schedule the backup with Windows Task Scheduler; nothing schedules it for you.

Backup memakai API backup online SQLite, sehingga konsisten saat server berjalan, dan hasilnya satu berkas mandiri yang diverifikasi (pemeriksaan integritas dan versi skema) sebelum skrip melaporkan berhasil. Restore memverifikasi berkas sebelum menyentuh basis data aktif dan menolak berjalan tanpa `-Force`. **Hentikan server sebelum restore.** Backup tidak dienkripsi dan memuat hash kata sandi: simpanlah dengan semestinya. Jadwalkan backup lewat Windows Task Scheduler; tidak ada penjadwalan bawaan.

For PostgreSQL the scripts call `pg_dump` and `pg_restore` with `-Postgres "<connection URI>"`; both tools must be on `PATH`. This was run once as a drill on a local PostgreSQL 17: register an account, back up, delete the rows, restore, sign in again. / Untuk PostgreSQL skrip memanggil `pg_dump` dan `pg_restore` dengan `-Postgres "<URI koneksi>"`; keduanya harus ada di `PATH`. Jalur ini dijalankan sekali sebagai latihan pada PostgreSQL 17 lokal: daftar akun, backup, hapus baris, restore, masuk lagi.

Rooms and queue tickets are held in memory and are lost when the server restarts; only accounts, sessions and finished match results are durable. / Ruang dan tiket antrean disimpan di memori dan hilang saat server dimulai ulang; hanya akun, sesi, dan hasil pertandingan selesai yang tersimpan permanen.

## Observing the server / Memantau server

`GET /health` returns the version. `GET /api/status` returns counts and tick timings, with nothing that identifies a player:

`GET /health` mengembalikan versi. `GET /api/status` mengembalikan jumlah dan waktu tick, tanpa data yang mengidentifikasi pemain:

```json
{ "version": "0.13.0", "rooms": 2, "players": 3, "spectators": 0, "queued": 1, "ticks": 5321, "overruns": 0, "meanTickMs": 0.41, "worstTickMs": 6.2 }
```

`overruns` counts ticks whose simulation and snapshot delivery took longer than 100 ms. A rising number means the host is overloaded. There is no dashboard, alerting or log shipping yet. / `overruns` menghitung tick yang simulasi dan pengiriman snapshot-nya melebihi 100 ms. Angka yang terus naik berarti host kelebihan beban. Belum ada dashboard, peringatan, atau pengiriman log.

## Load test / Uji beban

```powershell
$env:NUSANTARA_LOAD_ROOMS = 32; $env:NUSANTARA_LOAD_SECONDS = 30
dotnet test tests/BattleNusantara.Tests -c Release --filter "FullyQualifiedName~LoadTests" --logger "console;verbosity=detailed"
```

The line starting with `LOAD` holds the result. Clients and server share one machine and talk over loopback, so this shows what one host can simulate and serialize, not how it behaves over a real network. / Baris yang diawali `LOAD` memuat hasilnya. Klien dan server berbagi satu mesin dan berbicara lewat loopback, sehingga ini menunjukkan kemampuan satu host mensimulasikan dan menserialisasi, bukan perilakunya pada jaringan nyata.

## Local test services / Layanan uji lokal

Nine xUnit tests talk to real services and are skipped unless these are set, either as environment variables or as `KEY=VALUE` lines in `~/.nusantara/test.env` (outside the repository; it holds secrets).

Sembilan tes xUnit berbicara dengan layanan nyata dan dilewati kecuali nilai berikut diatur, sebagai variabel lingkungan atau baris `KEY=VALUE` di `~/.nusantara/test.env` (di luar repositori; berisi rahasia).

| Variable / Variabel | Used by / Dipakai oleh |
| --- | --- |
| `NUSANTARA_TEST_POSTGRES` | `PostgresStoreTests`: an Npgsql connection string; each test creates and drops its own schema / string koneksi Npgsql; tiap tes membuat dan menghapus skemanya sendiri |
| `NUSANTARA_TEST_REDIS` | `RedisQueueTests`: `host:port` of a Redis-protocol server with Lua scripting / server berprotokol Redis dengan skrip Lua |
| `NUSANTARA_TEST_NPC_ENDPOINT`, `_DEPLOYMENT`, `_KEY`, optional `_EFFORT` | `NpcLiveTests`: an Azure OpenAI deployment; two small calls per run / dua panggilan kecil per jalan |

On the development machine these point at PostgreSQL 17 (Windows service `postgresql-x64-17`, port 5432, database and role `nusantara_test`) and at Garnet, installed as the .NET tool `garnet-server` and started by hand with `garnet-server --bind 127.0.0.1 --port 6379 --lua`. Garnet is not a service: start it again after a reboot. Memurai could not be installed on that machine.

Pada mesin pengembangan, nilai itu menunjuk ke PostgreSQL 17 (layanan Windows `postgresql-x64-17`, port 5432, basis data dan role `nusantara_test`) dan ke Garnet, yang dipasang sebagai tool .NET `garnet-server` dan dijalankan manual dengan `garnet-server --bind 127.0.0.1 --port 6379 --lua`. Garnet bukan layanan: jalankan lagi setelah reboot. Memurai gagal dipasang pada mesin tersebut.
