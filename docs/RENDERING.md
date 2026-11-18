# Rendering evaluation / Evaluasi rendering

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

PLAN.md asks to "evaluate WebGPU, ray tracing and destruction against actual device budgets". This page records what was measured, what was only reasoned about, and the decision for v0.12. **Only WebGPU was measured.** Ray tracing and destruction were assessed on paper and were not prototyped.

PLAN.md meminta "evaluasi WebGPU, ray tracing, dan penghancuran terhadap anggaran perangkat nyata". Halaman ini mencatat apa yang diukur, apa yang hanya dinalar, dan keputusan untuk v0.12. **Hanya WebGPU yang diukur.** Ray tracing dan penghancuran dinilai di atas kertas dan tidak dibuat prototipenya.

## The device / Perangkat

One machine: the development laptop, Intel Core i7-8650U with integrated Intel UHD Graphics 620, Windows 11, Microsoft Edge. It is a 2017-class integrated GPU, which is a reasonable floor for a browser game and a poor predictor for discrete GPUs. No other device was measured.

Satu mesin: laptop pengembangan, Intel Core i7-8650U dengan Intel UHD Graphics 620 terintegrasi, Windows 11, Microsoft Edge. Ini GPU terintegrasi kelas 2017: batas bawah yang wajar untuk game browser, dan bukan peramal yang baik bagi GPU diskret. Perangkat lain tidak diukur.

## WebGPU against WebGL2 / WebGPU dibanding WebGL2

Method: a stand-alone page with Three.js 0.180.0, once with `WebGLRenderer` and once with `WebGPURenderer` (which reported the `WebGPUBackend`, so it was not the WebGL fallback). The scene imitates the game's cost profile: the arena slab, one shadow-casting sun with a 2048² shadow map, and N separate animated meshes with their own standard materials (the game draws each unit as a few separate meshes). 1440×900, headed Edge on the real GPU, 300 measured frames after 60 warm-up frames. "CPU" is the time spent inside `render()` on the main thread.

Metode: halaman mandiri dengan Three.js 0.180.0, sekali dengan `WebGLRenderer` dan sekali dengan `WebGPURenderer` (melaporkan `WebGPUBackend`, jadi bukan fallback WebGL). Scene meniru profil biaya game: lantai arena, satu matahari berbayangan dengan shadow map 2048², dan N mesh beranimasi terpisah dengan material standar masing-masing. 1440×900, Edge tampil pada GPU nyata, 300 frame terukur setelah 60 frame pemanasan. "CPU" adalah waktu di dalam `render()` pada thread utama.

| Meshes / Mesh | Renderer | CPU per frame | Frames per second / Frame per detik |
| --- | --- | --- | --- |
| 600 | WebGL2 | 7.6 ms | 60 (display limit / batas layar) |
| 600 | WebGPU | 15.9 ms | 52.8 |
| 3,000 | WebGL2 | 24.9 ms | 37.8 |
| 3,000 | WebGPU | 53.7 ms | 18.1 |

On this device WebGPU cost about twice the main-thread time of WebGL2 and delivered fewer frames at both sizes. One run per cell; the differences are far larger than run-to-run noise would explain, but the exact figures should not be quoted as precise.

Pada perangkat ini WebGPU memakan sekitar dua kali waktu thread utama WebGL2 dan menghasilkan lebih sedikit frame pada kedua ukuran. Satu kali jalan per sel; selisihnya jauh lebih besar daripada derau antarjalan, tetapi angka persisnya jangan dikutip sebagai presisi.

**Decision: stay on WebGL2 / Keputusan: tetap WebGL2.** Reasons:

- It is slower on the one device measured, and the game's bottleneck is draw-call overhead, which this test exercises directly. / Lebih lambat pada satu-satunya perangkat yang diukur.
- The desktop host runs in WebView2 and the browser tests run on SwiftShader; both are known-good with WebGL2. / Host desktop memakai WebView2 dan tes browser memakai SwiftShader; keduanya terbukti baik dengan WebGL2.
- `three.webgpu.js` is a second, larger renderer build that would have to be vendored for offline use, and the ocean shader uses `onBeforeCompile`, which the WebGPU renderer does not support (it would need a rewrite in TSL). / Build renderer kedua yang lebih besar harus di-vendor, dan shader laut memakai `onBeforeCompile` yang tidak didukung renderer WebGPU.

Re-measure when a discrete-GPU device is available or when the unit count needs GPU-driven instancing or compute. / Ukur ulang bila tersedia perangkat ber-GPU diskret atau bila jumlah unit menuntut instancing atau compute berbasis GPU.

## The game itself in v0.12 / Game itu sendiri di v0.12

Same laptop, real GPU, high quality, 1440×900: MOBA with 17–23 units and the opening cinematic, and Adventure in the rain, both held a smoothed frame time of about 16.7 ms (the 60 Hz display limit) with 80–155 draw calls and about 31,000–36,000 triangles. The enforced budgets are in [PERFORMANCE.md](PERFORMANCE.md).

Laptop yang sama, GPU nyata, kualitas tinggi, 1440×900: MOBA dengan 17–23 unit dan sinematik pembuka, serta Adventure saat hujan, keduanya bertahan pada waktu frame rata-rata sekitar 16,7 ms (batas layar 60 Hz) dengan 80–155 draw call dan sekitar 31.000–36.000 segitiga. Anggaran yang dijaga tes ada di [PERFORMANCE.md](PERFORMANCE.md).

## Landscapes and bloom in v0.14 / Lanskap dan bloom di v0.14

Same laptop, real GPU (ANGLE on Direct3D 11), Edge, 1440×900, measured with `stats()` a few seconds into each match. v0.14 draws a whole island per map: a terrain heightfield, the sea, instanced nature, a village with its people, jungle camps and wildlife.

Laptop yang sama, GPU nyata (ANGLE di Direct3D 11), Edge, 1440×900, diukur dengan `stats()` beberapa detik setelah pertandingan dimulai. v0.14 menggambar satu pulau penuh per peta.

| Scene / Adegan | Quality / Kualitas | Draw calls | Triangles / Segitiga | Frame time / Waktu frame |
| --- | --- | --- | --- | --- |
| MOBA, Jagat Arena, ~38 units | high, bloom + FXAA | 336 | 600,000 | 16.7 ms |
| MOBA, Dragon Bridges (large), ~50 units | high, bloom + FXAA | 390 | 875,000 | about 20 ms |
| MOBA, Jagat Arena | high, bloom with 4× MSAA | 333 | 600,000 | 31–36 ms |
| MOBA, Jagat Arena | high, no bloom | 323 | 602,000 | 16.7 ms |
| MOBA, Jagat Arena | power saving, decimated characters | 171 | 181,000 | 16.7 ms |

Shadows and triangles were not the problem: without bloom the large scene held the 60 Hz limit. The cost was a **multisampled half-float render target**, which roughly doubled the frame time on this integrated GPU; without multisampling the same bloom chain fits. The chain is therefore bloom, an output pass, a colour grade and **FXAA** for edges, on a half-float target without MSAA. If frames still stay above 26 ms for four seconds, the game turns bloom off for the rest of the session. A software rasterizer (SwiftShader, used by the browser tests) never gets bloom or shadows and draws half the scattered nature; there, a MOBA frame at power saving took about 280 ms.

Bayangan dan jumlah segitiga bukan masalahnya: tanpa bloom adegan besar tetap pada batas 60 Hz. Biayanya berasal dari **render target half-float dengan multisampling**, yang kira-kira menggandakan waktu frame pada GPU terintegrasi ini. Karena itu rantainya kini bloom, output, grade warna, dan **FXAA** pada target half-float tanpa MSAA. Bila frame tetap di atas 26 ms selama empat detik, bloom dimatikan untuk sisa sesi. Rasterizer perangkat lunak (SwiftShader) tidak pernah mendapat bloom atau bayangan dan hanya menggambar setengah dekorasi alam.

## Ray tracing — not measured / tidak diukur

Assessment only. Browsers expose no hardware ray-tracing API: WebGL2 has none, and WebGPU has no ray-tracing extension in shipping Edge. Ray tracing in the browser therefore means tracing in a fragment or compute shader against a hand-built acceleration structure. The measured device already spends about a third of its frame budget rasterizing a few hundred small meshes, so a software path tracer at 1440×900 is not plausible there. The scene is also a top-down arena lit by one sun, where a shadow map already gives the visible result. **Decision: not pursued.** If richer lighting is wanted, the cheaper candidates are baked ambient occlusion in the Blender assets and screen-space effects, each measured before adoption.

Hanya penilaian. Browser tidak menyediakan API ray tracing perangkat keras. Ray tracing di browser berarti menelusuri sinar di fragment atau compute shader terhadap struktur akselerasi buatan sendiri. Perangkat yang diukur sudah memakai sekitar sepertiga anggaran frame untuk merasterisasi beberapa ratus mesh kecil, sehingga path tracer perangkat lunak pada 1440×900 tidak masuk akal di sana. **Keputusan: tidak dilanjutkan.** Bila pencahayaan lebih kaya diinginkan, kandidat yang lebih murah adalah ambient occlusion yang di-bake pada aset Blender dan efek screen-space, masing-masing diukur sebelum dipakai.

## Destruction — not measured / tidak diukur

Assessment only. Two different things are meant by destruction:

- **Simulated destruction** (debris with physics that blocks units) would have to live in the deterministic 100 ms simulation, be sent in snapshots and rebuild navigation, as outposts already do. That is a gameplay feature with a network cost, not an art feature. / **Penghancuran tersimulasi** harus hidup di simulasi deterministik, dikirim lewat snapshot, dan membangun ulang navigasi. Itu fitur gameplay dengan biaya jaringan, bukan fitur seni.
- **Cosmetic destruction** (a tower or outpost breaking into pre-fractured pieces on the client when the snapshot says it died) needs no protocol change. Its cost is a short burst of extra draw calls; with the current budget of 400 calls and about 150 used, a few dozen pieces for a second or two would fit. / **Penghancuran kosmetik** tidak memerlukan perubahan protokol. Biayanya lonjakan draw call singkat; dengan anggaran 400 dan pemakaian sekitar 150, beberapa puluh pecahan selama satu-dua detik masih muat.

**Decision: not built in v0.12.** The cosmetic form is the reasonable next step once towers and outposts have real models to fracture in Blender; they are still primitives.

**Keputusan: tidak dibuat di v0.12.** Bentuk kosmetik adalah langkah berikutnya yang wajar setelah menara dan pos memiliki model nyata untuk dipecah di Blender; saat ini keduanya masih primitif.
