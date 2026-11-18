import { mkdir, copyFile, readFile, writeFile } from 'node:fs/promises';
const root = new URL('../src/BattleNusantara.UI/wwwroot/vendor/', import.meta.url);
await mkdir(root, { recursive: true });
for (const name of ['three.module.js', 'three.core.js']) await copyFile(new URL(`../node_modules/three/build/${name}`, import.meta.url), new URL(name, root));
let loader = await readFile(new URL('../node_modules/three/examples/jsm/loaders/GLTFLoader.js', import.meta.url), 'utf8');
loader = loader.replaceAll("from 'three'", "from './three.module.js'").replaceAll("'../utils/BufferGeometryUtils.js'", "'./BufferGeometryUtils.js'");
await writeFile(new URL('GLTFLoader.js', root), loader);
let utils = await readFile(new URL('../node_modules/three/examples/jsm/utils/BufferGeometryUtils.js', import.meta.url), 'utf8');
await writeFile(new URL('BufferGeometryUtils.js', root), utils.replaceAll("from 'three'", "from './three.module.js'"));
const skeleton = await readFile(new URL('../node_modules/three/examples/jsm/utils/SkeletonUtils.js', import.meta.url), 'utf8');
await writeFile(new URL('SkeletonUtils.js', root), skeleton.replaceAll("from 'three'", "from './three.module.js'"));
// Post-processing (bloom, output and FXAA) for the high-quality setting, flattened into the vendor folder.
const passes = ['EffectComposer', 'RenderPass', 'ShaderPass', 'MaskPass', 'Pass', 'UnrealBloomPass', 'OutputPass'];
const shaders = ['CopyShader', 'LuminosityHighPassShader', 'OutputShader', 'FXAAShader'];
for (const [folder, names] of [['postprocessing', passes], ['shaders', shaders]])
  for (const name of names) {
    const source = await readFile(new URL(`../node_modules/three/examples/jsm/${folder}/${name}.js`, import.meta.url), 'utf8');
    await writeFile(new URL(`${name}.js`, root), source.replaceAll("from 'three'", "from './three.module.js'").replace(/'\.\.\/shaders\/(\w+)\.js'/g, "'./$1.js'"));
  }
await copyFile(new URL('../node_modules/three/LICENSE', import.meta.url), new URL('THREE-LICENSE.txt', root));
console.log('Vendored Three.js, GLTFLoader, SkeletonUtils and post-processing for offline runtime.');
