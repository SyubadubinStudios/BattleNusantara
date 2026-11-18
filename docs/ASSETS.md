# Assets and notices / Aset dan atribusi

## Original assets / Aset asli

`assets/blender/create_temple.py` creates the quantum shrine in an isolated Blender scene. It was executed through Blender MCP and exports `src/BattleNusantara.UI/wwwroot/models/quantum-shrine.glb` plus an editable `assets/blender/quantum-shrine.blend`. The crystal has a rotation animation. The user's original Blender scene is preserved. Change the script's `ROOT` for another checkout.

Skrip tersebut dijalankan melalui Blender MCP, menggunakan scene terpisah dan mempertahankan scene awal pengguna. Model dan animasi inti merupakan aset asli prototipe. Skrip menemukan root repositori dari lokasinya sendiri; saat dijalankan lewat `exec()`, sertakan `__file__` atau atur `NUSANTARA_ROOT`. / The scripts find the repository root from their own location; when run through `exec()`, pass `__file__` or set `NUSANTARA_ROOT`.

`assets/blender/create_characters.py` (v0.13) builds 24 characters on one rig with six clips and Naga Basuki on its own rig, exporting `models/characters.glb`, `models/naga.glb` and `assets/blender/characters.blend`. `assets/blender/create_props.py` builds the tower, outpost, inscription, keris, mine and ship fragments (`models/props.glb`, `assets/blender/props.blend`). Both ran through Blender MCP. All designs are original; historical and mythological figures are interpreted freely, and no licensed comic or film character is modelled. The v0.12 warrior was replaced and removed. / Kedua skrip dijalankan melalui Blender MCP. Semua desain orisinal; tokoh sejarah dan mitologi ditafsirkan bebas, dan tidak ada tokoh komik atau film berlisensi yang dimodelkan.

**Licensed characters / Tokoh berlisensi.** The concept named comic and film heroes (for example Gundala and Sri Asih). They are owned by their rights holders and are not in the game. Satria Bledhek and Rara Angkasa are original heroes with comparable powers; their names, looks, stories and portraits were made for this project. / Konsep menyebut pahlawan komik dan film. Tokoh-tokoh itu tidak ada di game; Satria Bledhek dan Rara Angkasa adalah hero orisinal.

**Portraits / Potret.** The six hero portraits in `wwwroot/portraits/` (WebP, 384×576) were generated with GPT Image (via the Rodin MCP, GPT Image Flare) from prompts that describe our own designs, and reviewed before use; the 1024×1536 sources are in `assets/concepts/`. Check the image service's terms before a commercial release. / Dibuat dengan GPT Image dari prompt yang menggambarkan desain sendiri, lalu ditinjau.

**Voices / Suara.** The 32 clips in `wwwroot/voice/` (16 dialogue lines × Indonesian and English, MP3 64 kbps, about 1.6 MB) were generated with ElevenLabs (model eleven_v3; voices George, Bill, Adam and Callum from the ElevenLabs library) on the **free plan**. The free plan requires attribution ("Voices: ElevenLabs", shown in the credits) and does **not** permit commercial use. Regenerate them on a paid plan before any commercial release. / Dibuat dengan ElevenLabs **paket gratis**: wajib atribusi dan **tidak** boleh dipakai komersial; buat ulang dengan paket berbayar sebelum rilis komersial.

**Marketing / Pemasaran.** The key art in `assets/marketing/` (prompts describe our own hero designs) was generated with GPT Image via the Rodin MCP; `scripts/marketing.py` composes it with the portraits and real screenshots into `docs/marketing/`. / Key art dibuat dengan GPT Image; `scripts/marketing.py` menyusunnya menjadi materi media sosial.

**Music / Musik.** The gamelan-style music is an original composition written for the game and synthesized by Web Audio at runtime; no recording or sample is bundled. / Musik bergaya gamelan adalah gubahan asli yang disintesis saat game berjalan.

Hero symbols in menus, terrain, the Adventure props, rain, ocean, lighting, and particles are procedural. There are no commercial character models and no imported third-party art.

Medan, properti Adventure, hujan, laut, dan partikel bersifat prosedural. Tidak ada model tokoh komersial atau seni pihak ketiga yang diimpor. Rodin/GPT Image Flare belum digunakan pada tahap visual ini.

Version 0.2 reuses the original Blender shrine for fixed RTS ruins; collision bounds come from the server snapshot. No new third-party model is introduced. / Versi 0.2 memakai ulang candi Blender sebagai reruntuhan RTS dengan batas collision dari snapshot server.

## Third-party runtime / Runtime pihak ketiga

- Three.js **0.180.0**, MIT; notice copied to `src/BattleNusantara.UI/wwwroot/vendor/THREE-LICENSE.txt`.
- .NET, ASP.NET Core, and WPF packages retain their upstream licenses. Package versions are pinned in project files.
- Display fonts use installed Palatino Linotype/Georgia; body text uses installed Segoe UI/system sans-serif. No font files are redistributed.
- All sound (pad, rain, combat layer, drums, thunder, end phrase) is synthesized with Web Audio; no recording is bundled. / Semua suara disintesis; tidak ada rekaman.
- `SkeletonUtils.js` and `BufferGeometryUtils.js` come from the same Three.js package (MIT) and are vendored by `npm run vendor`.
- Microsoft Semantic Kernel **1.82.0**, MIT, used by the server only for the optional Keeper voice. Text it generates comes from the configured Azure OpenAI deployment under that service's terms; none is stored in the repository. / Dipakai server hanya untuk suara Juru Kunci opsional; teks buatan model tidak disimpan di repositori.
- PostgreSQL and Garnet are test-time services on the development machine and are not distributed with the game. / PostgreSQL dan Garnet hanya layanan uji, tidak didistribusikan bersama game.

The design lists additional fictional characters, but they are not shipped as game assets in this prototype. Obtain appropriate permissions before producing/distributing third-party character content. 

Nama karakter fiksi tambahan ada pada desain, belum disertakan sebagai aset game. Perizinan konten pihak ketiga harus diselesaikan sebelum distribusi. 

## Licences / Lisensi

The code is released under the MIT licence in `LICENSE`. The original models, portraits and music in this repository are part of the project and may be used under the same terms. The voice clips in `wwwroot/voice/` are **not** covered by the MIT licence: they were generated on the ElevenLabs free plan and remain subject to its terms (attribution, non-commercial use). Third-party runtime components keep their own licences, listed above.

Kode dirilis dengan lisensi MIT pada `LICENSE`. Model, potret, dan musik asli di repositori ini adalah bagian proyek dan dapat dipakai dengan ketentuan yang sama. Klip suara di `wwwroot/voice/` **tidak** termasuk lisensi MIT: klip itu dibuat dengan paket gratis ElevenLabs dan tetap tunduk pada ketentuannya (atribusi, non-komersial). Komponen pihak ketiga tetap memakai lisensinya masing-masing.
