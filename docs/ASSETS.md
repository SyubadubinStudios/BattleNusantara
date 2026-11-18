# Assets and notices / Aset dan atribusi

## Original assets / Aset asli

`assets/blender/create_temple.py` creates the quantum shrine in an isolated Blender scene. It was executed through Blender MCP and exports `src/BattleNusantara.UI/wwwroot/models/quantum-shrine.glb` plus an editable `assets/blender/quantum-shrine.blend`. The crystal has a rotation animation. The user's original Blender scene is preserved. Change the script's `ROOT` for another checkout.

Skrip tersebut dijalankan melalui Blender MCP, menggunakan scene terpisah dan mempertahankan scene awal pengguna. Model dan animasi inti merupakan aset asli prototipe. Skrip menemukan root repositori dari lokasinya sendiri; saat dijalankan lewat `exec()`, sertakan `__file__` atau atur `NUSANTARA_ROOT`. / The scripts find the repository root from their own location; when run through `exec()`, pass `__file__` or set `NUSANTARA_ROOT`.

`assets/blender/create_characters.py` (v0.13) builds 24 characters on one rig with six clips and Naga Basuki on its own rig, exporting `models/characters.glb`, `models/naga.glb` and `assets/blender/characters.blend`. `assets/blender/create_props.py` builds the tower, outpost, inscription, keris, mine and ship fragments (`models/props.glb`, `assets/blender/props.blend`). Both ran through Blender MCP. All designs are original; historical and mythological figures are interpreted freely, and no licensed comic or film character is modelled. The v0.12 warrior was replaced and removed. / Kedua skrip dijalankan melalui Blender MCP. Semua desain orisinal; tokoh sejarah dan mitologi ditafsirkan bebas, dan tidak ada tokoh komik atau film berlisensi yang dimodelkan.

**Audio formats / Format audio.** Every sound ships as Ogg Vorbis and as AAC (`.m4a`); the client picks Ogg where the browser plays it and AAC on WebKit for macOS. / Setiap suara tersedia sebagai Ogg Vorbis dan AAC.

**v0.14 world / Dunia v0.14.** All Blender assets were rebuilt through Blender MCP on a shared modelling kit (`assets/blender/nusantara_kit.py`: lofted superellipse bodies, bevels, vertex colours and ambient occlusion baked with Cycles into the vertex colours). `create_characters.py` now builds 34 variants on the shared rig: the 24 characters, six jungle creeps (tuyul, siluman kera, siluman macan, siluman buaya, genderuwo, Banaspati) and four villagers (petani, nelayan, ibu, anak). `create_lods.py` exports `models/characters-lod.glb`, the same variants decimated to about a third of their triangles for power saving. `create_temple.py` builds a terraced candi with relief panels, gates and lattice stupas. `create_props.py` adds the loot (a treasure chest, a jamu jar, an amulet and a mustika gem). `create_environment.py` builds the 36-object nature and village kit (`models/environment.glb`: palms, broadleaf trees, cemara, bamboo, banana, bushes, grass, reeds, ferns, rocks, cliffs, ruins, a candi bentar gate, statues, lamps, torches, banners, fences, huts, boats, a pier, rice fields, lilies, dead trees, dry shrubs and dunes; it also holds mountain and volcano pieces that the game no longer draws, since mountains are now raised from the terrain) and `create_animals.py` five animals on one four-legged rig (`models/animals.glb`: kancil, kerbau, babi hutan, kera pencuri, ayam). All designs are original. / Semua aset Blender dibangun ulang lewat Blender MCP dengan kit bersama: 34 varian karakter (termasuk monster hutan dan penduduk), versi ringan untuk hemat daya, candi berteras, harta, kit alam dan desa berisi 36 objek, serta lima hewan. Semua desain orisinal.

**Sound / Suara (v0.14).** `scripts/compose_audio.py` composes and synthesizes all music, ambience and sound effects with numpy and scipy (no samples, no recordings) and encodes them to Ogg Vorbis with ffmpeg into `wwwroot/audio/` (about 4 MB): the title theme, the exploration music and its battle layer, victory and defeat stingers, day, night and rain ambience, and 23 effects. They are original works of the project. ffmpeg (with libvorbis) is a build-time tool only and is not distributed. / Semua musik, suasana, dan efek suara digubah dan disintesis oleh `scripts/compose_audio.py`, tanpa sampel atau rekaman, lalu dikodekan ke Ogg Vorbis dengan ffmpeg. ffmpeg hanya alat saat build dan tidak didistribusikan.

**Licensed characters / Tokoh berlisensi.** The concept named comic and film heroes (for example Gundala and Sri Asih). They are owned by their rights holders and are not in the game. Satria Bledhek and Rara Angkasa are original heroes with comparable powers; their names, looks, stories and portraits were made for this project. / Konsep menyebut pahlawan komik dan film. Tokoh-tokoh itu tidak ada di game; Satria Bledhek dan Rara Angkasa adalah hero orisinal.

**Portraits / Potret.** The six hero portraits in `wwwroot/portraits/` (WebP, 384×576) were generated with GPT Image (via the Rodin MCP, GPT Image Flare) from prompts that describe our own designs, and reviewed before use; the 1024×1536 sources are in `assets/concepts/`. Check the image service's terms before a commercial release. / Dibuat dengan GPT Image dari prompt yang menggambarkan desain sendiri, lalu ditinjau.

**Voices / Suara.** The 32 clips in `wwwroot/voice/` (16 dialogue lines × Indonesian and English, MP3 64 kbps, about 1.6 MB) were generated with ElevenLabs (model eleven_v3; voices George, Bill, Adam and Callum from the ElevenLabs library) on the **free plan**. The free plan requires attribution ("Voices: ElevenLabs", shown in the credits) and does **not** permit commercial use. Regenerate them on a paid plan before any commercial release. / Dibuat dengan ElevenLabs **paket gratis**: wajib atribusi dan **tidak** boleh dipakai komersial; buat ulang dengan paket berbayar sebelum rilis komersial.

**Marketing / Pemasaran.** The key art in `assets/marketing/` (prompts describe our own hero designs) was generated with GPT Image via the Rodin MCP; `scripts/marketing.py` composes it with the portraits and real screenshots into `docs/marketing/`. / Key art dibuat dengan GPT Image; `scripts/marketing.py` menyusunnya menjadi materi media sosial.

**Music / Musik.** The gamelan-orchestral music is an original composition written for the game and rendered offline by `scripts/compose_audio.py`; no recording or sample is bundled. Until v0.13 it was synthesized by Web Audio at runtime. / Musik gamelan-orkestra adalah gubahan asli yang dirender oleh `scripts/compose_audio.py`; tidak ada rekaman atau sampel.

Hero symbols in menus, the terrain and its colours, the sea, rain, lighting, particles, birds and butterflies are procedural. There are no commercial character models and no imported third-party art.

Simbol hero, medan dan warnanya, laut, hujan, pencahayaan, partikel, burung, dan kupu-kupu bersifat prosedural. Tidak ada model tokoh komersial atau seni pihak ketiga yang diimpor.

Version 0.2 reuses the original Blender shrine for fixed RTS ruins; collision bounds come from the server snapshot. No new third-party model is introduced. / Versi 0.2 memakai ulang candi Blender sebagai reruntuhan RTS dengan batas collision dari snapshot server.

## Third-party runtime / Runtime pihak ketiga

- Three.js **0.180.0**, MIT; notice copied to `src/BattleNusantara.UI/wwwroot/vendor/THREE-LICENSE.txt`.
- .NET, ASP.NET Core, and WPF packages retain their upstream licenses. Package versions are pinned in project files.
- Display fonts use installed Palatino Linotype/Georgia; body text uses installed Segoe UI/system sans-serif. No font files are redistributed.
- All sound in `wwwroot/audio/` is synthesized by the project's own script; the only recordings bundled are the voice clips described above. / Semua suara di `wwwroot/audio/` disintesis skrip proyek sendiri; satu-satunya rekaman adalah klip suara dialog di atas.
- `SkeletonUtils.js`, `BufferGeometryUtils.js` and the post-processing files (`EffectComposer`, `RenderPass`, `ShaderPass`, `MaskPass`, `Pass`, `UnrealBloomPass`, `OutputPass`, `CopyShader`, `LuminosityHighPassShader`, `OutputShader`, `FXAAShader`) come from the same Three.js package (MIT) and are vendored by `npm run vendor`.
- Microsoft Semantic Kernel **1.82.0**, MIT, used by the server only for the optional Keeper voice. Text it generates comes from the configured Azure OpenAI deployment under that service's terms; none is stored in the repository. / Dipakai server hanya untuk suara Juru Kunci opsional; teks buatan model tidak disimpan di repositori.
- Photino.Blazor **4.0.13** and Photino.NET, Apache License 2.0, used by the macOS and Linux desktop host (`src/BattleNusantara.Photino`). / Dipakai host desktop macOS dan Linux.
- PostgreSQL and Garnet are test-time services on the development machine and are not distributed with the game. / PostgreSQL dan Garnet hanya layanan uji, tidak didistribusikan bersama game.

The design lists additional fictional characters, but they are not shipped as game assets in this prototype. Obtain appropriate permissions before producing/distributing third-party character content. 

Nama karakter fiksi tambahan ada pada desain, belum disertakan sebagai aset game. Perizinan konten pihak ketiga harus diselesaikan sebelum distribusi. 

## Licences / Lisensi

The code is released under the MIT licence in `LICENSE`. The original models, portraits, music and sound effects in this repository are part of the project and may be used under the same terms. The voice clips in `wwwroot/voice/` are **not** covered by the MIT licence: they were generated on the ElevenLabs free plan and remain subject to its terms (attribution, non-commercial use). Third-party runtime components keep their own licences, listed above.

Kode dirilis dengan lisensi MIT pada `LICENSE`. Model, potret, musik, dan efek suara asli di repositori ini adalah bagian proyek dan dapat dipakai dengan ketentuan yang sama. Klip suara di `wwwroot/voice/` **tidak** termasuk lisensi MIT: klip itu dibuat dengan paket gratis ElevenLabs dan tetap tunduk pada ketentuannya (atribusi, non-komersial). Komponen pihak ketiga tetap memakai lisensinya masing-masing.
