# Performance budgets / Anggaran performa

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

These are the budgets proposed for the v0.10 prototype and the measurements behind them. They were taken on **one development machine**, not on agreed target hardware, and the renderer numbers come from the automated browser run, which uses software rendering (SwiftShader). Treat the budgets as a regression guard until they are confirmed on real target devices.

Ini adalah anggaran yang diusulkan untuk prototipe v0.10 beserta pengukurannya. Pengukuran dilakukan pada **satu mesin pengembangan**, bukan perangkat target yang disepakati, dan angka renderer berasal dari pengujian browser otomatis yang memakai rendering perangkat lunak (SwiftShader). Perlakukan anggaran ini sebagai penjaga regresi sampai dikonfirmasi pada perangkat target sebenarnya.

## Budgets / Anggaran

| Metric / Metrik | Budget / Anggaran | Enforced by / Dijaga oleh |
| --- | --- | --- |
| Simulation tick + snapshot, mean / Tick simulasi + snapshot, rata-rata | < 5 ms (5% of the 100 ms tick / 5% dari tick 100 ms) | `PerformanceTests` |
| Managed allocation per tick / Alokasi per tick | < 256 KB | `PerformanceTests` |
| Snapshot JSON size / Ukuran JSON snapshot | < 32 KB | `PerformanceTests` |
| Units per match / Unit per pertandingan | ≤ 100 (`Match.MaxUnits`) | `PerformanceTests`, `FullMatchTests` |
| Adventure save size / Ukuran simpanan Adventure | ≤ 12,000 characters / karakter | `PerformanceTests`, `world.js` |
| Renderer draw calls per frame / Draw call per frame | < 400 | Browser test "renderer stays inside its draw budget" |
| Renderer triangles per frame / Segitiga per frame | < 150,000 | Same browser test / Tes browser yang sama |

The asserted limits are deliberately several times looser than the measurements, so the tests catch regressions rather than machine noise. / Batas yang diuji sengaja beberapa kali lebih longgar daripada hasil ukur, agar tes menangkap regresi, bukan derau mesin.

## Measurements / Pengukuran

Simulation, Release build, .NET 10, single thread. "Idle" runs ten simulated minutes with no player input; "scripted" plays the full match with the policy from `FullMatchTests`. Network traffic is the largest snapshot serialized as JSON times the 10 Hz tick rate, per connected client, before transport framing or compression.

Simulasi, build Release, .NET 10, satu thread. "Idle" menjalankan sepuluh menit simulasi tanpa masukan pemain; "scripted" memainkan pertandingan penuh dengan kebijakan dari `FullMatchTests`. Lalu lintas jaringan adalah snapshot terbesar dalam JSON dikali laju 10 Hz, per klien, sebelum pembingkaian transport atau kompresi.

| Scenario / Skenario | Tick mean | Tick worst | Snapshot mean | Allocation / tick | Largest snapshot | Traffic / client | Peak units |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RTS idle | 0.006 ms | 0.536 ms | 0.002 ms | 1.9 KB | 1.3 KB | 12.9 KB/s | 3 |
| RTS scripted | 0.032 ms | 0.072 ms | 0.004 ms | 5.1 KB | 3.1 KB | 30.0 KB/s | 10 |
| MOBA idle | 0.015 ms | 0.638 ms | 0.002 ms | 7.0 KB | 5.6 KB | 55.0 KB/s | 24 |
| MOBA scripted | 0.018 ms | 0.244 ms | 0.005 ms | 6.6 KB | 5.2 KB | 51.0 KB/s | 22 |
| Adventure idle | 0.010 ms | 0.514 ms | 0.004 ms | 5.8 KB | 3.8 KB | 36.8 KB/s | 15 |
| Adventure scripted | 0.006 ms | 0.016 ms | 0.003 ms | 3.7 KB | 3.3 KB | 32.4 KB/s | 12 |

A saved Adventure after three completed quests is 2,326 characters. / Simpanan Adventure setelah tiga misi selesai berukuran 2.326 karakter.

Renderer, v0.13, MOBA at about 45 seconds with 23 units on screen (power-saving quality, reduced motion, 1440×1000, Edge with SwiftShader): **162 draw calls, 38,458 triangles, 115 geometries**, with every unit a Blender model. In v0.12 the same scene was 154 calls and 37,788 triangles, so the per-unit models cost almost nothing extra: each character is still two skinned primitives, and the shared template geometry is uploaded once. The smoothed frame time in that run was about 174 ms, which reflects software rendering in the test browser, not player hardware. On the development laptop with its real GPU (Intel UHD 620, high quality) the game held about 16.7 ms per frame in MOBA and Adventure; see [RENDERING.md](RENDERING.md).

Renderer, v0.13, MOBA pada sekitar detik ke-45 dengan 23 unit di layar (kualitas hemat daya, kurangi animasi, 1440×1000, Edge dengan SwiftShader): **162 draw call, 38.458 segitiga, 115 geometri**, dengan setiap unit berupa model Blender. Pada v0.12 scene yang sama 154 draw call dan 37.788 segitiga, sehingga model per unit hampir tidak menambah biaya: setiap karakter tetap dua primitif ber-skin, dan geometri template diunggah sekali. Waktu frame rata-rata pada pengujian itu sekitar 174 ms; angka ini mencerminkan rendering perangkat lunak pada browser uji. Pada laptop pengembangan dengan GPU nyata (Intel UHD 620, kualitas tinggi) game bertahan sekitar 16,7 ms per frame di MOBA dan Adventure; lihat [RENDERING.md](RENDERING.md).

## What the numbers say / Arti angka-angka ini

- The simulation uses well under 1% of its tick, so 32 rooms on one server process are far inside the budget. No Rust or native code is justified. / Simulasi memakai jauh di bawah 1% dari tick, sehingga 32 ruang pada satu proses server masih jauh di dalam anggaran. Rust atau kode native belum beralasan.
- Allocation is a few kilobytes per tick, almost all from building the immutable snapshot. It is acceptable now and is the first thing to pool if unit counts grow. / Alokasi beberapa kilobyte per tick, hampir semuanya dari pembuatan snapshot. Saat ini dapat diterima dan menjadi kandidat pertama untuk pooling bila jumlah unit bertambah.
- Traffic is dominated by sending the full snapshot every tick. Extrapolating to 100 units gives a snapshot of roughly 20 KB (200 KB/s per client), still inside the 32 KB message budget; delta snapshots are the next step before raising player counts. / Lalu lintas didominasi pengiriman snapshot penuh tiap tick. Ekstrapolasi ke 100 unit memberi snapshot sekitar 20 KB (200 KB/dtk per klien), masih di dalam anggaran pesan 32 KB; snapshot delta adalah langkah berikutnya sebelum menaikkan jumlah pemain.
- Each unit is drawn as a few separate meshes: a humanoid is two skinned primitives, a ring and a health bar, plus two shadow draws on high quality, and each has its own animation mixer. Instanced skinning would cut this, but it is not needed at 100 units. / Tiap unit digambar sebagai beberapa mesh terpisah: humanoid berupa dua primitif ber-skin, cincin, dan bilah HP, ditambah dua draw bayangan pada kualitas tinggi, masing-masing dengan mixer animasi sendiri. Skinning ber-instance dapat memangkasnya, tetapi belum diperlukan pada 100 unit.

## Server under load / Server di bawah beban

Added in v0.11. Real SignalR clients against the real server pipeline, all in one process on the development machine over loopback: 32 rooms of 10 players (the room and player limits), every player sending two orders a second, for 30 seconds.

Ditambahkan di v0.11. Klien SignalR nyata terhadap pipeline server nyata, semuanya dalam satu proses pada mesin pengembangan lewat loopback: 32 ruang berisi 10 pemain (batas ruang dan pemain), tiap pemain mengirim dua perintah per detik, selama 30 detik.

| Rooms × players | Clients | Tick mean | Tick worst | Ticks over 100 ms | Snapshots delivered | Total traffic | Orders accepted |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 4 × 10 | 40 | 4.8 ms | 16.1 ms | 0 of 51 | 100% | about 1.3 MB/s | 364 of 400 |
| 32 × 10 | 320 | 16.6 ms | 57.6 ms | 0 of 305 | 100% | about 10 MB/s | 17,696 of 18,880 |

Here a tick includes simulating every room and handing every snapshot to SignalR. Budget: no more than 5% of ticks over 100 ms and at least 90% of snapshots delivered, enforced by `LoadTests` at the small size on every run. Traffic is estimated from sampled snapshot sizes, about 31 KB/s per client. Orders that were not accepted were moves the simulation rejected, not lost messages. Because the 320 clients share the server's CPU, a dedicated server should do better, but that has not been measured, and neither has a real network.

Di sini satu tick mencakup simulasi semua ruang dan penyerahan semua snapshot ke SignalR. Anggaran: paling banyak 5% tick melebihi 100 ms dan sedikitnya 90% snapshot terkirim, dijaga `LoadTests` pada ukuran kecil di setiap jalan. Lalu lintas diperkirakan dari sampel ukuran snapshot, sekitar 31 KB/dtk per klien. Perintah yang tidak diterima adalah gerakan yang ditolak simulasi, bukan pesan yang hilang. Karena 320 klien berbagi CPU dengan server, server khusus seharusnya lebih baik, tetapi itu belum diukur, begitu pula jaringan nyata.

## Not measured / Belum diukur

Frame rate and GPU time on real hardware, memory over long sessions, the WPF desktop host, real network conditions (latency, loss), and more than two players plus spectators in a room. / Laju frame dan waktu GPU pada perangkat nyata, memori pada sesi panjang, host desktop WPF, kondisi jaringan nyata (latensi, kehilangan paket), dan lebih dari dua pemain plus penonton dalam satu ruang.

## Reproducing / Mengulang pengukuran

```powershell
dotnet test tests/BattleNusantara.Tests -c Release --filter "FullyQualifiedName~PerformanceTests" --logger "console;verbosity=detailed"
npx playwright test -g "renderer stays inside its draw budget"
```

The simulation lines start with `PERF`; the renderer line starts with `RENDER`. In the browser, `stats()` exported by `world.js` returns the last frame's draw calls, triangles and smoothed frame time. / Baris simulasi diawali `PERF`; baris renderer diawali `RENDER`. Di browser, `stats()` dari `world.js` mengembalikan draw call, segitiga, dan waktu frame rata-rata dari frame terakhir.
