"""Regenerates the reference artefacts of the visual tests, and their manifest entries, from the tools
recorded for them.

    python -I scripts/visual-references.py photographs   # the photographs Pragmatic.Imaging.Tests encodes
    python -I scripts/visual-references.py metrics       # the image pairs and the values the metrics are held to
    python -I scripts/visual-references.py webp          # WebP files libwebp wrote, for the managed decoder
    python -I scripts/visual-references.py bmp           # the BMP Suite's files, for the managed BMP decoder
    python -I scripts/visual-references.py gif           # GIF files Pillow wrote, for the managed GIF decoder

Needs scikit-image, Pillow and NumPy (pip install scikit-image pillow, in a virtual environment: docs/TESTING.md,
"Images and PDF pages, compared visually"); `metrics` also downloads the CIEDE2000 test data from scikit-image's
repository at the tag of the installed version.

The rule this exists for: a reference is never produced by the code it checks. Every file written here
comes from scikit-image, Pillow or NumPy, never from a Pragmatic assembly, and the manifest beside it
(`references.json`) says which tool, which version and what was done. No test runs this script: a
regenerated reference is a change to commit, and shows up in the diff of the pull request.
"""
import datetime
import json
import shutil
import sys
import urllib.request
from pathlib import Path

import numpy as np
import PIL
import skimage
from PIL import Image, ImageFilter, features
from skimage import color, data, metrics

ROOT = Path(__file__).resolve().parent.parent
PHOTOGRAPHS = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Tests' / 'References' / 'Photographs'
METRICS = ROOT / 'shared' / 'VisualTesting.Tests' / 'References'
WEBP = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Codecs.Tests' / 'References' / 'WebP'
BMP = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Tests' / 'References' / 'Bmp'
GIF = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Tests' / 'References' / 'Gif'
TODAY = datetime.date.today().isoformat()
TOOLS = f'scikit-image {skimage.__version__}, Pillow {PIL.__version__}, NumPy {np.__version__}'
SKIMAGE_DATA = Path(data.__file__).parent

# Each photograph with the licence its own docstring in skimage.data states, quoted.
SOURCES = {
    'astronaut': ('astronaut.png', 'NASA, the astronaut Eileen Collins',
                  'Public domain: "No known copyright restrictions, released into the public domain."'),
    'coffee': ('coffee.png', 'Rachel Michetti, courtesy of Pikolo Espresso Bar',
               'CC0: "No copyright restrictions. CC0 by the photographer (Rachel Michetti)."'),
    'chelsea': ('chelsea.png', 'Stefan van der Walt',
                'CC0: "No copyright restrictions. CC0 by the photographer (Stefan van der Walt)."'),
    'rocket': ('rocket.jpg', 'SpaceX, the launch of DSCOVR on Falcon 9',
               'Public domain: "The image was captured by SpaceX and released in the public domain."'),
}


def write_manifest(directory, entries):
    manifest = {
        'about': 'Every reference in this directory, with what produced it. Regenerate with '
                 'scripts/visual-references.py; a reference is never produced by the code it checks.',
        'references': dict(sorted(entries.items())),
    }
    (directory / 'references.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def photographs():
    PHOTOGRAPHS.mkdir(parents=True, exist_ok=True)
    entries = {}
    for name, (file, author, licence) in SOURCES.items():
        source = SKIMAGE_DATA / file
        target = PHOTOGRAPHS / f'{name}.png'
        if source.suffix == '.png':
            shutil.copyfile(source, target)
            how = f'copied byte for byte from skimage/data/{file}'
        else:
            Image.open(source).convert('RGB').save(target, optimize=True)
            how = f'skimage/data/{file} decoded by Pillow and written as PNG (the source is itself a JPEG)'
        entries[target.name] = {
            'origin': f'scikit-image {skimage.__version__}, skimage.data.{name}: {author}',
            'licence': licence,
            'producedBy': f'scripts/visual-references.py photographs, {TOOLS}: {how}',
            'date': TODAY,
        }
    write_manifest(PHOTOGRAPHS, entries)


def luma(rgb):
    """Rec. 601 luma, the weights JPEG uses, in floating point over 0..255."""
    rgb = rgb.astype(np.float64)
    return 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]


def ssim(a, b):
    return float(metrics.structural_similarity(a, b, data_range=255, gaussian_weights=True, sigma=1.5,
                                               use_sample_covariance=False))


def measure(reference, distorted):
    """What the C# metrics are held to, each computed by scikit-image or NumPy."""
    difference = np.abs(reference.astype(np.int32) - distorted.astype(np.int32))
    delta_e = color.deltaE_ciede2000(color.rgb2lab(reference), color.rgb2lab(distorted))
    return {
        'maxAbsolute': [int(difference[..., c].max()) for c in range(3)],
        'meanAbsolute': [float(difference[..., c].mean()) for c in range(3)],
        'psnr': float(metrics.peak_signal_noise_ratio(reference, distorted, data_range=255)),
        'ssimLuma': ssim(luma(reference), luma(distorted)),
        'ssimChannels': [ssim(reference[..., c].astype(np.float64), distorted[..., c].astype(np.float64)) for c in range(3)],
        'deltaE2000Mean': float(delta_e.mean()),
        'deltaE2000Max': float(delta_e.max()),
    }


def metric_fixtures():
    METRICS.mkdir(parents=True, exist_ok=True)
    entries = {}
    values = {}
    # Two crops of 128 x 128: skin and hair, then porcelain against wood grain.
    crops = {'astronaut-face': (data.astronaut(), 100, 160), 'coffee-texture': (data.coffee(), 150, 230)}
    rng = np.random.default_rng(1014)
    for name, (image, top, left) in crops.items():
        original = np.ascontiguousarray(image[top:top + 128, left:left + 128, :3])
        source = name.split('-')[0]
        variants = {'original': original}

        pil = Image.fromarray(original)
        jpeg_path = METRICS / f'{name}.jpeg-q25.jpg'
        pil.save(jpeg_path, quality=25)
        variants['jpeg-q25'] = np.asarray(Image.open(jpeg_path).convert('RGB'))
        jpeg_path.unlink()
        variants['blur'] = np.asarray(pil.filter(ImageFilter.GaussianBlur(radius=1.5)))
        noise = rng.integers(-12, 13, size=original.shape)
        variants['noise'] = np.clip(original.astype(np.int32) + noise, 0, 255).astype(np.uint8)

        how = {
            'original': f'a 128 x 128 crop at row {top}, column {left} of skimage.data.{source}',
            'jpeg-q25': 'the original encoded by Pillow as JPEG at quality 25, then decoded by Pillow',
            'blur': 'the original through Pillow ImageFilter.GaussianBlur(radius=1.5)',
            'noise': 'the original plus NumPy default_rng(1014).integers(-12, 13) per sample, clipped to 0..255',
        }
        for variant, pixels in variants.items():
            file = f'{name}.{variant}.png'
            Image.fromarray(pixels).save(METRICS / file)
            entries[file] = {
                'origin': f'scikit-image {skimage.__version__}, skimage.data.{source}',
                'licence': SOURCES[source][2],
                'producedBy': f'scripts/visual-references.py metrics, {TOOLS}: {how[variant]}',
                'date': TODAY,
            }
            if variant != 'original':
                values[f'{name}.original.png|{file}'] = measure(original, pixels)

    (METRICS / 'metrics.json').write_text(json.dumps({
        'about': 'The value of every metric on every pair, computed by the tools named here, which the C# metrics must match.',
        'tools': TOOLS,
        'luma': 'Rec. 601: 0.299 R + 0.587 G + 0.114 B',
        'ssim': 'skimage.metrics.structural_similarity(data_range=255, gaussian_weights=True, sigma=1.5, use_sample_covariance=False)',
        'psnr': 'skimage.metrics.peak_signal_noise_ratio(data_range=255), over the three channels',
        'deltaE2000': 'skimage.color.deltaE_ciede2000 over skimage.color.rgb2lab (D65, 2 degrees)',
        'pairs': values,
    }, indent=2) + '\n', encoding='utf-8')
    entries['metrics.json'] = {
        'origin': 'computed on the image pairs in this directory',
        'licence': 'derived values, no licence of their own',
        'producedBy': f'scripts/visual-references.py metrics, {TOOLS}',
        'date': TODAY,
    }

    tag = f'v{skimage.__version__}'
    url = f'https://raw.githubusercontent.com/scikit-image/scikit-image/{tag}/tests/skimage/color/ciede2000_test_data.txt'
    with urllib.request.urlopen(url) as response:
        (METRICS / 'ciede2000_test_data.txt').write_bytes(response.read())
    entries['ciede2000_test_data.txt'] = {
        'origin': 'G. Sharma, W. Wu, E. N. Dalal, "The CIEDE2000 Color-Difference Formula: Implementation Notes, '
                  'Supplementary Test Data, and Mathematical Observations", Color Research and Application 30(1), 2005; '
                  f'as transcribed by scikit-image at {tag}, tests/skimage/color/ciede2000_test_data.txt',
        'licence': 'published test data; the transcription is part of scikit-image, BSD-3-Clause',
        'producedBy': f'scripts/visual-references.py metrics: downloaded from {url}',
        'date': TODAY,
    }
    write_manifest(METRICS, entries)


def webp_files():
    """WebP files libwebp wrote, through Pillow, for the managed decoder to read: every mode of the format the
    encoder reaches through Pillow's options, on crops of the photographs and on synthetic images."""
    WEBP.mkdir(parents=True, exist_ok=True)
    for old in WEBP.glob('*.webp'):
        old.unlink()
    entries = {}
    libwebp = features.version('webp')
    tools = f'Pillow {PIL.__version__} with libwebp {libwebp}, NumPy {np.__version__}'

    def save(file, image, how, source=None, **options):
        image.save(WEBP / file, 'WEBP', **options)
        settings = ', '.join(f'{key}={value!r}' for key, value in sorted(options.items()) if key not in ('append_images', 'exif'))
        entries[file] = {
            'origin': f'scikit-image {skimage.__version__}, skimage.data.{source}' if source else 'synthetic, made by the script',
            'licence': SOURCES[source][2] if source else 'no licence needed: generated pixels',
            'producedBy': f'scripts/visual-references.py webp, {tools}: {how}, saved with {settings}',
            'date': TODAY,
        }

    # Crops of an odd size, so the last macroblock row and column are partial.
    crops = {
        'astronaut': Image.fromarray(data.astronaut()[60:211, 140:343]),
        'coffee': Image.fromarray(data.coffee()[120:271, 200:403]),
        'chelsea': Image.fromarray(data.chelsea()[40:191, 100:303]),
    }
    for name, crop in crops.items():
        how = f'a 203 x 151 crop of skimage.data.{name}'
        for quality in (20, 75, 95):
            save(f'lossy-{name}-q{quality}.webp', crop, how, name, quality=quality, method=4)
        for method in (0, 6):
            save(f'lossless-{name}-m{method}.webp', crop, how, name, lossless=True, method=method)
    save('lossy-coffee-m0.webp', crops['coffee'], 'a 203 x 151 crop of skimage.data.coffee', 'coffee', quality=60, method=0)
    save('lossy-coffee-m6.webp', crops['coffee'], 'a 203 x 151 crop of skimage.data.coffee', 'coffee', quality=60, method=6)

    # Alpha over a photograph: a radial ramp, and a hard-edged disc that is fully transparent.
    rgb = np.asarray(crops['chelsea'])
    yy, xx = np.mgrid[0:rgb.shape[0], 0:rgb.shape[1]]
    ramp = np.clip(255 - np.hypot(yy - 75, xx - 100) * 2.2, 0, 255).astype(np.uint8)
    ramp[np.hypot(yy - 40, xx - 160) < 18] = 0
    rgba = Image.fromarray(np.dstack([rgb, ramp]))
    how = 'the chelsea crop with a radial alpha ramp and a transparent disc'
    save('lossy-alpha-q75.webp', rgba, how, 'chelsea', quality=75, alpha_quality=100)
    save('lossy-alpha-aq40.webp', rgba, how, 'chelsea', quality=75, alpha_quality=40)
    save('lossless-alpha.webp', rgba, how, 'chelsea', lossless=True)
    save('lossless-alpha-exact.webp', rgba, how + ', the colour under alpha 0 kept', 'chelsea', lossless=True, exact=True)

    # Few colours, so the encoder indexes them: 2, 4 and 16 colours pack 8, 4 and 2 pixels per byte.
    rng = np.random.default_rng(853)
    for colours in (2, 4, 16, 200):
        palette = rng.integers(0, 256, size=(colours, 4), dtype=np.uint8)
        palette[:, 3] = 255 if colours != 16 else palette[:, 3]
        index = (np.add.outer(np.arange(67) // 5, np.arange(45) // 3) + rng.integers(0, 2, size=(67, 45))) % colours
        save(f'lossless-palette-{colours}.webp', Image.fromarray(palette[index]),
             f'a 45 x 67 pattern over {colours} random colours, NumPy default_rng(853)', lossless=True)

    gradient = np.dstack([np.tile(np.arange(256, dtype=np.uint8), (64, 1)),
                          np.tile(np.arange(64, dtype=np.uint8)[:, None] * 4, (1, 256)),
                          np.full((64, 256), 128, np.uint8)])
    save('lossless-gradient.webp', Image.fromarray(gradient), 'a 256 x 64 two-way ramp', lossless=True)
    save('lossy-gradient.webp', Image.fromarray(gradient), 'a 256 x 64 two-way ramp', quality=50)

    # The smallest images, and one row or one column.
    for width, height in ((1, 1), (13, 1), (1, 13), (31, 13)):
        tile = Image.fromarray(rng.integers(0, 256, size=(height, width, 3), dtype=np.uint8))
        how = f'{width} x {height} random pixels, NumPy default_rng(853)'
        save(f'lossy-{width}x{height}.webp', tile, how, quality=80)
        save(f'lossless-{width}x{height}.webp', tile, how, lossless=True)

    # Metadata in the extended container, which the decoder steps over.
    exif = Image.Exif()
    exif[0x0112] = 6
    exif[0x010F] = 'Pragmatic'
    save('lossy-exif.webp', crops['astronaut'], 'the astronaut crop with an EXIF block (orientation 6)', 'astronaut',
         quality=75, exif=exif.tobytes())

    # Animations: the first frame is the image; later frames change a corner.
    base = crops['coffee']
    frames = [base]
    for shift in (1, 2):
        frame = np.asarray(base).copy()
        frame[10:60, 10:90] = np.roll(frame[10:60, 10:90], shift * 17, axis=1)
        frames.append(Image.fromarray(frame))
    how = 'three frames: the coffee crop, then two with a shifted rectangle'
    save('animated-lossy.webp', frames[0], how, 'coffee', save_all=True, append_images=frames[1:], duration=100, loop=0, quality=70)
    save('animated-lossless.webp', frames[0], how, 'coffee', save_all=True, append_images=frames[1:], duration=100, loop=0, lossless=True)
    translucent = [Image.fromarray(np.dstack([np.asarray(f), ramp])) for f in frames]
    save('animated-alpha.webp', translucent[0], how + ', with the chelsea alpha ramp', 'coffee', save_all=True,
         append_images=translucent[1:], duration=80, loop=2, quality=70, background=(255, 0, 0, 255))
    # Every alpha from 0 to 255 over white, in the first frame of an animation: a decoder that blends the
    # first frame onto its transparent canvas, instead of writing it, rounds the colour of the faint ones.
    faint = np.zeros((16, 16, 4), np.uint8)
    faint[..., :3] = 255
    faint[..., 3] = np.arange(256, dtype=np.uint8).reshape(16, 16)
    second = faint.copy()
    second[0:4, 0:4] = (0, 0, 255, 255)
    save('animated-translucent.webp', Image.fromarray(faint), 'two 16 x 16 frames: white at every alpha, then a blue corner',
         save_all=True, append_images=[Image.fromarray(second)], duration=100, lossless=True, exact=True)
    write_manifest(WEBP, entries)


BMP_SUITE = 'https://entropymine.com/jason/bmpsuite/releases/bmpsuite-2.8.zip'
# Their embedded ICC profiles are the one part of the suite its readme leaves out of the public domain.
BMP_WITH_EMBEDDED_PROFILES = {'rgb24prof.bmp', 'rgb24prof2.bmp'}


def bmp_files():
    """Jason Summers' BMP Suite 2.8, the files of its "good" and "questionable" sets, copied byte for byte: the
    decoder is held to what the native library made of each, the ones it refused included."""
    import io
    import zipfile
    BMP.mkdir(parents=True, exist_ok=True)
    for old in BMP.glob('*.bmp'):
        old.unlink()
    with urllib.request.urlopen(BMP_SUITE) as response:
        archive = zipfile.ZipFile(io.BytesIO(response.read()))
    entries = {}
    for member in sorted(archive.namelist()):
        parts = Path(member).parts
        if len(parts) != 3 or parts[1] not in ('g', 'q') or not member.endswith('.bmp'):
            continue
        name = parts[2]
        if name in BMP_WITH_EMBEDDED_PROFILES:
            continue
        (BMP / name).write_bytes(archive.read(member))
        entries[name] = {
            'origin': f'BMP Suite 2.8 by Jason Summers, {parts[1]}/{name}, {BMP_SUITE}',
            'licence': 'Public domain: "Image files generated by this program are not covered by this license, and are '
                       'in the public domain (except for the embedded ICC profiles)." (readme.txt); the files with an '
                       'embedded profile are left out',
            'producedBy': 'scripts/visual-references.py bmp: copied byte for byte from the release archive',
            'date': TODAY,
        }
    write_manifest(BMP, entries)


def gif_files():
    """GIF files Pillow wrote, for the managed decoder to read as the native library read them: palettes of 2 to 256
    colours, interlaced and not, transparency, a local colour table, a comment, the smallest sizes and an animation."""
    GIF.mkdir(parents=True, exist_ok=True)
    for old in GIF.glob('*.gif'):
        old.unlink()
    entries = {}
    tools = f'Pillow {PIL.__version__}, NumPy {np.__version__}'

    def save(file, image, how, source=None, **options):
        image.save(GIF / file, 'GIF', **options)
        settings = ', '.join(f'{key}={value!r}' for key, value in sorted(options.items()) if key != 'append_images')
        entries[file] = {
            'origin': f'scikit-image {skimage.__version__}, skimage.data.{source}' if source else 'synthetic, made by the script',
            'licence': SOURCES[source][2] if source else 'no licence needed: generated pixels',
            'producedBy': f'scripts/visual-references.py gif, {tools}: {how}' + (f', saved with {settings}' if settings else ''),
            'date': TODAY,
        }

    crops = {
        'astronaut': Image.fromarray(data.astronaut()[60:211, 140:343]),
        'coffee': Image.fromarray(data.coffee()[120:271, 200:403]),
        'chelsea': Image.fromarray(data.chelsea()[40:191, 100:303]),
    }
    for name, crop in crops.items():
        how = f'a 203 x 151 crop of skimage.data.{name}, reduced to 256 colours by Pillow'
        paletted = crop.quantize(256)
        save(f'{name}.gif', paletted, how, name, interlace=False)
        save(f'{name}-interlaced.gif', paletted, how, name, interlace=True)
    save('coffee-local-table.gif', crops['coffee'].quantize(64), 'the coffee crop in 64 colours', 'coffee', include_color_table=True,
         interlace=False)
    save('coffee-comment.gif', crops['coffee'].quantize(32), 'the coffee crop in 32 colours', 'coffee', comment=b'Pragmatic', interlace=False)

    # Transparency: the chelsea crop with a hard-edged transparent disc.
    rgb = np.asarray(crops['chelsea'])
    yy, xx = np.mgrid[0:rgb.shape[0], 0:rgb.shape[1]]
    alpha = np.where(np.hypot(yy - 75, xx - 100) < 40, 0, 255).astype(np.uint8)
    save('chelsea-transparent.gif', Image.fromarray(np.dstack([rgb, alpha])), 'the chelsea crop with a transparent disc', 'chelsea',
         interlace=False)

    # Few colours, so the minimum code size is 2, 2, 4 and 8.
    rng = np.random.default_rng(855)
    for colours in (2, 4, 16, 200):
        palette = rng.integers(0, 256, size=(colours, 3), dtype=np.uint8)
        index = (np.add.outer(np.arange(67) // 5, np.arange(45) // 3) + rng.integers(0, 2, size=(67, 45))) % colours
        save(f'palette-{colours}.gif', Image.fromarray(palette[index]),
             f'a 45 x 67 pattern over {colours} random colours, NumPy default_rng(855)', interlace=False)

    # The smallest images, one row and one column: under 16 pixels a side Pillow never interlaces.
    for width, height in ((1, 1), (13, 1), (1, 13), (31, 13)):
        tile = Image.fromarray(rng.integers(0, 256, size=(height, width, 3), dtype=np.uint8))
        save(f'random-{width}x{height}.gif', tile, f'{width} x {height} random pixels, NumPy default_rng(855)')

    # An animation: the first frame is the image; Pillow writes the later ones as smaller changed regions.
    base = np.asarray(crops['coffee'].quantize(128).convert('RGB'))
    frames = [Image.fromarray(base)]
    for shift in (1, 2):
        frame = base.copy()
        frame[10:60, 10:90] = np.roll(frame[10:60, 10:90], shift * 17, axis=1)
        frames.append(Image.fromarray(frame))
    save('animated.gif', frames[0], 'three frames: the coffee crop in 128 colours, then two with a shifted rectangle', 'coffee',
         save_all=True, append_images=frames[1:], duration=100, loop=0, interlace=False)
    write_manifest(GIF, entries)


COMMANDS = {'photographs': photographs, 'metrics': metric_fixtures, 'webp': webp_files, 'bmp': bmp_files, 'gif': gif_files}

if __name__ == '__main__':
    if len(sys.argv) != 2 or sys.argv[1] not in COMMANDS:
        sys.exit(f'usage: python -I scripts/visual-references.py {{{"|".join(COMMANDS)}}}')
    COMMANDS[sys.argv[1]]()
